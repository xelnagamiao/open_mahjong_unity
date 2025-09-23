package main

import (
	"github.com/gorilla/websocket"
	"log"
	"sync"
)

// ConnectionPool 用于管理所有 WebSocket 连接
type ConnectionPool struct {
	connections map[string]*websocket.Conn // 使用 playerId 作为键
	mu          sync.RWMutex               // 读写锁，保证并发安全
}

// Add 注册新的连接 New(playerid:conn)
func (p *ConnectionPool) Add(playerId string, conn *websocket.Conn) {
	p.mu.Lock()
	defer p.mu.Unlock()
	p.connections[playerId] = conn
	log.Printf("Player connected: %s. Total connections: %d", playerId, len(p.connections))
}

// Remove 移除断开的连接 del map(playerid)
func (p *ConnectionPool) Remove(playerId string) {
	p.mu.Lock()
	defer p.mu.Unlock()
	delete(p.connections, playerId)
	log.Printf("Player disconnected: %s. Total connections: %d", playerId, len(p.connections))
}

// Get 获取特定连接 playerid -> conn
func (p *ConnectionPool) Get(playerId string) (*websocket.Conn, bool) {
	p.mu.RLock()
	defer p.mu.RUnlock()
	conn, exists := p.connections[playerId]
	return conn, exists
}
