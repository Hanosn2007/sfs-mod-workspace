package main

import (
	"archive/zip"
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
)

func request(t *testing.T, s *server, method, path, token string, cookie *http.Cookie, origin string, body interface{}) *httptest.ResponseRecorder {
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
	if cookie != nil {
		r.AddCookie(cookie)
	}
	if origin != "" {
		r.Header.Set("Origin", origin)
	}
	w := httptest.NewRecorder()
	s.ServeHTTP(w, r)
	return w
}

func requireStatus(t *testing.T, w *httptest.ResponseRecorder, want int) map[string]interface{} {
	t.Helper()
	if w.Code != want {
		t.Fatalf("status %d, want %d: %s", w.Code, want, w.Body.String())
	}
	var result map[string]interface{}
	if err := json.Unmarshal(w.Body.Bytes(), &result); err != nil {
		t.Fatal(err)
	}
	return result
}

func TestWebAccountAndAdminPermissions(t *testing.T) {
	t.Setenv("BW_DEV_HTTP_COOKIE", "1")
	dir := t.TempDir()
	invite, err := initialize(dir, "Crew")
	if err != nil {
		t.Fatal(err)
	}
	s, err := openServer(dir)
	if err != nil {
		t.Fatal(err)
	}
	register := func(username string) *http.Cookie {
		w := request(t, s, "POST", "/api/register", "", nil, "http://example.com", map[string]string{
			"invite": invite, "username": username, "display_name": username, "password": "a-long-safe-password", "client": "web"})
		requireStatus(t, w, 201)
		if len(w.Result().Cookies()) != 1 {
			t.Fatal("web session cookie missing")
		}
		return w.Result().Cookies()[0]
	}
	ownerCookie := register("owner")
	gameLogin := requireStatus(t, request(t, s, "POST", "/api/login", "", nil, "", map[string]string{"username": "owner", "password": "a-long-safe-password", "client": "game"}), 200)
	gameToken := gameLogin["token"].(string)
	webMe := requireStatus(t, request(t, s, "GET", "/api/me", "", ownerCookie, "", nil), 200)
	gameMe := requireStatus(t, request(t, s, "GET", "/api/me", gameToken, nil, "", nil), 200)
	if webMe["account_id"] != gameMe["account_id"] || len(s.data.Accounts) != 1 {
		t.Fatal("web and game did not share one account")
	}
	requireStatus(t, request(t, s, "GET", "/api/admin/overview", "", ownerCookie, "", nil), 403)
	if err := bootstrapOwner(dir, s.data.Accounts[0].ID); err != nil {
		t.Fatal(err)
	}
	s, err = openServer(dir)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, request(t, s, "GET", "/api/admin/overview", "", ownerCookie, "", nil), 200)
	memberCookie := register("friend")
	publish := publishRequest{Name: "Rocket", Blueprint: `{"parts":[],"stages":[]}`, Version: `"1.6"`}
	entry := requireStatus(t, request(t, s, "POST", "/api/blueprints", "", ownerCookie, "http://example.com", publish), 201)
	id := entry["id"].(string)
	requireStatus(t, request(t, s, "POST", "/api/blueprints", "", ownerCookie, "http://evil.example", publish), 403)
	w := request(t, s, "GET", "/api/blueprints/"+id+"/download", "", memberCookie, "", nil)
	if w.Code != 200 {
		t.Fatalf("download: %d %s", w.Code, w.Body.String())
	}
	archive, err := zip.NewReader(bytes.NewReader(w.Body.Bytes()), int64(w.Body.Len()))
	if err != nil {
		t.Fatal(err)
	}
	if len(archive.File) != 2 || archive.File[0].Name != "Blueprint.txt" || archive.File[1].Name != "Version.txt" {
		t.Fatal("download archive has wrong files")
	}
	friendID := s.data.Accounts[1].ID
	requireStatus(t, request(t, s, "POST", "/api/admin/members/"+friendID+"/suspend", "", ownerCookie, "http://example.com", map[string]string{}), 200)
	requireStatus(t, request(t, s, "GET", "/api/blueprints", "", memberCookie, "", nil), 401)
	requireStatus(t, request(t, s, "POST", "/api/admin/members/"+friendID+"/restore", "", ownerCookie, "http://example.com", map[string]string{}), 200)
	requireStatus(t, request(t, s, "GET", "/api/blueprints", "", memberCookie, "", nil), 401)
	relogin := request(t, s, "POST", "/api/login", "", nil, "http://example.com", map[string]string{"username": "friend", "password": "a-long-safe-password", "client": "web"})
	requireStatus(t, relogin, 200)
	memberCookie = relogin.Result().Cookies()[0]
	requireStatus(t, request(t, s, "GET", "/api/blueprints", "", memberCookie, "", nil), 200)
	requireStatus(t, request(t, s, "POST", "/api/admin/blueprints/"+id+"/archive", "", ownerCookie, "http://example.com", map[string]string{}), 200)
	list := requireStatus(t, request(t, s, "GET", "/api/blueprints", "", memberCookie, "", nil), 200)
	if len(list["blueprints"].([]interface{})) != 0 {
		t.Fatal("archived blueprint remained visible")
	}
	requireStatus(t, request(t, s, "GET", "/api/blueprints/"+id, "", memberCookie, "", nil), 404)
	requireStatus(t, request(t, s, "POST", "/api/admin/blueprints/"+id+"/restore", "", ownerCookie, "http://example.com", map[string]string{}), 200)
	list = requireStatus(t, request(t, s, "GET", "/api/blueprints", "", memberCookie, "", nil), 200)
	if len(list["blueprints"].([]interface{})) != 1 {
		t.Fatal("restored blueprint missing")
	}
	requireStatus(t, request(t, s, "POST", "/api/admin/invite/rotate", "", ownerCookie, "http://example.com", map[string]string{}), 200)
	requireStatus(t, request(t, s, "POST", "/api/register", "", nil, "http://example.com", map[string]string{
		"invite": invite, "username": "another", "display_name": "Another", "password": "a-long-safe-password", "client": "web"}), 401)
	requireStatus(t, request(t, s, "POST", "/api/logout", "", ownerCookie, "http://example.com", map[string]string{}), 200)
	requireStatus(t, request(t, s, "GET", "/api/me", "", ownerCookie, "", nil), 401)
	requireStatus(t, request(t, s, "GET", "/api/me", gameToken, nil, "", nil), 200)
}
