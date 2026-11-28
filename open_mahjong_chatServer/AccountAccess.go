package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"math/rand"
	"net"
	"net/http"
	"os"
	"strconv"
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

type accountAccess struct {
	UserID         int64 `json:"user_id"`
	CanLogin       bool  `json:"can_login"`
	CanSend        bool  `json:"can_send"`
	ProfileImageID int   `json:"profile_image_id"`
}

type cachedAccessEntry struct {
	access    accountAccess
	expiresAt time.Time
}

type accessFlight struct {
	done   chan struct{}
	access accountAccess
	err    error
}

// accountAccessCache caches per-user chat permissions with TTL and singleflight
// deduplication so chat sends and background checks do not saturate the Python
// game server's PostgreSQL connection pool.
type accountAccessCache struct {
	mu       sync.RWMutex
	entries  map[int64]cachedAccessEntry
	inFlight map[int64]*accessFlight
	ttl      time.Duration
	upstream func(string, int64) (accountAccess, error)
}

func newAccountAccessCache(upstream func(string, int64) (accountAccess, error), ttl time.Duration) *accountAccessCache {
	if ttl <= 0 {
		ttl = 60 * time.Second
	}
	return &accountAccessCache{
		entries:  make(map[int64]cachedAccessEntry),
		inFlight: make(map[int64]*accessFlight),
		ttl:      ttl,
		upstream: upstream,
	}
}

func (c *accountAccessCache) Lookup(username string, uid int64) (accountAccess, error) {
	// Login requests (uid == 0) always verify live permissions and refresh cache.
	if uid == 0 {
		access, err := c.upstream(username, 0)
		if err == nil && access.UserID > 0 {
			c.Set(access)
		}
		return access, err
	}

	now := time.Now()
	c.mu.RLock()
	entry, ok := c.entries[uid]
	c.mu.RUnlock()
	if ok && now.Before(entry.expiresAt) {
		return entry.access, nil
	}

	c.mu.Lock()
	// Double-check after acquiring write lock.
	if entry, ok = c.entries[uid]; ok && time.Now().Before(entry.expiresAt) {
		c.mu.Unlock()
		return entry.access, nil
	}
	if flight, running := c.inFlight[uid]; running {
		c.mu.Unlock()
		<-flight.done
		return flight.access, flight.err
	}
	flight := &accessFlight{done: make(chan struct{})}
	c.inFlight[uid] = flight
	c.mu.Unlock()

	access, err := c.upstream("", uid)

	c.mu.Lock()
	if err == nil && access.UserID > 0 {
		c.entries[uid] = cachedAccessEntry{
			access:    access,
			expiresAt: time.Now().Add(c.ttl),
		}
	}
	flight.access = access
	flight.err = err
	delete(c.inFlight, uid)
	close(flight.done)
	c.mu.Unlock()

	return access, err
}

func (c *accountAccessCache) Set(access accountAccess) {
	if access.UserID <= 0 {
		return
	}
	c.mu.Lock()
	c.entries[access.UserID] = cachedAccessEntry{
		access:    access,
		expiresAt: time.Now().Add(c.ttl),
	}
	c.mu.Unlock()
}

func (c *accountAccessCache) Invalidate(uid int64) {
	if uid <= 0 {
		return
	}
	c.mu.Lock()
	delete(c.entries, uid)
	c.mu.Unlock()
}

func envDurationSeconds(name string, defaultSec int) time.Duration {
	raw := os.Getenv(name)
	if raw != "" {
		if sec, err := strconv.Atoi(raw); err == nil && sec > 0 {
			return time.Duration(sec) * time.Second
		}
	}
	return time.Duration(defaultSec) * time.Second
}

func gameAccountLookup(secret string) func(string, int64) (accountAccess, error) {
	url := os.Getenv("MAHJONG_CHAT_ACCESS_URL")
	if url == "" {
		url = "http://127.0.0.1:8081/internal/chat/access"
	}
	transport := &http.Transport{
		DialContext: (&net.Dialer{
			Timeout:   2 * time.Second,
			KeepAlive: 30 * time.Second,
		}).DialContext,
		MaxIdleConns:        64,
		MaxIdleConnsPerHost: 32,
		IdleConnTimeout:     60 * time.Second,
	}
	client := &http.Client{
		Timeout:   3 * time.Second,
		Transport: transport,
		CheckRedirect: func(*http.Request, []*http.Request) error {
			return http.ErrUseLastResponse
		},
	}
	return func(username string, uid int64) (accountAccess, error) {
		body, err := json.Marshal(struct {
			Username string `json:"username"`
			UserID   int64  `json:"user_id"`
		}{username, uid})
		if err != nil {
			return accountAccess{}, err
		}
		req, err := http.NewRequest(http.MethodPost, url, bytes.NewReader(body))
		if err != nil {
			return accountAccess{}, err
		}
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("X-Chat-Secret", secret)
		resp, err := client.Do(req)
		if err != nil {
			return accountAccess{}, err
		}
		defer resp.Body.Close()
		if resp.StatusCode == http.StatusNotFound {
			return accountAccess{}, nil
		}
		if resp.StatusCode != http.StatusOK {
			return accountAccess{}, fmt.Errorf("chat authorization status %d", resp.StatusCode)
		}
		var access accountAccess
		if err := json.NewDecoder(resp.Body).Decode(&access); err != nil {
			return accountAccess{}, err
		}
		if access.UserID <= 0 || (uid != 0 && uid != access.UserID) {
			return accountAccess{}, errors.New("invalid chat account")
		}
		return access, nil
	}
}

// Older clients can listen without sending heartbeats. Revoke those sessions too.
// Active bans are pushed immediately via /internal/chat/revoke; this loop serves
// as a low-frequency jittered safety net so hundreds of connections do not poll
// Python every 15 seconds.
func (rm *RoomManager) watchAccount(client *Client, uid int64, done <-chan struct{}) {
	interval := envDurationSeconds("MAHJONG_CHAT_WATCH_INTERVAL_SEC", 120)
	jitter := time.Duration(rand.Int63n(int64(min(interval, 30*time.Second))))
	timer := time.NewTimer(interval + jitter)
	defer timer.Stop()
	for {
		select {
		case <-done:
			return
		case <-timer.C:
			if rm.accessCache != nil {
				rm.accessCache.Invalidate(uid)
			}
			access, err := rm.lookupAccount("", uid)
			if err == nil && !access.CanLogin {
				client.CloseWithReason(websocket.ClosePolicyViolation, "account_banned")
				return
			}
			timer.Reset(interval)
		}
	}
}
