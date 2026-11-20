package main

import (
	"encoding/json"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	"github.com/gorilla/websocket"
)

func TestWebSocketLoginReplacementAndSingleDelivery(t *testing.T) {
	Pool = ConnectionPool{connections: make(map[string]*Client)}
	roomManager = *newTestRoomManager("integration-secret")

	server := httptest.NewServer(http.HandlerFunc(handleWebSocket))
	t.Cleanup(server.Close)
	wsURL := "ws" + strings.TrimPrefix(server.URL, "http") + "/chat/"

	first := dialTestWebSocket(t, wsURL+"first")
	t.Cleanup(func() { _ = first.Close() })
	loginTestWebSocket(t, first, "salasasa", roomManager.secretKey)
	readResponseType(t, first, "Tips")

	second := dialTestWebSocket(t, wsURL+"second")
	t.Cleanup(func() { _ = second.Close() })
	t.Cleanup(func() { _ = first.Close(); _ = second.Close(); waitForChatCleanup(t) })
	loginTestWebSocket(t, second, "salasasa", roomManager.secretKey)

	readResponseType(t, first, "login_kickout")
	if _, _, err := first.ReadMessage(); err == nil {
		t.Fatal("replaced connection remained open")
	} else if closeError, ok := err.(*websocket.CloseError); !ok || closeError.Code != websocket.ClosePolicyViolation {
		t.Fatalf("replaced connection closed with %v, want policy violation", err)
	}
	readResponseType(t, second, "Tips")

	if err := second.WriteJSON(map[string]any{
		"type": "sendChat",
		"data": map[string]any{"content": "hello", "roomId": 0},
	}); err != nil {
		t.Fatalf("send chat: %v", err)
	}
	readResponseType(t, second, "Chat")
	readResponseType(t, second, "sendChatOk")

	if err := second.SetReadDeadline(time.Now().Add(300 * time.Millisecond)); err != nil {
		t.Fatalf("set read deadline: %v", err)
	}
	if _, _, err := second.ReadMessage(); err == nil {
		t.Fatal("new connection received an unexpected duplicate message")
	} else if netError, ok := err.(net.Error); !ok || !netError.Timeout() {
		t.Fatalf("checking duplicate delivery returned unexpected error: %v", err)
	}
}

func TestLiveAccountRestrictions(t *testing.T) {
	Pool = ConnectionPool{connections: make(map[string]*Client)}
	roomManager = *newTestRoomManager("ban-test-secret")
	var ban atomic.Value
	ban.Store("login")
	roomManager.lookupAccount = func(name string, uid int64) (accountAccess, error) {
		if (uid == 0 && name != "speaker") || (uid != 0 && uid != 42) {
			t.Errorf("unexpected chat principal name=%q uid=%d", name, uid)
		}
		kind := ban.Load().(string)
		return accountAccess{UserID: 42, CanLogin: kind != "login" && kind != "full", CanSend: kind == "" || kind == "match", ProfileImageID: 2302}, nil
	}
	server := httptest.NewServer(http.HandlerFunc(handleWebSocket))
	defer server.Close()
	conn := dialTestWebSocket(t, "ws"+strings.TrimPrefix(server.URL, "http")+"/chat/ban-test")
	defer func() { conn.Close(); waitForChatCleanup(t) }()
	conn.SetReadDeadline(time.Now().Add(5 * time.Second))
	loginTestWebSocket(t, conn, "speaker", roomManager.secretKey)
	if readResponseType(t, conn, "False").Code != "account_banned" {
		t.Fatal("login ban was not enforced")
	}
	ban.Store("chat")
	loginTestWebSocket(t, conn, "speaker", roomManager.secretKey)
	readResponseType(t, conn, "Tips")
	for _, kind := range []string{"chat", "match", "", "full"} {
		ban.Store(kind)
		if err := conn.WriteJSON(map[string]any{"type": "sendChat", "requestId": "msg", "data": map[string]any{"roomId": 0, "content": "test"}}); err != nil {
			t.Fatal(err)
		}
		if kind == "chat" || kind == "full" {
			if response := readResponseType(t, conn, "False"); response.Success || response.RequestID != "msg" {
				t.Fatal("expected a correlated refusal")
			}
		} else {
			message := readResponseType(t, conn, "Chat")
			if message.SenderUserID != 42 || message.ProfileImageID != 2302 || message.Content != "speaker: test" {
				t.Fatalf("incorrect sender or content in broadcast: %+v", message)
			}
			readResponseType(t, conn, "sendChatOk")
		}
	}
}

func dialTestWebSocket(t *testing.T, url string) *websocket.Conn {
	t.Helper()
	conn, _, err := websocket.DefaultDialer.Dial(url, nil)
	if err != nil {
		t.Fatalf("dial %s: %v", url, err)
	}
	return conn
}

func loginTestWebSocket(t *testing.T, conn *websocket.Conn, username string, secret string) {
	t.Helper()
	if err := conn.WriteJSON(map[string]any{
		"type": "login",
		"data": map[string]any{
			"username": username,
			"userkey":  testUserKey(username, secret),
		},
	}); err != nil {
		t.Fatalf("login: %v", err)
	}
}

func readResponseType(t *testing.T, conn *websocket.Conn, expected string) ChatResponse {
	t.Helper()
	_, payload, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("read %s response: %v", expected, err)
	}
	var response ChatResponse
	if err := json.Unmarshal(payload, &response); err != nil {
		t.Fatalf("decode response %q: %v", payload, err)
	}
	if response.ResponseType != expected {
		t.Fatalf("response type = %q, want %q; payload=%s", response.ResponseType, expected, payload)
	}
	return response
}
