"""Protocol/privacy and replay contracts, separate from live socket acceptance."""

import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from .get_action import handle_action
from .test_flow import PLAIN, FLOAT, turn, act, state, river
from .state_machine import Phase as P
from ...room.hangzhou_room import HangzhouRoomValidator, create_hangzhou_room, handle_create_hangzhou_room
from ...game_calculation.hangzhou.rules import RULE_VERSION, SUB_RULE
from ...response import Response


def test_private_joker_waits_do_not_leak_to_other_viewers_and_are_versioned():
    s = turn(FLOAT, tips=True)
    snapshot = s.build_game_start_payload(0)["game_info"]
    assert snapshot["hangzhou_info"]["source_hand_tiles"] == FLOAT
    assert snapshot["hangzhou_info"]["waiting_by_discard"][46]
    assert snapshot["self_has_draw_slot"]
    for viewer in range(4):
        data = s.build_game_start_payload(viewer)["game_info"]
        assert data["hangzhou_info"]["source_hand_tiles"] == s.player_list[viewer].hand_tiles
        assert all((p["hand_tiles"] is not None) == (i == viewer) for i, p in enumerate(data["players_info"]))
        assert data["hangzhou_info"]["joker_tiles"] == [46]
        assert data["detailed_config"] == {"rule_version": RULE_VERSION}
    ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
    hints = [t for t in ticks if t[:2] == ["hangzhou", "hints"] and t[2] == 0]
    assert len(hints) == 1 and hints[0][3]["source_hand_tiles"] == FLOAT
    assert "source_hand_tiles" not in s.hangzhou_info()
    assert s.game_record["game_title"]["joker_tiles"] == [46]
    assert s.spectator_manager.game_title["rule_version"] == RULE_VERSION


def test_absolute_replay_scores_never_undo_an_already_recorded_win():
    s = turn()
    s.round_start_scores = [17, -2, -10, -5]
    for p, score in zip(s.player_list, s.round_start_scores):
        p.score = score
    s.game_record["game_round"]["round_index_1"]["hangzhou"]["start_scores"] = list(s.round_start_scores)
    s.game_record["game_round"]["round_index_1"]["action_ticks"].clear()
    s.record_state()
    act(s, 0, "hu_self")
    assert not s.deferred_scores_applied
    s.record_state()
    s.finalize_round_recording()
    running = list(s.round_start_scores)
    for tick in s.game_record["game_round"]["round_index_1"]["action_ticks"]:
        if tick[0] == "hu_self":
            running = [a + b for a, b in zip(running, tick[4])]
        elif tick[:2] == ["hangzhou", "state"]:
            assert tick[3] == running
    assert running == [29, -6, -14, -9]


def test_draw_concealed_kong_and_burn_are_hidden_in_every_live_view():
    s = turn([11] * 4 + PLAIN[4:], wall_tail=[45, 47])
    act(s, 0, "angang", target_tile=11)
    for viewer in range(4):
        info = s.build_game_start_payload(viewer)["game_info"]
        own = viewer == 0
        assert info["players_info"][0]["combination_tiles"] == ["G11" if own else "G0"]
        assert info["players_info"][0]["combination_mask"] == [[2, 11 if own else 0] * 4]
        kong = next(p for p in s.outbound_payloads if p.get("player_index") == viewer and p.get("action") == "angang")
        draw = next(p for p in s.outbound_payloads if p.get("player_index") == viewer and p.get("action") == "deal_gang_tile")
        burn = next(p for p in s.outbound_payloads if p.get("player_index") == viewer and p.get("action") == "hangzhou_tail_burn")
        assert kong["tile"] == (11 if own else 0)
        assert draw["tile"] == (47 if own else None)
        assert burn["tile"] == 0 and burn["do_action_info"]["hangzhou_burn_count"] == 1
        assert info["hangzhou_info"]["ledger"] is None


def test_added_kong_targets_old_pung_and_forced_cut_is_draw_identity():
    s = turn([11] + PLAIN[4:], ("k11",))
    s.cai_discard_locks = {1}
    s.open_action_window(s.begin_turn(0))
    info = s.build_pending_action_payload(0)["ask_hand_action_info"]
    assert info["forced_cut_tiles"] == [42]
    assert 11 in info["forbidden_cut_tiles"]
    act(s, 0, "jiagang", target_tile=11)
    for viewer in range(4):
        payload = next(p for p in s.outbound_payloads if p.get("player_index") == viewer and p.get("action") == "jiagang")
        assert payload["do_action_info"]["combination_target"] == "k11"
        assert len(payload["do_action_info"]["combination_mask"]) == 8


def test_ten_winds_replay_has_no_fictitious_fourteenth_tile_or_recycled_discard():
    s = turn(PLAIN[:-1] + [43])
    p = s.player_list[0]
    p.discard_origin_tiles = [41, 42, 42, 43, 43, 44, 44, 45, 47]
    p.discard_tiles = list(p.discard_origin_tiles)
    act(s, 0, "cut", TileId=43, cutClass=True)
    prompt = s.build_pending_action_payload(0)
    assert prompt["ask_hand_action_info"]["deal_tile_type"] is None
    assert not prompt["game_info"]["self_has_draw_slot"]
    act(s, 0, "hu_self")
    result = s.build_final_settlement_payload(0)["show_result_info"]
    assert result["hangzhou_win_source"] == "ten_winds" and result["hu_class"] == "hu_self"
    assert result["hepai_tile"] == 0 and len(result["hepai_player_hand"]) == 13
    assert not result["recycle_discard"]
    s.finalize_round_recording()
    count = len(s.game_record["game_round"]["round_index_1"]["action_ticks"])
    s.finalize_round_recording()
    ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert len(ticks) == count and ticks[-1] == ["end"]
    hu = next(t for t in ticks if t[0] == "hu_self")
    assert hu[5:] == [0, 0, -1, 0]
    assert next(t for t in ticks if t[:2] == ["hangzhou", "win_source"])[3] == "ten_winds"
    assert sum(ticks[-2][3]) == 0 and ticks[-2][2]["ledger"]["source"] == "ten_winds"


def test_reconnect_and_selected_spectator_restore_identical_authoritative_views():
    async def run():
        s = turn()
        captured = []
        async def capture(index, payload):
            captured.append((index, deepcopy(payload)))
        s.send_payload_to_player = capture
        s.player_list[1].tag_list.append("offline")
        await s.player_reconnect(102)
        assert "offline" not in s.player_list[1].tag_list
        assert captured == [(1, p) for p in s.restore_payloads(1)]
        await s.player_reconnect(999)
        assert len(captured) == 2
        ws = SimpleNamespace(send_json=AsyncMock())
        s.game_server = SimpleNamespace(user_id_to_connection={999: SimpleNamespace(websocket=ws)})
        await s.send_realtime_spectator_snapshot(999, 1)
        assert [call.args[0] for call in ws.send_json.call_args_list] == s.restore_payloads(1)
        count = ws.send_json.call_count
        await s.send_realtime_spectator_snapshot(999, -1)
        await s.send_realtime_spectator_snapshot(998, 1)
        assert ws.send_json.call_count == count
        act(s, 0, "hu_self")
        assert len(s.restore_payloads(1)) == 2 and s.build_pending_action_payload(1) is None
        s.machine.transition(P.READY)
        assert s.restore_payloads(1)[-1]["type"] == "gamestate/hangzhou/ready_status"
    asyncio.run(run())


def test_network_authentication_stale_tick_and_duplicate_replies():
    async def run():
        s = turn()
        ws = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: s),
            players={"own": SimpleNamespace(user_id=101), "outsider": SimpleNamespace(user_id=999)})
        message = dict(type="gamestate/hangzhou/cut_tile", gamestate_id=s.gamestate_id,
                       action_tick=s.server_action_tick, TileId=42, cutClass=True, cutIndex=13)
        for connection in ("outsider", "missing"):
            await handle_action(server, connection, message, ws)
        assert all(q.empty() for q in s.action_queues.values())
        await handle_action(server, "own", dict(message, action_tick=True), ws)
        assert ws.send_json.call_args.args[0]["type"].endswith("broadcast_hand_action")
        await handle_action(server, "own", dict(message, TileId=99), ws)
        assert ws.send_json.call_args.args[0]["type"] == "tips"
        await handle_action(server, "own", message, ws)
        await handle_action(server, "own", message, ws)
        assert s.action_queues[0].qsize() == 1
        s.open_action_window(s.begin_turn(0))
        assert s.action_queues[0].empty()
        act(s, 0, "hu_self")
        count = ws.send_json.call_count
        await handle_action(server, "own", dict(message, action_tick=-1), ws)
        assert ws.send_json.call_count == count
        s.machine.transition(P.READY)
        await handle_action(server, "own", dict(message, action_tick=-1), ws)
        assert ws.send_json.call_args.args[0]["type"].endswith("ready_status")
        s.action_dict, s.waiting_players_list = {0: ["ready"]}, [0]
        await handle_action(server, "own", dict(type="gamestate/hangzhou/send_action", action="ready", action_tick=s.server_action_tick), ws)
        assert s.action_queues[0].get_nowait()["action_type"] == "ready"
        s.room_rule = "other"
        await handle_action(server, "own", message, ws)
        server.gamestate_manager.get_game_state_by_gamestate_id = lambda _: None
        await handle_action(server, "own", message, ws)
    asyncio.run(run())


def test_pydantic_transport_preserves_hangzhou_extensions():
    s = turn(tips=True)
    start = Response.model_validate(s.build_game_start_payload(0)).model_dump(exclude_none=True)
    assert start["game_info"]["hangzhou_info"]["joker_tiles"] == [46]
    assert start["game_info"]["self_has_draw_slot"]
    prompt = Response.model_validate(s.build_pending_action_payload(0)).model_dump(exclude_none=True)
    assert prompt["ask_hand_action_info"]["hangzhou_info"]["source_hand_tiles"] == PLAIN
    act(s, 0, "hu_self")
    result = Response.model_validate(s.build_final_settlement_payload(0)).model_dump(exclude_none=True)
    assert result["show_result_info"]["hangzhou_win_source"] == "self_draw"
    assert result["show_result_info"]["hangzhou_fan_details"]["fan_cap"] == 4


@pytest.mark.parametrize("key,value", [
    ("use_flowers", True), ("open_cuohe", True), ("tactical_call", True), ("claim_protection", True),
    ("tian_di_ren_he", True), ("tips", "false"), ("allow_spectator", 1), ("game_round", True),
    ("game_round", 5), ("round_timer", -1), ("step_timer", 101), ("sub_rule", "hangzhou/other"),
    ("detailed_config", {"joker": 47}), ("detailed_config", {"rule_version": "old"}),
])
def test_room_rejects_nonstandard_or_invalid_configuration(key, value):
    config = dict(room_name="test", game_round=4, round_timer=20, step_timer=5)
    config[key] = value
    with pytest.raises(ValueError):
        HangzhouRoomValidator(**config)


@pytest.mark.parametrize("rounds", [1, 2, 3, 4])
@pytest.mark.parametrize("switch", [False, True])
def test_all_room_options_roundtrip(rounds, switch):
    config = HangzhouRoomValidator(room_name=" 杭州 ", game_round=rounds, round_timer=0, step_timer=0,
        tips=switch, tourist_limit=switch, allow_spectator=switch, count_tips=switch, pointer_tips=switch).model_dump()
    assert config["room_name"] == "杭州" and config["game_round"] == rounds
    assert config["detailed_config"] == {"rule_version": RULE_VERSION} and not config["use_flowers"]
    assert all(config[key] == switch for key in ("tips", "tourist_limit", "allow_spectator", "count_tips", "pointer_tips"))


def fake_room_manager():
    connection = SimpleNamespace(user_id=101, username="tester", current_room_id=None)
    manager = SimpleNamespace(game_server=SimpleNamespace(players={"c": connection}, db_manager=SimpleNamespace(
        get_user_settings=Mock(return_value={"username": "tester", "voice_id": 2}))),
        _reject_room_entry_conflicts=Mock(return_value=None), _normalize_event_id=lambda x: x,
        _validate_event_for_room=Mock(return_value=None), _generate_room_id=lambda: 123456,
        _apply_event_fields=Mock(), _broadcast_room_info=AsyncMock(), rooms={}, room_passwords={})
    return manager, connection


def test_room_creation_and_all_guard_failures_are_non_destructive():
    async def run():
        manager, connection = fake_room_manager()
        assert not (await create_hangzhou_room(manager, "missing", room_name="x")).success
        connection.current_room_id = 1
        assert not (await create_hangzhou_room(manager, "c", room_name="x")).success
        connection.current_room_id = None
        blocked = Response(type="tips", success=False, message="blocked")
        manager._reject_room_entry_conflicts.return_value = blocked
        assert await create_hangzhou_room(manager, "c", room_name="x") is blocked
        manager._reject_room_entry_conflicts.return_value = None
        manager._validate_event_for_room.return_value = blocked
        assert await create_hangzhou_room(manager, "c", room_name="x") is blocked
        manager._validate_event_for_room.return_value = None
        assert not (await create_hangzhou_room(manager, "c", room_name="x", use_flowers=True)).success
        manager.game_server.db_manager.get_user_settings.return_value = None
        assert not (await create_hangzhou_room(manager, "c", room_name="x")).success
        manager.game_server.db_manager.get_user_settings.return_value = {"username": "tester"}
        result = await create_hangzhou_room(manager, "c", room_name="x", password="test", event_id="event")
        assert result.success and connection.current_room_id == 123456
        assert manager.rooms[123456]["room_rule"] == "hangzhou"
        assert manager.rooms[123456]["sub_rule"] == SUB_RULE
        assert manager.room_passwords[123456] == "test"
        ws = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(room_manager=SimpleNamespace(create_Hangzhou_room=AsyncMock(return_value=result)))
        await handle_create_hangzhou_room(server, "c", dict(roomname="x", use_flowers=False), ws)
        assert server.room_manager.create_Hangzhou_room.call_args.kwargs["gameround"] == 4
        assert ws.send_json.call_args.args[0]["success"]
        manager, connection = fake_room_manager()
        result = await create_hangzhou_room(manager,"c",room_name="no-password",use_flowers=False,
            claim_protection=False,open_cuohe=False,tactical_call=False,tian_di_ren_he=False)
        assert result.success and not manager.room_passwords
    asyncio.run(run())
