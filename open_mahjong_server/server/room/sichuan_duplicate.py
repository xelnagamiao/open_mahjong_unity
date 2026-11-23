"""Shared creation of Sichuan eight-player lobbies over open/closed pairs."""
from copy import deepcopy
import asyncio
import secrets
import uuid
import re

from ..response import Response
from ..game_calculation.sichuan.duplicate_rules import BOARD_COUNTS, make_board, team_by_seat, board_from_tiles, rule_profile
from ..database.sichuan_duplicate import attach_paired_wall
from ..gamestate.public.sichuan_duplicate import PairedMatch, new_match


async def create_pair(manager, event_id, sub_rule, config, password, created_by):
    from .sichuan_timing import timing_config
    try:
        config = {**config, **timing_config(sub_rule, config)}
    except ValueError as error:
        return Response(type="tips", success=False, message=f"房间配置无效: {error}")
    if event_id:
        event = manager.game_server.db_manager.get_event(event_id)
        if not event or event.get("kind", "event") not in ("event", "base"):
            return Response(type="tips", success=False, message="赛事或基地不存在")
    elif not created_by or created_by <= 10:
        return Response(type="tips", success=False, message="请先登录")
    count = config.get("duplicate_round_count", 8 if sub_rule == "sichuan/doubles_duplicate" else 16)
    if type(count) is not int or count not in BOARD_COUNTS:
        return Response(type="tips", success=False, message="复式副数必须为 1、4、8、12 或 16")
    sync_mode = config.get("duplicate_sync_mode", False)
    if type(sync_mode) is not bool:
        return Response(type="tips", success=False, message="复式同步模式必须为布尔值")
    wall_source = config.get("wall_source")
    if wall_source is not None and wall_source not in ("training", "key", "seed", "manual"):
        return Response(type="tips", success=False, message="复式牌山来源无效")
    training = wall_source == "training"
    reuse = config.get("reuse_training_room", False)
    if type(reuse) is not bool or (reuse and not training):
        return Response(type="tips", success=False, message="重复使用仅支持训练牌山，且必须为布尔值")
    if reuse and not event_id:
        return Response(type="tips", success=False, message="重复使用仅在赛事或基地的训练房间开放")
    if reuse and config.get("random_seed") not in (None, "", 0, "0"):
        return Response(type="tips", success=False, message="重复使用训练房时不能指定随机种子")
    if "allow_spectator" in config and type(config["allow_spectator"]) is not bool:
        return Response(type="tips", success=False, message="allow_spectator必须为布尔值")
    if training and config.get("duplicate_key"):
        return Response(type="tips", success=False, message="训练牌山不能同时使用复式密钥")
    if not training and config.get("random_seed") not in (None, "", 0, "0"):
        return Response(type="tips", success=False, message="复式双打请选择自动生成或输入已有密钥；场景复现请使用普通房间")
    if training and not reuse:
        from ..gamestate.public.random_seed_manager import parse_user_master_seed
        try:
            raw_seed = config.get("random_seed")
            if not isinstance(raw_seed, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", raw_seed):
                raise ValueError("训练牌山需要非零的64位十六进制种子")
            parsed_seed = parse_user_master_seed(raw_seed)
            if not parsed_seed:
                raise ValueError("训练牌山需要非零的64位十六进制种子")
        except ValueError as error:
            return Response(type="tips", success=False, message=str(error))
        seed = format(parsed_seed, "064x")
    else:
        seed = secrets.token_hex(32)
    source = None
    if config.get("duplicate_key"):
        from ..database.duplicate_walls import load_duplicate_wall, validate_duplicate_wall
        source = load_duplicate_wall(manager.game_server.db_manager, config["duplicate_key"])
        validate_duplicate_wall(source, {"room_rule": "sichuan", "sub_rule": sub_rule, "event_id": event_id})
        if wall_source is not None and wall_source != source["wall_type"]:
            return Response(type="tips", success=False, message="所选密钥的牌山类型与来源不一致")
        wall_source = source["wall_type"]
        count = source["round_count"]
    elif wall_source in ("seed", "manual"):
        return Response(type="tips", success=False, message="复现牌山和手动牌山需要对应类型的已有密钥")
    wall_source = wall_source or "key"
    allow_spectator = config.get("allow_spectator", True) if training else False
    layout = rule_profile(sub_rule)["wall_layout"]
    match_id = str(uuid.uuid4())
    rooms = {}
    try:
        for table, label in (("open", "开室"), ("closed", "闭室")):
            room_config = dict(config, sub_rule=sub_rule, allow_spectator=allow_spectator, game_round=max(1, count // 4),
                               room_name=f"{config.get('room_name') or '复式双打'} · {label}")
            room_config.pop("duplicate_key", None)
            response = await manager.create_empty_event_room(event_id, "sichuan", room_config,
                password=password, created_by=created_by, broadcast=False, _paired_internal=True)
            if not response.success:
                raise ValueError(response.message)
            room_id = response.room_info["room_id"]
            rooms[table] = manager.rooms[room_id]
            # Both children must survive until the pair closes them together.
            rooms[table]["persist_empty"] = True
            rooms[table].update(count_tips=bool(config.get("count_tips", False)),
                                pointer_tips=bool(config.get("pointer_tips", True)))
        snapshot = new_match(match_id, sub_rule, {table: room["room_id"] for table, room in rooms.items()},
                             [board_from_tiles(tiles, n, layout=layout) for n, tiles in enumerate(source["round_tiles"], 1)] if source else
                             [make_board(seed, n, layout=layout) for n in range(1, count + 1)], seed, sync_mode=sync_mode)
        snapshot.update(wall_source=wall_source, wall_type=wall_source, allow_spectator=allow_spectator)
        if source:
            snapshot.update(source_wall_id=source["id"], key=source["key"], wall_type=source["wall_type"])
            if source.get("seed") is not None:
                snapshot["seed"] = source["seed"]
        for table, room in rooms.items():
            room.update(duplicate_match_id=match_id, duplicate_table=table,
                        duplicate_peer_room_id=rooms["closed" if table == "open" else "open"]["room_id"],
                        duplicate_round_count=count, duplicate_scoring=snapshot["profile"]["scoring"],
                        duplicate_sync_mode=sync_mode,
                        duplicate_status="waiting", is_duplicate=True, allow_spectator=allow_spectator,
                        wall_source=wall_source, duplicate_wall_type=wall_source, reuse_training_room=reuse,
                        team_by_seat=team_by_seat(table), roster_revision=0)
            room["duplicate_lobby_room_id"] = rooms["open"]["room_id"]
        snapshot["lobby"] = dict(version=1, name=config.get("room_name") or "复式双打", revision=0,
                                 waiting=[], member_order=[], settings={})
        snapshot.update(event_id=event_id, owner_user_id=created_by, reuse_training_room=reuse)
        if not training:
            attach_paired_wall(manager.game_server.db_manager, snapshot, event_id=event_id,
                               owner_id=created_by or 0, name=snapshot["lobby"]["name"])
            for table, room in rooms.items():
                room.update(duplicate_key=snapshot["key"], duplicate_game_id=snapshot["table_record_ids"][table])
        manager.game_server.gamestate_manager.sichuan_duplicate_matches[match_id] = PairedMatch(snapshot)
    except BaseException:
        # Neither room has been published yet. Roll back both in-memory rooms.
        for room in rooms.values():
            manager.rooms.pop(room["room_id"], None)
            manager.room_passwords.pop(room["room_id"], None)
        raise
    for room in rooms.values():
        await manager._broadcast_room_info(room["room_id"])
    from .sichuan_lobby import public_lobby
    return Response(type="room/create_room_done", success=True, message="八人复式房间已创建",
                    room_info=public_lobby(manager, rooms["open"]))


async def request_start(manager, room, connection_id, lobby_revision=None):
    from .room_seats import get_seats, is_full_bot_table
    from .room_teams import validate_team_start
    pair = manager.sichuan_duplicate_matches.get(room.get("duplicate_match_id"))
    if pair is None:
        return Response(type="tips", success=False, message="复式比赛未加载，请联系比赛管理员")
    async with pair.lock:
        if (manager.sichuan_duplicate_matches.get(pair.data["id"]) is not pair or
                pair.data["status"] != "waiting" or pair.data["completed_boards"]):
            return Response(type="tips", success=False, message="复式比赛已经开始或已中断")
        table = room["duplicate_table"]
        pair.ready[table] = (room["roster_revision"], tuple(get_seats(room)))
        rooms = {t: manager.game_server.room_manager.rooms.get(rid) for t, rid in pair.data["room_ids"].items()}
        if "lobby" in pair.data:
            from .sichuan_lobby import validate_start
            try:
                validate_start(manager.game_server.room_manager, pair, connection_id, lobby_revision)
            except ValueError as error:
                pair.ready.clear()
                return Response(type="tips", success=False, message=str(error))
            pair.ready = {t: (r["roster_revision"], tuple(get_seats(r))) for t, r in rooms.items()}
        for t, other in rooms.items():
            if other is None:
                return Response(type="tips", success=False, message="配对房间不存在")
            # An all-bot peer has no human host to press Start. Its fixed roster
            # becomes ready when the other table's host starts the pair.
            if is_full_bot_table(other):
                pair.ready[t] = (other["roster_revision"], tuple(get_seats(other)))
            if pair.ready.get(t) != (other.get("roster_revision"), tuple(get_seats(other))) or not manager.game_server.room_manager.all_players_ready(other):
                pair.ready.pop(t, None)
                other["duplicate_status"] = "waiting"
        if len(pair.ready) != 2:
            room["duplicate_status"] = "ready"
            await manager.game_server.room_manager._broadcast_room_info(room["room_id"])
            return Response(type="tips", success=True, message="本室已就绪，等待另一室房主开始")
        try:
            human_ids = [uid for r in rooms.values() for uid in get_seats(r) if uid > 10]
            if len(human_ids) != len(set(human_ids)):
                raise ValueError("同一玩家不能同时参加开室和闭室")
            for t, other in rooms.items():
                if pair.ready.get(t) != (other.get("roster_revision"), tuple(get_seats(other))):
                    raise ValueError("房间阵容已变化，请两室重新准备")
                validate_team_start(other, other["roster_revision"])
                bot_table = is_full_bot_table(other)
                manager.game_server.room_manager._sync_room_host(other)
                host_connection = manager.game_server.user_id_to_connection.get(other.get("host_user_id"))
                if not bot_table and not host_connection:
                    raise ValueError("有真人的房间，其房主须在线")
                error = await manager.start_game(None if bot_table else host_connection.Connect_id, other["room_id"], roster_revision=other["roster_revision"], _paired_prepare=True)
                if error is not None:
                    raise ValueError(error.message)
            candidate = deepcopy(pair.data)
            candidate["members"] = {t: [{"user_id": uid, "seat": i, "team_id": team_by_seat(t)[i]}
                                        for i, uid in enumerate(get_seats(r))] for t, r in rooms.items()}
            candidate["status"] = "running"
            pair.save(candidate)
            for state in pair.states.values():
                state.game_task = asyncio.create_task(manager.run_game(state, state.run_game_loop))
            for other in rooms.values():
                other["duplicate_status"] = "running"
                await manager.game_server.room_manager._broadcast_room_info(other["room_id"])
            pair.start_gate.set()
            return None
        except BaseException as error:
            pair.interrupt("start_failed")
            for state in list(pair.states.values()):
                await manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id, reason="start_failed")
            rm = manager.game_server.room_manager
            if manager.sichuan_duplicate_matches.get(pair.data["id"]) is pair:
                await finish_pair(rm, room, pair)
            if isinstance(error, asyncio.CancelledError):
                raise
            return Response(type="tips", success=False, message=f"复式双桌开局失败：{error}")


async def delete_pair(manager, room, *, only_if_empty=False):
    pair = manager.game_server.gamestate_manager.sichuan_duplicate_matches.get(room["duplicate_match_id"])
    if not pair:
        return Response(type="tips", success=False, message="配对状态未加载，不能单独删除房间")
    async with pair.lock:
        rooms = [manager.rooms.get(rid) for rid in pair.data["room_ids"].values()]
        if only_if_empty:
            from .sichuan_lobby import humans
            if (manager.rooms.get(room["room_id"]) is not room or room.get("event_id")
                    or room.get("room_type") != "custom" or "lobby" not in pair.data
                    or any(r is None or r.get("duplicate_match_id") != pair.data["id"] for r in rooms)):
                return Response(type="tips", success=False, message="房间已变化，未自动关闭")
            table_rooms = {table: manager.rooms[rid] for table, rid in pair.data["room_ids"].items()}
            if humans(pair, table_rooms):
                return Response(type="tips", success=False, message="房间仍有玩家，未自动关闭")
        if any(r and (r.get("is_game_running") or r.get("event_seating_pending")) for r in rooms):
            return Response(type="tips", success=False, message="开室或闭室仍在对局或组桌，暂不能删除整组")
        if not all(r and r.get("duplicate_match_id") == pair.data["id"] for r in rooms):
            return Response(type="tips", success=False, message="房间已变化，请刷新")
        await close_pair(manager, pair)
    return Response(type="tips", success=True, message="开室、闭室已整组删除")


async def close_pair(manager, pair):
    from .sichuan_lobby import humans, tables
    from ..gamestate.public.connection_delivery import send_bounded

    rooms = tables(manager, pair)
    root = rooms["open"]
    if not pair.completed:
        pair.data["status"] = "cancelled"
    pair.data["rooms_deleted"] = True
    manager.game_server.gamestate_manager.sichuan_duplicate_matches.pop(pair.data["id"], None)
    # Remove both rooms and detach all connections before delivering any packets.
    response = Response(type="room/leave_room_done", success=True, message="复式房间已结束",
                        room_id=root["room_id"], room_instance_id=root.get("instance_id"))
    members = humans(pair, rooms) if "lobby" in pair.data else {
        uid for child in rooms.values() for uid in child["player_list"] if uid > 10}
    connections = []
    for uid in members:
        conn = manager.game_server.user_id_to_connection.get(uid)
        if conn and getattr(conn, "current_room_id", None) in pair.data["room_ids"].values():
            conn.current_room_id = None
            connections.append(conn)
    for rid in pair.data["room_ids"].values():
        manager.rooms.pop(rid, None)
        manager.room_passwords.pop(rid, None)
    await asyncio.gather(*(send_bounded(conn.websocket, response.model_dump(exclude_none=True))
                           for conn in connections), return_exceptions=True)


async def finish_pair(manager, room, pair):
    """The last table releases the group, or resets only the reusable lobby."""
    from .sichuan_lobby import humans, tables
    rooms = tables(manager, pair)
    if any(child.get("is_game_running") for child in rooms.values()):
        return
    hub = getattr(manager.game_server.gamestate_manager, "match_spectator_hub", None)
    if hub:
        hub.feeds.pop(pair.data["id"], None)
    if any(child.get("admin_closing") for child in rooms.values()):
        return  # The administrator owns group closure.
    if not pair.data.get("reuse_training_room"):
        await close_pair(manager, pair)
        return
    lobby = deepcopy(pair.data["lobby"])
    lobby["waiting"] = [uid for uid in lobby["waiting"] if manager.is_room_member_online(uid)]
    members = {uid for uid in humans(pair, rooms) if manager.is_room_member_online(uid)}
    lobby["member_order"] = [uid for uid in lobby["member_order"] if uid in members]
    lobby["settings"] = {str(uid): lobby["settings"][str(uid)] for uid in members if str(uid) in lobby["settings"]}
    if not room.get("event_id") and not members:
        await close_pair(manager, pair)
        return
    old = pair.data
    seed = secrets.token_hex(32)
    count = old["planned_boards"]
    profile = old["profile"]
    mid = str(uuid.uuid4())
    data = new_match(mid, profile["sub_rule"], old["room_ids"],
                     [make_board(seed, i, layout=profile["wall_layout"]) for i in range(1, count + 1)],
                     seed, sync_mode=pair.synchronous)
    data.update(lobby=lobby, wall_source="training", wall_type="training",
                allow_spectator=old["allow_spectator"], event_id=old.get("event_id"),
                owner_user_id=old.get("owner_user_id"), reuse_training_room=True)
    data["lobby"]["revision"] += 1
    matches = manager.game_server.gamestate_manager.sichuan_duplicate_matches
    matches.pop(old["id"])
    matches[mid] = PairedMatch(data)
    for child in rooms.values():
        if not members:
            manager._remove_all_bots_from_room(child)
        child.update(duplicate_match_id=mid, duplicate_status="waiting", ready_list=[],
                     team_by_seat=team_by_seat(child["duplicate_table"]))
        child.pop("duplicate_progress", None)
        child["roster_revision"] += 1
    await manager._broadcast_room_info(rooms["open"]["room_id"])
