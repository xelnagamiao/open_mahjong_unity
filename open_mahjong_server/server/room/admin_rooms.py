"""平台管理端的房间快照和关闭操作；沿用房间、对局各自的清理入口。"""
import uuid
from .room_seats import get_seats


def list_admin_rooms(manager):
    items = []
    for room in list(manager.rooms.values()):
        room.setdefault("instance_id", str(uuid.uuid4()))
        seats = get_seats(room)
        settings = room.get("player_settings", {})
        players = []
        for seat, uid in enumerate(seats):
            if uid < 0:
                continue
            profile = settings.get(uid, settings.get(str(uid), {}))
            players.append({"user_id": uid, "seat_index": seat,
                            "username": profile.get("username", str(uid)), "is_bot": uid <= 10,
                            "online": uid > 10 and manager.is_room_member_online(uid)})
        items.append({key: room.get(key) for key in (
            "room_id", "instance_id", "room_name", "room_type", "room_rule", "sub_rule",
            "event_id", "host_user_id", "host_name", "max_player", "is_game_running",
            "active_gamestate_id", "duplicate_match_id", "duplicate_table", "duplicate_status",
            "event_seating_pending", "created_by_admin")})
        items[-1].update(players=players, online_count=sum(p["online"] for p in players),
                         persist_empty=manager._is_persist_empty_room(room))
    return items


async def close_admin_room(manager, room_id, instance_id):
    room = manager.rooms.get(room_id)
    if room is None:
        raise LookupError("房间不存在或已关闭")
    get_seats(room)
    if room.get("instance_id") != instance_id:
        raise ValueError("房间已发生变化，请刷新列表后重试")
    gm = manager.game_server.gamestate_manager
    targets = [room]
    if room.get("duplicate_match_id"):
        pair = gm.sichuan_duplicate_matches.get(room["duplicate_match_id"])
        if pair is None:
            raise ValueError("配对状态未加载，不能单独关闭房间")
        targets = [manager.rooms[rid] for rid in pair.data["room_ids"].values() if rid in manager.rooms]
        if any(target.get("duplicate_match_id") != room["duplicate_match_id"] for target in targets):
            raise ValueError("配对房间已发生变化，请刷新列表后重试")
    expected_rooms = {target["room_id"]: target for target in targets}
    if room.get("duplicate_match_id"):
        for rid in pair.data["room_ids"].values():
            expected_rooms.setdefault(rid, None)
    if any(r.get("event_seating_pending") for r in targets):
        raise ValueError("自动匹配正在组桌，请稍后关闭")
    if any(r.get("admin_closing") for r in targets):
        raise ValueError("房间正在关闭，请刷新列表")
    # 在第一个 await 前封住开局和入房；房间号复用由 instance_id 校验保护。
    for target in targets:
        target["admin_closing"] = True
    try:
        for target in targets:
            state = gm.get_game_state_by_room_id(target["room_id"])
            if state is not None:
                await gm.cleanup_game_state_complete(gamestate_id=state.gamestate_id, reason="admin_room_closed")
            target["is_game_running"] = False
        # 清理对局包含 await；其它管理入口可能已删房，不能影响期间复用号码的新房。
        if any(manager.rooms.get(rid) is not original and manager.rooms.get(rid) is not None
               for rid, original in expected_rooms.items()):
            raise ValueError("房间已发生变化，请刷新列表后重试")
        if room.get("duplicate_match_id"):
            from .sichuan_duplicate import delete_pair
            response = await delete_pair(manager, room)
            if not response.success:
                raise ValueError(response.message)
        else:
            await manager.destroy_room(room_id)
        return [target["room_id"] for target in targets]
    finally:
        for target in targets:
            target.pop("admin_closing", None)
