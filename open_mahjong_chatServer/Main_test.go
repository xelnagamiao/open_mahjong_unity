package main

import (
	"encoding/json"
	"net"
	"net/http"
	"net/http/httptest"
	"strings"
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
	loginTestWebSocket(t, second, "salasasa", roomManager.secretKey)

	readResponseType(t, first, "login_kickout")
	if _, _, err := first.ReadMessage(); err == nil {
		t.Fatal("replaced connection remained open")
	} else if closeError, ok := err.(*websocket.CloseError); !ok || closeError.Code != 4001 {
		t.Fatalf("replaced connection closed with %v, want close code 4001", err)
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
