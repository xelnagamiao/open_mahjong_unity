package main

import (
	"fmt"
	"github.com/gorilla/websocket"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func waitForChatCleanup(t *testing.T) {
	t.Helper()
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		Pool.mu.RLock()
		remaining := len(Pool.connections)
		Pool.mu.RUnlock()
		roomManager.mu.RLock()
		sessions := len(roomManager.uuidToUsername)
		roomManager.mu.RUnlock()
		if remaining == 0 && sessions == 0 {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatal("chat handlers did not release their sessions")
}

func TestChatLifecycleAcknowledgementsReconnectAndReplacement(t *testing.T) {
	Pool = ConnectionPool{connections: make(map[string]*Client)}
	roomManager = *newTestRoomManager("isolated-lifecycle-secret")
	server := httptest.NewServer(http.HandlerFunc(handleWebSocket))
	var sockets []*websocket.Conn
	t.Cleanup(func() {
		for _, socket := range sockets {
			_ = socket.Close()
		}
		waitForChatCleanup(t)
		server.Close()
	})
	dial := func(id string) *websocket.Conn {
		s := dialTestWebSocket(t, "ws"+strings.TrimPrefix(server.URL, "http")+"/chat/"+id)
		_ = s.SetReadDeadline(time.Now().Add(5 * time.Second))
		sockets = append(sockets, s)
		return s
	}
	send := func(s *websocket.Conn, typ, id string, data any) {
		t.Helper()
		if err := s.WriteJSON(map[string]any{"type": typ, "requestId": id, "data": data}); err != nil {
			t.Fatal(err)
		}
	}
	ack := func(s *websocket.Conn, typ, id string, ok bool) ChatResponse {
		t.Helper()
		responseType := "Tips"
		if !ok {
			responseType = "False"
		}
		r := readResponseType(t, s, responseType)
		if r.RequestID != id || r.RequestType != typ || r.Success != ok || r.ProtocolVersion != 2 {
			t.Fatalf("invalid acknowledgement: %+v", r)
		}
		return r
	}
	first := dial("first")
	send(first, "login", "bad-login", map[string]any{"username": "new-user", "userkey": "invalid"})
	if ack(first, "login", "bad-login", false).Code != "auth_failed" {
		t.Fatal("missing auth rejection code")
	}
	send(first, "sendChat", "anonymous-send", map[string]any{"roomId": 0, "content": "must reject"})
	ack(first, "sendChat", "anonymous-send", false)
	send(first, "login", "valid-login", map[string]any{"username": "new-user", "userkey": testUserKey("new-user", roomManager.secretKey)})
	ack(first, "login", "valid-login", true)
	send(first, "sendChat", "unjoined-send", map[string]any{"roomId": 42, "content": "must reject"})
	ack(first, "sendChat", "unjoined-send", false)
	send(first, "joinRoom", "join", map[string]any{"roomId": 42})
	ack(first, "joinRoom", "join", true)
	send(first, "sendChat", "send-1", map[string]any{"roomId": 42, "content": "room online"})
	readResponseType(t, first, "Chat")
	r := readResponseType(t, first, "sendChatOk")
	if !r.Success || r.RequestID != "send-1" {
		t.Fatal("missing send acknowledgement")
	}
	send(first, "ping", "heartbeat", nil)
	r = readResponseType(t, first, "pong")
	if r.RequestID != "heartbeat" {
		t.Fatal("heartbeat not correlated")
	}
	send(first, "leaveRoom", "leave", map[string]any{"roomId": 42})
	ack(first, "leaveRoom", "leave", true)
	send(first, "sendChat", "after-leave", map[string]any{"roomId": 42, "content": "must reject"})
	ack(first, "sendChat", "after-leave", false)

	current := first
	for i := 0; i < 20; i++ {
		next := dial(fmt.Sprintf("replacement-%d", i))
		loginTestWebSocket(t, next, "new-user", roomManager.secretKey)
		readResponseType(t, current, "login_kickout")
		if _, _, err := current.ReadMessage(); !websocket.IsCloseError(err, websocket.ClosePolicyViolation) {
			t.Fatalf("kick: %v", err)
		}
		readResponseType(t, next, "Tips")
		send(next, "sendChat", "replacement-send", map[string]any{"roomId": 0, "content": "still usable"})
		readResponseType(t, next, "Chat")
		readResponseType(t, next, "sendChatOk")
		current = next
	}
	_ = current.Close()
	resumed := dial("resumed")
	loginTestWebSocket(t, resumed, "new-user", roomManager.secretKey)
	readResponseType(t, resumed, "Tips")
	send(resumed, "joinRoom", "rejoin", map[string]any{"roomId": 42})
	ack(resumed, "joinRoom", "rejoin", true)
	send(resumed, "sendChat", "resumed-send", map[string]any{"roomId": 42, "content": "reconnected"})
	readResponseType(t, resumed, "Chat")
	readResponseType(t, resumed, "sendChatOk")
}
