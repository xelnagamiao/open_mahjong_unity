package main

import (
	"github.com/gorilla/websocket"
	"log"
	"sync"
	"time"
)

// Client wraps a WebSocket connection and serializes all writes. Gorilla
// WebSocket permits one concurrent reader and one concurrent writer, so every
// server-side write must go through this type.
type Client struct {
	conn      *websocket.Conn
	writeMu   sync.Mutex
	closeOnce sync.Once
}

func NewClient(conn *websocket.Conn) *Client {
	return &Client{conn: conn}
}

func (c *Client) ReadMessage() (int, []byte, error) {
	return c.conn.ReadMessage()
}

func (c *Client) WriteJSON(value any) error {
	c.writeMu.Lock()
	defer c.writeMu.Unlock()
	return c.conn.WriteJSON(value)
}

func (c *Client) Close() {
	c.closeOnce.Do(func() {
		_ = c.conn.Close()
	})
}

func (c *Client) CloseWithReason(code int, reason string) {
	c.closeOnce.Do(func() {
		c.writeMu.Lock()
		_ = c.conn.WriteControl(
			websocket.CloseMessage,
			websocket.FormatCloseMessage(code, reason),
			time.Now().Add(time.Second),
		)
		c.writeMu.Unlock()
		_ = c.conn.Close()
	})
}

// ConnectionPool manages all WebSocket connections by connection ID.
type ConnectionPool struct {
	connections map[string]*Client
	mu          sync.RWMutex
}

// Add registers a connection. A connection ID is immutable for the lifetime of
// a socket, so a duplicate ID is rejected instead of silently replacing the
// existing connection.
func (p *ConnectionPool) Add(playerID string, client *Client) bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	if _, exists := p.connections[playerID]; exists {
		return false
	}
	p.connections[playerID] = client
	log.Printf("Player connected: %s. Total connections: %d", playerID, len(p.connections))
	return true
}

// Remove deletes a connection only when it still points to the expected
// client. This prevents delayed cleanup from deleting a newer registration.
func (p *ConnectionPool) Remove(playerID string, expected *Client) bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	current, exists := p.connections[playerID]
	if !exists || current != expected {
		return false
	}
	delete(p.connections, playerID)
	log.Printf("Player disconnected: %s. Total connections: %d", playerID, len(p.connections))
	return true
}

func (p *ConnectionPool) Get(playerID string) (*Client, bool) {
	p.mu.RLock()
	defer p.mu.RUnlock()
	client, exists := p.connections[playerID]
	return client, exists
}
