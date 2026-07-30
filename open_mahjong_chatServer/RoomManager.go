package main

import (
	"bufio"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"log"
	"os"
	"strconv"
	"sync"
)

type ChatResponse struct {
	ResponseType string `json:"responseType"`
	TargetRoomID int    `json:"roomId"`
	Content      string `json:"content"`
}

type LoginResult struct {
	Response             ChatResponse
	ReplacedConnectionID string
	Success              bool
}

// RoomManager owns authenticated-session and room-membership state.
//
// A username has exactly one active connection ID. Maps are used as sets for
// room membership so login/join operations are idempotent.
type RoomManager struct {
	secretKey      string
	mu             sync.RWMutex
	uuidToUsername map[string]string
	usernameToUUID map[string]string
	usernameRooms  map[string]map[int]struct{}
	roomUsers      map[int]map[string]struct{}
}

func GetSecretKey() string {
	keyFile, err := os.Open("secret_key.txt")
	if err != nil {
		log.Fatalf("failed to open secret_key.txt: %v", err)
	}
	defer keyFile.Close()

	scanner := bufio.NewScanner(keyFile)
	if !scanner.Scan() {
		if err := scanner.Err(); err != nil {
			log.Fatalf("failed to read secret_key.txt: %v", err)
		}
		log.Fatal("secret_key.txt is empty")
	}
	secretKey := scanner.Text()
	if secretKey == "" {
		log.Fatal("secret_key.txt is empty")
	}
	return secretKey
}

func (rm *RoomManager) loginChatHall(uuid string, username string, userKey string) LoginResult {
	input := username + rm.secretKey
	hashBytes := sha256.Sum256([]byte(input))
	expectedKey := hex.EncodeToString(hashBytes[:])
	if expectedKey != userKey {
		log.Printf("chat login rejected: username=%q connection_id=%s", username, uuid)
		return LoginResult{
			Response: ChatResponse{
				ResponseType: "False",
				TargetRoomID: 0,
				Content:      "登录聊天大厅失败,用户密钥错误",
			},
		}
	}

	rm.mu.Lock()
	defer rm.mu.Unlock()

	// A connection that authenticates as another username must first release
	// its previous identity.
	if previousUsername, exists := rm.uuidToUsername[uuid]; exists && previousUsername != username {
		rm.removeSessionLocked(uuid, previousUsername)
	}

	replacedUUID := rm.usernameToUUID[username]
	replacedConnectionID := ""
	if replacedUUID != "" && replacedUUID != uuid {
		replacedConnectionID = replacedUUID
		delete(rm.uuidToUsername, replacedUUID)
		rm.removeUsernameRoomsLocked(username)
	}

	rm.uuidToUsername[uuid] = username
	rm.usernameToUUID[username] = uuid
	rm.addMembershipLocked(username, 0)

	log.Printf("chat login succeeded: username=%q connection_id=%s replaced_connection_id=%s", username, uuid, replacedUUID)
	return LoginResult{
		Response: ChatResponse{
			ResponseType: "Tips",
			TargetRoomID: 0,
			Content:      "登录聊天大厅成功",
		},
		ReplacedConnectionID: replacedConnectionID,
		Success:              true,
	}
}

func (rm *RoomManager) joinRoom(uuid string, roomID int) ChatResponse {
	rm.mu.Lock()
	defer rm.mu.Unlock()

	username, ok := rm.currentUsernameLocked(uuid)
	if !ok {
		return ChatResponse{ResponseType: "False", TargetRoomID: roomID, Content: "加入房间失败,用户未登录"}
	}
	rm.addMembershipLocked(username, roomID)
	return ChatResponse{
		ResponseType: "Tips",
		TargetRoomID: roomID,
		Content:      "加入房间" + strconv.Itoa(roomID) + "成功",
	}
}

func (rm *RoomManager) exitRoom(uuid string, roomID int) ChatResponse {
	rm.mu.Lock()
	defer rm.mu.Unlock()

	username, ok := rm.currentUsernameLocked(uuid)
	if !ok {
		return ChatResponse{ResponseType: "False", TargetRoomID: roomID, Content: "退出房间失败,用户未登录"}
	}
	rm.removeMembershipLocked(username, roomID)
	return ChatResponse{
		ResponseType: "Tips",
		TargetRoomID: roomID,
		Content:      "退出房间" + strconv.Itoa(roomID) + "成功",
	}
}

// logout removes state only when uuid still owns the username session. A stale
// handler from a replaced connection must never tear down the new session.
func (rm *RoomManager) logout(uuid string) bool {
	rm.mu.Lock()
	defer rm.mu.Unlock()

	username, exists := rm.uuidToUsername[uuid]
	if !exists {
		return false
	}
	delete(rm.uuidToUsername, uuid)
	if rm.usernameToUUID[username] != uuid {
		return false
	}
	rm.removeSessionLocked(uuid, username)
	return true
}

func (rm *RoomManager) broadcastChat(uuid string, message string, targetRoom int, pool *ConnectionPool) bool {
	username, recipientIDs, ok := rm.chatRecipients(uuid, targetRoom)
	if !ok {
		return false
	}

	response := ChatResponse{
		ResponseType: "Chat",
		TargetRoomID: targetRoom,
		Content:      fmt.Sprintf("%s: %s", username, message),
	}
	delivered := false
	for _, recipientID := range recipientIDs {
		client, exists := pool.Get(recipientID)
		if !exists {
			continue
		}
		if err := client.WriteJSON(response); err != nil {
			log.Printf("failed to send chat message to connection %s: %v", recipientID, err)
			continue
		}
		delivered = true
	}
	return delivered
}

// chatRecipients takes a consistent, unique snapshot without holding the room
// lock during network I/O.
func (rm *RoomManager) chatRecipients(uuid string, targetRoom int) (string, []string, bool) {
	rm.mu.RLock()
	defer rm.mu.RUnlock()

	username, ok := rm.currentUsernameLocked(uuid)
	if !ok {
		return "", nil, false
	}
	if _, member := rm.usernameRooms[username][targetRoom]; !member {
		return "", nil, false
	}

	roomMembers := rm.roomUsers[targetRoom]
	recipients := make([]string, 0, len(roomMembers))
	for memberUsername := range roomMembers {
		if recipientID := rm.usernameToUUID[memberUsername]; recipientID != "" {
			recipients = append(recipients, recipientID)
		}
	}
	return username, recipients, len(recipients) > 0
}

func (rm *RoomManager) currentUsernameLocked(uuid string) (string, bool) {
	username, exists := rm.uuidToUsername[uuid]
	return username, exists && rm.usernameToUUID[username] == uuid
}

func (rm *RoomManager) addMembershipLocked(username string, roomID int) {
	if rm.usernameRooms[username] == nil {
		rm.usernameRooms[username] = make(map[int]struct{})
	}
	if rm.roomUsers[roomID] == nil {
		rm.roomUsers[roomID] = make(map[string]struct{})
	}
	rm.usernameRooms[username][roomID] = struct{}{}
	rm.roomUsers[roomID][username] = struct{}{}
}

func (rm *RoomManager) removeMembershipLocked(username string, roomID int) {
	if rooms := rm.usernameRooms[username]; rooms != nil {
		delete(rooms, roomID)
		if len(rooms) == 0 {
			delete(rm.usernameRooms, username)
		}
	}
	if users := rm.roomUsers[roomID]; users != nil {
		delete(users, username)
		if len(users) == 0 {
			delete(rm.roomUsers, roomID)
		}
	}
}

func (rm *RoomManager) removeUsernameRoomsLocked(username string) {
	for roomID := range rm.usernameRooms[username] {
		if users := rm.roomUsers[roomID]; users != nil {
			delete(users, username)
			if len(users) == 0 {
				delete(rm.roomUsers, roomID)
			}
		}
	}
	delete(rm.usernameRooms, username)
}

func (rm *RoomManager) removeSessionLocked(uuid string, username string) {
	delete(rm.uuidToUsername, uuid)
	if rm.usernameToUUID[username] == uuid {
		delete(rm.usernameToUUID, username)
		rm.removeUsernameRoomsLocked(username)
	}
}
