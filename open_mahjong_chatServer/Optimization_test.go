package main

import (
	"bytes"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/gorilla/websocket"
)

func TestAccountAccessCacheTTLAndSingleflight(t *testing.T) {
	var calls atomic.Int32
	gate := make(chan struct{})
	cache := newAccountAccessCache(func(name string, uid int64) (accountAccess, error) {
		n := calls.Add(1)
		if n == 2 {
			<-gate
		}
		return accountAccess{UserID: 77, CanLogin: true, CanSend: true, ProfileImageID: 5}, nil
	}, 200*time.Millisecond)

	// Login (uid=0) populates cache for uid=77.
	acc, err := cache.Lookup("alice", 0)
	if err != nil || acc.UserID != 77 {
		t.Fatalf("login lookup failed: %+v %v", acc, err)
	}
	if calls.Load() != 1 {
		t.Fatalf("expected 1 upstream call, got %d", calls.Load())
	}

	// Subsequent uid lookups hit cache without calling upstream.
	for i := 0; i < 5; i++ {
		if _, err := cache.Lookup("", 77); err != nil {
			t.Fatalf("cached lookup failed: %v", err)
		}
	}
	if calls.Load() != 1 {
		t.Fatalf("cache miss on fresh entry: upstream calls=%d", calls.Load())
	}

	// Invalidate forces a singleflight upstream call across concurrent readers.
	cache.Invalidate(77)
	var wg sync.WaitGroup
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if _, err := cache.Lookup("", 77); err != nil {
				t.Errorf("concurrent lookup failed: %v", err)
			}
		}()
	}
	time.Sleep(20 * time.Millisecond)
	close(gate)
	wg.Wait()
	if calls.Load() != 2 {
		t.Fatalf("expected singleflight to coalesce 8 concurrent misses into 1 call (total 2), got %d", calls.Load())
	}
}

func TestInternalRevokeInvalidatesCacheAndKicksActiveSession(t *testing.T) {
	Pool = ConnectionPool{connections: make(map[string]*Client)}
	roomManager = *newTestRoomManager("revoke-secret")
	var banned atomic.Bool
	cache := newAccountAccessCache(func(name string, uid int64) (accountAccess, error) {
		return accountAccess{UserID: 99, CanLogin: !banned.Load(), CanSend: !banned.Load()}, nil
	}, time.Minute)
	roomManager.accessCache = cache
	roomManager.lookupAccount = cache.Lookup

	mux := http.NewServeMux()
	mux.HandleFunc("/chat/", handleWebSocket)
	mux.HandleFunc("/internal/chat/revoke", handleInternalRevoke)
	server := httptest.NewServer(mux)
	defer server.Close()

	conn := dialTestWebSocket(t, "ws"+strings.TrimPrefix(server.URL, "http")+"/chat/revoke-conn")
	defer func() { _ = conn.Close(); waitForChatCleanup(t) }()
	_ = conn.SetReadDeadline(time.Now().Add(5 * time.Second))
	loginTestWebSocket(t, conn, "bob", roomManager.secretKey)
	readResponseType(t, conn, "Tips")

	// Unauthorized revoke must be rejected.
	reqBad, _ := http.NewRequest(http.MethodPost, server.URL+"/internal/chat/revoke", bytes.NewBufferString(`{"user_id":99,"kick":true}`))
	respBad, err := http.DefaultClient.Do(reqBad)
	if err != nil {
		t.Fatal(err)
	}
	_ = respBad.Body.Close()
	if respBad.StatusCode != http.StatusForbidden {
		t.Fatalf("expected 403 without secret, got %d", respBad.StatusCode)
	}

	// Authorized revoke invalidates cache and kicks the socket immediately.
	banned.Store(true)
	body, _ := json.Marshal(map[string]any{"user_id": 99, "kick": true, "reason": "account_banned"})
	req, _ := http.NewRequest(http.MethodPost, server.URL+"/internal/chat/revoke", bytes.NewReader(body))
	req.Header.Set("X-Chat-Secret", "revoke-secret")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("expected 200 on revoke, got %d", resp.StatusCode)
	}

	if _, _, err := conn.ReadMessage(); !websocket.IsCloseError(err, websocket.ClosePolicyViolation) {
		t.Fatalf("expected close policy violation after revoke, got %v", err)
	}
}

func TestTrySendPreparedDropsFullSlowClientWithoutBlocking(t *testing.T) {
	slow := &Client{
		sendCh:    make(chan *websocket.PreparedMessage, 1),
		done:      make(chan struct{}),
		writeDone: make(chan struct{}),
	}
	pm, err := websocket.NewPreparedMessage(websocket.TextMessage, []byte(`{"responseType":"Chat"}`))
	if err != nil {
		t.Fatal(err)
	}
	if !slow.TrySendPrepared(pm) {
		t.Fatal("first enqueue should succeed")
	}
	start := time.Now()
	if slow.TrySendPrepared(pm) {
		t.Fatal("enqueue on full slow client should return false")
	}
	if time.Since(start) > 100*time.Millisecond {
		t.Fatalf("TrySendPrepared blocked for %s", time.Since(start))
	}
	select {
	case <-slow.done:
	default:
		t.Fatal("slow client should be closed when send queue overflows")
	}
}
