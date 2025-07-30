package main

import (
	"fmt"
	"github.com/gorilla/websocket"
	"log"
	"net/http"
	"strings"
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
	// 允许所有来源 (生产环境应设置更严格的检查)
	CheckOrigin: func(r *http.Request) bool {
		return true
	},
}

func handleWebSocket(w http.ResponseWriter, r *http.Request) {
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
	Pool.Add(playerId, conn)    // 玩家id:连接服务的指针
	defer Pool.Remove(playerId) // 连接断开时清理掉引用

	log.Printf("WebSocket connection established for player: %s", playerId)

	// 4. 处理来自客户端的消息
	for {
		messageType, message, err := conn.ReadMessage()
		if err != nil {
			if websocket.IsUnexpectedCloseError(err, websocket.CloseGoingAway, websocket.CloseAbnormalClosure) {
				log.Printf("Error reading message from player %s: %v", playerId, err)
			} else {
				log.Printf("Player %s disconnected: %v", playerId, err)
			}
			break // 跳出循环，连接将关闭，defer 会执行清理
		}

		// 在这里处理收到的消息
		// 例如，解析 JSON，处理游戏逻辑等
		log.Printf("Received from player %s (type: %d): %s", playerId, messageType, string(message))

		// 5. 示例：回显消息给发送者 (Echo)
		// 你可以根据需要修改这里，比如广播给其他玩家，处理游戏状态等
		echoMessage := fmt.Sprintf("Echo to %s: %s", playerId, string(message))
		if err := conn.WriteMessage(messageType, []byte(echoMessage)); err != nil {
			log.Printf("Error sending echo to player %s: %v", playerId, err)
			break // 如果发送失败，通常意味着连接有问题，断开
		}

		// 6. 示例：广播消息给所有玩家 (取消注释以启用)
		// broadcastMsg := fmt.Sprintf("Player %s says: %s", playerId, string(message))
		// pool.Broadcast([]byte(broadcastMsg))
	}
}

func main() {

	// 设置路由处理函数
	http.HandleFunc("/chat/", handleWebSocket) // 注意尾部的斜杠，匹配 /chat/ 后面的内容

	port := "8081"
	log.Printf("WebSocket server starting on ws://localhost:%s", port)
	log.Printf("Expecting connections on paths like: ws://localhost:%s/chat/<playerId>", port)

	// 启动 HTTP 服务器
	if err := http.ListenAndServe(":"+port, nil); err != nil {
		log.Fatal("ListenAndServe error:", err)
	}

}
