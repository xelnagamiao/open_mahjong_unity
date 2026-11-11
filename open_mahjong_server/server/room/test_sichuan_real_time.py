"""Creation, persistence and paired-lobby isolation of duplicate claim clocks."""
from copy import deepcopy
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .test_sichuan_duplicate import rooms, run_async
from .test_sichuan_personal_duplicate import login, create
from .test_sichuan_lobby import create as create_lobby, join, act
from .sichuan_lobby import pair_for, public_lobby
from .sichuan_timing import SECOND_WAIT_SECONDS, timing_config
from .room_router import handle_room_message
from ..response import Response
from ..game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE


@pytest.mark.parametrize("sub_rule", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@pytest.mark.parametrize("real_time", [False, True])
@pytest.mark.parametrize("seconds", SECOND_WAIT_SECONDS)
@run_async
async def test_personal_pair_keeps_independent_switch_and_seconds_in_both_snapshots(rooms, sub_rule, real_time, seconds):
    login(rooms)
    response = await create(rooms, sub_rule, real_time=real_time, tactical_grace_seconds=seconds,
                            tactical_call=False, duplicate_round_count=1)
    assert response.success, response.message
    assert response.room_info["real_time"] is real_time
    assert response.room_info["tactical_grace_seconds"] == seconds
    pair = pair_for(rooms, rooms.rooms[response.room_info["room_id"]])
    for room in [*rooms.rooms.values(), *pair.data["room_snapshots"].values()]:
        assert room["real_time"] is real_time
        assert room["tactical_grace_seconds"] == seconds
        assert room["tactical_call"] is False
    restored = await rooms.sync_my_room("host")
    assert restored.room_info["real_time"] is real_time
    assert restored.room_info["tactical_grace_seconds"] == seconds


@pytest.mark.parametrize("sub_rule", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@run_async
async def test_legacy_paired_room_defaults_are_off_and_five_seconds(rooms, sub_rule):
    response = await rooms.create_empty_event_room("evt_pair", "sichuan", {
        "sub_rule": sub_rule, "duplicate_round_count": 1,
    }, created_by=101)
    assert response.success
    for room in rooms.rooms.values():
        assert room["real_time"] is False
        assert room["tactical_grace_seconds"] == 5
        assert room["tactical_call"] is True


@pytest.mark.parametrize("patch", [
    *[{"real_time": value} for value in (0, 1, "false", None, [], {})],
    *[{"tactical_grace_seconds": value} for value in (True, False, 0, 4, 21, 5.0, "5", None, [], {})],
])
@pytest.mark.parametrize("personal", [False, True])
@run_async
async def test_invalid_timing_never_creates_a_partial_pair(rooms, patch, personal):
    if personal:
        login(rooms)
        response = await create(rooms, duplicate_round_count=1, **patch)
    else:
        response = await rooms.create_empty_event_room("evt_pair", "sichuan", {
            "sub_rule": SBR_DUPLICATE, "duplicate_round_count": 1, **patch,
        }, created_by=101)
    assert not response.success
    assert not rooms.rooms and not rooms.saved


@pytest.mark.parametrize("sub_rule", ["sichuan/standard", "sichuan/xueliu", "sichuan/xueliu_exchange",
                                      "sichuan/doubles", "sichuan/xueliu_doubles"])
@run_async
async def test_ordinary_sichuan_keeps_original_fields_and_rejects_activation(rooms, sub_rule):
    legacy = await rooms.create_empty_event_room("evt_pair", "sichuan", {"sub_rule": sub_rule}, created_by=101)
    assert legacy.success
    assert "real_time" not in legacy.room_info and "tactical_grace_seconds" not in legacy.room_info
    count = len(rooms.rooms)
    for patch in ({"real_time": True}, {"tactical_grace_seconds": 8}):
        response = await rooms.create_empty_event_room("evt_pair", "sichuan", {
            "sub_rule": sub_rule, **patch,
        }, created_by=101)
        assert not response.success
    assert len(rooms.rooms) == count


@run_async
async def test_other_rules_do_not_accept_real_time(rooms):
    response = await rooms.create_empty_event_room("evt_pair", "guobiao", {
        "sub_rule": "guobiao/standard", "real_time": True,
    }, created_by=101)
    assert not response.success and not rooms.rooms
    assert timing_config("guobiao/standard", {}) == {}


@pytest.mark.parametrize("explicit", [False, True])
@run_async
async def test_socket_create_forwards_new_fields_with_legacy_defaults(explicit):
    server = NS(players={}, create_Sichuan_room=AsyncMock(
        return_value=Response(type="room/create_room_done", success=True, message="ok")))
    socket = NS(send_json=AsyncMock())
    request = dict(type="room/create_Sichuan_room", roomname="timing", gameround=1, password="",
                   roundTimerValue=20, stepTimerValue=5, tips=False, sub_rule=SBR_DUPLICATE)
    if explicit:
        request.update(real_time=True, tactical_grace_seconds=15)
    await handle_room_message(server, "missing", request, socket)
    values = server.create_Sichuan_room.await_args.kwargs
    assert values["real_time"] is explicit
    assert values["tactical_grace_seconds"] == (15 if explicit else 5)


@run_async
async def test_host_timing_update_is_atomic_clears_both_ready_sets_and_retains_seconds_when_off(rooms):
    root = await create_lobby(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    await act(rooms, root, 101, "seat", seat_index=4)
    await act(rooms, root, 102, "seat", seat_index=0)
    pair = pair_for(rooms, root)
    for child in rooms.rooms.values():
        child["ready_list"] = [102]
    pair.ready.update(open="ready", closed="ready")
    revision = pair.data["lobby"]["revision"]
    socket = NS(send_json=AsyncMock())
    await handle_room_message(rooms.game_server, "u101", dict(type="room/set_sichuan_real_time",
        room_id=root["room_id"], real_time=True, tactical_grace_seconds=10), socket)
    socket.send_json.assert_not_called()
    assert pair.data["lobby"]["revision"] == revision + 1 and not pair.ready
    for child in rooms.rooms.values():
        assert child["real_time"] and child["tactical_grace_seconds"] == 10 and not child["ready_list"]
    assert pair.data["room_snapshots"] == {table: deepcopy(rooms.rooms[rid]) for table, rid in pair.data["room_ids"].items()}
    assert await rooms.set_sichuan_real_time("u101", root["room_id"], False, 10) is None
    assert all(not child["real_time"] and child["tactical_grace_seconds"] == 10 for child in rooms.rooms.values())
    revision = pair.data["lobby"]["revision"]
    assert await rooms.set_sichuan_real_time("u101", root["room_id"], False, 10) is None
    assert pair.data["lobby"]["revision"] == revision


@run_async
async def test_invalid_or_unauthorized_updates_leave_pair_unchanged(rooms):
    root = await create_lobby(rooms)
    for uid in (101, 102):
        await join(rooms, root, uid)
    before = public_lobby(rooms, root)
    for actor, enabled, seconds in (("u102", True, 8), ("missing", True, 8), ("u101", 1, 8),
                                    ("u101", True, "5"), ("u101", True, 4)):
        assert not (await rooms.set_sichuan_real_time(actor, root["room_id"], enabled, seconds)).success
        assert public_lobby(rooms, root) == before
    pair = pair_for(rooms, root)
    for status in ("running", "interrupted", "completed"):
        pair.data["status"] = status
        assert not (await rooms.set_sichuan_real_time("u101", root["room_id"], True, 8)).success
        assert all(not child["real_time"] for child in rooms.rooms.values())


@run_async
async def test_timing_update_persistence_failure_rolls_back_room_and_ready_state(rooms):
    root = await create_lobby(rooms)
    await join(rooms, root, 101)
    pair = pair_for(rooms, root)
    before, children = deepcopy(pair.data), deepcopy(rooms.rooms)
    def fail(*_):
        raise OSError("save unavailable")
    pair.persist = fail
    with pytest.raises(OSError):
        await rooms.set_sichuan_real_time("u101", root["room_id"], True, 15)
    assert pair.data == before and rooms.rooms == children


@pytest.mark.parametrize("sub_rule", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@pytest.mark.parametrize("real_time", [False, True])
@run_async
async def test_restart_restores_both_room_timing_settings(rooms, monkeypatch, sub_rule, real_time):
    from .sichuan_duplicate import restore_pairs
    response = await rooms.create_empty_event_room("evt_pair", "sichuan", dict(
        sub_rule=sub_rule, duplicate_round_count=1, real_time=real_time, tactical_grace_seconds=20,
    ), created_by=101)
    assert response.success
    snapshot = deepcopy(rooms.saved[-1])
    monkeypatch.setattr("server.room.sichuan_duplicate.PairedMatchStore.unfinished", lambda _: [snapshot])
    rooms.rooms.clear()
    rooms.game_server.gamestate_manager.sichuan_duplicate_matches.clear()
    restore_pairs(rooms.game_server)
    assert len(rooms.rooms) == 2
    for room in rooms.rooms.values():
        assert room["real_time"] is real_time and room["tactical_grace_seconds"] == 20
        assert public_lobby(rooms, room)["real_time"] is real_time
