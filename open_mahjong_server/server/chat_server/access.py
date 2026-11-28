"""Read-only account permissions for the independently running lobby/room chat."""
import json
import logging
import os
import secrets
import urllib.request

from fastapi import Header, HTTPException
from pydantic import BaseModel, Field

from ..public.account_bans import CHAT_BANS
from ..database.db_manager import LOGIN_BAN_TYPES

logger = logging.getLogger(__name__)


def notify_chat_revoke(secret_key: str, user_id: int, kick: bool = True, reason: str = 'account_banned') -> bool:
    """Best-effort push to Go chat server to invalidate cached permissions and optionally kick."""
    if not secret_key or not user_id:
        return False
    url = os.environ.get('MAHJONG_CHAT_REVOKE_URL', 'http://127.0.0.1:8083/internal/chat/revoke').strip()
    if not url:
        return False
    payload = json.dumps({'user_id': int(user_id), 'kick': bool(kick), 'reason': reason}).encode('utf-8')
    req = urllib.request.Request(
        url,
        data=payload,
        method='POST',
        headers={'Content-Type': 'application/json', 'X-Chat-Secret': secret_key},
    )
    try:
        with urllib.request.urlopen(req, timeout=1.5) as resp:
            return 200 <= resp.status < 300
    except Exception:
        logger.debug('通知 Go 聊天服刷新封禁状态失败 user_id=%s', user_id, exc_info=True)
        return False


class ChatAccount(BaseModel):
    username: str = Field(default='', max_length=255)
    user_id: int = Field(default=0, ge=0)


def register_chat_access(app, server):
    @app.post('/internal/chat/access', include_in_schema=False)
    def chat_access(body: ChatAccount, x_chat_secret: str = Header(default='')):
        key = getattr(server.chat_server, 'secret_key', '')
        if not key or not secrets.compare_digest(x_chat_secret.encode(), key.encode()):
            raise HTTPException(status_code=403)
        user = server.db_manager.chat_account_access(body.username, body.user_id)
        if not user:
            raise HTTPException(status_code=404, detail='账号不存在')
        active = user['active_ban']
        return {'user_id': user['user_id'], 'profile_image_id': user['profile_image_id'], 'can_login': active not in LOGIN_BAN_TYPES,
                'can_send': active not in CHAT_BANS}
