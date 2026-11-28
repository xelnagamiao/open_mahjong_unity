package main

import (
	"encoding/json"
	"errors"
	"log"
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

const (
	clientSendQueueSize = 64
	clientWriteTimeout  = 5 * time.Second
	clientMaxMessageBytes = 16 * 1024
)

var errClientClosed = errors.New("client closed")

// Client wraps a WebSocket connection with a single writer goroutine so
// broadcasts never block on slow or half-open recipients while preserving
// per-connection FIFO message ordering.
type Client struct {
	conn       *websocket.Conn
	sendCh     chan *websocket.PreparedMessage
	done       chan struct{}
	writeDone  chan struct{}
	closeOnce  sync.Once
	closeMu    sync.Mutex
	closeCode  int
	closeText  string
	hasReason  bool
}

func NewClient(conn *websocket.Conn) *Client {
	if conn != nil {
		conn.SetReadLimit(clientMaxMessageBytes)
	}
	c := &Client{
		conn:      conn,
		sendCh:    make(chan *websocket.PreparedMessage, clientSendQueueSize),
		done:      make(chan struct{}),
		writeDone: make(chan struct{}),
	}
	if conn != nil {
		go c.writePump()
	} else {
		close(c.writeDone)
	}
	return c
}

func (c *Client) writePump() {
	defer close(c.writeDone)
	for {
		select {
		case pm := <-c.sendCh:
			_ = c.conn.SetWriteDeadline(time.Now().Add(clientWriteTimeout))
			if err := c.conn.WritePreparedMessage(pm); err != nil {
				c.closeWithoutWait()
				return
			}
		case <-c.done:
			// Drain already-enqueued messages (e.g. login_kickout) before sending close control frame.
			for {
				select {
				case pm := <-c.sendCh:
					_ = c.conn.SetWriteDeadline(time.Now().Add(time.Second))
					if err := c.conn.WritePreparedMessage(pm); err != nil {
						_ = c.conn.Close()
						return
					}
				default:
					c.closeMu.Lock()
					hasReason := c.hasReason
					code := c.closeCode
					reason := c.closeText
					c.closeMu.Unlock()
					if hasReason {
						_ = c.conn.WriteControl(
							websocket.CloseMessage,
							websocket.FormatCloseMessage(code, reason),
							time.Now().Add(time.Second),
						)
					}
					_ = c.conn.Close()
					return
				}
			}
		}
	}
}

func (c *Client) ReadMessage() (int, []byte, error) {
	return c.conn.ReadMessage()
}

func (c *Client) WriteJSON(value any) error {
	payload, err := json.Marshal(value)
	if err != nil {
		return err
	}
	pm, err := websocket.NewPreparedMessage(websocket.TextMessage, payload)
	if err != nil {
		return err
	}
	select {
	case <-c.done:
		return errClientClosed
	case c.sendCh <- pm:
		return nil
	case <-time.After(clientWriteTimeout):
		c.Close()
		return errors.New("client write queue timeout")
	}
}

// TrySendPrepared enqueues a pre-serialized broadcast message without blocking.
// If a slow client's send queue is full, it is disconnected so it cannot stall
// other recipients in the room.
func (c *Client) TrySendPrepared(pm *websocket.PreparedMessage) bool {
	select {
	case <-c.done:
		return false
	default:
	}
	select {
	case c.sendCh <- pm:
		return true
	default:
		c.Close()
		return false
	}
}

func (c *Client) closeWithoutWait() {
	c.closeOnce.Do(func() {
		if c.done != nil {
			close(c.done)
		} else if c.conn != nil {
			_ = c.conn.Close()
		}
	})
}

func (c *Client) Close() {
	c.closeWithoutWait()
}

func (c *Client) CloseWithReason(code int, reason string) {
	c.closeMu.Lock()
	c.closeCode = code
	c.closeText = reason
	c.hasReason = true
	c.closeMu.Unlock()
	c.closeWithoutWait()
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
