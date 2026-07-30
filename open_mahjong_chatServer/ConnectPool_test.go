package main

import "testing"

func TestConnectionPoolRejectsDuplicateAndUsesConditionalRemove(t *testing.T) {
	pool := ConnectionPool{connections: make(map[string]*Client)}
	first := &Client{}
	other := &Client{}

	if !pool.Add("connection", first) {
		t.Fatal("first registration was rejected")
	}
	if pool.Add("connection", other) {
		t.Fatal("duplicate registration was accepted")
	}
	if pool.Remove("connection", other) {
		t.Fatal("a different client removed the active connection")
	}
	if current, exists := pool.Get("connection"); !exists || current != first {
		t.Fatal("conditional remove changed the active connection")
	}
	if !pool.Remove("connection", first) {
		t.Fatal("active client could not remove its connection")
	}
}
