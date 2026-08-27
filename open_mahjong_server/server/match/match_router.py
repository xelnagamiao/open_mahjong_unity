"""
匹配消息路由
"""
import logging
from ..response import Response

logger = logging.getLogger(__name__)


async def handle_match_message(game_server, connect_id: str, message: dict, websocket):
    """处理匹配相关消息"""
    message_type = message.get("type", "").strip("/")

    if message_type == "match/join_queue":
        queue_type = message.get("queue_type", "")
        response = await game_server.match_manager.join_queue(connect_id, queue_type)
        await websocket.send_json(response.dict(exclude_none=True))

    elif message_type == "match/leave_queue":
        response = await game_server.match_manager.leave_queue(connect_id)
        await websocket.send_json(response.dict(exclude_none=True))

    elif message_type == "match/get_queue_status":
        status = game_server.match_manager.get_queue_status()
        player = game_server.players.get(connect_id)
        user_id = getattr(player, "user_id", None) if player else None
        my_queue, match_committed = game_server.match_manager.get_my_match_state(user_id)
        response = Response(
            type="match/queue_status",
            success=True,
            message="队列状态",
            my_queue=my_queue,
            match_committed=match_committed,
        )
        response_dict = response.dict(exclude_none=True)
        response_dict["queue_status"] = status
        await websocket.send_json(response_dict)

    else:
        logger.warning(f"未知的匹配消息类型: {message_type}")
