package main

import (
	"bytes"
	"encoding/json"
	"net/http/httptest"
	"testing"
)

func workspaceRequest(t *testing.T, s *server, method, path, token, workspaceID string, body interface{}) *httptest.ResponseRecorder {
	t.Helper()
	var raw []byte
	if body != nil {
		var err error
		raw, err = json.Marshal(body)
		if err != nil {
			t.Fatal(err)
		}
	}
	r := httptest.NewRequest(method, "http://example.com"+path, bytes.NewReader(raw))
	if token != "" {
		r.Header.Set("Authorization", "Bearer "+token)
	}
	if workspaceID != "" {
		r.Header.Set("X-Workspace-ID", workspaceID)
	}
	w := httptest.NewRecorder()
	s.ServeHTTP(w, r)
	return w
}

func TestMultipleWorkspacesKeepFriendsAndIsolateAccess(t *testing.T) {
	dir := t.TempDir()
	firstInvite, err := initialize(dir, "Friends")
	if err != nil {
		t.Fatal(err)
	}
	s, err := openServer(dir)
	if err != nil {
		t.Fatal(err)
	}
	register := func(username string) (string, string) {
		result := requireStatus(t, workspaceRequest(t, s, "POST", "/api/register", "", "", map[string]string{
			"invite": firstInvite, "username": username, "display_name": username, "password": "a-long-safe-password", "client": "game",
		}), 201)
		return result["token"].(string), result["account_id"].(string)
	}
	ownerToken, ownerID := register("owner")
	memberToken, memberID := register("friend")
	outsiderToken, _ := register("outsider")
	if err := bootstrapOwner(dir, ownerID); err != nil {
		t.Fatal(err)
	}
	s, err = openServer(dir)
	if err != nil {
		t.Fatal(err)
	}
	firstID := s.data.Workspaces[0].ID
	requireStatus(t, workspaceRequest(t, s, "POST", "/api/workspaces", memberToken, "", map[string]string{"name": "Lab"}), 403)
	created := requireStatus(t, workspaceRequest(t, s, "POST", "/api/workspaces", ownerToken, "", map[string]string{"name": "Lab"}), 201)
	labID, labInvite := created["id"].(string), created["invite"].(string)
	if labID == firstID || created["role"] != "owner" || len(labInvite) != 48 {
		t.Fatalf("unexpected workspace creation response: %#v", created)
	}
	if s.data.Workspaces[0].ID != firstID || s.data.Workspaces[0].Name != "Friends" || s.data.Workspaces[0].InviteHash != hash(firstInvite) {
		t.Fatal("creating Lab changed the Friends workspace")
	}
	list := requireStatus(t, workspaceRequest(t, s, "GET", "/api/workspaces", ownerToken, "", nil), 200)
	if list["current_workspace_id"] != firstID || len(list["workspaces"].([]interface{})) != 2 {
		t.Fatalf("owner workspaces: %#v", list)
	}
	list = requireStatus(t, workspaceRequest(t, s, "GET", "/api/workspaces", memberToken, "", nil), 200)
	if len(list["workspaces"].([]interface{})) != 1 {
		t.Fatal("new workspace appeared for an uninvited member")
	}
	requireStatus(t, workspaceRequest(t, s, "GET", "/api/me", memberToken, labID, nil), 401)
	requireStatus(t, workspaceRequest(t, s, "POST", "/api/workspaces/join", memberToken, "", map[string]string{"invite": labInvite}), 201)
	requireStatus(t, workspaceRequest(t, s, "POST", "/api/workspaces/join", memberToken, "", map[string]string{"invite": labInvite}), 409)
	me := requireStatus(t, workspaceRequest(t, s, "GET", "/api/me", memberToken, labID, nil), 200)
	if me["workspace_id"] != labID || me["role"] != "member" {
		t.Fatalf("selected workspace was not used: %#v", me)
	}
	if requireStatus(t, workspaceRequest(t, s, "GET", "/api/me", memberToken, "", nil), 200)["workspace_id"] != firstID {
		t.Fatal("headerless old-client default changed")
	}
	requireStatus(t, workspaceRequest(t, s, "GET", "/api/me", outsiderToken, labID, nil), 401)
	requireStatus(t, workspaceRequest(t, s, "GET", "/api/me", ownerToken, "bad-id", nil), 401)
	publish := publishRequest{Name: "Lab Rocket", Version: `"1.6"`, Blueprint: `{"parts":[],"stages":[]}`}
	entry := requireStatus(t, workspaceRequest(t, s, "POST", "/api/blueprints", ownerToken, labID, publish), 201)
	blueprintID := entry["id"].(string)
	labList := requireStatus(t, workspaceRequest(t, s, "GET", "/api/blueprints", memberToken, labID, nil), 200)
	if len(labList["blueprints"].([]interface{})) != 1 {
		t.Fatal("Lab blueprint missing")
	}
	friendsList := requireStatus(t, workspaceRequest(t, s, "GET", "/api/blueprints", memberToken, firstID, nil), 200)
	if len(friendsList["blueprints"].([]interface{})) != 0 {
		t.Fatal("Lab blueprint leaked into Friends")
	}
	requireStatus(t, workspaceRequest(t, s, "GET", "/api/blueprints/"+blueprintID, memberToken, firstID, nil), 404)
	requireStatus(t, workspaceRequest(t, s, "GET", "/api/blueprints/"+blueprintID, outsiderToken, labID, nil), 401)
	admin := requireStatus(t, workspaceRequest(t, s, "GET", "/api/admin/overview", ownerToken, labID, nil), 200)
	if admin["workspace_name"] != "Lab" || len(admin["members"].([]interface{})) != 2 {
		t.Fatalf("Lab admin scope: %#v", admin)
	}
	requireStatus(t, workspaceRequest(t, s, "POST", "/api/admin/members/"+memberID+"/suspend", ownerToken, labID, map[string]string{}), 200)
	requireStatus(t, workspaceRequest(t, s, "GET", "/api/me", memberToken, labID, nil), 401)
	requireStatus(t, workspaceRequest(t, s, "GET", "/api/me", memberToken, firstID, nil), 200)
	fallback := requireStatus(t, workspaceRequest(t, s, "GET", "/api/workspaces", memberToken, labID, nil), 200)
	if fallback["current_workspace_id"] != firstID || len(fallback["workspaces"].([]interface{})) != 1 {
		t.Fatalf("inaccessible selection prevented workspace recovery: %#v", fallback)
	}
	firstAdmin := requireStatus(t, workspaceRequest(t, s, "GET", "/api/admin/overview", ownerToken, firstID, nil), 200)
	if len(firstAdmin["audit"].([]interface{})) != 0 {
		t.Fatal("Lab audit leaked into Friends")
	}
	reopened, err := openServer(dir)
	if err != nil {
		t.Fatal(err)
	}
	if len(reopened.data.Workspaces) != 2 || len(reopened.data.Memberships) != 5 {
		t.Fatal("workspace memberships did not survive restart")
	}
	requireStatus(t, workspaceRequest(t, reopened, "POST", "/api/logout", memberToken, labID, map[string]string{}), 200)
	requireStatus(t, workspaceRequest(t, reopened, "GET", "/api/me", memberToken, firstID, nil), 401)
}

func TestWorkspaceWebMutationsRequireSameOrigin(t *testing.T) {
	t.Setenv("BW_DEV_HTTP_COOKIE", "1")
	dir := t.TempDir()
	invite, err := initialize(dir, "Friends")
	if err != nil {
		t.Fatal(err)
	}
	s, err := openServer(dir)
	if err != nil {
		t.Fatal(err)
	}
	registration := request(t, s, "POST", "/api/register", "", nil, "http://example.com", map[string]string{
		"invite": invite, "username": "owner", "display_name": "Owner", "password": "a-long-safe-password", "client": "web",
	})
	requireStatus(t, registration, 201)
	cookie := registration.Result().Cookies()[0]
	if err := bootstrapOwner(dir, s.data.Accounts[0].ID); err != nil {
		t.Fatal(err)
	}
	s, err = openServer(dir)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, request(t, s, "POST", "/api/workspaces", "", cookie, "", map[string]string{"name": "Lab"}), 403)
	requireStatus(t, request(t, s, "POST", "/api/workspaces", "", cookie, "http://evil.example", map[string]string{"name": "Lab"}), 403)
	requireStatus(t, request(t, s, "POST", "/api/workspaces", "", cookie, "http://example.com", map[string]string{"name": "Lab"}), 201)
}
