"""Read-only account permissions for the independently running lobby/room chat."""
import secrets

from fastapi import Header, HTTPException
from pydantic import BaseModel, Field

from ..public.account_bans import CHAT_BANS
from ..database.db_manager import LOGIN_BAN_TYPES


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
