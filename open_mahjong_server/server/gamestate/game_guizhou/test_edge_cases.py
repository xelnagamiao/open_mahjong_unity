"""Adversarial requests and lifecycle boundaries; no database or live sockets."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from ...game_calculation.guizhou.ledger import Kong, settle
from ...game_calculation.guizhou.rules import TILES, meld_tiles
from ...game_calculation.guizhou.test_ledger import players
from ...room.guizhou_room import create_guizhou_room
from .actions import ActionPolicy, ready_cuts, legal_cuts, turn_actions
from .get_action import handle_action
from .state_machine import Phase as P
from .test_flow import PLAIN, PUNGS, act, configured_turn, physical, state
from .test_contracts import attach_record, fake_room_manager, multi_ron_state, ticks


@pytest.mark.parametrize("changes", [
    {"players": players()[:3]}, {"ready_values": [0, 0]},
    {"winners": [0, 0]}, {"winners": [True]}, {"winners": [4]},
    {"source": "invented"}, {"source": "draw"},
    {"winners": [0, 1], "scores": {0: 3, 1: 3}},
    {"indicator": 41},
    {"kongs": [Kong(1, 11, "unknown")]},
    {"kongs": [Kong(1, 11, "direct", None)]},
    {"kongs": [Kong(1, 11, "direct", 1)]},
])
def test_ledger_rejects_invalid_settlement_contract_without_mutating_hands(changes):
    data = dict(players=players(), winners=[0], source="self_draw", scores={0: 3})
    data.update(changes)
    before = deepcopy(data["players"])
    with pytest.raises(ValueError):
        settle(**data)
    assert data["players"] == before


@pytest.mark.parametrize("code", ["kXX", "G41", "g90", "s11", None])
def test_corrupt_melds_are_rejected_before_calculation(code):
    with pytest.raises(ValueError):
        meld_tiles(code)


@pytest.mark.parametrize("winners,source,tile,payer", [
    ([], "self_draw", 29, None), ([0, 0], "self_draw", 29, None),
    ([True], "self_draw", 29, None), ([4], "self_draw", 29, None),
    ([0], "wrong", 29, None), ([0, 1], "self_draw", 29, None),
    ([0], "self_draw", 11, None), ([0], "discard", 29, None),
    ([0], "discard", 29, 0), ([0], "discard", 29, 1),
    ([0], "rob_kong", 29, 1),
])
def test_invalid_wins_are_atomic(winners, source, tile, payer):
    s = configured_turn(PUNGS)
    before = physical(s)
    with pytest.raises(ValueError):
        s.settle_winners(winners, source, tile, payer=payer)
    assert physical(s) == before
    assert not s.deferred_hu_settlements and s.round_settlement is None


def test_invalid_shape_cannot_force_self_draw_and_winners_cannot_win_twice():
    s = configured_turn(PLAIN[:-1] + [39])
    with pytest.raises(ValueError):
        s.settle_winners([0], "self_draw", 39)
    s.player_list[0].is_hu = True
    assert not s.can_win(0, "discard", 28)
    s.player_list[0].is_hu = False
    s.player_list[0].has_draw_slot = False
    assert not s.can_win(0, "self_draw", 39)


def test_exact_wall_contract_and_reinitialization_gate():
    s = state()
    with pytest.raises(ValueError):
        s.initialize_round(wall=[11] * 108)
    wall = [t for t in TILES for _ in range(4)]
    s.initialize_round(wall=wall)
    assert s.tiles_list == wall[53:]
    s.open_action_window(s.opening_window())
    with pytest.raises(RuntimeError):
        s.initialize_round()


@pytest.mark.parametrize("data", [
    {"TileId": 28, "cutClass": False},
    {"TileId": 11, "cutIndex": -1},
    {"TileId": 28, "cutIndex": -1},
])
def test_legacy_cut_requests_preserve_draw_vs_held_identity(data):
    s = configured_turn()
    tile, position, drawn = s._cut_identity(0, data)
    assert s.player_list[0].hand_tiles[position] == tile
    assert drawn == (position == 13)
    if data.get("cutClass") is False:
        assert position == 12 and not drawn


def test_ready_cannot_hand_cut_the_other_identical_copy():
    s = configured_turn()
    s.player_list[0].ready_kind = "soft_ready"
    with pytest.raises(ValueError, match="只能摸切"):
        s._cut_identity(0, {"TileId": 28, "cutClass": False})
    s.player_list[0].has_draw_slot = False
    assert not legal_cuts(s, 0)


def test_cut_and_kong_requests_with_invalid_positions_are_atomic():
    s = configured_turn()
    for data in ({"action_type": "cut", "TileId": 11, "cutIndex": 2},
                 {"action_type": "angang", "target_tile": True},
                 {"action_type": "jiagang", "target_tile": 11}):
        before = physical(s)
        with pytest.raises(ValueError):
            s.validate_response(0, data, [data["action_type"]])
        assert physical(s) == before
    s.player_list[0].hand_tiles[-1] = 39
    with pytest.raises(ValueError, match="不在手牌"):
        s._cut_identity(0, {"TileId": 39, "cutClass": False})


def test_stale_windows_partial_responses_and_client_settlement_never_advance():
    s = configured_turn()
    window = s.live_pending_window
    with pytest.raises(ValueError):
        s.apply_action_results(window, {})
    with pytest.raises(ValueError):
        s.apply_action_results(window, {}, settlements=[{"winner": 0}])
    with pytest.raises(ValueError):
        s.apply_action_results(dict(window), {})
    with pytest.raises(ValueError):
        s.validate_response(0, None, [])
    assert s.live_pending_window is window and s.machine.phase == P.TURN
    s.machine.transition(P.END)
    s.machine.transition(P.READY)
    s.live_pending_window = {"status": P.READY.value, "action_tick": s.server_action_tick, "actions": {}}
    with pytest.raises(ValueError, match="不接受"):
        s.apply_action_results(s.live_pending_window, {})


def test_old_queue_is_drained_and_no_cut_is_offered_without_a_draw():
    s = configured_turn()
    s.action_queues[0].put_nowait({"action_type": "cut", "TileId": 11})
    s.action_events[0].set()
    s.open_action_window(s.begin_turn(0))
    assert s.action_queues[0].empty() and not s.action_events[0].is_set()
    s.player_list[0].discard_count = 0
    s.player_list[0].has_draw_slot = False
    assert not ready_cuts(s, 0)
    s.player_list[0].ready_kind = "hard_ready"
    assert not turn_actions(s, 0, discard_only=True)[0]


def test_waiting_tile_refresh_excludes_only_a_real_draw_slot():
    s = configured_turn()
    policy = ActionPolicy()
    assert 28 in policy.refresh_waiting_tiles(s, 0, exclude_last_tile=True)
    assert not policy.refresh_waiting_tiles(s, 0)
    s.player_list[0].hand_tiles.pop()
    s.player_list[0].has_draw_slot = False
    assert 28 in policy.refresh_waiting_tiles(s, 0, exclude_last_tile=True)


def test_ready_record_markers_and_duplicate_hu_record_are_idempotent():
    s = attach_record(configured_turn(PUNGS))
    s.declare_ready(0, "soft_ready")
    assert ticks(s)[-1] == ["guizhou", "ready", 0, "soft_ready"]
    s.settle_winners([0], "self_draw", 29)
    before = deepcopy(ticks(s))
    s.record_visible_action({"action": "hu_self", "player": 0})
    assert ticks(s) == before
    other = configured_turn()
    other.finalize_round_recording()
    other = attach_record(other)
    before = deepcopy(ticks(other))
    other.record_visible_action({"action": "hu_first", "player": 2})
    assert ticks(other) == before


def test_first_discard_chicken_won_immediately_loses_charge_bonus():
    s = configured_turn()
    p = s.player_list[1]
    p.hand_tiles = [12,12,12,22,22,22,33,33,33,38,38,38,31]
    s.player_list[0].hand_tiles[-1] = 31
    s.open_action_window(s.begin_turn(0))
    act(s, 0, "cut", TileId=31, cutClass=True, cutIndex=13)
    replies = {i: {"action_type": "hu" if i == 1 else "pass"} for i,a in s.action_dict.items() if a}
    s.apply_action_results(s.live_pending_window, replies)
    assert s.chickens[31].won
    assert not any(t.reason == "冲锋鸡加分" and t.tile == 31 for t in s.round_settlement.transfers)


@pytest.mark.parametrize("reason", ["no_connection", "no_login", "game_conflict", "event_conflict", "no_settings"])
def test_create_room_access_rejections_leave_room_maps_untouched(reason):
    async def run():
        manager, connection = fake_room_manager()
        denied = SimpleNamespace(success=False)
        if reason == "no_connection": manager.game_server.players.clear()
        elif reason == "no_login": connection.user_id = 0
        elif reason == "game_conflict": manager._reject_room_entry_conflicts.return_value = denied
        elif reason == "event_conflict": manager._validate_event_for_room.return_value = denied
        else: manager.game_server.db_manager.get_user_settings.return_value = None
        result = await create_guizhou_room(manager, "c", room_name="test")
        assert not result.success and not manager.rooms and not manager.room_passwords
        manager._broadcast_room_info.assert_not_awaited()
    asyncio.run(run())


def test_room_without_password_does_not_leave_password_entry():
    async def run():
        manager, _ = fake_room_manager()
        result = await create_guizhou_room(manager, "c", room_name="test")
        assert result.success and not manager.room_passwords
    asyncio.run(run())


def test_protocol_handles_detached_viewers_and_inactive_connections():
    async def run():
        s = configured_turn()
        assert s._adapt({"game_info": {}}, -1)["game_info"]["self_hand_tiles"] is None
        no_actor = dict(s.live_pending_window, player=None)
        assert s._ask_payload(0, no_actor)["ask_hand_action_info"]["player_index"] == 0
        # Normalized universal field actually consumed by the Unity turn clock.
        assert s._ask_payload(0, no_actor)["ask_hand_action_info"]["kong_candidates"] == {"angang": [], "jiagang": []}
        s.live_pending_window = None
        assert len(s.restore_payloads(0)) == 1
        s.send_payload_to_player = AsyncMock()
        await s.player_reconnect(9999)
        s.send_payload_to_player.assert_not_awaited()
        await s.player_reconnect(101)
        s.send_payload_to_player.assert_awaited_once()
        await s.send_realtime_spectator_snapshot(1, 0)
        s.game_server = SimpleNamespace(user_id_to_connection={1: SimpleNamespace(websocket=None)})
        await s.send_realtime_spectator_snapshot(1, 0)
        await s.send_realtime_spectator_snapshot(2, 0)
    asyncio.run(run())


def test_three_win_panels_are_sent_in_order_to_every_seat(monkeypatch):
    async def run():
        s = multi_ron_state()
        s.settle_winners([1, 2, 3], "discard", 39, payer=0)
        s._send_claim_protection_payload = AsyncMock()
        from ..public import round_end_timing
        monkeypatch.setattr(round_end_timing, "sichuan_settle_hu_panel_wait_seconds", lambda _: 0)
        await s.present_final_settlements()
        calls = s._send_claim_protection_payload.call_args_list
        assert len(calls) == 12
        assert [c.args[1]["show_result_info"]["hepai_player_index"] for c in calls] == [1]*4+[2]*4+[3]*4
        assert all(c.args[1]["show_result_info"]["recycle_discard"] for c in calls[-4:])
    asyncio.run(run())


def test_network_missing_state_wrong_family_and_stale_ready_responses():
    async def run():
        s = configured_turn()
        ws = SimpleNamespace(send_json=AsyncMock())
        manager = SimpleNamespace(get_game_state_by_gamestate_id=lambda _: None)
        server = SimpleNamespace(gamestate_manager=manager, players={"c": SimpleNamespace(user_id=101)})
        await handle_action(server, "c", {}, ws)
        manager.get_game_state_by_gamestate_id = lambda _: SimpleNamespace(room_rule="guobiao")
        await handle_action(server, "c", {}, ws)
        manager.get_game_state_by_gamestate_id = lambda _: s
        await handle_action(server, "missing", {}, ws)
        ws.send_json.assert_not_awaited()
        s.end_draw()
        s.machine.transition(P.READY)
        await handle_action(server, "c", {"action_tick": -1}, ws)
        assert ws.send_json.call_args.args[0]["type"] == "gamestate/guizhou/ready_status"
        ws.send_json.reset_mock()
        s.machine.transition(P.START)
        s.live_pending_window = None
        await handle_action(server, "c", {"action_tick": -1}, ws)
        ws.send_json.assert_not_awaited()
    asyncio.run(run())


@pytest.mark.parametrize("claim,count", [("peng", 2), ("gang", 3)])
def test_claimed_ready_chicken_moves_marker_and_assigns_responsibility(claim, count):
    s = configured_turn()
    p = s.player_list[0]
    p.hand_tiles = [12,13,14,22,23,24,35,36,37,39,39,39,31,31]
    p.discard_count = 0
    receiver = s.player_list[1]
    receiver.hand_tiles = [31]*count + [11,14,17,19,21,24,27,29,32,35,38][:13-count]
    s.open_action_window(s.begin_turn(0))
    act(s, 0, "guizhou_ready")
    act(s, 0, "cut", TileId=31, cutClass=True, cutIndex=13)
    assert p.discard_riichi_flags == [True]
    replies = {i: {"action_type": claim if i == 1 else "pass"} for i,a in s.action_dict.items() if a}
    s.apply_action_results(s.live_pending_window, replies)
    assert not p.discard_tiles and p.pending_ready_marker
    assert s.chickens[31].supplier == 0 and s.chickens[31].claimant == 1
    if claim == "gang":
        assert s.kongs[-1] == Kong(1, 31, "direct", 0)
        assert receiver.has_draw_slot and s.machine.phase == P.TURN
    else:
        assert not receiver.has_draw_slot and s.machine.phase == P.DISCARD_ONLY
    # The original ready marker follows the declarer's next real discard.
    s.machine.transition(P.RESPONSE)
    s.open_action_window(s.draw_for(0))
    act(s, 0, "cut", TileId=p.hand_tiles[-1], cutClass=True, cutIndex=len(p.hand_tiles)-1)
    assert p.discard_riichi_flags == [True] and not p.pending_ready_marker


def test_legal_cut_api_and_index_only_legacy_cut_agree():
    s = configured_turn()
    assert s.legal_discard_tiles(0) == set(s.player_list[0].hand_tiles)
    assert s._cut_identity(0, {"TileId": 11, "cutIndex": 0}) == (11, 0, False)
    s.player_list[0].ready_kind = "soft_ready"
    assert s.legal_discard_tiles(0) == {28}
    s.waiting_players_list.clear()
    assert s._build_timeout_action(0)["TileId"] == 28


def test_empty_wall_draw_dispatches_one_draw_settlement():
    s = configured_turn()
    s.player_list[0].hand_tiles = PUNGS[:-1]
    s.player_list[0].has_draw_slot = False
    s.tiles_list.clear()
    s.draw_for(1)
    assert s.machine.phase == P.END
    assert s.round_settlement.changes == [24, -8, -8, -8]


def test_bot_kongs_and_hidden_opening_kong_use_only_public_information():
    from .bot import choose_action, pick_cut
    from .test_contracts import prepare_added_kong
    s = configured_turn([11]*4 + [12,13,14,21,22,23,31,32,33,39])
    assert choose_action(s, 0, ["angang", "cut"]) == {"action_type": "angang", "target_tile": 11}
    other = s.player_list[1]
    other.combination_tiles = ["G29"]
    other.combination_mask = [[2,29]*4]
    s.hidden_opening_kongs = {(1, 0)}
    before = pick_cut(s, 0)
    other.combination_tiles = ["G38"]
    assert pick_cut(s, 0) == before
    s = prepare_added_kong(False)
    assert choose_action(s, 0, ["jiagang", "cut"]) == {"action_type": "jiagang", "target_tile": 29}
    assert choose_action(s, 0, ["pass"]) == {"action_type": "pass"}


def test_human_next_round_ready_keeps_time_bank_and_loop_failure_cancels_bots():
    async def run():
        s = configured_turn()
        s.end_draw()
        s.machine.transition(P.READY)
        s.waiting_players_list = [0]
        s.action_dict = {0: ["ready"]}
        s.player_list[0].remaining_time = 7
        s.action_queues[0].put_nowait({"action_type": "ready"})
        assert (await s.wait_action(1))[0]["action_type"] == "ready"
        assert s.player_list[0].remaining_time == 7
        pending = asyncio.create_task(asyncio.Event().wait())
        s.bot_tasks.add(pending)
        s.initialize_round = lambda: (_ for _ in ()).throw(RuntimeError("test lifecycle abort"))
        with pytest.raises(RuntimeError, match="lifecycle abort"):
            await s.run_game_loop()
        await asyncio.gather(pending, return_exceptions=True)
        assert pending.cancelled()
    asyncio.run(run())


def test_protocol_without_game_snapshot_and_explicit_disabled_house_options():
    from ...room.guizhou_room import GuizhouRoomValidator
    s = configured_turn()
    assert s._adapt({"type": "test"}, 2) == {"type": "test", "player_index": 2}
    validated = GuizhouRoomValidator(room_name="test", game_round=1, round_timer=0, step_timer=5, use_flowers=False,
        claim_protection=False, open_cuohe=False, tactical_call=False, tian_di_ren_he=False)
    assert not validated.use_flowers and not validated.tian_di_ren_he
