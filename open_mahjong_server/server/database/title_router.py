"""WebSocket title operations and cosmetic-only online refreshes."""
import asyncio
import logging

from .titles import equip_title, get_title_catalog, get_title_state

logger = logging.getLogger(__name__)


def _lock(server):
    if not hasattr(server, "title_sync_lock"):
        server.title_sync_lock = asyncio.Lock()
    return server.title_sync_lock


async def _send(websocket, payload):
    try:
        await websocket.send_json(payload)
    except Exception:
        logger.debug("头衔同步目标已断线", exc_info=True)


async def _sync_locked(server, user_ids):
    catalog = await asyncio.to_thread(get_title_catalog, server.db_manager)
    candidates = set(server.user_id_to_connection)
    candidates.update(server.gamestate_manager.user_id_to_game_state)
    for room in server.room_manager.rooms.values():
        candidates.update(uid for uid in room.get("player_list", []) if uid > 10)
    targets = candidates if user_ids is None else candidates.intersection(user_ids)
    changes = []
    for uid in sorted(targets):
        state = await asyncio.to_thread(get_title_state, server.db_manager, uid)
        selected = state["equipped_title_id"]
        # Every rule consumes these shared room settings / player objects.
        for room in server.room_manager.rooms.values():
            settings = room.get("player_settings", {})
            for key in (uid, str(uid)):
                if key in settings:
                    settings[key]["title_id"] = selected
        game = server.gamestate_manager.get_game_state_by_user_id(uid)
        if game:
            for player in getattr(game, "player_list", []):
                if player.user_id == uid:
                    player.title_used = selected
        changes.append({"user_id":uid, "title_id":selected})
        connection = server.user_id_to_connection.get(uid)
        if connection:
            await _send(connection.websocket, {"type":"title/state", "success":True, "title_state":state})
    # Only public cosmetics are broadcast; ownership and grants stay private.
    payload = {"type":"title/update", "success":True, "title_catalog":catalog, "title_changes":changes}
    await asyncio.gather(*(_send(p.websocket, payload) for p in list(server.players.values())))


async def sync_titles(server, user_ids=None):
    async with _lock(server):
        await _sync_locked(server, user_ids)


async def handle_title_message(server, connection_id, message, websocket):
    kind = message.get("type")
    request_id = str(message.get("request_id") or "")[:64]
    reply = {"type":kind, "request_id":request_id, "success":False}
    try:
        if kind == "title/catalog":
            reply.update(success=True, title_catalog=await asyncio.to_thread(get_title_catalog, server.db_manager))
        elif kind in ("title/get", "title/equip"):
            player = server.players.get(connection_id)
            if not player or not player.user_id:
                raise ValueError("请先登录后再管理头衔")
            # Ignore any client-supplied user_id. The authenticated connection owns the request.
            async with _lock(server):
                if kind == "title/equip":
                    state = await asyncio.to_thread(equip_title, server.db_manager, player.user_id, message.get("title_id"))
                else:
                    state = await asyncio.to_thread(get_title_state, server.db_manager, player.user_id)
                reply.update(success=True, title_state=state, message="头衔已保存" if kind == "title/equip" else "")
                await _send(websocket, reply)
                if kind == "title/equip":
                    await _sync_locked(server, [player.user_id])
                return
        else:
            raise ValueError("未知的头衔操作")
    except ValueError as error:
        reply["message"] = str(error)
    except Exception:
        logger.exception("头衔操作失败")
        reply["message"] = "头衔服务暂不可用，请稍后重试"
    await _send(websocket, reply)
