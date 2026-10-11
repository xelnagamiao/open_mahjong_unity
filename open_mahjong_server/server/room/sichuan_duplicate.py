"""Only event administration can create a Sichuan open/closed pair."""
from copy import deepcopy
import secrets
import uuid

from ..response import Response, public_room_data
from ..game_calculation.sichuan.duplicate_rules import BOARD_COUNTS, make_board, team_by_seat
from ..database.sichuan_duplicate import PairedMatchStore
from ..gamestate.public.sichuan_duplicate import PairedMatch, new_match


async def create_pair(manager, event_id, sub_rule, config, password, created_by):
    event = manager.game_server.db_manager.get_event(event_id)
    if not event or event.get("kind", "event") != "event":
        return Response(type="tips", success=False, message="复式双打只能在比赛场中创建")
    count = config.get("duplicate_round_count", 8 if sub_rule == "sichuan/doubles_duplicate" else 16)
    if type(count) is not int or count not in BOARD_COUNTS:
        return Response(type="tips", success=False, message="复式副数必须为 1、4、8、12 或 16")
    if config.get("random_seed") or config.get("duplicate_key"):
        return Response(type="tips", success=False, message="复式双打的牌墙由比赛后台统一生成")
    match_id, seed = str(uuid.uuid4()), secrets.token_hex(32)
    rooms = {}
    try:
        for table, label in (("open", "开室"), ("closed", "闭室")):
            room_config = dict(config, allow_spectator=False, game_round=max(1, count // 4),
                               room_name=f"{config.get('room_name') or '复式双打'} · {label}")
            response = await manager.create_empty_event_room(event_id, "sichuan", room_config,
                password=password, created_by=created_by, broadcast=False, _paired_internal=True)
            if not response.success:
                raise ValueError(response.message)
            room_id = response.room_info["room_id"]
            rooms[table] = manager.rooms[room_id]
        snapshot = new_match(match_id, sub_rule, {table: room["room_id"] for table, room in rooms.items()},
                             [make_board(seed, n) for n in range(1, count + 1)], seed)
        store = PairedMatchStore(manager.game_server.db_manager)
        for table, room in rooms.items():
            room.update(duplicate_match_id=match_id, duplicate_table=table,
                        duplicate_peer_room_id=rooms["closed" if table == "open" else "open"]["room_id"],
                        duplicate_round_count=count, duplicate_scoring=snapshot["profile"]["scoring"],
                        duplicate_status="waiting", is_duplicate=True, allow_spectator=False,
                        team_by_seat=team_by_seat(table), roster_revision=0)
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
    return Response(type="room/create_room_done", success=True, message="开室、闭室已成对创建",
                    room_info=public_room_data(rooms["open"]))


async def request_start(manager, room, connection_id):
    import asyncio
    from .room_seats import get_seats
    from .room_teams import validate_team_start
    from ..database.duplicate_walls import reserve_duplicate_game
    pair = manager.sichuan_duplicate_matches.get(room.get("duplicate_match_id"))
    if pair is None:
        return Response(type="tips", success=False, message="复式比赛未加载，请联系比赛管理员")
    async with pair.lock:
        if pair.data["status"] != "waiting":
            return Response(type="tips", success=False, message="复式比赛已经开始或已中断")
        table = room["duplicate_table"]
        pair.ready[table] = (room["roster_revision"], tuple(get_seats(room)))
        rooms = {t: manager.game_server.room_manager.rooms.get(rid) for t, rid in pair.data["room_ids"].items()}
        for t, other in rooms.items():
            if other is None:
                return Response(type="tips", success=False, message="配对房间不存在")
            if pair.ready.get(t) != (other.get("roster_revision"), tuple(get_seats(other))) or not manager.game_server.room_manager.all_players_ready(other):
                pair.ready.pop(t, None)
                other["duplicate_status"] = "waiting"
        if len(pair.ready) != 2:
            room["duplicate_status"] = "ready"
            await manager.game_server.room_manager._broadcast_room_info(room["room_id"])
            return Response(type="tips", success=True, message="本室已就绪，等待另一室房主开始")
        try:
            human_ids = [uid for r in rooms.values() for uid in get_seats(r) if uid > 10]
            if len(human_ids) != 8:
                raise ValueError("双打复式需要八名真人选手")
            if len(human_ids) != len(set(human_ids)):
                raise ValueError("同一玩家不能同时参加开室和闭室")
            for t, other in rooms.items():
                if pair.ready.get(t) != (other.get("roster_revision"), tuple(get_seats(other))):
                    raise ValueError("房间阵容已变化，请两室重新准备")
                validate_team_start(other, other["roster_revision"])
                host_connection = manager.game_server.user_id_to_connection.get(other["host_user_id"])
                if not host_connection:
                    raise ValueError("两室房主均须在线")
                if pair.data["completed_boards"] and get_seats(other) != [m["user_id"] for m in pair.data["members"][t]]:
                    raise ValueError("续赛须由原八名选手按原座位入座")
                if not other.get("duplicate_game_id"):
                    other["duplicate_game_id"] = reserve_duplicate_game(manager.game_server.db_manager, pair.data["wall_id"])
                error = await manager.start_game(host_connection.Connect_id, other["room_id"], roster_revision=other["roster_revision"], _paired_prepare=True)
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


async def delete_pair(manager, room):
    pair = manager.game_server.gamestate_manager.sichuan_duplicate_matches.get(room["duplicate_match_id"])
    if not pair:
        return Response(type="tips", success=False, message="配对状态未加载，不能单独删除房间")
    async with pair.lock:
        rooms = [manager.rooms.get(rid) for rid in pair.data["room_ids"].values()]
        if any(r and (r.get("is_game_running") or r.get("event_seating_pending")) for r in rooms):
            return Response(type="tips", success=False, message="开室或闭室仍在对局或组桌，暂不能删除整组")
        candidate = deepcopy(pair.data)
        candidate["rooms_deleted"] = True
        if not pair.completed:
            candidate["status"] = "cancelled"
        pair.save(candidate)
        pair.ready.clear()
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
            r.update(duplicate_status="waiting", ready_list=[], duplicate_progress=pair.public_info())
            r["roster_revision"] += 1
            await manager._broadcast_room_info(r["room_id"])
    return {"success": True, "message": "未完成副已为两室同时换牌，已完成副的分数保留；原选手入座准备后续赛"}


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
                record_id = server.db_manager.store_sichuan_game_record(record, players, "events", "normal")
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
        for table, saved in snapshot["room_snapshots"].items():
            room = deepcopy(saved)
            room.update(instance_id=str(uuid.uuid4()), player_list=[], seat_list=[-1]*4,
                        ready_list=[], host_user_id=None, player_settings={}, is_game_running=False,
                        duplicate_key=snapshot["key"], duplicate_status=pair.data["status"],
                        duplicate_progress=pair.public_info())
            room.pop("active_gamestate_id", None)
            room["roster_revision"] = room.get("roster_revision", 0) + 1
            server.room_manager.rooms[room["room_id"]] = room
            if snapshot.get("room_password"):
                server.room_manager.room_passwords[room["room_id"]] = snapshot["room_password"]
