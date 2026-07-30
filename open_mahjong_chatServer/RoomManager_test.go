package main

import (
	"crypto/sha256"
	"encoding/hex"
	"sync"
	"testing"
)

func newTestRoomManager(secret string) *RoomManager {
	return &RoomManager{
		secretKey:      secret,
		uuidToUsername: make(map[string]string),
		usernameToUUID: make(map[string]string),
		usernameRooms:  make(map[string]map[int]struct{}),
		roomUsers:      make(map[int]map[string]struct{}),
	}
}

func testUserKey(username string, secret string) string {
	hash := sha256.Sum256([]byte(username + secret))
	return hex.EncodeToString(hash[:])
}

func TestLoginReplacementIsUniqueAndStaleLogoutIsSafe(t *testing.T) {
	const (
		secret   = "test-secret"
		username = "salasasa"
		firstID  = "connection-1"
		secondID = "connection-2"
	)
	rm := newTestRoomManager(secret)
	key := testUserKey(username, secret)

	first := rm.loginChatHall(firstID, username, key)
	if !first.Success || first.ReplacedConnectionID != "" {
		t.Fatalf("unexpected first login result: %+v", first)
	}
	rm.joinRoom(firstID, 42)
	rm.joinRoom(firstID, 42)
	if got := len(rm.roomUsers[42]); got != 1 {
		t.Fatalf("duplicate join created %d room members, want 1", got)
	}

	second := rm.loginChatHall(secondID, username, key)
	if !second.Success || second.ReplacedConnectionID != firstID {
		t.Fatalf("unexpected replacement result: %+v", second)
	}
	if got := rm.usernameToUUID[username]; got != secondID {
		t.Fatalf("active session = %q, want %q", got, secondID)
	}
	if _, exists := rm.uuidToUsername[firstID]; exists {
		t.Fatal("replaced connection remains authenticated")
	}
	if got := len(rm.roomUsers[0]); got != 1 {
		t.Fatalf("lobby has %d members, want 1", got)
	}
	if _, exists := rm.roomUsers[42]; exists {
		t.Fatal("rooms from the replaced session were not cleared")
	}

	if rm.logout(firstID) {
		t.Fatal("stale logout unexpectedly removed a session")
	}
	if got := rm.usernameToUUID[username]; got != secondID {
		t.Fatalf("stale logout removed active session; got %q", got)
	}

	sender, recipients, ok := rm.chatRecipients(secondID, 0)
	if !ok || sender != username {
		t.Fatalf("failed to resolve active sender: sender=%q ok=%v", sender, ok)
	}
	if len(recipients) != 1 || recipients[0] != secondID {
		t.Fatalf("recipients = %v, want [%s]", recipients, secondID)
	}
}

func TestRepeatedLoginAndJoinAreIdempotent(t *testing.T) {
	const (
		secret   = "test-secret"
		username = "salasasa"
		userID   = "connection-1"
	)
	rm := newTestRoomManager(secret)
	key := testUserKey(username, secret)

	rm.loginChatHall(userID, username, key)
	repeated := rm.loginChatHall(userID, username, key)
	if !repeated.Success || repeated.ReplacedConnectionID != "" {
		t.Fatalf("unexpected repeated login result: %+v", repeated)
	}
	rm.joinRoom(userID, 7)
	rm.joinRoom(userID, 7)

	if got := len(rm.usernameRooms[username]); got != 2 {
		t.Fatalf("user belongs to %d rooms, want lobby plus room 7", got)
	}
	if got := len(rm.roomUsers[7]); got != 1 {
		t.Fatalf("room has %d members, want 1", got)
	}
}

func TestConcurrentReplacementAndMembershipAccess(t *testing.T) {
	const (
		secret   = "test-secret"
		username = "salasasa"
	)
	rm := newTestRoomManager(secret)
	key := testUserKey(username, secret)

	var wg sync.WaitGroup
	for index := 0; index < 20; index++ {
		wg.Add(1)
		go func(id string) {
			defer wg.Done()
			rm.loginChatHall(id, username, key)
			rm.joinRoom(id, 9)
			rm.chatRecipients(id, 0)
			rm.logout(id)
		}(string(rune('a' + index)))
	}
	wg.Wait()
}
