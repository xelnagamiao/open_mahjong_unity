"""Shared creation of Sichuan eight-player lobbies over open/closed pairs."""
from copy import deepcopy
import secrets
import uuid

from ..response import Response, public_room_data
from ..game_calculation.sichuan.duplicate_rules import BOARD_COUNTS, make_board, team_by_seat, board_from_tiles, rule_profile
from ..database.sichuan_duplicate import PairedMatchStore
from ..gamestate.public.sichuan_duplicate import PairedMatch, new_match


async def create_pair(manager, event_id, sub_rule, config, password, created_by):
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
    if config.get("random_seed") not in (None, "", 0, "0"):
        return Response(type="tips", success=False, message="复式双打请选择自动生成或输入已有密钥；场景复现请使用普通房间")
    source = None
    if config.get("duplicate_key"):
        from ..database.duplicate_walls import load_duplicate_wall, validate_duplicate_wall
        source = load_duplicate_wall(manager.game_server.db_manager, config["duplicate_key"])
        validate_duplicate_wall(source, {"room_rule": "sichuan", "sub_rule": sub_rule, "event_id": event_id})
        count = source["round_count"]
    layout = rule_profile(sub_rule)["wall_layout"]
    match_id, seed = str(uuid.uuid4()), secrets.token_hex(32)
    rooms = {}
    try:
        for table, label in (("open", "开室"), ("closed", "闭室")):
            room_config = dict(config, allow_spectator=False, game_round=max(1, count // 4),
                               room_name=f"{config.get('room_name') or '复式双打'} · {label}")
            room_config.pop("duplicate_key", None)
            response = await manager.create_empty_event_room(event_id, "sichuan", room_config,
                password=password, created_by=created_by, broadcast=False, _paired_internal=True)
            if not response.success:
                raise ValueError(response.message)
            room_id = response.room_info["room_id"]
            rooms[table] = manager.rooms[room_id]
            # The existing paired lobby retains both children through all-bot
            # completion. Personal pairs use the same lifetime as venue pairs.
            rooms[table]["persist_empty"] = True
            rooms[table].update(count_tips=bool(config.get("count_tips", False)),
                                pointer_tips=bool(config.get("pointer_tips", True)))
        snapshot = new_match(match_id, sub_rule, {table: room["room_id"] for table, room in rooms.items()},
                             [board_from_tiles(tiles, n, layout=layout) for n, tiles in enumerate(source["round_tiles"], 1)] if source else
                             [make_board(seed, n, layout=layout) for n in range(1, count + 1)], seed, sync_mode=sync_mode)
        if source:
            snapshot.update(source_wall_id=source["id"], key=source["key"], wall_type=source["wall_type"])
            if source.get("seed") is not None:
                snapshot["seed"] = source["seed"]
        store = PairedMatchStore(manager.game_server.db_manager)
        for table, room in rooms.items():
            room.update(duplicate_match_id=match_id, duplicate_table=table,
                        duplicate_peer_room_id=rooms["closed" if table == "open" else "open"]["room_id"],
                        duplicate_round_count=count, duplicate_scoring=snapshot["profile"]["scoring"],
                        duplicate_sync_mode=sync_mode,
                        duplicate_status="waiting", is_duplicate=True, allow_spectator=False,
                        team_by_seat=team_by_seat(table), roster_revision=0)
            room["duplicate_lobby_room_id"] = rooms["open"]["room_id"]
        snapshot["lobby"] = dict(version=1, name=config.get("room_name") or "复式双打", revision=0,
                                 waiting=[], member_order=[], settings={})
        snapshot["room_snapshots"] = deepcopy(rooms)
        snapshot["room_password"] = password
        store.create(snapshot, event_id=event_id, owner_id=created_by or 0, name=config.get("room_name") or "复式双打")
        for room in rooms.values():
            room["duplicate_key"] = snapshot["key"]
        manager.game_server.gamestate_manager.sichuan_duplicate_matches[match_id] = PairedMatch(snapshot, store.save)
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
    import asyncio
    from .room_seats import get_seats, is_full_bot_table
    from .room_teams import validate_team_start
    from ..database.duplicate_walls import reserve_duplicate_game
    pair = manager.sichuan_duplicate_matches.get(room.get("duplicate_match_id"))
    if pair is None:
        return Response(type="tips", success=False, message="复式比赛未加载，请联系比赛管理员")
    async with pair.lock:
        if pair.data["status"] == "interrupted" and "lobby" in pair.data and not room.get("event_id"):
            # Personal rooms expose the existing unfinished-board recovery to
            # their host; venue recovery remains an administrator operation.
            from .sichuan_lobby import validate_start
            rm = manager.game_server.room_manager
            try:
                recovering = validate_start(rm, pair, connection_id, lobby_revision)
                if any(r.get("is_game_running") for r in recovering.values()):
                    raise ValueError("请等待两室完成中断清理")
                pair.restart_unfinished_board()
                for r in recovering.values():
                    r.update(duplicate_status="waiting", duplicate_progress=pair.public_info(r["duplicate_table"]))
            except ValueError as error:
                return Response(type="tips", success=False, message=str(error))
        if pair.data["status"] != "waiting":
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
                if pair.data["completed_boards"] and get_seats(other) != [m["user_id"] for m in pair.data["members"][t]]:
                    raise ValueError("续赛须由原阵容按原座位入座")
                if not other.get("duplicate_game_id"):
                    other["duplicate_game_id"] = reserve_duplicate_game(manager.game_server.db_manager, pair.data["wall_id"])
                error = await manager.start_game(None if bot_table else host_connection.Connect_id, other["room_id"], roster_revision=other["roster_revision"], _paired_prepare=True)
                if error is not None:
                    raise ValueError(error.message)
            candidate = deepcopy(pair.data)
            candidate["members"] = {t: [{"user_id": uid, "seat": i, "team_id": team_by_seat(t)[i]}
                                        for i, uid in enumerate(get_seats(r))] for t, r in rooms.items()}
            candidate["room_snapshots"] = deepcopy(rooms)
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
        candidate = deepcopy(pair.data)
        candidate["rooms_deleted"] = True
        if not pair.completed:
            candidate["status"] = "cancelled"
        pair.save(candidate)
        pair.ready.clear()
        if "lobby" in pair.data:
            from .sichuan_lobby import humans, tables
            root = manager.rooms[pair.data["room_ids"]["open"]]
            response = Response(type="room/leave_room_done", success=True, message="房间已解散",
                                room_id=root["room_id"], room_instance_id=root.get("instance_id"))
            for uid in humans(pair, tables(manager, pair)):
                connection = manager.game_server.user_id_to_connection.get(uid)
                if connection:
                    connection.current_room_id = None
                    await connection.websocket.send_json(response.model_dump(exclude_none=True))
        for rid in pair.data["room_ids"].values():
            await manager.destroy_room(rid)
    return Response(type="tips", success=True, message="开室、闭室已整组删除")


async def restart_pair(manager, room_id, event_id):
    room = manager.rooms.get(room_id)
    if not room or room.get("event_id") != event_id or not room.get("duplicate_match_id"):
        raise ValueError("复式房间不属于该比赛场")
    pair = manager.game_server.gamestate_manager.sichuan_duplicate_matches[room["duplicate_match_id"]]
    async with pair.lock:
        rooms = [manager.rooms[rid] for rid in pair.data["room_ids"].values()]
        if any(r.get("is_game_running") for r in rooms):
            raise ValueError("请等待两室完成中断清理")
        pair.restart_unfinished_board()
        for r in rooms:
            r.update(duplicate_status="waiting", ready_list=[], duplicate_progress=pair.public_info(r["duplicate_table"]))
            r["roster_revision"] += 1
            await manager._broadcast_room_info(r["room_id"])
    return {"success": True, "message": "未配对完成的副已为两室重新发牌，已配对结算的分数保留；原选手入座准备后续赛"}


def restore_pairs(server):
    """Restore durable pairs after process restart, without resuming exposed hands."""
    store = PairedMatchStore(server.db_manager)
    for snapshot in store.unfinished():
        if snapshot.get("rooms_deleted"):
            continue
        pair = PairedMatch(snapshot, store.save)
        if pair.completed:
            from types import SimpleNamespace
            for table in ("open", "closed"):
                if pair.data.get("record_ids", {}).get(table):
                    continue
                record = pair.data.get("final_records", {}).get(table)
                if record is None:
                    # A process may stop after the final barrier commit but
                    # before either table reaches its record-saving hook.
                    from ..gamestate.game_sichuan.DuplicateGameState import DuplicateMixin
                    checkpoint = pair.data.get("checkpoints", {}).get(table)
                    if not checkpoint or not checkpoint.get("players"):
                        continue
                    players = [SimpleNamespace(**p, record_counter=SimpleNamespace(rank_result=0)) for p in checkpoint["players"]]
                    recovered = SimpleNamespace(duplicate_match=pair, duplicate_table=table, player_list=players)
                    DuplicateMixin._finish_team_match(recovered)
                    record = recovered.game_record
                else:
                    players = [SimpleNamespace(**{k: v for k, v in p.items() if k != "rank"},
                        record_counter=SimpleNamespace(rank_result=p["rank"])) for p in pair.data["final_players"][table]]
                room_type = (pair.data.get("room_snapshots", {}).get(table) or {}).get("room_type", "events")
                record_id = server.db_manager.store_sichuan_game_record(record, players, room_type, "normal")
                candidate = deepcopy(pair.data)
                candidate.setdefault("final_records", {})[table] = deepcopy(record)
                candidate.setdefault("final_players", {})[table] = [dict(
                    user_id=p.user_id, username=p.username, score=p.score, original_player_index=p.original_player_index,
                    rank=p.record_counter.rank_result) for p in players]
                candidate.setdefault("record_ids", {})[table] = record_id
                pair.save(candidate)
        if snapshot["status"] == "running":
            pair.interrupt("server_restart")
        server.gamestate_manager.sichuan_duplicate_matches[snapshot["id"]] = pair
        if "lobby" in pair.data:
            pair.data["lobby"].update(waiting=[], member_order=[], settings={})
            pair.data["lobby"]["revision"] += 1
        for table, saved in snapshot["room_snapshots"].items():
            room = deepcopy(saved)
            room.update(instance_id=str(uuid.uuid4()), player_list=[], seat_list=[-1]*4,
                        ready_list=[], host_user_id=None, player_settings={}, is_game_running=False,
                        duplicate_key=snapshot["key"], duplicate_status=pair.data["status"],
                        duplicate_sync_mode=pair.synchronous, duplicate_progress=pair.public_info(table))
            room.pop("active_gamestate_id", None)
            room["roster_revision"] = room.get("roster_revision", 0) + 1
            server.room_manager.rooms[room["room_id"]] = room
            if snapshot.get("room_password"):
                server.room_manager.room_passwords[room["room_id"]] = snapshot["room_password"]
