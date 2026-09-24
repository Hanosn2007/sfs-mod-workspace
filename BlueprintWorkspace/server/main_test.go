package main

import (
	"bytes"
	"encoding/json"
	"net/http/httptest"
	"testing"
)

func TestWorkspaceFlow(t *testing.T) {
	dir := t.TempDir()
	invite, err := initialize(dir, "Friends")
	if err != nil {
		t.Fatal(err)
	}
	s, err := openServer(dir)
	if err != nil {
		t.Fatal(err)
	}
	call := func(method, path, token string, body interface{}, want int) map[string]interface{} {
		t.Helper()
		var raw []byte
		if body != nil {
			raw, err = json.Marshal(body)
			if err != nil {
				t.Fatal(err)
			}
		}
		req := httptest.NewRequest(method, path, bytes.NewReader(raw))
		if token != "" {
			req.Header.Set("Authorization", "Bearer "+token)
		}
		w := httptest.NewRecorder()
		s.ServeHTTP(w, req)
		if w.Code != want {
			t.Fatalf("%s %s: got %d, want %d: %s", method, path, w.Code, want, w.Body.String())
		}
		var result map[string]interface{}
		if err := json.Unmarshal(w.Body.Bytes(), &result); err != nil {
			t.Fatal(err)
		}
		return result
	}
	call("POST", "/api/register", "", map[string]string{"invite": "000000000000000000000000000000000000000000000000", "username": "alice", "display_name": "Alice", "password": "12345678", "client": "game"}, 401)
	call("POST", "/api/register", "", map[string]string{"invite": invite, "username": "alice", "display_name": "Alice", "password": "1234567", "client": "game"}, 400)
	call("POST", "/api/register", "", map[string]string{"invite": invite, "username": "alice", "display_name": "Alice", "password": "中文密码", "client": "game"}, 400)
	a := call("POST", "/api/register", "", map[string]string{"invite": invite, "username": "alice", "display_name": "Alice", "password": "12345678", "client": "game"}, 201)["token"].(string)
	b := call("POST", "/api/register", "", map[string]string{"invite": invite, "username": "bob", "display_name": "Alice", "password": "12345678", "client": "game"}, 201)["token"].(string)
	call("GET", "/api/blueprints", "", nil, 401)
	data := publishRequest{Name: "Rocket", Version: `"1.6.00.16"`, Blueprint: `{"parts":[],"stages":[]}`}
	call("POST", "/api/blueprints", a, publishRequest{Name: "../bad", Version: data.Version, Blueprint: data.Blueprint}, 400)
	entry := call("POST", "/api/blueprints", a, data, 201)
	id := entry["id"].(string)
	if call("POST", "/api/blueprints", a, data, 200)["id"] != id {
		t.Fatal("duplicate upload created a new entry")
	}
	renamed := data
	renamed.Name = "Rocket Copy"
	if call("POST", "/api/blueprints", a, renamed, 201)["id"] == id {
		t.Fatal("a differently named blueprint was incorrectly deduplicated")
	}
	if call("POST", "/api/blueprints", b, data, 201)["id"] == id {
		t.Fatal("same display name caused two accounts to share an upload")
	}
	items := call("GET", "/api/blueprints", b, nil, 200)["blueprints"].([]interface{})
	if len(items) != 3 {
		t.Fatalf("Bob saw %d blueprints, want 3", len(items))
	}
	fetched := call("GET", "/api/blueprints/"+id, b, nil, 200)
	if fetched["blueprint"] != data.Blueprint || fetched["version"] != data.Version {
		t.Fatal("fetched blueprint does not match uploaded files")
	}
	call("GET", "/api/blueprints/../../state.json", b, nil, 404)
	if _, err := openServer(dir); err != nil {
		t.Fatalf("state did not survive restart: %v", err)
	}
}
