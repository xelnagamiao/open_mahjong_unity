"""
管理后台用户操作 HTTP 接口（踢下线等，仅供 Node 管理端 localhost 代理调用）。
"""
import logging

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from ..response import MessageInfo, Response

logger = logging.getLogger(__name__)

MAX_KICK_REASON_LEN = 500


class AdminKickUserBody(BaseModel):
    user_id: int = Field(..., gt=0)
    reason: str = Field(default="管理员已将您的账号踢下线", max_length=MAX_KICK_REASON_LEN)


class AdminSyncUsernameBody(BaseModel):
    user_id: int = Field(..., gt=0)
    username: str = Field(..., min_length=1, max_length=255)


def register_admin_user_routes(app: FastAPI, game_server) -> None:
    @app.get("/admin/user/{user_id}/online")
    async def admin_user_online(user_id: int):
        player = game_server.user_id_to_connection.get(user_id)
        return {
            "success": True,
            "online": player is not None,
            "user_id": user_id,
            "username": player.username if player else None,
        }

    @app.post("/admin/user/kick")
    async def admin_kick_user(body: AdminKickUserBody):
        reason = (body.reason or "管理员已将您的账号踢下线").strip()
        kicked = await game_server.kick_user_by_id(body.user_id, reason)
        if not kicked:
            raise HTTPException(status_code=404, detail="用户当前不在线")
        logger.info("管理员踢下线 user_id=%s", body.user_id)
        return {
            "success": True,
            "user_id": body.user_id,
            "message": "已踢下线",
        }

    @app.post("/admin/user/sync-username")
    async def admin_sync_username(body: AdminSyncUsernameBody):
        username = body.username.strip()
        player = game_server.user_id_to_connection.get(body.user_id)
        if player is not None:
            player.username = username
            logger.info("已同步在线用户名 user_id=%s username=%s", body.user_id, username)
        return {
            "success": True,
            "user_id": body.user_id,
            "online": player is not None,
            "username": username,
        }
