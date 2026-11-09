"""One eight-member lobby projected over the existing paired four-seat tables."""
from copy import deepcopy
from contextlib import contextmanager

from ..response import Response, public_room_data
from .room_seats import get_seats, seat_player, unseat_player
from .room_teams import invalidate_ready

TABLES = ("closed", "open")
CAPACITY = 8


def pair_for(manager, room):
    if not room or not room.get("duplicate_lobby_room_id"):
        return None
    return manager.game_server.gamestate_manager.sichuan_duplicate_matches.get(room.get("duplicate_match_id"))


def tables(manager, pair):
    result = {table: manager.rooms.get(pair.data["room_ids"][table]) for table in TABLES}
    if any(room is None for room in result.values()):
        raise ValueError("复式房间未完整加载，请刷新后重试")
    return result


def humans(pair, rooms):
    active = set(pair.data["lobby"]["waiting"])
    active.update(uid for room in rooms.values() for uid in room["player_list"] if uid > 10)
    order = pair.data["lobby"]["member_order"]
    return [uid for uid in order if uid in active] + sorted(active.difference(order))


def host(pair, rooms):
    return next(iter(humans(pair, rooms)), 0)


def member_count(pair, rooms):
    return len(humans(pair, rooms)) + sum(uid in (0, 2, 3) for room in rooms.values() for uid in get_seats(room))


def editable(pair, rooms):
    return pair.data["status"] in ("waiting", "interrupted") and not any(
        room.get("is_game_running") or room.get("event_seating_pending") or room.get("admin_closing")
        for room in rooms.values())


def public_lobby(manager, room):
    pair = pair_for(manager, room)
    if pair is None:
        return public_room_data(room)
    rooms = tables(manager, pair)
    lobby = pair.data["lobby"]
    people = humans(pair, rooms)
    root = rooms["open"]
    result = deepcopy(root)
    seats = [uid for table in TABLES for uid in get_seats(rooms[table])]
    settings = {str(uid): deepcopy(lobby.get("settings", {}).get(str(uid), {})) for uid in people}
    for table in TABLES:
        for uid in rooms[table]["player_list"]:
            profiles = rooms[table].get("player_settings", {})
            profile = profiles.get(uid, profiles.get(str(uid)))
            if profile:
                settings[str(uid)] = deepcopy(profile)
    owner = host(pair, rooms)
    result.update(room_id=root["room_id"], instance_id=root["instance_id"], room_name=lobby["name"],
                  max_player=CAPACITY, player_list=people + [uid for uid in seats if 0 <= uid <= 10],
                  seat_list=seats, team_by_seat=[1, 0, 1, 0, 0, 1, 0, 1],
                  host_user_id=owner, host_name=settings.get(str(owner), {}).get("username", ""),
                  player_settings=settings, roster_revision=lobby["revision"],
                  ready_list=list({uid for r in rooms.values() for uid in r.get("ready_list", [])}),
                  is_game_running=any(r.get("is_game_running") for r in rooms.values()),
                  duplicate_table=None, duplicate_peer_room_id=None, duplicate_status=pair.data["status"],
                  duplicate_lobby={"version": 1, "waiting": list(lobby["waiting"]), "can_edit": editable(pair, rooms)})
    # A common room must never disclose the peer's pace, hands, pending results, or wall key.
    result.pop("duplicate_progress", None)
    result.pop("active_gamestate_id", None)
    return public_room_data(result)


def find_member(manager, user_id):
    for pair in getattr(manager.game_server.gamestate_manager, "sichuan_duplicate_matches", {}).values():
        if "lobby" not in pair.data or pair.data.get("rooms_deleted"):
            continue
        try:
            rooms = tables(manager, pair)
        except ValueError:
            continue
        if user_id in humans(pair, rooms):
            return rooms["open"]
    return None


async def broadcast(manager, room):
    pair = pair_for(manager, room)
    rooms = tables(manager, pair)
    response = Response(type="room/refresh_room_info", success=True, message="房间信息更新",
                        room_info=public_lobby(manager, room)).model_dump(exclude_none=True)
    from ..gamestate.public.connection_delivery import send_bounded
    for uid in humans(pair, rooms):
        connection = manager.game_server.user_id_to_connection.get(uid)
        if connection:
            await send_bounded(connection.websocket, response)


def commit(pair, rooms, *, roster=False):
    if roster:
        pair.data["lobby"]["revision"] += 1
        for room in rooms.values():
            invalidate_ready(room)
    candidate = deepcopy(pair.data)
    candidate["room_snapshots"] = deepcopy(rooms)
    pair.save(candidate)
    if roster:
        pair.ready.clear()


@contextmanager
def rollback_on_error(pair, rooms):
    previous, previous_rooms = deepcopy(pair.data), deepcopy(rooms)
    try:
        yield
    except BaseException:
        pair.data = previous
        for table, saved in previous_rooms.items():
            rooms[table].clear()
            rooms[table].update(saved)
        raise


def location(rooms, user_id):
    return next(((table, index) for table, room in rooms.items()
                 for index, uid in enumerate(get_seats(room)) if uid == user_id), None)


def slot(rooms, index):
    if type(index) is not int or not 0 <= index < CAPACITY:
        raise ValueError("座位编号无效")
    table, seat = TABLES[index // 4], index % 4
    return table, seat, rooms[table]


async def join(manager, connection_id, room, password):
    pair = pair_for(manager, room)
    async with pair.lock:
        rooms = tables(manager, pair)
        if pair.data["status"] in ("completed", "cancelled"):
            return Response(type="error_message", success=False, message="该组复式比赛已结束，请加入新的比赛房间")
        player = manager.game_server.players.get(connection_id)
        if player is None or not player.user_id:
            return Response(type="error_message", success=False, message="请先登录")
        if player.user_id in humans(pair, rooms):
            seated = location(rooms, player.user_id)
            player.current_room_id = rooms[seated[0]]["room_id"] if seated else rooms["open"]["room_id"]
            return Response(type="room/refresh_room_info", success=True, message="已恢复房间", room_info=public_lobby(manager, room))
        if not editable(pair, rooms):
            return Response(type="error_message", success=False, message="游戏正在进行中，无法加入")
        root = rooms["open"]
        if root.get("has_password") and manager.room_passwords.get(root["room_id"]) != password:
            return Response(type="error_message", success=False, message="密码错误")
        if member_count(pair, rooms) >= CAPACITY:
            return Response(type="error_message", success=False, message="房间已满")
        blocked = manager._reject_room_entry_conflicts(player.user_id, "加入房间")
        if blocked:
            return blocked
        if player.current_room_id:
            return Response(type="error_message", success=False, message="玩家已在其他房间中，请先离开当前房间")
        if root.get("tourist_limit") and getattr(player, "is_tourist", False):
            return Response(type="error_message", success=False, message="该房间不允许游客加入")
        denied = manager._can_join_venue_room(root.get("event_id"), player.user_id)
        if denied:
            return Response(type="error_message", success=False, message=denied)
        previous, saved_rooms = deepcopy(pair.data), deepcopy(rooms)
        settings = manager.game_server.db_manager.get_user_settings(player.user_id)
        settings = settings if isinstance(settings, dict) else {}
        lobby = pair.data["lobby"]
        lobby["waiting"].append(player.user_id)
        lobby["member_order"].append(player.user_id)
        lobby.setdefault("settings", {})[str(player.user_id)] = dict(
            user_id=player.user_id, username=settings.get("username", player.username),
            **{key: settings.get(key, 1) for key in ("title_id", "profile_image_id", "character_id", "voice_id")})
        try:
            commit(pair, rooms, roster=True)
        except BaseException:
            pair.data = previous
            for table, saved in saved_rooms.items():
                rooms[table].clear()
                rooms[table].update(saved)
            raise
        player.current_room_id = root["room_id"]
    await manager._broadcast_room_info(root["room_id"])
    return Response(type="room/join_room_done", success=True, message="加入房间成功", room_info=public_lobby(manager, root))


async def action(manager, connection_id, room_id, message, *, admin_bot=False):
    room = manager.rooms.get(room_id)
    pair = pair_for(manager, room)
    if pair is None:
        return Response(type="tips", success=False, message="不是八人复式房间")
    try:
        async with pair.lock:
            rooms = tables(manager, pair)
            player = manager.game_server.players.get(connection_id)
            if not admin_bot and (player is None or player.user_id not in humans(pair, rooms)):
                raise ValueError("玩家不在房间中")
            if not editable(pair, rooms):
                raise ValueError("对局中或比赛已结束，阵容已锁定")
            if type(message.get("roster_revision")) is not int or message["roster_revision"] != pair.data["lobby"]["revision"]:
                raise ValueError("房间阵容已变化，请刷新后重新操作")
            kind = message.get("action")
            before, saved_rooms = deepcopy(pair.data), deepcopy(rooms)
            owner = host(pair, rooms)
            actor = owner if admin_bot else player.user_id
            if admin_bot and kind != "add_bot":
                raise ValueError("管理操作无效")
            changed_users = set()
            try:
                if kind == "seat":
                    target_table, target_index, target_room = slot(rooms, message.get("seat_index"))
                    source_index = message.get("source_seat")
                    if source_index is not None:
                        source_table, source_seat, source_room = slot(rooms, source_index)
                        subject = get_seats(source_room)[source_seat]
                    else:
                        subject = message.get("user_id", actor)
                        if type(subject) is not int or subject <= 10 or subject not in humans(pair, rooms):
                            raise ValueError("请选择房间中的玩家")
                        source = location(rooms, subject)
                        source_table, source_seat = source if source else (None, None)
                        source_room = rooms.get(source_table)
                    if subject < 0 or (actor != owner and subject != actor):
                        raise ValueError("只能调整自己的座位")
                    occupant = get_seats(target_room)[target_index]
                    if source_room is target_room and source_seat == target_index:
                        return None
                    if occupant >= 0 and (actor != owner or source_room is None):
                        raise ValueError("该座位已有人，请选择空位或由房主交换")
                    if source_room is not None:
                        unseat_player(source_room, subject, source_seat)
                    else:
                        pair.data["lobby"]["waiting"].remove(subject)
                    if occupant >= 0:
                        unseat_player(target_room, occupant, target_index)
                        seat_player(source_room, occupant, source_seat)
                        changed_users.add(occupant)
                    seat_player(target_room, subject, target_index)
                    changed_users.add(subject)
                    for table_room in rooms.values():
                        for uid in (subject, occupant):
                            if uid >= 0 and uid in table_room["player_list"]:
                                profile = pair.data["lobby"].get("settings", {}).get(str(uid))
                                if uid <= 10:
                                    profile = next((r.get("player_settings", {}).get(uid) for r in saved_rooms.values()
                                                    if r.get("player_settings", {}).get(uid)), None)
                                if profile:
                                    table_room.setdefault("player_settings", {})[uid] = deepcopy(profile)
                elif kind == "stand":
                    subject = message.get("user_id", actor)
                    if type(subject) is not int or subject <= 10 or subject not in humans(pair, rooms):
                        raise ValueError("请选择房间中的玩家")
                    if actor != owner and subject != actor:
                        raise ValueError("只能调整自己的座位")
                    source = location(rooms, subject)
                    if source is None:
                        return None
                    unseat_player(rooms[source[0]], subject, source[1])
                    pair.data["lobby"]["waiting"].append(subject)
                    changed_users.add(subject)
                elif kind == "add_bot":
                    if actor != owner:
                        raise ValueError("只有房主可以添加机器人")
                    if pair.data["status"] != "waiting":
                        raise ValueError("复式比赛当前不能添加机器人")
                    if member_count(pair, rooms) >= CAPACITY:
                        raise ValueError("房间已满")
                    bot = message.get("bot_kind")
                    if type(bot) is not int or bot not in (0, 2):
                        raise ValueError("该规则不支持这种机器人")
                    _, index, target_room = slot(rooms, message.get("seat_index"))
                    seat_player(target_room, bot, index)
                    target_room.setdefault("player_settings", {})[bot] = dict(user_id=bot,
                        username="麻雀罗伯特" if bot == 0 else "牌效罗伯特", title_id=1,
                        profile_image_id=1, character_id=1, voice_id=1)
                elif kind == "remove":
                    if actor != owner:
                        raise ValueError("只有房主可以移除玩家")
                    if message.get("seat_index") is not None:
                        _, index, target_room = slot(rooms, message["seat_index"])
                        subject = get_seats(target_room)[index]
                        if subject < 0:
                            raise ValueError("该座位没有玩家")
                    else:
                        subject = message.get("user_id")
                        target_room = None
                        if type(subject) is not int or subject not in pair.data["lobby"]["waiting"]:
                            raise ValueError("该玩家不在等候区")
                    if subject == actor:
                        raise ValueError("请使用离开房间")
                    if target_room is not None:
                        unseat_player(target_room, subject, index)
                    if subject > 10:
                        lobby = pair.data["lobby"]
                        if subject in lobby["waiting"]:
                            lobby["waiting"].remove(subject)
                        lobby["member_order"].remove(subject)
                        lobby.get("settings", {}).pop(str(subject), None)
                        changed_users.add(subject)
                else:
                    raise ValueError("房间操作无效")
                commit(pair, rooms, roster=True)
            except BaseException:
                pair.data = before
                for table, saved in saved_rooms.items():
                    rooms[table].clear()
                    rooms[table].update(saved)
                raise
            for uid in changed_users:
                connection = manager.game_server.user_id_to_connection.get(uid)
                if connection:
                    source = location(rooms, uid)
                    connection.current_room_id = rooms[source[0]]["room_id"] if source else (
                        rooms["open"]["room_id"] if uid in humans(pair, rooms) else None)
            removed = subject if kind == "remove" and subject > 10 else None
        if removed:
            connection = manager.game_server.user_id_to_connection.get(removed)
            if connection:
                await connection.websocket.send_json(Response(type="room/leave_room_done", success=True,
                    message="您已被移出房间", room_id=rooms["open"]["room_id"],
                    room_instance_id=rooms["open"].get("instance_id")).model_dump(exclude_none=True))
        await manager._broadcast_room_info(rooms["open"]["room_id"])
        return None
    except ValueError as error:
        return Response(type="tips", success=False, message=str(error))


async def ready(manager, connection_id, room, value, revision):
    pair = pair_for(manager, room)
    async with pair.lock:
        rooms = tables(manager, pair)
        player = manager.game_server.players.get(connection_id)
        if not player or player.user_id not in humans(pair, rooms):
            return Response(type="tips", success=False, message="玩家不在房间中")
        if not editable(pair, rooms) or type(value) is not bool:
            return Response(type="tips", success=False, message="当前不能更改准备状态")
        if type(revision) is not int or revision != pair.data["lobby"]["revision"]:
            return Response(type="tips", success=False, message="房间阵容已变化，请刷新后重新操作")
        source = location(rooms, player.user_id)
        if source is None:
            return Response(type="tips", success=False, message="请先入座")
        if player.user_id == host(pair, rooms):
            return Response(type="tips", success=False, message="房主无需准备")
        with rollback_on_error(pair, rooms):
            target = rooms[source[0]].setdefault("ready_list", [])
            if player.user_id in target:
                target.remove(player.user_id)
            if value:
                target.append(player.user_id)
            commit(pair, rooms)
    await manager._broadcast_room_info(rooms["open"]["room_id"])
    return None


async def leave(manager, connection_id, room):
    pair = pair_for(manager, room)
    async with pair.lock:
        rooms = tables(manager, pair)
        player = manager.game_server.players.get(connection_id)
        if not player or player.user_id not in humans(pair, rooms):
            return Response(type="tips", success=False, message="玩家不在房间中")
        if any(r.get("is_game_running") for r in rooms.values()):
            return Response(type="tips", success=False, message="双打对局中阵容已锁定，请通过对局离开或重连")
        with rollback_on_error(pair, rooms):
            uid = player.user_id
            source = location(rooms, uid)
            if source:
                unseat_player(rooms[source[0]], uid, source[1])
            lobby = pair.data["lobby"]
            if uid in lobby["waiting"]:
                lobby["waiting"].remove(uid)
            lobby["member_order"].remove(uid)
            lobby.get("settings", {}).pop(str(uid), None)
            if not humans(pair, rooms):
                for child in rooms.values():
                    manager._remove_all_bots_from_room(child)
            commit(pair, rooms, roster=True)
        player.current_room_id = None
        root = rooms["open"]
    await manager._broadcast_room_info(root["room_id"])
    return Response(type="room/leave_room_done", success=True, message="离开房间成功",
                    room_id=root["room_id"], room_instance_id=root.get("instance_id"))


def validate_start(manager, pair, connection_id, revision):
    rooms = tables(manager, pair)
    player = manager.game_server.players.get(connection_id)
    owner = host(pair, rooms)
    if not player or player.user_id != owner:
        raise ValueError("只有房主能开始游戏")
    if type(revision) is not int or revision != pair.data["lobby"]["revision"]:
        raise ValueError("房间阵容已变化，请刷新后重新操作")
    if pair.data["lobby"]["waiting"] or any(uid < 0 for room in rooms.values() for uid in get_seats(room)):
        raise ValueError("需要八名玩家全部入座")
    for room in rooms.values():
        for uid in room["player_list"]:
            if uid <= 10:
                continue
            if not manager.is_room_member_online(uid):
                raise ValueError("有玩家离线，请等待重连")
            if uid != owner and uid not in room.get("ready_list", []):
                raise ValueError("有玩家尚未准备")
    return rooms


async def setting(manager, connection_id, room, key, value):
    pair = pair_for(manager, room)
    async with pair.lock:
        rooms = tables(manager, pair)
        player = manager.game_server.players.get(connection_id)
        if not player or player.user_id != host(pair, rooms):
            return Response(type="tips", success=False, message="只有房主可以修改房间设置")
        if not editable(pair, rooms):
            return Response(type="tips", success=False, message="请在开局前调整房间设置")
        from ..gamestate.public.ai.pacing import BOT_SPEEDS
        if (key == "bot_speed" and (not isinstance(value, str) or value not in BOT_SPEEDS)) or (key == "claim_protection" and type(value) is not bool):
            return Response(type="tips", success=False, message="房间设置无效")
        with rollback_on_error(pair, rooms):
            for child in rooms.values():
                child[key] = value
            commit(pair, rooms)
    await manager._broadcast_room_info(rooms["open"]["room_id"])
    return None
