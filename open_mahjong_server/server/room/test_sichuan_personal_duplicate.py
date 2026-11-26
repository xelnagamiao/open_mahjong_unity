from copy import deepcopy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, Mock
import asyncio

import pytest

from .test_sichuan_duplicate import rooms, run_async
from .sichuan_lobby import action, pair_for, public_lobby, tables
from ..game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE, TILES


def login(rooms):
    player = NS(user_id=101, username="host", current_room_id=None, Connect_id="host",
                websocket=NS(send_json=AsyncMock()), is_tourist=False)
    rooms.game_server.players["host"] = player
    rooms.game_server.user_id_to_connection[101] = player
    return player


async def create(rooms, sub=SBR_DUPLICATE, **options):
    return await rooms.create_Sichuan_room("host", "个人八人房", 1, "", 127, 17, True,
        random_seed=options.pop('random_seed', '0'), sub_rule=sub, **options)


@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@pytest.mark.parametrize("count", [1, 4, 8, 12, 16])
@pytest.mark.parametrize("sync", [False, True])
@run_async
async def test_personal_create_enters_existing_eight_player_waiting_area(rooms, sub, count, sync):
    player = login(rooms)
    result = await create(rooms, sub, duplicate_round_count=count, duplicate_sync_mode=sync)
    assert result.success, result.message
    info = result.room_info
    assert result.type == "room/create_room_done"
    assert info["max_player"] == 8 and info["seat_list"] == [-1] * 8
    assert info["duplicate_lobby"]["waiting"] == [101] and info["host_user_id"] == 101
    assert info["room_type"] == "custom" and not info.get("event_id")
    assert player.current_room_id == info["room_id"]
    assert "duplicate_key" not in info
    assert len(rooms.rooms) == 2
    assert all(r["duplicate_round_count"] == count and r["duplicate_sync_mode"] == sync
               and r["round_timer"] == 127 and r["step_timer"] == 17
               and r["persist_empty"] and not r["allow_spectator"] for r in rooms.rooms.values())


@pytest.mark.parametrize("sub,rule", [(SBR_DUPLICATE, "sichuan/sbr"), (XUELIU_DUPLICATE, "sichuan/xueliu")])
@pytest.mark.parametrize("kind", ["manual", "seed", "key"])
@run_async
async def test_personal_existing_key_overrides_selected_count(rooms, monkeypatch, sub, rule, kind):
    login(rooms)
    wall = dict(id=55, key="dup_" + "b" * 32, rule=rule, scope="personal", owner_user_id=101,
        event_id=None, wall_type=kind, use_flowers=False, round_count=4,
        tiles=list(TILES), round_tiles=[list(TILES)] * 4, seed="wall-seed")
    monkeypatch.setattr("server.database.duplicate_walls.load_duplicate_wall", lambda *_: deepcopy(wall))
    result = await create(rooms, sub, duplicate_key=wall["key"], duplicate_round_count=16)
    assert result.success
    assert all(r["duplicate_round_count"] == 4 for r in rooms.rooms.values())
    assert rooms.saved[0]["source_wall_id"] == 55


@pytest.mark.parametrize("event_id", ["evt_pair", "base_pair"])
@run_async
async def test_venue_socket_requires_existing_admin_authority(rooms, event_id):
    login(rooms)
    rooms.game_server.db_manager.get_event_admin_role.return_value = None
    assert not (await create(rooms, event_id=event_id)).success
    assert not rooms.rooms
    rooms.game_server.db_manager.get_event_admin_role.return_value = "admin"
    result = await create(rooms, event_id=event_id, duplicate_round_count=1)
    assert result.success and all(r["event_id"] == event_id for r in rooms.rooms.values())


@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@run_async
async def test_personal_rejects_venue_key_and_keeps_no_half_pair(rooms, monkeypatch, sub):
    login(rooms)
    wall = dict(rule="sichuan/sbr" if sub == SBR_DUPLICATE else "sichuan/xueliu", event_id="other-venue")
    monkeypatch.setattr("server.database.duplicate_walls.load_duplicate_wall", lambda *_: wall)
    result = await create(rooms, sub, duplicate_key="dup_" + "a" * 32)
    assert not result.success and not rooms.rooms and not rooms.saved


@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@pytest.mark.parametrize("legacy_reuse", [False, True])
@pytest.mark.parametrize("submitted", [False, True])
@run_async
async def test_last_human_departure_respects_submitted_board(rooms, monkeypatch, sub, legacy_reuse, submitted):
    from ..gamestate.game_sichuan.DuplicateGameState import DuplicateMixin
    from ..gamestate.test_room_lifecycle import parked_loop

    player = login(rooms)
    result = await create(rooms, sub, wall_source="training", duplicate_round_count=1, random_seed="1" * 64)
    assert result.success, result.message
    root = rooms.rooms[result.room_info["room_id"]]
    pair = pair_for(rooms, root)
    pair.data["reuse_training_room"] = legacy_reuse
    for index in range(8):
        message = dict(action="seat" if index == 4 else "add_bot", seat_index=index,
                       roster_revision=pair.data["lobby"]["revision"], bot_kind=0)
        assert await action(rooms, "host", root["room_id"], message) is None
    monkeypatch.setattr(DuplicateMixin, "run_game_loop", parked_loop)
    gm = rooms.game_server.gamestate_manager
    assert await gm.start_game("host", root["room_id"], roster_revision=pair.data["lobby"]["revision"]) is None
    await asyncio.sleep(0)
    if submitted:
        human_table = gm.get_game_state_by_user_id(player.user_id)
        pair.data["table_completed_boards"][human_table.duplicate_table] = len(pair.data["boards"])
        human_table.game_status = "duplicate_wait"
    await asyncio.wait_for(gm.abandon_player_game("host", player.user_id), timeout=2)
    if submitted:
        assert pair.data["status"] == "running" and len(rooms.rooms) == 2
        assert all(not state.game_task.done() for state in pair.states.values())
        pair.data.update(status="completed", completed_boards=1,
                         table_completed_boards={"open": 1, "closed": 1})
        for state in pair.states.values():
            await gm.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
    assert all(state.game_task.done() for state in pair.states.values())
    assert not gm.gamestate_id_to_game_state and not gm.sichuan_duplicate_matches
    assert not rooms.rooms and not rooms.active_game_room_ids
    assert player.current_room_id is None


@run_async
async def test_personal_all_bot_child_finish_preserves_host_and_existing_group_cleanup(rooms):
    login(rooms)
    result = await create(rooms)
    root = rooms.rooms[result.room_info["room_id"]]
    pair = pair_for(rooms, root)
    async def act(kind, **fields):
        return await action(rooms, "host", root["room_id"], dict(action=kind,
            roster_revision=pair.data["lobby"]["revision"], **fields))
    assert await act("seat", seat_index=4) is None
    for i in range(4): assert await act("add_bot", seat_index=i, bot_kind=0) is None
    closed = tables(rooms, pair)["closed"]
    root.update(active_gamestate_id="peer", is_game_running=True)
    closed.update(active_gamestate_id="done", is_game_running=True)
    pair.data["status"] = "completed"
    await rooms.finish_custom_game_room(closed["room_id"], expected_gamestate_id="done", expected_instance_id=closed["instance_id"])
    assert len(rooms.rooms) == 2
    await rooms.finish_custom_game_room(root["room_id"], expected_gamestate_id="peer", expected_instance_id=root["instance_id"])
    assert rooms.game_server.players["host"].current_room_id is None
    assert not rooms.rooms and pair.data["rooms_deleted"]


@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@run_async
async def test_socket_dispatch_preserves_duplicate_options(rooms, sub):
    from .room_router import handle_room_message
    server = rooms.game_server
    server.create_Sichuan_room = AsyncMock(return_value=NS(model_dump=lambda **_: {"success": True}))
    socket = NS(send_json=AsyncMock())
    await handle_room_message(server, "missing", dict(type="room/create_Sichuan_room", sub_rule=sub,
        roomname="八人", gameround=1, password="", roundTimerValue=127, stepTimerValue=17, tips=True,
        duplicate_key="dup_" + "a" * 32, duplicate_round_count=4, duplicate_sync_mode=True), socket)
    kwargs = server.create_Sichuan_room.call_args.kwargs
    assert kwargs["duplicate_round_count"] == 4 and kwargs["duplicate_sync_mode"] is True
    assert kwargs["duplicate_key"].startswith("dup_")


@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@pytest.mark.parametrize("interrupted", [False, True])
@pytest.mark.parametrize("training", [False, True])
@run_async
async def test_personal_one_human_seven_bots_start_real_states_and_reject_interrupted_start(rooms, monkeypatch, sub, interrupted, training):
    from ..gamestate.test_room_lifecycle import make_server, parked_loop
    from ..gamestate.game_sichuan.DuplicateGameState import DuplicateMixin
    server = rooms.game_server
    human = make_server([101]).players["101"]
    human.current_room_id = None
    human.Connect_id = "host"
    server.players["host"] = human
    server.user_id_to_connection[101] = human
    options=dict(wall_source='training',random_seed='0123456789abcdef'*4) if training else {}
    result = await create(rooms, sub, duplicate_round_count=4, **options)
    assert result.success, result.message
    root = rooms.rooms[result.room_info["room_id"]]
    pair = pair_for(rooms, root)
    async def act(kind, **fields):
        return await action(rooms, "host", root["room_id"], dict(action=kind,
            roster_revision=pair.data["lobby"]["revision"], **fields))
    assert await act("seat", seat_index=4) is None
    for i in (0,1,2,3,5,6,7): assert await act("add_bot", seat_index=i, bot_kind=2) is None
    original = deepcopy(pair.data["boards"])
    if interrupted:
        pair.interrupt("server_restart")
        rejected = await server.gamestate_manager.start_game("other", root["room_id"],
            roster_revision=pair.data["lobby"]["revision"])
        assert not rejected.success and pair.data["boards"] == original
        rejected = await server.gamestate_manager.start_game("host", root["room_id"], roster_revision=pair.data["lobby"]["revision"])
        assert not rejected.success and not pair.states
        return
    reserve=Mock(return_value='reserved-id')
    monkeypatch.setattr("server.database.duplicate_walls.reserve_duplicate_game", reserve)
    monkeypatch.setattr(DuplicateMixin, "run_game_loop", parked_loop)
    gm = server.gamestate_manager
    try:
        assert await gm.start_game("host", root["room_id"], roster_revision=pair.data["lobby"]["revision"]) is None
        await asyncio.sleep(0)
        assert pair.data["status"] == "running" and len(pair.states) == 2
        assert set(gm.user_id_to_game_state) == {101}
        for state in pair.states.values():
            assert len(state.player_list) == 4 and state.round_time == 127 and state.step_time == 17
            assert state.room_type == "custom"
        if training:
            reserve.assert_not_called()
            assert all(not state.duplicate_key and state.duplicate_wall_type=='training' for state in pair.states.values())
    finally:
        for gid in list(gm.gamestate_id_to_game_state): await gm.cleanup_game_state_complete(gamestate_id=gid)


@pytest.mark.parametrize("status", ["waiting", "completed", "interrupted"])
@pytest.mark.parametrize("event", [None, "evt_pair", "base_pair"])
@run_async
async def test_last_human_leave_closes_only_personal_group(rooms, status, event):
    login(rooms)
    response = await create(rooms, event_id=event)
    root = rooms.rooms[response.room_info["room_id"]]
    pair = pair_for(rooms, root)
    pair.data["status"] = status
    result = await rooms.leave_room("host", root["room_id"])
    assert result.success and rooms.game_server.players["host"].current_room_id is None
    if event:
        assert len(rooms.rooms) == 2 and not pair.data.get("rooms_deleted")
        assert not public_lobby(rooms, root)["player_list"]
    else:
        assert not rooms.rooms and pair.data["rooms_deleted"]
        assert pair.data["status"] == ("completed" if status == "completed" else "cancelled")


@run_async
async def test_player_joining_between_last_leave_and_close_is_not_removed(rooms, monkeypatch):
    from .sichuan_duplicate import delete_pair
    login(rooms)
    result = await create(rooms)
    root = rooms.rooms[result.room_info["room_id"]]
    guest = NS(user_id=102, username="new arrival", current_room_id=None, Connect_id="guest",
               websocket=NS(send_json=AsyncMock()), is_tourist=False)
    rooms.game_server.players["guest"] = guest
    rooms.game_server.user_id_to_connection[102] = guest
    async def join_before_close(manager, room, **options):
        assert (await manager.join_room("guest", root["room_id"], "")).success
        return await delete_pair(manager, room, **options)
    monkeypatch.setattr("server.room.sichuan_duplicate.delete_pair", join_before_close)
    assert (await rooms.leave_room("host", root["room_id"])).success
    assert len(rooms.rooms) == 2 and guest.current_room_id == root["room_id"]
    assert public_lobby(rooms, root)["host_user_id"] == 102


@run_async
async def test_auto_close_does_not_delete_a_reused_room_id(rooms):
    from .sichuan_duplicate import delete_pair
    login(rooms)
    response = await create(rooms)
    root = rooms.rooms[response.room_info["room_id"]]
    replacement = dict(root, instance_id="replacement", duplicate_match_id="other-match")
    rooms.rooms[root["room_id"]] = replacement
    assert not (await delete_pair(rooms, root, only_if_empty=True)).success
    assert rooms.rooms[root["room_id"]] is replacement

@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@run_async
async def test_personal_training_cannot_enable_reuse(rooms, sub):
    player = login(rooms)
    response = await create(rooms, sub, wall_source="training", reuse_training_room=True)
    assert not response.success and "赛事或基地" in response.message
    assert player.current_room_id is None
    assert not rooms.rooms and not rooms.game_server.gamestate_manager.sichuan_duplicate_matches
    assert not rooms.saved
