package main

import (
	"crypto/subtle"
	"net/http"
	"strings"
	"time"
	"unicode/utf8"
)

type workspaceView struct {
	ID   string `json:"id"`
	Name string `json:"name"`
	Role string `json:"role"`
}

func (s *server) listWorkspaces(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	defer s.mu.Unlock()
	defaultWorkspace, acc, _, _, _ := s.authIgnoringWorkspace(r)
	current := defaultWorkspace
	if selected, _, _, _, _ := s.auth(r); selected != nil {
		current = selected
	}
	if current == nil {
		fail(w, http.StatusUnauthorized, "unauthorized")
		return
	}
	views := make([]workspaceView, 0)
	for _, mem := range s.data.Memberships {
		if mem.AccountID != acc.ID || mem.Status != "active" {
			continue
		}
		for _, ws := range s.data.Workspaces {
			if ws.ID == mem.WorkspaceID {
				views = append(views, workspaceView{ID: ws.ID, Name: ws.Name, Role: mem.Role})
				break
			}
		}
	}
	respond(w, http.StatusOK, map[string]interface{}{"workspaces": views, "current_workspace_id": current.ID})
}

func (s *server) createWorkspace(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Name string `json:"name"`
	}
	if !decode(w, r, &req) {
		return
	}
	req.Name = strings.TrimSpace(req.Name)
	if !utf8.ValidString(req.Name) || req.Name == "" || len(req.Name) > 80 {
		fail(w, http.StatusBadRequest, "workspace name must be 1-80 bytes")
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	_, acc, mem, _, cookieUsed := s.auth(r)
	if acc == nil {
		fail(w, http.StatusUnauthorized, "unauthorized")
		return
	}
	if mem.Role != "owner" {
		fail(w, http.StatusForbidden, "owner role required")
		return
	}
	if cookieUsed && !requireSameOrigin(w, r) {
		return
	}
	id, err := randomHex(12)
	if err != nil {
		fail(w, 500, "id generation failed")
		return
	}
	invite, err := randomHex(24)
	if err != nil {
		fail(w, 500, "invite generation failed")
		return
	}
	memberID, err := randomHex(12)
	if err != nil {
		fail(w, 500, "id generation failed")
		return
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		fail(w, 500, "state error")
		return
	}
	next.Workspaces = append(next.Workspaces, workspace{ID: id, Name: req.Name, InviteHash: hash(invite), Blueprints: []blueprint{}})
	next.Memberships = append(next.Memberships, membership{ID: memberID, AccountID: acc.ID, WorkspaceID: id, Role: "owner", Status: "active"})
	next.Audit = append(next.Audit, auditEvent{At: time.Now().UTC(), WorkspaceID: id, ActorID: acc.ID, Action: "create_workspace", Target: id})
	if err := s.commit(next); err != nil {
		fail(w, 500, "storage error")
		return
	}
	respond(w, http.StatusCreated, map[string]string{"id": id, "name": req.Name, "role": "owner", "invite": invite})
}

func (s *server) joinWorkspace(w http.ResponseWriter, r *http.Request) {
	var req struct {
		Invite string `json:"invite"`
	}
	if !decode(w, r, &req) {
		return
	}
	if len(req.Invite) != 48 || !isHex(req.Invite) {
		fail(w, http.StatusBadRequest, "invalid invite code")
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	_, acc, _, _, cookieUsed := s.authIgnoringWorkspace(r)
	if acc == nil {
		fail(w, http.StatusUnauthorized, "unauthorized")
		return
	}
	if cookieUsed && !requireSameOrigin(w, r) {
		return
	}
	var target *workspace
	needle := hash(req.Invite)
	for i := range s.data.Workspaces {
		if subtle.ConstantTimeCompare([]byte(s.data.Workspaces[i].InviteHash), []byte(needle)) == 1 {
			target = &s.data.Workspaces[i]
			break
		}
	}
	if target == nil {
		fail(w, http.StatusUnauthorized, "invalid invite code")
		return
	}
	for _, mem := range s.data.Memberships {
		if mem.AccountID == acc.ID && mem.WorkspaceID == target.ID {
			fail(w, http.StatusConflict, "already a workspace member")
			return
		}
	}
	memberID, err := randomHex(12)
	if err != nil {
		fail(w, 500, "id generation failed")
		return
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		fail(w, 500, "state error")
		return
	}
	next.Memberships = append(next.Memberships, membership{ID: memberID, AccountID: acc.ID, WorkspaceID: target.ID, Role: "member", Status: "active"})
	next.Audit = append(next.Audit, auditEvent{At: time.Now().UTC(), WorkspaceID: target.ID, ActorID: acc.ID, Action: "join_workspace", Target: acc.ID})
	if err := s.commit(next); err != nil {
		fail(w, 500, "storage error")
		return
	}
	respond(w, http.StatusCreated, workspaceView{ID: target.ID, Name: target.Name, Role: "member"})
}
