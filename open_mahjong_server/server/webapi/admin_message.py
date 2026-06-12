"""
管理后台消息推送 HTTP 接口（仅供 Node 管理端 localhost 代理调用）。
"""
import logging

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from ..response import MessageInfo, Response

logger = logging.getLogger(__name__)

MAX_TITLE_LEN = 64
MAX_CONTENT_LEN = 2000


class AdminMessageBody(BaseModel):
    title: str = Field(..., min_length=1, max_length=MAX_TITLE_LEN)
    content: str = Field(..., min_length=1, max_length=MAX_CONTENT_LEN)


class AdminUserMessageBody(AdminMessageBody):
    user_id: int = Field(..., gt=0)


def _build_admin_response(title: str, content: str) -> Response:
    return Response(
        type="message",
        success=True,
        message="admin_notice",
        message_info=MessageInfo(title=title, content=content),
    )


async def _send_to_connection(websocket, response: Response) -> bool:
    try:
        await websocket.send_json(response.dict(exclude_none=True))
        return True
    except Exception as exc:
        logger.warning("向玩家推送管理消息失败: %s", exc)
        return False


def register_admin_message_routes(app: FastAPI, game_server) -> None:
    """将 /admin/message/* 路由挂到已创建的 FastAPI app 上。"""

    @app.post("/admin/message/broadcast")
    async def admin_broadcast_message(body: AdminMessageBody):
        response = _build_admin_response(body.title.strip(), body.content.strip())
        sent = 0
        failed = 0
        for user_id, player in list(game_server.user_id_to_connection.items()):
            ok = await _send_to_connection(player.websocket, response)
            if ok:
                sent += 1
                logger.info("管理广播消息已送达 user_id=%s", user_id)
            else:
                failed += 1

        return {
            "success": True,
            "sent": sent,
            "failed": failed,
            "total_online": len(game_server.user_id_to_connection),
        }

    @app.post("/admin/message/user")
    async def admin_send_user_message(body: AdminUserMessageBody):
        user_id = body.user_id
        player = game_server.user_id_to_connection.get(user_id)
        if player is None:
            raise HTTPException(status_code=404, detail="用户当前不在线")

        response = _build_admin_response(body.title.strip(), body.content.strip())
        ok = await _send_to_connection(player.websocket, response)
        if not ok:
            raise HTTPException(status_code=502, detail="消息发送失败")

        logger.info("管理单播消息已送达 user_id=%s", user_id)
        return {
            "success": True,
            "user_id": user_id,
            "username": player.username,
        }
