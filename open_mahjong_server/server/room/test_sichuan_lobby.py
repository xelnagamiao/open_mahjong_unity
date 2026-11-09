from copy import deepcopy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .test_sichuan_duplicate import rooms, run_async
from .sichuan_lobby import action, public_lobby, pair_for, tables, member_count


async def create(rooms):
    result = await rooms.create_empty_event_room("evt_pair", "sichuan",
        {"sub_rule": "sichuan/doubles_duplicate", "duplicate_round_count": 4, "room_name": "八人测试"}, created_by=101)
    assert result.success
    root = rooms.rooms[result.room_info["room_id"]]
    for uid in range(101, 111):
        connection = NS(user_id=uid, username=f"P{uid}", current_room_id=None,
                        Connect_id=f"u{uid}", websocket=NS(send_json=AsyncMock()))
        rooms.game_server.players[connection.Connect_id] = connection
        rooms.game_server.user_id_to_connection[uid] = connection
    return root


async def join(rooms, root, uid):
    return await rooms.join_room(f"u{uid}", root["room_id"], "")


async def act(rooms, root, uid, kind, **fields):
    return await action(rooms, f"u{uid}", root["room_id"], dict(action=kind,
        roster_revision=pair_for(rooms, root).data["lobby"]["revision"], **fields))


@run_async
async def test_one_public_room_eight_waiters_and_no_automatic_seating(rooms):
    root = await create(rooms)
    for uid in range(101, 109):
        assert (await join(rooms, root, uid)).success
    info = public_lobby(rooms, root)
    assert len(rooms.rooms) == 2 and all(r["max_player"] == 4 for r in rooms.rooms.values())
    assert info["max_player"] == 8 and len(info["player_list"]) == 8
    assert info["seat_list"] == [-1] * 8
    assert info["duplicate_lobby"]["waiting"] == list(range(101, 109))
    assert info["room_name"] == "八人测试" and info["host_user_id"] == 101
    assert len(rooms.list_event_rooms("evt_pair")) == len(rooms.get_room_list(event_id="evt_pair").room_list) == 1
    assert not (await join(rooms, root, 109)).success
    assert not (await act(rooms, root, 101, "add_bot", seat_index=0, bot_kind=0)).success


@run_async
async def test_self_seating_cross_table_move_and_stand_preserve_capacity(rooms):
    root = await create(rooms)
    await join(rooms, root, 101)
    assert await act(rooms, root, 101, "seat", seat_index=0) is None
    assert public_lobby(rooms, root)["team_by_seat"][0] == 1
    assert await act(rooms, root, 101, "seat", seat_index=6) is None
    assert public_lobby(rooms, root)["seat_list"] == [-1] * 6 + [101, -1]
    assert rooms.game_server.players["u101"].current_room_id == root["room_id"]
    assert await act(rooms, root, 101, "stand") is None
    assert public_lobby(rooms, root)["duplicate_lobby"]["waiting"] == [101]
    assert public_lobby(rooms, root)["player_list"] == [101]


@run_async
async def test_host_moves_waiting_player_and_swaps_occupied_tables(rooms):
    root = await create(rooms)
    for uid in (101, 102, 103):
        await join(rooms, root, uid)
    await act(rooms, root, 101, "seat", seat_index=0)
    await act(rooms, root, 102, "seat", seat_index=4)
    assert not (await act(rooms, root, 103, "seat", user_id=102, seat_index=2)).success
    assert not (await act(rooms, root, 102, "seat", seat_index=0)).success
    assert await act(rooms, root, 101, "seat", source_seat=0, seat_index=4) is None
    info = public_lobby(rooms, root)
    assert info["seat_list"][0] == 102 and info["seat_list"][4] == 101
    assert info["host_user_id"] == 101
    assert await act(rooms, root, 101, "seat", user_id=103, seat_index=7) is None


@run_async
async def test_stale_and_concurrent_seat_claims_never_replace_occupant(rooms):
    import asyncio
    root = await create(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    revision = pair_for(rooms, root).data["lobby"]["revision"]
    requests = [action(rooms, f"u{uid}", root["room_id"],
        dict(action="seat", seat_index=0, roster_revision=revision)) for uid in (101, 102)]
    result = await asyncio.gather(*requests)
    assert sum(r is None for r in result) == 1
    assert public_lobby(rooms, root)["seat_list"][0] in (101, 102)
    assert len(public_lobby(rooms, root)["duplicate_lobby"]["waiting"]) == 1


@run_async
async def test_repeated_bot_kind_is_seat_scoped_and_counts_toward_eight(rooms):
    root = await create(rooms)
    await join(rooms, root, 101)
    await act(rooms, root, 101, "seat", seat_index=0)
    for index in range(1, 8):
        assert await act(rooms, root, 101, "add_bot", seat_index=index, bot_kind=0) is None
    assert len(public_lobby(rooms, root)["player_list"]) == 8
    assert not (await join(rooms, root, 102)).success
    assert await act(rooms, root, 101, "remove", seat_index=5) is None
    seats = public_lobby(rooms, root)["seat_list"]
    assert seats[5] == -1 and seats.count(0) == 6
    assert (await join(rooms, root, 102)).success


@run_async
async def test_ready_requires_seating_and_any_seat_change_invalidates_both_tables(rooms):
    root = await create(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    pair = pair_for(rooms, root)
    assert not (await rooms.set_player_ready("u102", root["room_id"], True, pair.data["lobby"]["revision"])).success
    await act(rooms, root, 101, "seat", seat_index=0)
    await act(rooms, root, 102, "seat", seat_index=4)
    assert await rooms.set_player_ready("u102", root["room_id"], True, pair.data["lobby"]["revision"]) is None
    assert public_lobby(rooms, root)["ready_list"] == [102]
    await act(rooms, root, 101, "seat", seat_index=2)
    assert public_lobby(rooms, root)["ready_list"] == []


@run_async
async def test_one_host_starts_two_four_player_games(rooms, monkeypatch):
    from ..gamestate.gamestate_manager import GameStateManager
    root = await create(rooms)
    for uid in range(101, 109):
        await join(rooms, root, uid)
        await act(rooms, root, uid, "seat", seat_index=uid - 101)
    pair = pair_for(rooms, root)
    for uid in range(102, 109):
        await rooms.set_player_ready(f"u{uid}", root["room_id"], True, pair.data["lobby"]["revision"])
    gm = rooms.game_server.gamestate_manager
    monkeypatch.setattr("server.database.duplicate_walls.reserve_duplicate_game", lambda *_: "reserved")
    gm.start_game = AsyncMock(return_value=None)
    result = await GameStateManager.start_game(gm, "u101", root["room_id"], roster_revision=pair.data["lobby"]["revision"])
    assert result is None and gm.start_game.await_count == 2
    assert pair.data["status"] == "running" and pair.start_gate.is_set()
    assert all(call.kwargs["_paired_prepare"] for call in gm.start_game.await_args_list)
    assert all(len(r["seat_list"]) == 4 for r in rooms.rooms.values())
    assert not (await act(rooms, root, 101, "stand")).success


@run_async
async def test_persistence_failure_rolls_back_cross_table_swap(rooms):
    root = await create(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    await act(rooms, root, 101, "seat", seat_index=0)
    await act(rooms, root, 102, "seat", seat_index=4)
    pair = pair_for(rooms, root)
    before = public_lobby(rooms, root)
    def fail(*_):
        raise OSError("save unavailable")
    pair.persist = fail
    with pytest.raises(OSError):
        await act(rooms, root, 101, "seat", source_seat=0, seat_index=4)
    assert public_lobby(rooms, root) == before


@run_async
async def test_waiting_member_sync_leave_and_host_succession(rooms):
    root = await create(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    assert rooms.find_player_room(102) is root
    sync = await rooms.sync_my_room("u102")
    assert sync.success and sync.room_info["duplicate_lobby"]["waiting"] == [101, 102]
    assert (await rooms.leave_room("u101", root["room_id"])).success
    assert public_lobby(rooms, root)["host_user_id"] == 102
    assert rooms.game_server.players["u101"].current_room_id is None


@run_async
async def test_existing_member_join_restores_its_table_without_taking_another_seat(rooms):
    root = await create(rooms)
    await join(rooms, root, 101)
    await act(rooms, root, 101, "seat", seat_index=0)
    connection = rooms.game_server.players["u101"]
    connection.current_room_id = None
    assert (await join(rooms, root, 101)).success
    assert connection.current_room_id == tables(rooms, pair_for(rooms, root))["closed"]["room_id"]
    assert public_lobby(rooms, root)["player_list"] == [101]


@run_async
async def test_finished_match_retains_existing_roster_but_cannot_be_reused(rooms):
    root = await create(rooms)
    await join(rooms, root, 101)
    await act(rooms, root, 101, "seat", seat_index=0)
    pair = pair_for(rooms, root)
    pair.data["status"] = "completed"
    assert not (await join(rooms, root, 102)).success
    assert (await rooms.sync_my_room("u101")).room_info["seat_list"][0] == 101
    assert not public_lobby(rooms, root)["duplicate_lobby"]["can_edit"]
    assert not (await rooms.game_server.gamestate_manager.start_game("u101", root["room_id"],
        roster_revision=pair.data["lobby"]["revision"])).success


@run_async
async def test_unified_broadcast_reaches_waiters_without_exposing_peer_progress(rooms):
    from .room_manager import RoomManager
    root = await create(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    root["duplicate_progress"] = {"scores": [99, -99], "table_completed_boards": 3}
    await RoomManager._broadcast_room_info(rooms, root["room_id"])
    for uid in (101, 102):
        payload = rooms.game_server.players[f"u{uid}"].websocket.send_json.call_args.args[0]
        assert payload["room_info"]["room_id"] == root["room_id"]
        assert "duplicate_progress" not in payload["room_info"]
        assert "duplicate_key" not in payload["room_info"]


@run_async
async def test_existing_finished_all_bot_table_cleanup_remains_in_effect(rooms):
    root = await create(rooms)
    await join(rooms, root, 101)
    await act(rooms, root, 101, "seat", seat_index=4)
    for index in (0, 1, 2, 3):
        await act(rooms, root, 101, "add_bot", seat_index=index, bot_kind=0)
    pair = pair_for(rooms, root)
    closed = tables(rooms, pair)["closed"]
    closed.update(active_gamestate_id="done", is_game_running=True)
    pair.data["status"] = "completed"
    await rooms.finish_custom_game_room(closed["room_id"], expected_gamestate_id="done", expected_instance_id=closed["instance_id"])
    assert closed["seat_list"] == [-1] * 4
    assert public_lobby(rooms, root)["seat_list"][4] == 101


@run_async
async def test_host_on_closed_table_changes_both_tables_bot_speed(rooms):
    root = await create(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    await act(rooms, root, 101, "seat", seat_index=0)
    assert await rooms.set_bot_speed("u101", root["room_id"], "slow") is None
    assert all(r["bot_speed"] == "slow" for r in rooms.rooms.values())
    assert not (await rooms.set_bot_speed("u102", root["room_id"], "fast")).success
    assert not (await rooms.set_bot_speed("u101", root["room_id"], {})).success


@pytest.mark.parametrize("kind", ["ready", "leave", "setting"])
@run_async
async def test_failed_persistence_keeps_membership_and_readiness_atomic(rooms, kind):
    root = await create(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    await act(rooms, root, 102, "seat", seat_index=0)
    pair = pair_for(rooms, root)
    before = public_lobby(rooms, root)
    def fail(*_):
        raise OSError("save unavailable")
    pair.persist = fail
    if kind == "leave":
        assert not (await rooms.leave_room("u102", root["room_id"])).success
    else:
        with pytest.raises(OSError):
            if kind == "ready":
                await rooms.set_player_ready("u102", root["room_id"], True, pair.data["lobby"]["revision"])
            else:
                await rooms.set_bot_speed("u101", root["room_id"], "slow")
    assert public_lobby(rooms, root) == before


@run_async
async def test_only_current_clients_enter_new_eight_player_rooms(rooms):
    from .room_router import handle_join_room
    root = await create(rooms)
    socket = NS(send_json=AsyncMock())
    rooms.game_server.join_room = AsyncMock()
    await handle_join_room(rooms.game_server, "u101", dict(room_id=root["room_id"], password=""), socket)
    assert not socket.send_json.call_args.args[0]["success"]
    rooms.game_server.join_room.assert_not_awaited()
    await handle_join_room(rooms.game_server, "u101", dict(room_id=root["room_id"], password="", duplicate_lobby_version=1), socket)
    rooms.game_server.join_room.assert_awaited_once()


@run_async
async def test_admin_list_and_pair_close_include_waiting_members(rooms):
    from .admin_rooms import list_admin_rooms
    from .sichuan_duplicate import delete_pair
    root = await create(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    items = list_admin_rooms(rooms)
    assert len(items) == 1 and items[0]["max_player"] == 8 and len(items[0]["players"]) == 2
    assert all(p["waiting"] for p in items[0]["players"])
    assert (await delete_pair(rooms, root)).success
    assert not rooms.rooms
    assert all(rooms.game_server.players[f"u{uid}"].current_room_id is None for uid in (101, 102))


@run_async
async def test_real_eight_player_games_keep_selected_compass_seats(rooms, monkeypatch):
    import asyncio
    from ..gamestate.test_room_lifecycle import make_server, parked_loop
    from ..gamestate.game_sichuan.DuplicateGameState import DuplicateMixin
    root = await create(rooms)
    real = make_server(list(range(101, 109)))
    for uid in range(101, 109):
        connection = real.players[str(uid)]
        connection.current_room_id = None
        rooms.game_server.players[f"u{uid}"] = connection
        rooms.game_server.players[str(uid)] = connection
        rooms.game_server.user_id_to_connection[uid] = connection
    for uid, index in zip(range(101, 109), (7, 0, 5, 2, 1, 6, 3, 4)):
        assert (await join(rooms, root, uid)).success
        assert await act(rooms, root, uid, "seat", seat_index=index) is None
    pair = pair_for(rooms, root)
    for uid in range(102, 109):
        await rooms.set_player_ready(f"u{uid}", root["room_id"], True, pair.data["lobby"]["revision"])
    monkeypatch.setattr("server.database.duplicate_walls.reserve_duplicate_game", lambda *_: "reserved-eight")
    monkeypatch.setattr(DuplicateMixin, "run_game_loop", parked_loop)
    gm = rooms.game_server.gamestate_manager
    try:
        assert await gm.start_game("u101", root["room_id"], roster_revision=pair.data["lobby"]["revision"]) is None
        await asyncio.sleep(0)
        for table, state in pair.states.items():
            assert [p.user_id for p in state.player_list] == tables(rooms, pair)[table]["seat_list"]
            # The parked loop precedes game_loop_sichuan's original-index initialization.
            assert [m["seat"] for m in pair.data["members"][table]] == [0, 1, 2, 3]
            assert [p.team_id for p in state.player_list] == tables(rooms, pair)[table]["team_by_seat"]
    finally:
        for gid in list(gm.gamestate_id_to_game_state):
            await gm.cleanup_game_state_complete(gamestate_id=gid)
