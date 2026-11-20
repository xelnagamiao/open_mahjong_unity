package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"os"
	"time"

	"github.com/gorilla/websocket"
)

type accountAccess struct {
	UserID         int64 `json:"user_id"`
	CanLogin       bool  `json:"can_login"`
	CanSend        bool  `json:"can_send"`
	ProfileImageID int   `json:"profile_image_id"`
}

func gameAccountLookup(secret string) func(string, int64) (accountAccess, error) {
	url := os.Getenv("MAHJONG_CHAT_ACCESS_URL")
	if url == "" {
		url = "http://127.0.0.1:8081/internal/chat/access"
	}
	client := &http.Client{Timeout: 3 * time.Second, CheckRedirect: func(*http.Request, []*http.Request) error {
		return http.ErrUseLastResponse
	}}
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
func (rm *RoomManager) watchAccount(client *Client, uid int64, done <-chan struct{}) {
	ticker := time.NewTicker(15 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-done:
			return
		case <-ticker.C:
			access, err := rm.lookupAccount("", uid)
			if err == nil && !access.CanLogin {
				client.CloseWithReason(websocket.ClosePolicyViolation, "account_banned")
				return
			}
		}
	}
}
