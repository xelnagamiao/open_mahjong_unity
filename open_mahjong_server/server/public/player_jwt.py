"""验证网站玩家 JWT（与 open_mahjong_web/server/utils/jwt.js 算法一致）。"""
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import logging
import time
from typing import Any, Optional

logger = logging.getLogger(__name__)

PLAYER_AUDIENCE = "player"


def _b64url_decode(data: str) -> bytes:
    pad = "=" * (-len(data) % 4)
    return base64.urlsafe_b64decode(data + pad)


def _b64url_encode(raw: bytes) -> str:
    return base64.urlsafe_b64encode(raw).rstrip(b"=").decode("ascii")


def verify_player_token(token: str, secret: str, audience: str = PLAYER_AUDIENCE) -> Optional[dict[str, Any]]:
    """校验 HS256 JWT，成功返回 payload，失败返回 None。"""
    if not token or not secret or not isinstance(token, str):
        return None
    parts = token.split(".")
    if len(parts) != 3:
        return None
    header_b64, body_b64, sig_b64 = parts
    expected = _b64url_encode(
        hmac.new(
            secret.encode("utf-8"),
            f"{header_b64}.{body_b64}".encode("ascii"),
            hashlib.sha256,
        ).digest()
    )
    try:
        sig_bytes = sig_b64.encode("ascii")
        exp_bytes = expected.encode("ascii")
        if len(sig_bytes) != len(exp_bytes) or not hmac.compare_digest(sig_bytes, exp_bytes):
            return None
        header = json.loads(_b64url_decode(header_b64).decode("utf-8"))
        payload = json.loads(_b64url_decode(body_b64).decode("utf-8"))
    except Exception:
        logger.debug("player JWT 解析失败", exc_info=True)
        return None

    if not isinstance(header, dict) or header.get("alg") != "HS256":
        return None
    if not isinstance(payload, dict):
        return None
    exp = payload.get("exp")
    if exp is not None:
        try:
            if int(exp) < int(time.time()):
                return None
        except (TypeError, ValueError):
            return None
    aud = payload.get("aud")
    if audience and aud != audience:
        return None
    user_id = payload.get("user_id")
    username = payload.get("username")
    if user_id is None or not username:
        return None
    try:
        payload["user_id"] = int(user_id)
    except (TypeError, ValueError):
        return None
    payload["username"] = str(username)
    return payload
