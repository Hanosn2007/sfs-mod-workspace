package main

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"fmt"
	"net/http"
	"os"
	"regexp"
	"strings"
	"time"
	"unicode/utf8"

	"golang.org/x/crypto/argon2"
)

type account struct {
	ID           string `json:"id"`
	Username     string `json:"username"`
	DisplayName  string `json:"display_name"`
	PasswordHash string `json:"password_hash"`
	Status       string `json:"status"`
}

type membership struct {
	ID          string `json:"id"`
	AccountID   string `json:"account_id"`
	WorkspaceID string `json:"workspace_id"`
	Role        string `json:"role"`
	Status      string `json:"status"`
}

type session struct {
	ID        string    `json:"id"`
	AccountID string    `json:"account_id"`
	TokenHash string    `json:"token_hash"`
	Kind      string    `json:"kind"`
	CreatedAt time.Time `json:"created_at"`
	ExpiresAt time.Time `json:"expires_at"`
	RevokedAt time.Time `json:"revoked_at"`
}

var usernamePattern = regexp.MustCompile(`^[a-z0-9][a-z0-9_-]{2,31}$`)

func bootstrapOwner(dir, accountID string) error {
	s, err := openServer(dir)
	if err != nil {
		return err
	}
	if s.data.SchemaVersion != 2 {
		return errors.New("unsupported state schema")
	}
	if accountID == "" {
		return errors.New("-account is required")
	}
	var next state
	if err := clone(s.data, &next); err != nil {
		return err
	}
	for _, m := range next.Memberships {
		if m.Role == "owner" && m.Status == "active" {
			return errors.New("an active owner already exists")
		}
	}
	for i := range next.Memberships {
		if next.Memberships[i].AccountID == accountID {
			next.Memberships[i].Role = "owner"
			return s.commit(next)
		}
	}
	return errors.New("account not found")
}

func normalizeUsername(value string) string { return strings.ToLower(strings.TrimSpace(value)) }
func validPassword(value string) bool {
	return utf8.ValidString(value) && utf8.RuneCountInString(value) >= 8 && utf8.RuneCountInString(value) <= 72 && len(value) <= 256
}

func passwordHash(password string) (string, error) {
	if !validPassword(password) {
		return "", errors.New("password must be 8-72 characters")
	}
	salt := make([]byte, 16)
	if _, err := rand.Read(salt); err != nil {
		return "", err
	}
	key := argon2.IDKey([]byte(password), salt, 2, 19*1024, 1, 32)
	return fmt.Sprintf("argon2id$v=19$m=19456,t=2,p=1$%s$%s", base64.RawStdEncoding.EncodeToString(salt), base64.RawStdEncoding.EncodeToString(key)), nil
}

func verifyPassword(encoded, password string) bool {
	parts := strings.Split(encoded, "$")
	if len(parts) != 5 || parts[0] != "argon2id" || parts[1] != "v=19" || parts[2] != "m=19456,t=2,p=1" {
		return false
	}
	salt, err := base64.RawStdEncoding.DecodeString(parts[3])
	if err != nil || len(salt) != 16 {
		return false
	}
	want, err := base64.RawStdEncoding.DecodeString(parts[4])
	if err != nil || len(want) != 32 {
		return false
	}
	got := argon2.IDKey([]byte(password), salt, 2, 19*1024, 1, 32)
	return subtle.ConstantTimeCompare(got, want) == 1
}

func (s *server) issueSession(next *state, accountID, kind string) (string, error) {
	token, err := randomHex(32)
	if err != nil {
		return "", err
	}
	id, err := randomHex(12)
	if err != nil {
		return "", err
	}
	expiry := time.Now().UTC().Add(180 * 24 * time.Hour)
	if kind == "web" {
		expiry = time.Now().UTC().Add(30 * 24 * time.Hour)
	}
	next.Sessions = append(next.Sessions, session{ID: id, AccountID: accountID, TokenHash: hash(token), Kind: kind, CreatedAt: time.Now().UTC(), ExpiresAt: expiry})
	return token, nil
}

func (s *server) setWebCookie(w http.ResponseWriter, token string) {
	path := s.basePath
	if path != "/" {
		path += "/"
	}
	http.SetCookie(w, &http.Cookie{Name: "bw_session", Value: token, Path: path, MaxAge: 30 * 24 * 3600,
		HttpOnly: true, Secure: os.Getenv("BW_DEV_HTTP_COOKIE") != "1", SameSite: http.SameSiteStrictMode})
}

func (s *server) clearWebCookie(w http.ResponseWriter) {
	path := s.basePath
	if path != "/" {
		path += "/"
	}
	http.SetCookie(w, &http.Cookie{Name: "bw_session", Value: "", Path: path, MaxAge: -1,
		HttpOnly: true, Secure: os.Getenv("BW_DEV_HTTP_COOKIE") != "1", SameSite: http.SameSiteStrictMode})
}

func (s *server) auth(r *http.Request) (*workspace, *account, *membership, *session, bool) {
	selectedWorkspace := r.Header.Get("X-Workspace-ID")
	if selectedWorkspace != "" && (len(selectedWorkspace) != 24 || !isHex(selectedWorkspace)) {
		return nil, nil, nil, nil, false
	}
	var token string
	cookieUsed := false
	if auth := r.Header.Get("Authorization"); strings.HasPrefix(auth, "Bearer ") {
		token = strings.TrimPrefix(auth, "Bearer ")
	}
	if token == "" {
		if cookie, err := r.Cookie("bw_session"); err == nil {
			token = cookie.Value
			cookieUsed = true
		}
	}
	if len(token) != 64 {
		return nil, nil, nil, nil, false
	}
	needle := hash(token)
	for si := range s.data.Sessions {
		ses := &s.data.Sessions[si]
		if subtle.ConstantTimeCompare([]byte(ses.TokenHash), []byte(needle)) != 1 || !ses.RevokedAt.IsZero() ||
			(!ses.ExpiresAt.IsZero() && time.Now().After(ses.ExpiresAt)) {
			continue
		}
		for ai := range s.data.Accounts {
			acc := &s.data.Accounts[ai]
			if acc.ID != ses.AccountID || acc.Status != "active" {
				continue
			}
			for mi := range s.data.Memberships {
				mem := &s.data.Memberships[mi]
				if mem.AccountID != acc.ID || mem.Status != "active" || (selectedWorkspace != "" && mem.WorkspaceID != selectedWorkspace) {
					continue
				}
				for wi := range s.data.Workspaces {
					ws := &s.data.Workspaces[wi]
					if ws.ID == mem.WorkspaceID {
						return ws, acc, mem, ses, cookieUsed
					}
				}
			}
		}
	}
	return nil, nil, nil, nil, false
}

// Account-wide operations must remain usable when a saved workspace selection
// has become inaccessible (for example after a membership is suspended).
func (s *server) authIgnoringWorkspace(r *http.Request) (*workspace, *account, *membership, *session, bool) {
	copy := r.Clone(r.Context())
	copy.Header.Del("X-Workspace-ID")
	return s.auth(copy)
}

func requireSameOrigin(w http.ResponseWriter, r *http.Request) bool {
	origin := r.Header.Get("Origin")
	if origin == "" {
		fail(w, 403, "browser origin required")
		return false
	}
	if origin != "https://"+r.Host && !(os.Getenv("BW_DEV_HTTP_COOKIE") == "1" && origin == "http://"+r.Host) {
		fail(w, 403, "cross-origin request denied")
		return false
	}
	return true
}
