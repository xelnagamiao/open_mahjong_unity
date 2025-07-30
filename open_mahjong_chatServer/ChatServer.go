package main

import (
	"bufio"
	"fmt"
	// "github.com/gorilla/websocket"
	"os"
)

type ChatManager struct {
	secretKey  string            // 密钥
	usersMap   map[string]string // 用户ID与用户名的映射
	userToRoom map[string][]int  // 通过用户ID获取所在房间的列表
	roomToUser map[int][]string  // 通过房间ID获取用户列表
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

func main() {
}
