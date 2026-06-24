"""从 WebSocket / HTTP 请求解析客户端 IP。"""
from fastapi import WebSocket


def get_client_ip_from_websocket(websocket: WebSocket) -> str:
    forwarded = websocket.headers.get("x-forwarded-for")
    if forwarded:
        return forwarded.split(",")[0].strip()
    if websocket.client and websocket.client.host:
        return websocket.client.host
    return "unknown"
