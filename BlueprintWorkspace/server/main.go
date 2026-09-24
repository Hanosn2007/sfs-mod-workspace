package main

import (
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

const maxBody = 3 << 20
const maxBlueprint = 2 << 20

type blueprint struct {
	ID        string    `json:"id"`
	Name      string    `json:"name"`
	Author    string    `json:"author"`
	AuthorID  string    `json:"author_account_id,omitempty"`
	Version   string    `json:"version"`
	SHA256    string    `json:"sha256"`
	CreatedAt time.Time `json:"created_at"`
	Archived  bool      `json:"archived"`
}

type workspace struct {
	ID         string      `json:"id"`
	Name       string      `json:"name"`
	InviteHash string      `json:"invite_hash"`
	Blueprints []blueprint `json:"blueprints"`
}

type state struct {
	SchemaVersion int          `json:"schema_version"`
	Workspaces    []workspace  `json:"workspaces"`
	Accounts      []account    `json:"accounts"`
	Memberships   []membership `json:"memberships"`
	Sessions      []session    `json:"sessions"`
	Audit         []auditEvent `json:"audit"`
}

type server struct {
	mu       sync.Mutex
	dir      string
	data     state
	basePath string
}

type publishRequest struct {
	Name      string `json:"name"`
	Version   string `json:"version"`
	Blueprint string `json:"blueprint"`
}

func main() {
	if len(os.Args) < 2 {
		log.Fatal("usage: blueprint-server init|serve|rotate-invite|bootstrap-owner [options]")
	}
	switch os.Args[1] {
	case "init":
		fs := flag.NewFlagSet("init", flag.ExitOnError)
		dir := fs.String("data", "./data", "private data directory")
		name := fs.String("name", "Friends", "workspace name")
		fs.Parse(os.Args[2:])
		invite, err := initialize(*dir, *name)
		if err != nil {
			log.Fatal(err)
		}
		fmt.Println("Workspace created. Invite code (show only to members):", invite)
	case "serve":
		fs := flag.NewFlagSet("serve", flag.ExitOnError)
		dir := fs.String("data", "./data", "private data directory")
		listen := fs.String("listen", "127.0.0.1:8787", "HTTP listener (put HTTPS reverse proxy in front)")
		fs.Parse(os.Args[2:])
		s, err := openServer(*dir)
		if err != nil {
			log.Fatal(err)
		}
		if s.data.SchemaVersion != 2 {
			log.Fatal("unsupported state schema")
		}
		log.Printf("listening on %s", *listen)
		log.Fatal((&http.Server{
			Addr: *listen, Handler: s, ReadHeaderTimeout: 5 * time.Second,
			ReadTimeout: 15 * time.Second, WriteTimeout: 20 * time.Second,
		}).ListenAndServe())
	case "rotate-invite":
		fs := flag.NewFlagSet("rotate-invite", flag.ExitOnError)
		dir := fs.String("data", "./data", "private data directory; stop the server first")
		workspaceID := fs.String("workspace", "", "workspace ID; required when more than one workspace exists")
		fs.Parse(os.Args[2:])
		s, err := openServer(*dir)
		if err != nil {
			log.Fatal(err)
		}
		index := -1
		for i := range s.data.Workspaces {
			if s.data.Workspaces[i].ID == *workspaceID || (*workspaceID == "" && len(s.data.Workspaces) == 1) {
				index = i
				break
			}
		}
		if index < 0 {
			log.Fatal("specify a valid -workspace ID")
		}
		invite, err := randomHex(24)
		if err != nil {
			log.Fatal(err)
		}
		var next state
		if err := clone(s.data, &next); err != nil {
			log.Fatal(err)
		}
		next.Workspaces[index].InviteHash = hash(invite)
		if err := s.commit(next); err != nil {
			log.Fatal(err)
		}
		fmt.Println("New invite code (old code is invalid):", invite)
	case "bootstrap-owner":
		fs := flag.NewFlagSet("bootstrap-owner", flag.ExitOnError)
		dir := fs.String("data", "./data", "private data directory; stop the server first")
		accountID := fs.String("account", "", "existing account ID")
		fs.Parse(os.Args[2:])
		if err := bootstrapOwner(*dir, *accountID); err != nil {
			log.Fatal(err)
		}
		fmt.Println("Owner role assigned")
	default:
		log.Fatal("usage: blueprint-server init|serve|rotate-invite|bootstrap-owner [options]")
	}
}

func initialize(dir, name string) (string, error) {
	name = strings.TrimSpace(name)
	if name == "" || len(name) > 80 {
		return "", errors.New("workspace name must be 1-80 characters")
	}
	if _, err := os.Stat(filepath.Join(dir, "state.json")); err == nil {
		return "", errors.New("workspace already initialized")
	} else if !os.IsNotExist(err) {
		return "", err
	}
	if err := os.MkdirAll(dir, 0700); err != nil {
		return "", err
	}
	id, err := randomHex(12)
	if err != nil {
		return "", err
	}
	invite, err := randomHex(24)
	if err != nil {
		return "", err
	}
	data := state{SchemaVersion: 2, Workspaces: []workspace{{ID: id, Name: name, InviteHash: hash(invite), Blueprints: []blueprint{}}}}
	if err := writeJSONAtomic(filepath.Join(dir, "state.json"), data); err != nil {
		return "", err
	}
	return invite, nil
}

func openServer(dir string) (*server, error) {
	raw, err := os.ReadFile(filepath.Join(dir, "state.json"))
	if err != nil {
		return nil, err
	}
	var data state
	if err := json.Unmarshal(raw, &data); err != nil {
		return nil, err
	}
	if len(data.Workspaces) == 0 {
		return nil, errors.New("no workspace configured")
	}
	basePath := strings.TrimRight(os.Getenv("BW_BASE_PATH"), "/")
	if basePath == "" {
		basePath = "/"
	}
	return &server{dir: dir, data: data, basePath: basePath}, nil
}

func (s *server) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("X-Content-Type-Options", "nosniff")
	switch {
	case r.URL.Path == "/api/health" && r.Method == http.MethodGet:
		respond(w, http.StatusOK, map[string]string{"status": "ok"})
	case r.URL.Path == "/api/register" && r.Method == http.MethodPost:
		s.register(w, r)
	case r.URL.Path == "/api/login" && r.Method == http.MethodPost:
		s.login(w, r)
	case r.URL.Path == "/api/me" && r.Method == http.MethodGet:
		s.me(w, r)
	case r.URL.Path == "/api/logout" && r.Method == http.MethodPost:
		s.logout(w, r)
	case r.URL.Path == "/api/workspaces" && r.Method == http.MethodGet:
		s.listWorkspaces(w, r)
	case r.URL.Path == "/api/workspaces" && r.Method == http.MethodPost:
		s.createWorkspace(w, r)
	case r.URL.Path == "/api/workspaces/join" && r.Method == http.MethodPost:
		s.joinWorkspace(w, r)
	case r.URL.Path == "/api/blueprints" && r.Method == http.MethodGet:
		s.list(w, r)
	case r.URL.Path == "/api/blueprints" && r.Method == http.MethodPost:
		s.publish(w, r)
	case strings.HasPrefix(r.URL.Path, "/api/blueprints/") && strings.HasSuffix(r.URL.Path, "/download") && r.Method == http.MethodGet:
		s.download(w, r)
	case strings.HasPrefix(r.URL.Path, "/api/blueprints/") && r.Method == http.MethodGet:
		s.fetch(w, r)
	case strings.HasPrefix(r.URL.Path, "/api/admin/"):
		s.admin(w, r)
	case r.Method == http.MethodGet:
		s.web(w, r)
	default:
		http.NotFound(w, r)
	}
}

func (s *server) list(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	defer s.mu.Unlock()
	ws, _, _, _, _ := s.auth(r)
	if ws == nil {
		fail(w, 401, "unauthorized")
		return
	}
	visible := make([]blueprint, 0, len(ws.Blueprints))
	for _, item := range ws.Blueprints {
		if !item.Archived {
			visible = append(visible, item)
		}
	}
	respond(w, 200, map[string]interface{}{"workspace_name": ws.Name, "blueprints": visible})
}

func (s *server) publish(w http.ResponseWriter, r *http.Request) {
	var req publishRequest
	if !decode(w, r, &req) {
		return
	}
	req.Name = strings.TrimSpace(req.Name)
	if req.Name == "" || len(req.Name) > 100 || strings.ContainsAny(req.Name, "/\\\x00\r\n") ||
		len(req.Version) == 0 || len(req.Version) > 80 || len(req.Blueprint) > maxBlueprint || !validBlueprint(req.Blueprint) {
		fail(w, 400, "invalid blueprint, name, or version")
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	ws, author, _, _, cookieUsed := s.auth(r)
	if ws == nil {
		fail(w, 401, "unauthorized")
		return
	}
	if cookieUsed && !requireSameOrigin(w, r) {
		return
	}
	contentHash := hash(req.Name + "\x00" + req.Blueprint + "\x00" + req.Version)
	for _, b := range ws.Blueprints {
		if !b.Archived && b.SHA256 == contentHash && b.AuthorID == author.ID {
			respond(w, 200, b)
			return
		}
	}
	id, err := randomHex(12)
	if err != nil {
		fail(w, 500, "id generation failed")
		return
	}
	entry := blueprint{ID: id, Name: req.Name, Author: author.DisplayName, AuthorID: author.ID, Version: req.Version, SHA256: contentHash, CreatedAt: time.Now().UTC()}
	if err := os.MkdirAll(filepath.Join(s.dir, "blobs"), 0700); err != nil {
		fail(w, 500, "storage error")
		return
	}
	if err := writeJSONAtomic(filepath.Join(s.dir, "blobs", id+".json"), req); err != nil {
		fail(w, 500, "storage error")
		return
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		fail(w, 500, "state error")
		return
	}
	for wi := range next.Workspaces {
		if next.Workspaces[wi].ID == ws.ID {
			next.Workspaces[wi].Blueprints = append([]blueprint{entry}, next.Workspaces[wi].Blueprints...)
			break
		}
	}
	if err := s.commit(next); err != nil {
		fail(w, 500, "storage error")
		return
	}
	respond(w, http.StatusCreated, entry)
}

func (s *server) fetch(w http.ResponseWriter, r *http.Request) {
	id := strings.TrimPrefix(r.URL.Path, "/api/blueprints/")
	if len(id) != 24 || !isHex(id) {
		fail(w, 404, "not found")
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	ws, _, _, _, _ := s.auth(r)
	if ws == nil {
		fail(w, 401, "unauthorized")
		return
	}
	for _, entry := range ws.Blueprints {
		if entry.ID != id || entry.Archived {
			continue
		}
		raw, err := os.ReadFile(filepath.Join(s.dir, "blobs", id+".json"))
		if err != nil {
			fail(w, 500, "storage error")
			return
		}
		var data publishRequest
		if err := json.Unmarshal(raw, &data); err != nil {
			fail(w, 500, "storage error")
			return
		}
		respond(w, 200, map[string]interface{}{"id": id, "name": entry.Name, "author": entry.Author, "version": data.Version, "blueprint": data.Blueprint})
		return
	}
	fail(w, 404, "not found")
}

func (s *server) commit(next state) error {
	if err := writeJSONAtomic(filepath.Join(s.dir, "state.json"), next); err != nil {
		return err
	}
	s.data = next
	return nil
}

func validBlueprint(raw string) bool {
	var v struct {
		Parts  json.RawMessage `json:"parts"`
		Stages json.RawMessage `json:"stages"`
	}
	if json.Unmarshal([]byte(raw), &v) != nil {
		return false
	}
	return len(v.Parts) > 0 && v.Parts[0] == '[' && len(v.Stages) > 0 && v.Stages[0] == '['
}

func decode(w http.ResponseWriter, r *http.Request, dst interface{}) bool {
	r.Body = http.MaxBytesReader(w, r.Body, maxBody)
	dec := json.NewDecoder(r.Body)
	if err := dec.Decode(dst); err != nil {
		fail(w, 400, "invalid JSON or request too large")
		return false
	}
	var extra interface{}
	if err := dec.Decode(&extra); err != io.EOF {
		fail(w, 400, "unexpected trailing JSON")
		return false
	}
	return true
}

func respond(w http.ResponseWriter, status int, value interface{}) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(value)
}
func fail(w http.ResponseWriter, status int, message string) {
	respond(w, status, map[string]string{"error": message})
}
func hash(value string) string {
	sum := sha256.Sum256([]byte(value))
	return hex.EncodeToString(sum[:])
}
func isHex(value string) bool { _, err := hex.DecodeString(value); return err == nil }
func randomHex(bytes int) (string, error) {
	b := make([]byte, bytes)
	if _, err := rand.Read(b); err != nil {
		return "", err
	}
	return hex.EncodeToString(b), nil
}
func clone(src state, dst *state) error {
	b, err := json.Marshal(src)
	if err != nil {
		return err
	}
	return json.Unmarshal(b, dst)
}

func writeJSONAtomic(path string, value interface{}) error {
	f, err := os.CreateTemp(filepath.Dir(path), ".bw-*")
	if err != nil {
		return err
	}
	defer os.Remove(f.Name())
	if err := f.Chmod(0600); err != nil {
		f.Close()
		return err
	}
	if err := json.NewEncoder(f).Encode(value); err != nil {
		f.Close()
		return err
	}
	if err := f.Sync(); err != nil {
		f.Close()
		return err
	}
	if err := f.Close(); err != nil {
		return err
	}
	return os.Rename(f.Name(), path)
}
