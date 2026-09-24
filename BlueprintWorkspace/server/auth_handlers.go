package main

import (
	"crypto/subtle"
	"net/http"
	"strings"
	"time"
)

type credentialsRequest struct {
	Invite      string `json:"invite"`
	Username    string `json:"username"`
	DisplayName string `json:"display_name"`
	Password    string `json:"password"`
	Client      string `json:"client"`
}

func (s *server) register(w http.ResponseWriter, r *http.Request) {
	var req credentialsRequest
	if !decode(w, r, &req) {
		return
	}
	if req.Client != "game" && !requireSameOrigin(w, r) {
		return
	}
	req.Username = normalizeUsername(req.Username)
	req.DisplayName = strings.TrimSpace(req.DisplayName)
	if !usernamePattern.MatchString(req.Username) || len(req.DisplayName) == 0 || len(req.DisplayName) > 60 || !validPassword(req.Password) || len(req.Invite) != 48 {
		fail(w, 400, "invalid username, display name, password, or invite")
		return
	}
	s.mu.Lock()
	validInvite := false
	for _, ws := range s.data.Workspaces {
		if subtle.ConstantTimeCompare([]byte(ws.InviteHash), []byte(hash(req.Invite))) == 1 {
			validInvite = true
			break
		}
	}
	s.mu.Unlock()
	if !validInvite {
		fail(w, 401, "invalid invite code")
		return
	}
	password, err := passwordHash(req.Password)
	if err != nil {
		fail(w, 400, "invalid password")
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	for _, acc := range s.data.Accounts {
		if acc.Username == req.Username {
			fail(w, 409, "username already exists")
			return
		}
	}
	for wi := range s.data.Workspaces {
		if subtle.ConstantTimeCompare([]byte(s.data.Workspaces[wi].InviteHash), []byte(hash(req.Invite))) != 1 {
			continue
		}
		var next state
		if err := clone(s.data, &next); err != nil {
			fail(w, 500, "state error")
			return
		}
		accountID, err := randomHex(12)
		if err != nil {
			fail(w, 500, "id error")
			return
		}
		memberID, err := randomHex(12)
		if err != nil {
			fail(w, 500, "id error")
			return
		}
		next.Accounts = append(next.Accounts, account{ID: accountID, Username: req.Username, DisplayName: req.DisplayName, PasswordHash: password, Status: "active"})
		ws := &next.Workspaces[wi]
		next.Memberships = append(next.Memberships, membership{ID: memberID, AccountID: accountID, WorkspaceID: ws.ID, Role: "member", Status: "active"})
		kind := "web"
		if req.Client == "game" {
			kind = "game"
		}
		token, err := s.issueSession(&next, accountID, kind)
		if err != nil {
			fail(w, 500, "session error")
			return
		}
		if err := s.commit(next); err != nil {
			fail(w, 500, "storage error")
			return
		}
		if kind == "web" {
			s.setWebCookie(w, token)
		}
		response := map[string]string{"workspace_id": ws.ID, "workspace_name": ws.Name, "account_id": accountID, "display_name": req.DisplayName}
		if kind == "game" {
			response["token"] = token
		}
		respond(w, 201, response)
		return
	}
	fail(w, 401, "invalid invite code")
}

func (s *server) login(w http.ResponseWriter, r *http.Request) {
	var req credentialsRequest
	if !decode(w, r, &req) {
		return
	}
	if req.Client != "game" && !requireSameOrigin(w, r) {
		return
	}
	req.Username = normalizeUsername(req.Username)
	if !usernamePattern.MatchString(req.Username) || len(req.Password) > 256 {
		fail(w, 401, "invalid credentials")
		return
	}
	s.mu.Lock()
	var acc account
	for _, candidate := range s.data.Accounts {
		if candidate.Username == req.Username {
			acc = candidate
			break
		}
	}
	s.mu.Unlock()
	if acc.ID == "" || !verifyPassword(acc.PasswordHash, req.Password) {
		fail(w, 401, "invalid credentials")
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	var ws *workspace
	var current *account
	for i := range s.data.Accounts {
		if s.data.Accounts[i].ID == acc.ID {
			current = &s.data.Accounts[i]
			break
		}
	}
	if current == nil || current.Status != "active" {
		fail(w, 403, "account disabled")
		return
	}
	for _, mem := range s.data.Memberships {
		if mem.AccountID != acc.ID || mem.Status != "active" {
			continue
		}
		for i := range s.data.Workspaces {
			if s.data.Workspaces[i].ID == mem.WorkspaceID {
				ws = &s.data.Workspaces[i]
				break
			}
		}
		if ws != nil {
			break
		}
	}
	if ws == nil {
		fail(w, 403, "workspace membership disabled")
		return
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		fail(w, 500, "state error")
		return
	}
	kind := "web"
	if req.Client == "game" {
		kind = "game"
	}
	token, err := s.issueSession(&next, acc.ID, kind)
	if err != nil {
		fail(w, 500, "session error")
		return
	}
	if err := s.commit(next); err != nil {
		fail(w, 500, "storage error")
		return
	}
	if kind == "web" {
		s.setWebCookie(w, token)
	}
	response := map[string]string{"workspace_id": ws.ID, "workspace_name": ws.Name, "account_id": acc.ID, "display_name": current.DisplayName}
	if kind == "game" {
		response["token"] = token
	}
	respond(w, 200, response)
}

func (s *server) me(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	defer s.mu.Unlock()
	ws, acc, mem, ses, _ := s.auth(r)
	if ws == nil {
		fail(w, 401, "unauthorized")
		return
	}
	respond(w, 200, map[string]interface{}{"account_id": acc.ID, "username": acc.Username, "display_name": acc.DisplayName,
		"workspace_id": ws.ID, "workspace_name": ws.Name, "role": mem.Role, "session_kind": ses.Kind})
}

func (s *server) logout(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	defer s.mu.Unlock()
	_, _, _, ses, cookieUsed := s.authIgnoringWorkspace(r)
	if ses == nil {
		fail(w, 401, "unauthorized")
		return
	}
	if cookieUsed && !requireSameOrigin(w, r) {
		return
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		fail(w, 500, "state error")
		return
	}
	for i := range next.Sessions {
		if next.Sessions[i].ID == ses.ID {
			next.Sessions[i].RevokedAt = time.Now().UTC()
			break
		}
	}
	if err := s.commit(next); err != nil {
		fail(w, 500, "storage error")
		return
	}
	if cookieUsed {
		s.clearWebCookie(w)
	}
	respond(w, 200, map[string]string{"status": "signed_out"})
}
