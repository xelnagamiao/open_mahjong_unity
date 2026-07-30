package main

import (
	"encoding/json"
	"github.com/gorilla/websocket"
	"log"
	"net/http"
	"strings"
)

// 全局连接池实例
var Pool = ConnectionPool{
	connections: make(map[string]*Client),
}

// 房间管理器实例
var roomManager = RoomManager{
	secretKey:      GetSecretKey(),
	uuidToUsername: make(map[string]string),
	usernameToUUID: make(map[string]string),
	usernameRooms:  make(map[string]map[int]struct{}),
	roomUsers:      make(map[int]map[string]struct{}),
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
	client := NewClient(conn)
	if !Pool.Add(playerId, client) {
		log.Printf("Duplicate connection ID rejected: %s", playerId)
		client.Close()
		return
	}
	log.Printf("WebSocket connection established for player: %s", playerId) // 连接建立

	defer func() {
		roomManager.logout(playerId)
		Pool.Remove(playerId, client)
		client.Close()
		log.Printf("WebSocket connection closed for player: %s", playerId)
	}()

	// 4. 处理来自客户端的消息
	authenticated := false
	for {
		_, message, err := client.ReadMessage()
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
		log.Printf("收到消息 type: %s, data: %s", jsonMsg.Type, string(jsonMsg.Data))
		// 处理不同类型的消息
		switch jsonMsg.Type {
		// 登录游戏大厅
		case "login":
			if authenticated {
				if err := client.WriteJSON(ChatResponse{
					ResponseType: "False",
					TargetRoomID: 0,
					Content:      "当前聊天连接已经登录",
				}); err != nil {
					return
				}
				continue
			}
			var loginMsg LoginMsg
			if err := json.Unmarshal(jsonMsg.Data, &loginMsg); err != nil {
				log.Printf("Error parsing login message from player %s: %v", playerId, err)
				continue // 跳过当前消息，继续处理下一条
			}
			// 登录游戏大厅 发送登录大厅结果
			result := roomManager.loginChatHall(playerId, loginMsg.Username, loginMsg.Userkey)
			authenticated = result.Success
			if result.Success && result.ReplacedConnectionID != "" {
				if oldClient, exists := Pool.Get(result.ReplacedConnectionID); exists {
					kickout := ChatResponse{
						ResponseType: "login_kickout",
						TargetRoomID: 0,
						Content:      "您的账号已在其他位置登录，当前聊天连接已断开",
					}
					if err := oldClient.WriteJSON(kickout); err != nil {
						log.Printf("Failed to notify replaced connection %s: %v", result.ReplacedConnectionID, err)
					}
					Pool.Remove(result.ReplacedConnectionID, oldClient)
					oldClient.CloseWithReason(4001, "login_kickout")
				}
			}
			if err := client.WriteJSON(result.Response); err != nil {
				log.Printf("Error sending login message to player %s: %v", playerId, err)
				return
			}

		// 加入聊天房间
		case "joinRoom":
			var joinRoomMsg JoinRoomMsg
			if err := json.Unmarshal(jsonMsg.Data, &joinRoomMsg); err != nil {
				log.Printf("Error parsing join room message from player %s: %v", playerId, err)
				continue
			}
			// 加入聊天房间 发送加入房间结果
			resp := roomManager.joinRoom(playerId, joinRoomMsg.RoomId)
			if err := client.WriteJSON(resp); err != nil {
				log.Printf("Error sending join room success message to player %s: %v", playerId, err)
				return
			}

		// 离开聊天房间
		case "leaveRoom":
			var leaveRoomMsg LeaveRoomMsg
			if err := json.Unmarshal(jsonMsg.Data, &leaveRoomMsg); err != nil {
				log.Printf("Error parsing leave room message from player %s: %v", playerId, err)
				continue
			}
			// 处理离开房间消息 发送离开房间结果
			resp := roomManager.exitRoom(playerId, leaveRoomMsg.RoomId)
			if err := client.WriteJSON(resp); err != nil {
				log.Printf("Error sending leave room success message to player %s: %v", playerId, err)
				return
			}

		// 发送聊天消息
		case "sendChat":
			var sendChatMsg SendChatMsg
			if err := json.Unmarshal(jsonMsg.Data, &sendChatMsg); err != nil {
				log.Printf("Error parsing send chat message from player %s: %v", playerId, err)
				continue
			}
			if roomManager.broadcastChat(playerId, sendChatMsg.Content, sendChatMsg.RoomId, &Pool) {
				// 聊天成功，向客户端发送聊天成功消息
				resp := ChatResponse{ResponseType: "sendChatOk", TargetRoomID: sendChatMsg.RoomId, Content: sendChatMsg.Content} // 发送接受到的聊天信息原句
				if err := client.WriteJSON(resp); err != nil {
					log.Printf("Error sending chat success message to player %s: %v", playerId, err)
					return
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
