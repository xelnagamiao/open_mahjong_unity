package main

import (
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"strings"

	"github.com/gorilla/websocket"
)

// 全局连接池实例
var Pool = ConnectionPool{
	connections: make(map[string]*websocket.Conn),
}

// 房间管理器实例
var roomManager = RoomManager{
	secretKey:      GetSecretKey(),
	uuidToUsername: make(map[string]string),
	usernameTouuid: make(map[string]string),
	usernameToRoom: make(map[string][]int),
	roomToUsername: make(map[int][]string),
}

// 定义 WebSocket 连接的升级器
var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin: func(r *http.Request) bool {
		return true
	},
	// 显式设置 Subprotocols，允许客户端使用任意子协议
	Subprotocols: []string{}, // 空切片表示“接受任何子协议名，不校验”
	// 或者你可以指定：[]string{"chat", "json", "v1"}
}

func handleWebSocket(w http.ResponseWriter, r *http.Request) {
	log.Printf(" 收到 WebSocket 请求: %s", r.URL.Path)
	// URL 格式: /chat/{playerId} 提取 playerId
	pathParts := strings.Split(r.URL.Path, "/")
	if len(pathParts) < 3 || pathParts[1] != "chat" { // 路径格式错误正确
		http.Error(w, "Invalid path", http.StatusBadRequest)
		return
	}
	playerId := pathParts[2]
	if playerId == "" { // 玩家 ID 不能为空
		http.Error(w, "Player ID is required", http.StatusBadRequest)
		return
	}

	// 升级 HTTP 连接到 WebSocket
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil { // 连接升级失败
		log.Printf("Failed to upgrade connection for player %s: %v", playerId, err)
		return
	}
	defer conn.Close()

	// 3. 将新连接添加到连接池
	Pool.Add(playerId, conn)           // 玩家id:连接服务的指针
	defer Pool.Remove(playerId)        // 连接断开时清理掉引用
	defer roomManager.logout(playerId) // 玩家断开连接时退出所有房间

	log.Printf("WebSocket connection established for player: %s", playerId)

	// 4. 处理来自客户端的消息
	for {
		_, message, err := conn.ReadMessage()
		if err != nil {
			if websocket.IsUnexpectedCloseError(err, websocket.CloseGoingAway, websocket.CloseAbnormalClosure) {
				log.Printf("Error reading message from player %s: %v", playerId, err)
			} else {
				log.Printf("Player %s disconnected: %v", playerId, err)
			}
			break // 跳出循环，连接将关闭，defer 会执行清理
		}

		var jsonMsg struct {
			Type string          `json:"type"`
			Data json.RawMessage `json:"data"`
		}

		type LoginMsg struct {
			Username string `json:"username"`
			Userkey  string `json:"userkey"`
		}

		type JoinRoomMsg struct {
			RoomId int `json:"roomId"`
		}

		type LeaveRoomMsg struct {
			RoomId int `json:"roomId"`
		}

		type SendChatMsg struct {
			Content string `json:"content"`
			RoomId  int    `json:"roomId"`
		}

		// 解析 JSON 消息
		if err := json.Unmarshal(message, &jsonMsg); err != nil {
			log.Printf("Error parsing JSON message from player %s: %v", playerId, err)
			continue // 跳过当前消息，继续处理下一条
		}

		// 处理不同类型的消息
		switch jsonMsg.Type {
		case "login":
			var loginMsg LoginMsg
			if err := json.Unmarshal(jsonMsg.Data, &loginMsg); err != nil {
				log.Printf("Error parsing login message from player %s: %v", playerId, err)
				continue // 跳过当前消息，继续处理下一条
			}
			// 登录游戏大厅
			if roomManager.loginChatHall(playerId, loginMsg.Username, loginMsg.Userkey) {
				// 登录成功，向客户端发送登录成功消息
				loginSuccessMsg := fmt.Sprintf("Login success for player %s", playerId)
				if err := conn.WriteMessage(websocket.TextMessage, []byte(loginSuccessMsg)); err != nil {
					log.Printf("Error sending login success message to player %s: %v", playerId, err)
					break // 如果发送失败，通常意味着连接有问题，断开
				}
			}
		case "joinRoom":
			var joinRoomMsg JoinRoomMsg
			if err := json.Unmarshal(jsonMsg.Data, &joinRoomMsg); err != nil {
				log.Printf("Error parsing join room message from player %s: %v", playerId, err)
				continue // 跳过当前消息，继续处理下一条
			}
			// 加入聊天房间
			if roomManager.joinRoom(playerId, joinRoomMsg.RoomId) {
				// 加入房间成功，向客户端发送加入房间成功消息
				joinRoomSuccessMsg := fmt.Sprintf("Join room success for player %s", playerId)
				if err := conn.WriteMessage(websocket.TextMessage, []byte(joinRoomSuccessMsg)); err != nil {
					log.Printf("Error sending join room success message to player %s: %v", playerId, err)
					break // 如果发送失败，通常意味着连接有问题，断开
				}
			}
		case "leaveRoom":
			var leaveRoomMsg LeaveRoomMsg
			if err := json.Unmarshal(jsonMsg.Data, &leaveRoomMsg); err != nil {
				log.Printf("Error parsing leave room message from player %s: %v", playerId, err)
				continue // 跳过当前消息，继续处理下一条
			}
			// 处理离开房间消息
			if roomManager.exitRoom(playerId, leaveRoomMsg.RoomId) {
				// 离开房间成功，向客户端发送离开房间成功消息
				leaveRoomSuccessMsg := fmt.Sprintf("Leave room success for player %s", playerId)
				if err := conn.WriteMessage(websocket.TextMessage, []byte(leaveRoomSuccessMsg)); err != nil {
					log.Printf("Error sending leave room success message to player %s: %v", playerId, err)
					break // 如果发送失败，通常意味着连接有问题，断开
				}
			}
		case "sendChat":
			var sendChatMsg SendChatMsg
			if err := json.Unmarshal(jsonMsg.Data, &sendChatMsg); err != nil {
				log.Printf("Error parsing send chat message from player %s: %v", playerId, err)
				continue // 跳过当前消息，继续处理下一条
			}
			// 处理聊天消息
			if roomManager.broadcastChat(playerId, sendChatMsg.Content, sendChatMsg.RoomId, &Pool) {
				// 聊天成功，向客户端发送聊天成功消息
				chatSuccessMsg := fmt.Sprintf("Chat success for player %s", playerId)
				if err := conn.WriteMessage(websocket.TextMessage, []byte(chatSuccessMsg)); err != nil {
					log.Printf("Error sending chat success message to player %s: %v", playerId, err)
					break // 如果发送失败，通常意味着连接有问题，断开
				}
			}
		default:
			log.Printf("Unknown message type from player %s: %s", playerId, jsonMsg.Type)
			continue // 跳过当前消息，继续处理下一条
		}
	}
}

func main() {

	// 设置路由处理函数
	http.HandleFunc("/chat/", handleWebSocket) // 匹配所有 /chat/ 开头的路径

	port := "8083"
	log.Printf("WebSocket server starting on ws://localhost:%s", port)
	log.Printf("Expecting connections on paths like: ws://localhost:%s/chat/<playerId>", port)

	// 启动 HTTP 服务器
	if err := http.ListenAndServe(":"+port, nil); err != nil {
		log.Fatal("ListenAndServe error:", err)
	}

}
