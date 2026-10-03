"""Offline rule/protocol regression tests: no live services, database or Unity."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from .actions import claim_actions, kong_tiles
from .get_action import handle_action
from .test_flow import PLAIN, PUNGS, act, configured_turn, physical, start, state
from .state_machine import Phase as P
from ...game_calculation.guizhou.rules import RULE_VERSION, normalize_config, score_hand, waiting_tiles
from ...room.guizhou_room import GuizhouRoomValidator, create_guizhou_room, handle_create_guizhou_room


def attach_record(s):
    s.start_game_recording()
    s.start_round_recording()
    return s


def ticks(s):
    return s.game_record["game_round"][f"round_index_{s.round_index}"]["action_ticks"]


@pytest.mark.parametrize("bad", [None, "tiles", [True], [11, "12"], [11, []], [41]*14])
def test_malformed_hand_is_not_a_win_or_wait(bad):
    assert score_hand(bad) is None
    assert not waiting_tiles(bad)


@pytest.mark.parametrize("config", [{"jokers": True}, {"rule_version": "mil-unknown"}, [], False])
def test_fixed_rule_version_rejects_extra_house_rules(config):
    with pytest.raises(ValueError):
        normalize_config(config)


def test_initial_kong_stays_hidden_until_all_first_discards():
    s = configured_turn([11]*4 + [12, 13, 14, 21, 22, 23, 31, 32, 33, 39])
    p = s.player_list[0]
    for player in s.player_list:
        player.discard_count = 0
    p.initial_quads = {11}
    attach_record(s)
    s.open_action_window(s.begin_turn(0))
    act(s, 0, "angang", target_tile=11)
    s.apply_action_results(s.live_pending_window, {})
    assert not s.kongs[-1].hanbao
    assert s.hidden_opening_kongs == {(0, 0)}
    assert s.guizhou_info(0)["kongs"][0]["tile"] == 11
    for viewer in (1, 2, 3):
        payload = s.build_game_start_payload(viewer)
        info = payload["game_info"]
        assert info["guizhou_info"]["kongs"][0]["tile"] == 0
        assert info["players_info"][0]["combination_tiles"] == ["G0"]
        assert info["players_info"][0]["combination_mask"] == [[2, 0]*4]
        broadcast = next(v for v in s.outbound_payloads if v.get("player_index") == viewer and v.get("action") == "angang")
        assert broadcast["tile"] == 0 and broadcast["meld_code"] == "G0"
    for player in s.player_list:
        player.discard_count = 1
    s._reveal_opening_kongs()
    assert s.opening_revealed and not s.hidden_opening_kongs
    assert s.guizhou_info(1)["kongs"][0]["tile"] == 11
    assert p.combination_mask[0] == [2, 11, 0, 11, 0, 11, 2, 11]
    assert ticks(s)[-1][:2] == ["guizhou", "reveal_kongs"]


def prepare_added_kong(rob=True):
    s = configured_turn([11, 12, 13, 21, 22, 23, 31, 32, 33, 29, 29])
    owner = s.player_list[0]
    owner.combination_tiles = ["k29"]
    owner.combination_mask = [[1, 29, 0, 29, 0, 29]]
    # The robbing hand does not contain any of the four 9饼 already owned by 0.
    s.player_list[1].hand_tiles = [27, 28, 11, 12, 13, 14, 15, 16, 21, 22, 23, 39, 39]
    if not rob:
        s.player_list[1].hand_tiles = [12, 13, 15, 17, 18, 19, 21, 23, 24, 27, 32, 35, 39]
    owner.hand_tiles.remove(29)  # Keep exactly one fourth copy, in the draw slot.
    owner.hand_tiles.insert(0, 18)
    attach_record(s)
    s.open_action_window(s.begin_turn(0))
    return s


def test_rob_added_kong_is_mandatory_transactional_and_replay_explicit():
    s = prepare_added_kong()
    owner = s.player_list[0]
    before = list(owner.hand_tiles)
    act(s, 0, "jiagang", target_tile=29)
    assert owner.hand_tiles == before and owner.combination_tiles == ["k29"]
    assert s.action_dict[1] == ["hu"]
    with pytest.raises(ValueError):
        act(s, 1, "pass")
    responses = {i: {"action_type": "hu"} for i, a in s.action_dict.items() if a}
    s.apply_action_results(s.live_pending_window, responses)
    assert owner.combination_tiles == ["k29"] and owner.hand_tiles.count(29) == 0
    assert not s.kongs and s.pending_kong is None
    assert s.round_settlement.changes[1] >= 9
    win = s.final_settlement_payloads(1)[0]["show_result_info"]
    assert win["is_qianggang"] and win["ron_discarder_index"] == 0
    assert len(win["hepai_player_hand"]) == 14
    assert next(t for t in ticks(s) if t[:2] == ["guizhou", "win_source"])[4:7] == ["rob_kong", 29, True]
    assert not any(t[0] == "jg" for t in ticks(s))


def test_unrobbed_added_kong_commits_once_and_then_draws():
    s = prepare_added_kong(False)
    act(s, 0, "jiagang", target_tile=29)
    responses = {i: {"action_type": "pass"} for i, a in s.action_dict.items() if a}
    s.apply_action_results(s.live_pending_window, responses)
    assert s.player_list[0].combination_tiles == ["g29"]
    assert s.kongs[-1].kind == "added" and not s.kongs[-1].hanbao
    assert any(t[0] == "jg" for t in ticks(s))
    assert s.player_list[0].has_draw_slot
    commits = [v for v in s.outbound_payloads if v.get("action") == "jiagang"]
    assert len(commits) == 4
    for payload in commits:
        info = payload["do_action_info"]
        assert info["combination_target"] == "k29"
        assert info["is_mo_gang"] is True
        assert len(info["combination_mask"]) == 8
        owner_view = next(p for p in payload["game_info"]["players_info"] if p["player_index"] == 0)
        assert owner_view["combination_tiles"] == ["g29"]
        assert owner_view["combination_mask"] == [info["combination_mask"]]


def test_initial_and_reconnected_draw_slot_is_scoped_to_its_owner():
    s = configured_turn([11, 12, 13, 14, 15, 16, 21, 22, 23, 34, 35, 36, 28, 39])
    s.player_list[0].has_draw_slot = True
    assert s.build_game_start_payload(0)["game_info"]["guizhou_info"]["self_has_draw_slot"]
    for viewer in (1, 2, 3):
        assert not s.build_game_start_payload(viewer)["game_info"]["guizhou_info"]["self_has_draw_slot"]
    assert not s.guizhou_info()["self_has_draw_slot"]
    s.player_list[0].has_draw_slot = False
    assert not s.build_game_start_payload(0)["game_info"]["guizhou_info"]["self_has_draw_slot"]
    s.player_list[0].has_draw_slot = True
    s.end_draw()
    assert not s.build_game_start_payload(0)["game_info"]["guizhou_info"]["self_has_draw_slot"]


def test_early_end_reveals_opening_kong_before_checking_all_hands():
    s = configured_turn([11]*4 + [12,13,14,21,22,23,31,32,33,39])
    for p in s.player_list:
        p.discard_count = 0
    s.player_list[0].initial_quads = {11}
    s.open_action_window(s.begin_turn(0))
    act(s, 0, "angang", target_tile=11)
    s.apply_action_results(s.live_pending_window, {})
    assert s.hidden_opening_kongs
    s.end_draw()
    assert not s.hidden_opening_kongs
    assert s.player_list[0].combination_mask[0] == [2,11,0,11,0,11,2,11]
    assert s.final_settlement_payloads(3)[0]["show_result_info"]["guizhou_end_hands"][0] == s.player_list[0].hand_tiles


def multi_ron_state():
    s = configured_turn()
    s.player_list[1].hand_tiles = [11]*3 + [12]*3 + [22]*3 + [24]*3 + [39]
    s.player_list[2].hand_tiles = [13,13,14,14,15,15,16,16,17,17,18,18,39]
    s.player_list[3].hand_tiles = [31,31,31,32,33,34,35,36,37,38,38,38,39]
    s.player_list[0].discard_tiles = [39]
    s.player_list[0].discard_riichi_flags = [False]
    s.discard_log = [(0, 39)]
    s.tiles_list = [38]
    attach_record(s)
    s.machine.transition(P.RESPONSE)
    return s


def test_three_ron_one_physical_tile_stable_identity_and_one_ledger():
    s = multi_ron_state()
    for i, original in enumerate((2, 0, 3, 1)):
        s.player_list[i].original_player_index = original
    s.settle_winners([3, 1, 2], "discard", 39, payer=0)
    assert not s.player_list[0].discard_tiles
    assert [p.won_tiles for p in s.player_list] == [[], [], [], [39]]
    assert s.next_dealer == 0
    results = s.final_settlement_payloads(0)
    expected = {p.original_player_index: s.round_settlement.changes[p.player_index] for p in s.player_list}
    cumulative = dict.fromkeys(range(4), 0)
    for i, payload in enumerate(results):
        info = payload["show_result_info"]
        assert info["multi_ron"] and info["recycle_discard"] == (i == 2)
        assert info["guizhou_round_changes"] == expected
        assert set(info["guizhou_end_hands"]) == set(range(4))
        assert all(len(info["guizhou_end_hands"][w]) == 14 for w in (1, 2, 3))
        for identity, value in info["score_changes"].items():
            cumulative[identity] += value
    assert cumulative == expected
    scores = [p.score for p in s.player_list]
    assert [p.score for p in s.player_list] == scores
    s.final_settlement_payloads(3)
    assert [p.score for p in s.player_list] == scores
    with pytest.raises(ValueError):
        s.settle_winners([1], "discard", 39, payer=0)


def test_draw_has_single_score_tick_and_delta_history():
    s = configured_turn()
    s.player_list[0].hand_tiles = PUNGS[:-1]
    s.tiles_list = []
    attach_record(s)
    s.end_draw()
    assert s.next_dealer == 0
    payload = s.final_settlement_payloads(0)[0]["show_result_info"]
    assert sum(payload["score_changes"].values()) == 0
    assert payload["score_changes"][0] > 0
    s.finalize_round_recording()
    score_tick = [t for t in ticks(s) if t[:2] == ["guizhou", "draw_score"]]
    assert len(score_tick) == 1 and score_tick[0][2] == s.round_settlement.changes
    for p in s.player_list:
        assert int(p.score_history[-1]) == s.round_settlement.changes[p.player_index]
    previous = deepcopy(ticks(s))
    s.finalize_round_recording()
    assert ticks(s) == previous
    with pytest.raises(ValueError):
        s.end_draw()


def test_tail_upper_then_lower_interleaved_with_front_draws():
    s = configured_turn()
    s.tiles_list = [11, 12, 13, 14, 15, 16]
    s.draw_for(0, replacement=True)
    assert s.player_list[0].hand_tiles[-1] == 15
    s.draw_for(1)
    assert s.player_list[1].hand_tiles[-1] == 11
    s.draw_for(0, replacement=True)
    assert s.player_list[0].hand_tiles[-1] == 16
    s.draw_for(0, replacement=True)
    assert s.player_list[0].hand_tiles[-1] == 13
    assert s.tiles_list == [12, 14]


def test_single_winner_rotates_identity_next_dealer_and_final_hand_ends():
    s = configured_turn(PUNGS, index=2)
    s.settle_winners([2], "self_draw", 29)
    assert s.next_dealer == 2
    s.machine.transition(P.READY)
    s.advance_round_after_ready()
    assert [p.original_player_index for p in s.player_list] == [2, 3, 0, 1]
    s.initialize_round()
    assert [p.player_index for p in s.player_list] == [0, 1, 2, 3]
    s.current_round = 4
    s.machine.transition(P.TURN)
    s.end_draw()
    assert s.match_finishing
    s.machine.transition(P.READY)
    with pytest.raises(RuntimeError):
        s.advance_round_after_ready()


@pytest.mark.parametrize("index,drawn,tile,position", [
    (True, False, 11, 0), (4, False, 11, 0), (0, "false", 11, 0),
    (0, True, 11, 13), (0, False, 28, 999), (0, False, True, 0),
])
def test_malformed_cut_never_changes_state(index, drawn, tile, position):
    s = configured_turn()
    before = physical(s)
    with pytest.raises(ValueError):
        s.validate_response(index, {"action_type": "cut", "TileId": tile, "cutClass": drawn, "cutIndex": position}, ["cut"])
    assert physical(s) == before


def test_network_auth_stale_tick_and_duplicate_are_rejected():
    async def run():
        s = configured_turn()
        ws = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: s),
                                 players={"own": SimpleNamespace(user_id=101), "outside": SimpleNamespace(user_id=999)})
        message = dict(type="gamestate/guizhou/cut_tile", gamestate_id=s.gamestate_id,
                       action_tick=s.server_action_tick, TileId=28, cutClass=True, cutIndex=13)
        await handle_action(server, "outside", message, ws)
        assert all(q.empty() for q in s.action_queues.values())
        await handle_action(server, "own", dict(message, action_tick=-1), ws)
        assert ws.send_json.call_args.args[0]["type"].endswith("broadcast_hand_action")
        await handle_action(server, "own", dict(message, TileId=99), ws)
        assert ws.send_json.call_args.args[0]["type"] == "tips"
        await handle_action(server, "own", message, ws)
        assert s.action_queues[0].qsize() == 1
        await handle_action(server, "own", message, ws)
        assert s.action_queues[0].qsize() == 1
    asyncio.run(run())


def test_reconnect_and_spectator_snapshots_retain_same_projection():
    async def run():
        s = state()
        start(s)
        sent = []
        async def capture(index, payload): sent.append((index, payload))
        s.send_payload_to_player = capture
        s.player_list[1].tag_list.append("offline")
        await s.player_reconnect(102)
        assert "offline" not in s.player_list[1].tag_list
        assert sent[0][0] == 1
        view = sent[0][1]["game_info"]["players_info"]
        assert all((p["hand_tiles"] is not None) == (i == 1) for i, p in enumerate(view))
        ws = SimpleNamespace(send_json=AsyncMock())
        s.game_server = SimpleNamespace(user_id_to_connection={999: SimpleNamespace(websocket=ws)})
        await s.send_realtime_spectator_snapshot(999, 1)
        assert ws.send_json.call_args_list[0].args[0] == sent[0][1]
        count = ws.send_json.call_count
        await s.send_realtime_spectator_snapshot(999, -1)
        assert ws.send_json.call_count == count
    asyncio.run(run())


@pytest.mark.parametrize("key,value", [
    ("use_flowers", True), ("open_cuohe", True), ("tactical_call", True), ("claim_protection", True),
    ("tian_di_ren_he", True), ("tips", "false"), ("allow_spectator", 1), ("game_round", True),
    ("game_round", 5), ("round_timer", -1), ("step_timer", 101), ("sub_rule", "guizhou/joker"),
    ("detailed_config", {"jokers": True}),
])
def test_room_validator_rejects_invalid_configuration(key, value):
    config = dict(room_name="test", game_round=1, round_timer=20, step_timer=5)
    config[key] = value
    with pytest.raises(ValueError):
        GuizhouRoomValidator(**config)


def test_room_configuration_defaults_and_ordinary_toggle_branches():
    for game_round in range(1, 5):
        for switch in (False, True):
            data = GuizhouRoomValidator(room_name=" test ", game_round=game_round, round_timer=0,
                step_timer=0, tips=switch, tourist_limit=switch, allow_spectator=switch,
                count_tips=switch, pointer_tips=switch).model_dump()
            assert data["room_name"] == "test" and data["game_round"] == game_round
            assert data["tips"] == switch and data["allow_spectator"] == switch
            assert data["detailed_config"] == {"rule_version": RULE_VERSION}
            assert not any(data[k] for k in ("use_flowers", "open_cuohe", "tactical_call", "claim_protection"))


def fake_room_manager():
    connection = SimpleNamespace(user_id=101, username="tester", current_room_id=None)
    manager = SimpleNamespace(game_server=SimpleNamespace(players={"c": connection}, db_manager=SimpleNamespace(
        get_user_settings=Mock(return_value={"username": "tester", "voice_id": 2}))),
        _reject_room_entry_conflicts=Mock(return_value=None), _normalize_event_id=lambda x: x,
        _validate_event_for_room=Mock(return_value=None), _generate_room_id=lambda: 123456,
        _apply_event_fields=Mock(), _broadcast_room_info=AsyncMock(), rooms={}, room_passwords={})
    return manager, connection


def test_create_room_validates_before_mutation_and_emits_canonical_profile():
    async def run():
        manager, connection = fake_room_manager()
        invalid = await create_guizhou_room(manager, "c", room_name="test", use_flowers=True)
        assert not invalid.success and not manager.rooms and connection.current_room_id is None
        result = await create_guizhou_room(manager, "c", room_name="test", password="pw", event_id="e", count_tips=True)
        assert result.success and connection.current_room_id == 123456
        room = manager.rooms[123456]
        assert room["room_rule"] == "guizhou" and room["sub_rule"] == "guizhou/standard"
        assert room["player_settings"][101]["voice_id"] == 2 and room["count_tips"]
        assert manager.room_passwords[123456] == "pw"
        assert manager._apply_event_fields.call_args.args[1] == "e"
        assert not (await create_guizhou_room(manager, "c", room_name="test")).success
    asyncio.run(run())


def test_room_wire_handler_forwards_fixed_flags_for_validation():
    async def run():
        result = SimpleNamespace(model_dump=lambda **_: {"success": False})
        call = AsyncMock(return_value=result)
        server = SimpleNamespace(room_manager=SimpleNamespace(create_Guizhou_room=call))
        ws = SimpleNamespace(send_json=AsyncMock())
        await handle_create_guizhou_room(server, "c", {"roomname": "x", "use_flowers": True}, ws)
        assert call.call_args.kwargs["use_flowers"] is True
        assert ws.send_json.call_count == 1
    asyncio.run(run())
