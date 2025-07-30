package main

import (
	"bufio"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"github.com/gorilla/websocket"
	"log"
	"os"
	"sync"
)

// 聊天管理器
type RoomManager struct {
	secretKey      string            // 密钥
	mu             sync.RWMutex      // 读写锁
	uuidToUsername map[string]string // 用户ID与用户名的映射
	usernameTouuid map[string]string // 用户名与用户ID的映射
	usernameToRoom map[string][]int  // 通过用户ID获取所在房间列表
	roomToUsername map[int][]string  // 通过房间ID获取用户列表
}

func GetSecretKey() string {
	keyfile, _ := os.Open("secret_key.txt") // 读取密钥文件
	defer keyfile.Close()                   // 完成函数后关闭
	scanner := bufio.NewScanner(keyfile)    // 打开文件读取器
	scanner.Scan()                          // 读取第一行
	secretKey := scanner.Text()             // 保存密钥
	fmt.Println("secret key:", secretKey)
	return secretKey
}

// 不启用的构造方法
func (rm *RoomManager) init() {
	rm.roomToUsername[0] = []string{} // 初始化房间0的用户列表为空
}

// 登录聊天服务器方法(自动登录聊天大厅)
func (rm *RoomManager) loginChatHall(username string, userKey, uuid string) bool {

	input := username + rm.secretKey           // 拼接用户名和盐值
	hasher := sha256.New()                     // 创建哈希器
	hasher.Write([]byte(input))                // 将拼接后的字符串转换为字节切片并写入哈希器
	hashBytes := hasher.Sum(nil)               // 获取哈希结果的字节切片
	hexString := hex.EncodeToString(hashBytes) // 将哈希结果转换为16进制字符串

	if hexString == userKey { // 验证用户密钥是否正确
		rm.mu.Lock()                       // 加读写锁
		defer rm.mu.Unlock()               // 解锁
		rm.uuidToUsername[uuid] = username // 保存用户ID和用户名的映射
		rm.usernameTouuid[username] = uuid // 保存用户名和用户ID的映射
		// 保存用户所在房间0(0 = 聊天大厅)
		rm.usernameToRoom[username] = append(rm.usernameToRoom[username], 0)
		// 保存房间0的用户列表
		rm.roomToUsername[0] = append(rm.roomToUsername[0], username)
		return true // 登录成功
	}
	return false // 登录失败
}

// 登录后加入特定房间方法(加入游戏房间或者聊天频道)
func (rm *RoomManager) joinRoom(uuid string, roomID int) bool {
	rm.mu.Lock()         // 加读写锁
	defer rm.mu.Unlock() // 解锁

	if username, ok := rm.uuidToUsername[uuid]; ok { // 用户已登录
		// 将房间加入用户的房间列表
		rm.usernameToRoom[username] = append(rm.usernameToRoom[username], roomID)
		// 将用户加入房间的用户列表
		rm.roomToUsername[roomID] = append(rm.roomToUsername[roomID], username)
		return true
	}
	return false // 用户未登录
}

// 退出房间方法
func (rm *RoomManager) exitRoom(uuid string, roomID int) bool {
	rm.mu.Lock()         // 加读写锁
	defer rm.mu.Unlock() // 解锁

	if username, ok := rm.uuidToUsername[uuid]; ok { // 用户已登录
		// 退出房间
		rm.usernameToRoom[username] = rm.removeIntFromSlice(rm.usernameToRoom[username], roomID)
		// 退出房间的用户列表
		rm.roomToUsername[roomID] = rm.removeStringFromSlice(rm.roomToUsername[roomID], username)
		return true
	}
	return false // 用户未登录
}

// 注销用户方法
func (rm *RoomManager) logout(uuid string) bool {
	rm.mu.Lock()         // 加读写锁
	defer rm.mu.Unlock() // 解锁

	if username, ok := rm.uuidToUsername[uuid]; ok { // 用户已登录
		needToExitRooms := rm.usernameToRoom[username] // 需要退出的房间列表
		for _, roomID := range needToExitRooms {       // 把所在房间的username删除
			rm.roomToUsername[roomID] = rm.removeStringFromSlice(rm.roomToUsername[roomID], username)
		}
		// 删除用户的房间列表
		delete(rm.usernameToRoom, username)
		// 删除用户的用户名和用户ID的映射
		delete(rm.usernameTouuid, username)
		// 删除用户的用户ID和用户名的映射
		delete(rm.uuidToUsername, uuid)
		return true
	}
	return false // 用户未登录
}

// 广播消息方法
func (rm *RoomManager) broadcast(uuid string, message string, targetRoom int, Pool *ConnectionPool) {
	// 加只读锁
	var recipients []*websocket.Conn                 // 用于存储有效的连接指针
	if username, ok := rm.uuidToUsername[uuid]; ok { // 用户已登录
		rm.mu.RLock()
		for _, user := range rm.roomToUsername[targetRoom] { // 遍历房间内的用户
			if user != username { // 排除自己
				if conn, exists := Pool.Get(rm.usernameTouuid[user]); exists {
					// 连接存在就复制指针
					recipients = append(recipients, conn)
				}
			}
		}
		rm.mu.RUnlock() // 解锁
		// 遍历 recipients[]conn切片发送消息
		for _, conn := range recipients {
			// 检查 WriteMessage 是否出错
			if err := conn.WriteMessage(websocket.TextMessage, []byte(username+": "+message)); err != nil {
				log.Printf("Error sending message to connection: %v", err)
			}
		}
	}
}

// 从 []int 切片中移除指定的整数
func (rm *RoomManager) removeIntFromSlice(slice []int, item int) []int {
	for i, v := range slice {
		if v == item {
			// 找到元素，使用切片操作将其移除
			// 将 i 之后的元素向前移动一位，覆盖掉 i 位置的元素
			// 然后返回长度减一的新切片
			return append(slice[:i], slice[i+1:]...)
		}
	}
	// 如果没有找到要删除的元素，返回原切片
	return slice
}

// 从 []string 切片中移除指定的字符串
func (rm *RoomManager) removeStringFromSlice(slice []string, item string) []string {
	for i, v := range slice {
		if v == item {
			return append(slice[:i], slice[i+1:]...)
		}
	}
	return slice
}
