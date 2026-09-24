package main

import (
	"archive/zip"
	"bytes"
	"embed"
	"encoding/json"
	"net/http"
	"os"
	"path/filepath"
	"strings"
)

//go:embed web/*
var webFiles embed.FS

func (s *server) web(w http.ResponseWriter, r *http.Request) {
	var name, contentType string
	switch r.URL.Path {
	case "/", "/index.html":
		name, contentType = "web/index.html", "text/html; charset=utf-8"
	case "/app.js":
		name, contentType = "web/app.js", "text/javascript; charset=utf-8"
	case "/style.css":
		name, contentType = "web/style.css", "text/css; charset=utf-8"
	case "/admin/", "/admin/index.html":
		name, contentType = "web/admin.html", "text/html; charset=utf-8"
	case "/admin.js":
		name, contentType = "web/admin.js", "text/javascript; charset=utf-8"
	default:
		http.NotFound(w, r)
		return
	}
	raw, err := webFiles.ReadFile(name)
	if err != nil {
		http.Error(w, "page unavailable", 500)
		return
	}
	w.Header().Set("Content-Type", contentType)
	w.Header().Set("X-Frame-Options", "DENY")
	w.Header().Set("Referrer-Policy", "no-referrer")
	w.Header().Set("Content-Security-Policy", "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'")
	_, _ = w.Write(raw)
}

func (s *server) download(w http.ResponseWriter, r *http.Request) {
	id := strings.TrimSuffix(strings.TrimPrefix(r.URL.Path, "/api/blueprints/"), "/download")
	if len(id) != 24 || !isHex(id) {
		fail(w, 404, "not found")
		return
	}
	s.mu.Lock()
	ws, _, _, _, _ := s.auth(r)
	if ws == nil {
		s.mu.Unlock()
		fail(w, 401, "unauthorized")
		return
	}
	allowed := false
	for _, b := range ws.Blueprints {
		if b.ID == id && !b.Archived {
			allowed = true
			break
		}
	}
	if !allowed {
		s.mu.Unlock()
		fail(w, 404, "not found")
		return
	}
	raw, err := os.ReadFile(filepath.Join(s.dir, "blobs", id+".json"))
	s.mu.Unlock()
	if err != nil {
		fail(w, 500, "storage error")
		return
	}
	var item publishRequest
	if json.Unmarshal(raw, &item) != nil {
		fail(w, 500, "storage error")
		return
	}
	var output bytes.Buffer
	archive := zip.NewWriter(&output)
	for _, file := range []struct{ name, content string }{{"Blueprint.txt", item.Blueprint}, {"Version.txt", item.Version}} {
		f, err := archive.Create(file.name)
		if err != nil {
			fail(w, 500, "archive error")
			return
		}
		if _, err := f.Write([]byte(file.content)); err != nil {
			fail(w, 500, "archive error")
			return
		}
	}
	if archive.Close() != nil {
		fail(w, 500, "archive error")
		return
	}
	w.Header().Set("Content-Type", "application/zip")
	w.Header().Set("Content-Disposition", "attachment; filename=blueprint-"+id+".zip")
	w.WriteHeader(200)
	_, _ = w.Write(output.Bytes())
}
