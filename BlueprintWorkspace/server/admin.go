package main

import (
	"net/http"
	"strings"
	"time"
)

type auditEvent struct {
	At          time.Time `json:"at"`
	WorkspaceID string    `json:"workspace_id,omitempty"`
	ActorID     string    `json:"actor_id"`
	Action      string    `json:"action"`
	Target      string    `json:"target"`
}

type adminMember struct {
	AccountID      string `json:"account_id"`
	Username       string `json:"username"`
	DisplayName    string `json:"display_name"`
	Role           string `json:"role"`
	Status         string `json:"status"`
	ActiveSessions int    `json:"active_sessions"`
}

func (s *server) admin(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	defer s.mu.Unlock()
	ws, actor, actorMember, _, cookieUsed := s.auth(r)
	if ws == nil {
		fail(w, 401, "unauthorized")
		return
	}
	if actorMember.Role != "owner" && actorMember.Role != "admin" {
		fail(w, 403, "admin role required")
		return
	}
	if r.Method == http.MethodPost && cookieUsed && !requireSameOrigin(w, r) {
		return
	}
	path := strings.TrimPrefix(r.URL.Path, "/api/admin/")
	switch {
	case path == "overview" && r.Method == http.MethodGet:
		s.adminOverview(w, ws)
	case path == "invite/rotate" && r.Method == http.MethodPost:
		s.rotateInvite(w, ws, actor)
	case strings.HasPrefix(path, "members/") && r.Method == http.MethodPost:
		s.changeMember(w, ws, actor, actorMember, strings.TrimPrefix(path, "members/"), r)
	case strings.HasPrefix(path, "blueprints/") && r.Method == http.MethodPost:
		s.changeBlueprint(w, ws, actor, strings.TrimPrefix(path, "blueprints/"))
	default:
		http.NotFound(w, r)
	}
}

func (s *server) adminOverview(w http.ResponseWriter, ws *workspace) {
	members := make([]adminMember, 0)
	for _, mem := range s.data.Memberships {
		if mem.WorkspaceID != ws.ID {
			continue
		}
		for _, acc := range s.data.Accounts {
			if acc.ID != mem.AccountID {
				continue
			}
			view := adminMember{AccountID: acc.ID, Username: acc.Username, DisplayName: acc.DisplayName,
				Role: mem.Role, Status: mem.Status}
			for _, ses := range s.data.Sessions {
				if ses.AccountID == acc.ID && ses.RevokedAt.IsZero() && (ses.ExpiresAt.IsZero() || time.Now().Before(ses.ExpiresAt)) {
					view.ActiveSessions++
				}
			}
			members = append(members, view)
			break
		}
	}
	audit := make([]auditEvent, 0, 30)
	for _, event := range s.data.Audit {
		if event.WorkspaceID == ws.ID || (event.WorkspaceID == "" && ws.ID == s.data.Workspaces[0].ID) {
			audit = append(audit, event)
		}
	}
	if len(audit) > 30 {
		audit = audit[len(audit)-30:]
	}
	respond(w, 200, map[string]interface{}{"workspace_name": ws.Name, "members": members,
		"blueprints": ws.Blueprints, "audit": audit})
}

func (s *server) rotateInvite(w http.ResponseWriter, ws *workspace, actor *account) {
	code, err := randomHex(24)
	if err != nil {
		fail(w, 500, "code error")
		return
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		fail(w, 500, "state error")
		return
	}
	for i := range next.Workspaces {
		if next.Workspaces[i].ID == ws.ID {
			next.Workspaces[i].InviteHash = hash(code)
			break
		}
	}
	next.Audit = append(next.Audit, auditEvent{At: time.Now().UTC(), WorkspaceID: ws.ID, ActorID: actor.ID, Action: "rotate_invite", Target: ws.ID})
	if err := s.commit(next); err != nil {
		fail(w, 500, "storage error")
		return
	}
	respond(w, 200, map[string]string{"invite": code})
}

func (s *server) changeMember(w http.ResponseWriter, ws *workspace, actor *account, actorMember *membership, path string, r *http.Request) {
	parts := strings.Split(path, "/")
	if len(parts) != 2 || len(parts[0]) != 24 || !isHex(parts[0]) {
		fail(w, 404, "member not found")
		return
	}
	targetID, action := parts[0], parts[1]
	if action != "suspend" && action != "restore" && action != "role" {
		fail(w, 404, "action not found")
		return
	}
	if targetID == actor.ID && action != "restore" {
		fail(w, 403, "cannot change your own membership")
		return
	}
	var requestedRole string
	if action == "role" {
		if actorMember.Role != "owner" {
			fail(w, 403, "owner role required")
			return
		}
		var req struct {
			Role string `json:"role"`
		}
		if !decode(w, r, &req) {
			return
		}
		requestedRole = req.Role
		if requestedRole != "member" && requestedRole != "admin" {
			fail(w, 400, "invalid role")
			return
		}
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		fail(w, 500, "state error")
		return
	}
	for i := range next.Memberships {
		m := &next.Memberships[i]
		if m.AccountID != targetID || m.WorkspaceID != ws.ID {
			continue
		}
		if m.Role == "owner" {
			fail(w, 403, "owner membership is protected")
			return
		}
		if m.Role == "admin" && actorMember.Role != "owner" {
			fail(w, 403, "owner role required for admins")
			return
		}
		switch action {
		case "suspend":
			m.Status = "suspended"
			otherActiveMembership := false
			for _, other := range next.Memberships {
				if other.AccountID == targetID && other.WorkspaceID != ws.ID && other.Status == "active" {
					otherActiveMembership = true
					break
				}
			}
			if !otherActiveMembership {
				for si := range next.Sessions {
					if next.Sessions[si].AccountID == targetID && next.Sessions[si].RevokedAt.IsZero() {
						next.Sessions[si].RevokedAt = time.Now().UTC()
					}
				}
			}
		case "restore":
			m.Status = "active"
		case "role":
			m.Role = requestedRole
		}
		response := map[string]string{"status": "ok"}
		if action == "suspend" {
			code, err := randomHex(24)
			if err != nil {
				fail(w, 500, "code error")
				return
			}
			for wi := range next.Workspaces {
				if next.Workspaces[wi].ID == ws.ID {
					next.Workspaces[wi].InviteHash = hash(code)
					break
				}
			}
			response["invite"] = code
			next.Audit = append(next.Audit, auditEvent{At: time.Now().UTC(), WorkspaceID: ws.ID, ActorID: actor.ID, Action: "rotate_invite_after_suspend", Target: ws.ID})
		}
		next.Audit = append(next.Audit, auditEvent{At: time.Now().UTC(), WorkspaceID: ws.ID, ActorID: actor.ID, Action: action + "_member", Target: targetID})
		if err := s.commit(next); err != nil {
			fail(w, 500, "storage error")
			return
		}
		respond(w, 200, response)
		return
	}
	fail(w, 404, "member not found")
}

func (s *server) changeBlueprint(w http.ResponseWriter, ws *workspace, actor *account, path string) {
	parts := strings.Split(path, "/")
	if len(parts) != 2 || len(parts[0]) != 24 || !isHex(parts[0]) {
		fail(w, 404, "blueprint not found")
		return
	}
	if parts[1] != "archive" && parts[1] != "restore" {
		fail(w, 404, "action not found")
		return
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		fail(w, 500, "state error")
		return
	}
	for wi := range next.Workspaces {
		if next.Workspaces[wi].ID != ws.ID {
			continue
		}
		for bi := range next.Workspaces[wi].Blueprints {
			b := &next.Workspaces[wi].Blueprints[bi]
			if b.ID != parts[0] {
				continue
			}
			b.Archived = parts[1] == "archive"
			next.Audit = append(next.Audit, auditEvent{At: time.Now().UTC(), WorkspaceID: ws.ID, ActorID: actor.ID, Action: parts[1] + "_blueprint", Target: b.ID})
			if err := s.commit(next); err != nil {
				fail(w, 500, "storage error")
				return
			}
			respond(w, 200, map[string]string{"status": "ok"})
			return
		}
	}
	fail(w, 404, "blueprint not found")
}
