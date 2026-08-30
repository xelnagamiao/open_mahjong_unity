"""
赛事/基地相关 WebSocket 消息（type 以 event/ 开头）。
"""
import logging
from typing import Optional

from ..response import Response, Record_info, Player_record_info

logger = logging.getLogger(__name__)


def _require_login(game_server, Connect_id: str) -> Optional[tuple]:
    player = game_server.players.get(Connect_id)
    if not player or not player.user_id:
        return None
    return player.user_id, player.websocket, player


async def _send(websocket, response: Response):
    try:
        await websocket.send_json(response.model_dump(mode="json", exclude_none=True))
    except Exception as exc:  # pragma: no cover
        logger.warning(f"event _send 失败: {exc}")


def _public_entry_summary(entry_config: dict) -> dict:
    cfg = entry_config or {}
    return {
        "forbid_tourist": bool(cfg.get("forbid_tourist", False)),
        "min_rank": cfg.get("min_rank") or "",
        "max_rank": cfg.get("max_rank") or "",
        "has_join_code": bool(str(cfg.get("join_code") or "").strip()),
        "member_can_create_room": bool(cfg.get("member_can_create_room", False)),
        "auto_approve": bool(cfg.get("auto_approve", False)),
        "unregistered_can_create_room": bool(cfg.get("unregistered_can_create_room", False)),
        "unregistered_can_ready": bool(cfg.get("unregistered_can_ready", False)),
    }


def _check_entry_config(event: dict, player, join_code: str = "") -> Optional[str]:
    cfg = game_server_parse_config(event)
    if cfg.get("forbid_tourist") and getattr(player, "is_tourist", False):
        return "该场馆不允许游客参加"
    expected = str(cfg.get("join_code") or "").strip()
    if expected and str(join_code or "").strip() != expected:
        return "口令不正确"
    return None


def game_server_parse_config(event: dict) -> dict:
    from ..database.db_manager import DatabaseManager
    return DatabaseManager.parse_entry_config(event.get("entry_config"))


def _fmt_day(value) -> str:
    if value is None:
        return ""
    if hasattr(value, "isoformat"):
        return value.isoformat()[:10]
    text = str(value).strip()
    if not text or text.lower() == "none":
        return ""
    return text[:10]


async def handle_event_message(game_server, Connect_id: str, message: dict, websocket):
    message_type = message.get("type", "").strip("/")

    auth = _require_login(game_server, Connect_id)
    if auth is None:
        await _send(
            websocket,
            Response(type=message_type, success=False, message="请先登录"),
        )
        return
    user_id, _ws, player = auth
    db = game_server.db_manager

    try:
        if message_type == "event/list_public":
            kind = (message.get("kind") or "").strip() or None
            if kind not in (None, "event", "base"):
                kind = None
            events = db.list_public_active_venues(kind)
            for item in events:
                item["entry_summary"] = _public_entry_summary(item.get("entry_config") or {})
                item.pop("entry_config", None)
            await _send(
                websocket,
                Response(
                    type="event/list_public",
                    success=True,
                    message="ok",
                    event_list=events,
                ),
            )
        elif message_type == "event/get_detail":
            await _handle_get_detail(game_server, user_id, player, message, websocket)
        elif message_type == "event/register":
            await _handle_register(game_server, user_id, player, message, websocket)
        elif message_type == "event/cancel_register":
            await _handle_cancel_register(game_server, user_id, message, websocket)
        elif message_type == "event/ready":
            await _handle_ready(game_server, user_id, player, message, websocket, True)
        elif message_type == "event/unready":
            await _handle_ready(game_server, user_id, player, message, websocket, False)
        elif message_type == "event/list_ready":
            await _handle_list_ready(game_server, user_id, message, websocket)
        elif message_type == "event/list_registrations":
            await _handle_list_registrations(game_server, user_id, message, websocket)
        elif message_type == "event/review_registration":
            await _handle_review_registration(game_server, user_id, message, websocket)
        elif message_type == "event/create_empty_room":
            await _handle_create_empty_room(game_server, user_id, message, websocket)
        elif message_type == "event/seat_table":
            await _handle_seat_table(game_server, user_id, message, websocket)
        elif message_type == "event/list_records":
            await _handle_list_records(game_server, message, websocket)
        else:
            logger.warning(f"未知的赛事消息路径: {message_type}")
            await _send(
                websocket,
                Response(type=message_type, success=False, message="未知的赛事请求"),
            )
    except Exception as exc:
        logger.error(f"处理 {message_type} 失败: {exc}", exc_info=True)
        await _send(
            websocket,
            Response(type=message_type, success=False, message="服务器异常"),
        )


async def _handle_get_detail(game_server, user_id, player, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    event = game_server.db_manager.get_event(event_id)
    if not event:
        await _send(websocket, Response(type="event/get_detail", success=False, message="场馆不存在"))
        return
    role = game_server.db_manager.get_event_admin_role(event_id, user_id)
    registration = game_server.db_manager.get_event_registration(event_id, user_id)
    ready = game_server.db_manager.is_user_event_ready(event_id, user_id)
    announcements = game_server.db_manager.list_event_announcements(event_id)
    cfg = game_server_parse_config(event)
    schedule = game_server.db_manager.get_event_schedule(event_id)
    detail = {
        "event_id": event.get("event_id"),
        "name": event.get("name"),
        "description": event.get("description") or "",
        "status": event.get("status"),
        "kind": event.get("kind") or "event",
        "created_at": _fmt_day(event.get("created_at")),
        "closed_at": _fmt_day(event.get("closed_at")) or None,
        "planned_start_at": schedule.get("planned_start_at") or "",
        "planned_end_at": schedule.get("planned_end_at") or "",
        "my_role": role,
        "is_admin": bool(role),
        "registration": registration,
        "is_ready": ready,
        "ready_count": game_server.db_manager.count_event_ready_players(event_id),
        "entry_summary": _public_entry_summary(cfg),
        "announcements": announcements,
    }
    await _send(
        websocket,
        Response(type="event/get_detail", success=True, message="ok", event_detail=detail),
    )


async def _handle_register(game_server, user_id, player, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    event = game_server.db_manager.get_event(event_id)
    if not event:
        await _send(websocket, Response(type="event/register", success=False, message="场馆不存在"))
        return
    if event.get("status") != "active":
        await _send(websocket, Response(type="event/register", success=False, message="场馆未开启"))
        return
    blocked = _check_entry_config(event, player, message.get("join_code") or "")
    if blocked:
        await _send(websocket, Response(type="event/register", success=False, message=blocked))
        return
    existing = game_server.db_manager.get_event_registration(event_id, user_id)
    if existing and existing.get("status") == "approved":
        await _send(websocket, Response(type="event/register", success=False, message="您已通过报名"))
        return
    cfg = game_server_parse_config(event)
    auto = bool(cfg.get("auto_approve"))
    status = "approved" if auto else "pending"
    contact = str(message.get("contact") or "").strip()[:200]
    remark = str(message.get("remark") or "").strip()[:500]
    row = game_server.db_manager.upsert_event_registration(event_id, user_id, contact, remark, status)
    if not row:
        await _send(websocket, Response(type="event/register", success=False, message="报名失败"))
        return
    await _send(
        websocket,
        Response(type="event/register", success=True, message="报名已提交" if status == "pending" else "报名已通过", event_detail={"registration": row}),
    )


async def _handle_cancel_register(game_server, user_id, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    ok = game_server.db_manager.cancel_event_registration(event_id, user_id)
    await _send(
        websocket,
        Response(type="event/cancel_register", success=ok, message="已取消报名" if ok else "取消失败"),
    )


async def _handle_ready(game_server, user_id, player, message, websocket, ready: bool):
    event_id = str(message.get("event_id") or "").strip()
    event = game_server.db_manager.get_event(event_id)
    type_name = "event/ready" if ready else "event/unready"
    if not event or event.get("status") != "active":
        await _send(websocket, Response(type=type_name, success=False, message="场馆未开启"))
        return
    if ready:
        blocked = game_server.room_manager._reject_room_entry_conflicts(user_id, "加入等待")
        if blocked:
            await _send(websocket, Response(type=type_name, success=False, message=blocked.message))
            return
        registration = game_server.db_manager.get_event_registration(event_id, user_id)
        role = game_server.db_manager.get_event_admin_role(event_id, user_id)
        approved = bool(registration and registration.get("status") == "approved")
        cfg = game_server_parse_config(event)
        if not role and not approved and not cfg.get("unregistered_can_ready"):
            await _send(websocket, Response(type=type_name, success=False, message="请先通过报名"))
            return
    ok = game_server.db_manager.set_event_ready(event_id, user_id, ready)
    await _send(
        websocket,
        Response(type=type_name, success=ok, message="已加入等待" if ready else "已取消等待"),
    )


async def _require_admin(game_server, event_id: str, user_id: int):
    event = game_server.db_manager.get_event(event_id)
    if not event:
        return None, "场馆不存在"
    role = game_server.db_manager.get_event_admin_role(event_id, user_id)
    if not role:
        return None, "没有管理权限"
    return event, None


async def _handle_list_ready(game_server, user_id, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    event, err = await _require_admin(game_server, event_id, user_id)
    if err:
        await _send(websocket, Response(type="event/list_ready", success=False, message=err))
        return
    items = game_server.db_manager.list_event_ready_players(event_id)
    await _send(
        websocket,
        Response(type="event/list_ready", success=True, message="ok", ready_players=items),
    )


async def _handle_list_registrations(game_server, user_id, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    _, err = await _require_admin(game_server, event_id, user_id)
    if err:
        await _send(websocket, Response(type="event/list_registrations", success=False, message=err))
        return
    status = (message.get("status") or "").strip() or None
    items = game_server.db_manager.list_event_registrations(event_id, status)
    await _send(
        websocket,
        Response(type="event/list_registrations", success=True, message="ok", registration_list=items),
    )


async def _handle_review_registration(game_server, user_id, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    _, err = await _require_admin(game_server, event_id, user_id)
    if err:
        await _send(websocket, Response(type="event/review_registration", success=False, message=err))
        return
    target_id = int(message.get("user_id") or 0)
    status = str(message.get("status") or "").strip()
    note = str(message.get("review_note") or "")
    row = game_server.db_manager.review_event_registration(event_id, target_id, status, user_id, note)
    if not row:
        await _send(websocket, Response(type="event/review_registration", success=False, message="审核失败"))
        return
    await _send(websocket, Response(type="event/review_registration", success=True, message="已处理"))


async def _handle_create_empty_room(game_server, user_id, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    event, err = await _require_admin(game_server, event_id, user_id)
    if err:
        await _send(websocket, Response(type="event/create_empty_room", success=False, message=err))
        return
    if event.get("status") != "active":
        await _send(websocket, Response(type="event/create_empty_room", success=False, message="场馆未开启"))
        return
    room_rule = str(message.get("room_rule") or "guobiao").strip()
    room_config = message.get("room_config") or {}
    if not isinstance(room_config, dict):
        room_config = {}
    response = await game_server.room_manager.create_empty_event_room(
        event_id=event_id,
        room_rule=room_rule,
        room_config=room_config,
        password=str(message.get("password") or ""),
        created_by=user_id,
    )
    response.type = "event/create_empty_room"
    await _send(websocket, response)


async def _handle_seat_table(game_server, user_id, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    event, err = await _require_admin(game_server, event_id, user_id)
    if err:
        await _send(websocket, Response(type="event/seat_table", success=False, message=err))
        return
    raw_ids = message.get("user_ids") or []
    try:
        user_ids = [int(x) for x in raw_ids]
    except (TypeError, ValueError):
        await _send(websocket, Response(type="event/seat_table", success=False, message="玩家 ID 无效"))
        return
    room_rule = str(message.get("room_rule") or "guobiao").strip()
    room_config = message.get("room_config") if isinstance(message.get("room_config"), dict) else {}
    response = await game_server.room_manager.seat_event_table(
        admin_user_id=user_id,
        event_id=event_id,
        user_ids=user_ids,
        room_rule=room_rule,
        room_config=room_config,
    )
    await _send(websocket, response)


async def _handle_list_records(game_server, message, websocket):
    event_id = str(message.get("event_id") or "").strip()
    event = game_server.db_manager.get_event(event_id)
    if not event:
        await _send(websocket, Response(type="event/list_records", success=False, message="场馆不存在"))
        return
    limit = message.get("limit", 20)
    offset = message.get("offset", 0)
    records = game_server.db_manager.list_event_records(event_id, limit=limit, offset=offset)
    record_list = []
    for game_record in records:
        players_info = []
        for player_data in game_record.get("players") or []:
            players_info.append(Player_record_info(
                user_id=player_data["user_id"],
                username=player_data["username"],
                score=player_data["score"],
                rank=player_data["rank"],
                original_player_index=player_data.get("original_player_index"),
            ))
        record_list.append(Record_info(
            game_id=game_record["game_id"],
            rule=game_record.get("rule") or "",
            sub_rule=game_record.get("sub_rule"),
            match_type=game_record.get("match_type"),
            created_at=game_record["created_at"],
            players=players_info,
        ))
    await _send(
        websocket,
        Response(type="event/list_records", success=True, message="ok", record_list=record_list),
    )
