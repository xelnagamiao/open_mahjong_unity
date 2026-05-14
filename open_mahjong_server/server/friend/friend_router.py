"""
好友模块消息路由器，处理 type 以 "friend/" 开头的消息。
"""
import logging
from typing import Optional

from ..response import Response, FriendInfo

logger = logging.getLogger(__name__)


def _require_login(game_server, Connect_id: str) -> Optional[tuple]:
    """返回 (user_id, username, websocket) 或 None（已发送 tips）。"""
    player = game_server.players.get(Connect_id)
    if not player or not player.user_id:
        return None
    return player.user_id, player.username or "", player.websocket


async def _send(websocket, response: Response):
    try:
        await websocket.send_json(response.dict(exclude_none=True))
    except Exception as exc:  # pragma: no cover
        logger.warning(f"_send 发送失败: {exc}")


async def handle_friend_message(game_server, Connect_id: str, message: dict, websocket):
    """好友/关注/实时观战的总入口。"""
    message_type = message.get("type", "").strip("/")

    auth = _require_login(game_server, Connect_id)
    if auth is None:
        await _send(
            websocket,
            Response(type=message_type, success=False, message="请先登录"),
        )
        return
    user_id, username, _ws = auth

    try:
        if message_type == "friend/add_friend":
            await _handle_add_friend(game_server, user_id, message, websocket)
        elif message_type == "friend/remove_friend":
            await _handle_remove_friend(game_server, user_id, message, websocket)
        elif message_type == "friend/list_friends":
            await _handle_list_friends(game_server, user_id, websocket)
        elif message_type == "friend/request_realtime":
            await _handle_request_realtime(game_server, user_id, username, message, websocket)
        elif message_type == "friend/respond_realtime":
            await _handle_respond_realtime(game_server, user_id, message, websocket)
        elif message_type == "friend/cancel_realtime":
            await _handle_cancel_realtime(game_server, user_id, message, websocket)
        elif message_type == "friend/exit_realtime":
            await _handle_exit_realtime(game_server, user_id, websocket)
        elif message_type == "friend/kick_realtime":
            await _handle_kick_realtime(game_server, user_id, message, websocket)
        elif message_type == "friend/list_realtime_spectators":
            await _handle_list_realtime_spectators(game_server, user_id, websocket)
        else:
            logger.warning(f"未知的好友消息路径: {message_type}")
    except Exception as exc:
        logger.error(f"处理 {message_type} 失败: {exc}", exc_info=True)
        await _send(
            websocket,
            Response(type=message_type, success=False, message="服务器异常"),
        )


async def _handle_add_friend(game_server, user_id: int, message: dict, websocket):
    friend_user_id = message.get("friend_user_id")
    try:
        friend_user_id = int(friend_user_id)
    except (TypeError, ValueError):
        await _send(
            websocket,
            Response(
                type="friend/add_friend",
                success=False,
                message="目标 UID 非法",
            ),
        )
        return
    result = game_server.db_manager.add_friend(user_id, friend_user_id)
    friend_list = game_server.friend_manager.build_friend_list_payload(user_id)
    payload_list = [FriendInfo(**item) for item in friend_list]
    await _send(
        websocket,
        Response(
            type="friend/add_friend",
            success=result["success"],
            message=result["message"],
            friend_list=payload_list,
            friend_count=len(payload_list),
            friend_max=game_server.db_manager.FRIEND_MAX,
        ),
    )


async def _handle_remove_friend(game_server, user_id: int, message: dict, websocket):
    friend_user_id = message.get("friend_user_id")
    try:
        friend_user_id = int(friend_user_id)
    except (TypeError, ValueError):
        await _send(
            websocket,
            Response(type="friend/remove_friend", success=False, message="目标 UID 非法"),
        )
        return
    ok = game_server.db_manager.remove_friend(user_id, friend_user_id)
    friend_list = game_server.friend_manager.build_friend_list_payload(user_id)
    payload_list = [FriendInfo(**item) for item in friend_list]
    await _send(
        websocket,
        Response(
            type="friend/remove_friend",
            success=ok,
            message="已取消关注" if ok else "取消关注失败",
            friend_list=payload_list,
            friend_count=len(payload_list),
            friend_max=game_server.db_manager.FRIEND_MAX,
        ),
    )


async def _handle_list_friends(game_server, user_id: int, websocket):
    friend_list = game_server.friend_manager.build_friend_list_payload(user_id)
    payload_list = [FriendInfo(**item) for item in friend_list]
    await _send(
        websocket,
        Response(
            type="friend/list_friends",
            success=True,
            message="ok",
            friend_list=payload_list,
            friend_count=len(payload_list),
            friend_max=game_server.db_manager.FRIEND_MAX,
        ),
    )


async def _handle_request_realtime(game_server, user_id: int, username: str, message: dict, websocket):
    target_user_id = message.get("target_user_id")
    try:
        target_user_id = int(target_user_id)
    except (TypeError, ValueError):
        await _send(
            websocket,
            Response(
                type="friend/realtime_request_result",
                success=False,
                message="目标 UID 非法",
            ),
        )
        return
    response = await game_server.friend_manager.request_realtime(user_id, username, target_user_id)
    await _send(websocket, response)


async def _handle_respond_realtime(game_server, user_id: int, message: dict, websocket):
    request_id = str(message.get("request_id") or "")
    accept = bool(message.get("accept", False))
    response = await game_server.friend_manager.respond_request(user_id, request_id, accept)
    await _send(websocket, response)


async def _handle_cancel_realtime(game_server, user_id: int, message: dict, websocket):
    request_id = str(message.get("request_id") or "")
    response = await game_server.friend_manager.cancel_request(user_id, request_id)
    await _send(websocket, response)


async def _handle_exit_realtime(game_server, user_id: int, websocket):
    response = await game_server.friend_manager.exit_realtime(user_id)
    await _send(websocket, response)


async def _handle_kick_realtime(game_server, user_id: int, message: dict, websocket):
    spectator_user_id = message.get("spectator_user_id")
    try:
        spectator_user_id = int(spectator_user_id)
    except (TypeError, ValueError):
        await _send(
            websocket,
            Response(type="friend/realtime_kick_result", success=False, message="UID 非法"),
        )
        return
    response = await game_server.friend_manager.kick_realtime(user_id, spectator_user_id)
    await _send(websocket, response)


async def _handle_list_realtime_spectators(game_server, user_id: int, websocket):
    """B 端用：返回当前正挂在自己座位的实时观战者。"""
    from ..response import RealtimeSpectatorEntry

    game_state = game_server.gamestate_manager.get_game_state_by_user_id(user_id)
    if game_state is None:
        await _send(
            websocket,
            Response(
                type="friend/list_realtime_spectators",
                success=True,
                message="您当前不在游戏中",
                realtime_spectators=[],
            ),
        )
        return
    host_player_index = None
    if hasattr(game_state, "player_list"):
        for p in game_state.player_list:
            if getattr(p, "user_id", None) == user_id:
                host_player_index = getattr(p, "player_index", None)
                break
    spectators = getattr(game_state, "realtime_spectators", [])
    entries = [
        RealtimeSpectatorEntry(user_id=sp.user_id, username=sp.username)
        for sp in spectators
        if host_player_index is None or sp.player_index == host_player_index
    ]
    await _send(
        websocket,
        Response(
            type="friend/list_realtime_spectators",
            success=True,
            message="ok",
            realtime_spectators=entries,
            realtime_gamestate_id=getattr(game_state, "gamestate_id", None),
        ),
    )
