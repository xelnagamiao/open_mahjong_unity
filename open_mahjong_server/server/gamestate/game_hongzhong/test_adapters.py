"""动作窗口、机器人、时限和真实协议对象的红中适配回归。"""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from . import bot, hints, result
from .test_flow import HAND, draw, hand, make_state, ticks
from ...game_calculation.hongzhong import rules as book
from ..game_taiwan.boardcast import (broadcast_game_start, send_realtime_spectator_snapshot,
    _build_do_action_payload)

STATE = "server.gamestate.game_hongzhong.HongzhongGameState"
BOT = "server.gamestate.game_hongzhong.bot"


def connection():
    return SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))


def activate(state, index, actions, status="waiting_hand_action"):
    state.current_player_index = index
    state.game_status = status
    state.action_dict = {i: actions if i == index else [] for i in range(4)}
    state.prepare_action_window()
    state.waiting_players_list = [index]
    state.bot_speed = "instant"
    state.player_list[index].user_id = index


def test_actual_self_draw_acceptance_and_replacement():
    async def scenario():
        state = make_state(); player = hand(state, 0, HAND); draw(player, 45)
        state.last_draw_after_kong = True
        await state._prepare_hand_action_after_draw()
        assert state.game_status == "waiting_hand_action"
        assert "hu_self" in state.action_dict[0]
        assert "杠上花" in state.result_dict["hu_self"]["fan_names"]
        state.accept_self_draw(0)
        assert state.game_status == "END"
        assert state.pending_winners[0]["tile"] == 45
        assert state.pending_winners[0]["detail"] == book.score(HAND + [45], winning_tile=45, replacement=True)
        assert state._liability_payer_for_win() is None
        state._register_initial_heavenly_ready()
        state._mark_eight_flowers_if_ready(0)
        state._remember_claim_liability(0, 1, 11)
        assert not player.declared_ready
    asyncio.run(scenario())


def test_real_cut_invalid_cut_ready_forgery_and_timeout():
    async def scenario():
        state = make_state(); player = hand(state, 0, HAND); draw(player, 45)
        await state._warm_hints(0)
        before = deepcopy(player.hand_tiles)
        await state.execute_cut(0, {"TileId": 45, "cutClass": True}, declare_ready=True)
        await state.execute_cut(0, {"TileId": 19, "cutClass": False})
        assert player.hand_tiles == before and ticks(state)[0][0] == "hongzhong"
        await state.execute_timeout_cut(0)
        assert player.discard_tiles == [45]
        assert player.hand_tiles == HAND and not player.has_draw_slot
        assert state._hint_cache[0]["source_hand_tiles"] == HAND
        assert state.game_status == "deal_card"
        assert not player.declared_ready
        state.current_player_index = 0
        hand(state, 1, HAND)
        state.tiles_list = [28]
        await state._deal_normal()
        assert state.current_player_index == 1 and state.last_draw_was_last
        assert "hu_self" in state.action_dict[1]
        await state.execute_timeout_cut(1)
        await state._deal_normal()
        assert state.game_status == "END" and state.draw_reason == "exhaustive"
    asyncio.run(scenario())


def test_forbidden_claims_kongs_and_candidates_do_not_mutate():
    async def scenario():
        state = make_state(); player = hand(state, 0, [45] * 4 + HAND[4:]); draw(player, 11)
        before = list(player.hand_tiles)
        await state.execute_angang(0, 45)
        await state.execute_jiagang(0, 45)
        await state.execute_claim(1, "peng")
        state.player_list[0].discard_tiles = [45]
        await state.execute_claim(1, "gang")
        state.player_list[0].discard_tiles = [11]
        await state.execute_claim(1, "chi_left")
        assert player.hand_tiles == before and state.kong_ledger == []
        assert state.build_private_hand_action_info(0)["kong_candidates"] == {"angang": [], "jiagang": []}
        player = hand(state, 0, [11] * 4 + [22, 23, 24, 31, 32, 33, 27, 28, 29]); draw(player, 45)
        assert state.build_private_hand_action_info(0)["kong_candidates"]["angang"] == [11]
        player.tag_list.append("peida")
        assert not state.kong_allowed(0, 11, "concealed")
        player.tag_list.clear(); state.tiles_list = []
        assert not state.kong_allowed(0, 11, "concealed")
    asyncio.run(scenario())


def test_hand_action_window_includes_available_kongs_and_clock_without_timestamp():
    state = make_state()
    draw(hand(state, 0, [11] * 3 + HAND[3:]), 11)
    assert "angang" in state.check_hand_actions(0)[0]
    draw(hand(state, 0, [22, 23, 24, 31, 32, 33, 27, 28, 29, 45], ["k11"]), 11)
    assert "jiagang" in state.check_hand_actions(0)[0]
    assert state.claim_clock(state.player_list[0], reconnecting=True) == (20, 5)


@pytest.mark.parametrize("allowed,phase,action", [
    (["hu_self", "cut"], "waiting_hand_action", "hu_self"),
    (["gang", "peng", "pass"], "waiting_action_after_cut", "gang"),
    (["peng", "pass"], "waiting_action_after_cut", "peng"),
    (["pass"], "waiting_action_after_cut", "pass"),
])
def test_bot_submits_actual_authorized_action_queue(allowed, phase, action):
    async def scenario():
        state = make_state(); draw(hand(state, 0, HAND), 45)
        activate(state, 0, allowed, phase)
        await bot.hongzhong_bot_action(state, 0, allowed, phase)
        queued = state.action_queues[0].get_nowait()
        assert queued["action_type"] == action
        assert state.action_events[0].is_set()
    asyncio.run(scenario())


@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_bot_kong_selection_uses_physical_numbers(kind):
    async def scenario():
        state = make_state()
        if kind == "angang":
            draw(hand(state, 0, [11] * 3 + HAND[3:]), 11)
        else:
            draw(hand(state, 0, [22, 23, 24, 31, 32, 33, 27, 28, 29, 45], ["k11"]), 11)
        activate(state, 0, [kind, "cut"])
        await bot.hongzhong_bot_action(state, 0, [kind, "cut"], state.game_status)
        queued = state.action_queues[0].get_nowait()
        assert queued["action_type"] == kind and queued["target_tile"] == 11
    asyncio.run(scenario())


def test_bot_cut_core_calculation_and_public_visibility_only():
    async def scenario():
        state = make_state(); draw(hand(state, 0, HAND), 45)
        state.player_list[1].discard_tiles = [19, 39]
        state.player_list[1].combination_tiles = ["k17"]
        activate(state, 0, ["angang", "jiagang", "cut"])
        await bot.hongzhong_bot_action(state, 0, state.action_dict[0], state.game_status)
        queued = state.action_queues[0].get_nowait()
        assert queued["action_type"] == "cut" and queued["TileId"] in state.player_list[0].hand_tiles
        assert queued["TileId"] != 45
        not_ready = [11, 13, 16, 19, 21, 24, 27, 31, 34, 37, 39, 45, 45, 45]
        chosen, position = bot.best_discard(not_ready, [], {})
        assert chosen != 45 and not_ready[position] == chosen
    asyncio.run(scenario())


@pytest.mark.parametrize("scenario", ["not_actionable", "stale_before", "empty", "no_cut", "empty_claim", "stale_after"])
def test_bot_ignores_unavailable_or_expired_decisions(scenario):
    async def run():
        state = make_state(); draw(hand(state, 0, HAND), 45)
        actions = ["cut"] if scenario != "no_cut" else ["angang"]
        phase = "waiting_action_after_cut" if scenario == "empty_claim" else "waiting_hand_action"
        if scenario == "empty_claim": actions = []
        activate(state, 0, actions, phase)
        if scenario == "empty": state.player_list[0].hand_tiles = []
        async def actionable(*_):
            if scenario == "stale_before": state.server_action_tick += 1
            return scenario != "not_actionable"
        async def calculate(_state, fn, *args):
            answer = fn(*args)
            if scenario == "stale_after": state.server_action_tick += 1
            return answer
        with patch(BOT + "._wait_until_actionable", side_effect=actionable), patch(BOT + ".run_room_bot_cpu", side_effect=calculate):
            await bot.hongzhong_bot_action(state, 0, actions, phase)
        assert state.action_queues[0].empty()
    asyncio.run(run())


def test_real_hand_and_claim_timeout_preserve_clock_policy():
    async def scenario():
        state = make_state(step_timer=0, round_timer=0)
        draw(hand(state, 0, HAND), 45)
        activate(state, 0, ["cut"])
        await state.wait_action()
        assert state.player_list[0].discard_tiles == [45]
        state.player_list[0].discard_tiles = [11]
        hand(state, 1, [11, 11] + HAND[2:])
        state.current_player_index = 0
        state.game_status = "waiting_action_after_cut"
        state.action_dict = state.check_discard_actions(11)
        state.prepare_action_window()
        await state.wait_action()
        assert not state.player_list[1].combination_tiles and state.game_status == "deal_card"
        assert not state.player_list[1].water
    asyncio.run(scenario())


def test_network_snapshots_reconnect_and_spectator_reveal_only_viewed_seat():
    async def scenario():
        state = make_state(allow_spectator=True, room_id=987001)
        for i in range(4):
            hand(state, i, [t + (i % 3) * 10 for t in HAND if t < 20] + [45])
            state._hint_cache[i] = hints.compute_hand(tuple(state.player_list[i].hand_tiles), (), None, False)[0]
            state.game_server.user_id_to_connection[101 + i] = connection()
        state.game_server.user_id_to_connection[999] = connection()
        state.send_to_realtime_spectators = AsyncMock()
        await broadcast_game_start(state)
        for i in range(4):
            conn = state.game_server.user_id_to_connection[101 + i]
            payload = conn.websocket.send_json.call_args.args[0]["game_info"]
            assert payload["hongzhong_hints"]["source_hand_tiles"] == state.player_list[i].hand_tiles
            assert [p["player_index"] for p in payload["players_info"] if "hand_tiles" in p] == [i]
        state.player_list[1].tag_list.append("offline")
        await state.player_reconnect(102)
        assert "offline" not in state.player_list[1].tag_list
        await state.player_reconnect(444)
        await send_realtime_spectator_snapshot(state, 999, 2)
        snapshot = state.game_server.user_id_to_connection[999].websocket.send_json.call_args.args[0]["game_info"]
        assert [p["player_index"] for p in snapshot["players_info"] if "hand_tiles" in p] == [2]
        assert snapshot["hongzhong_hints"]["source_hand_tiles"] == state.player_list[2].hand_tiles
        action = _build_do_action_payload(state, viewer_index=2, action_list=["deal_tile"], action_player=0, deal_tile=45)
        assert action.get("deal_tile") in (0, None) and "hongzhong_hints" not in action
        await result.broadcast_result(state, hu_class="liuju")
        expected = deepcopy(state._terminal_result)
        assert len(expected["show_result_info"]["revealed_hands"]) == 4
        await state.player_reconnect(102)
        assert state.game_server.user_id_to_connection[102].websocket.send_json.call_args.args[0] == expected
        assert state._terminal_result == expected
    asyncio.run(scenario())


def test_result_send_failures_do_not_skip_remaining_peers_or_reconnect_snapshot():
    async def scenario():
        state = make_state()
        state.player_list[0].user_id = 0
        state.player_list[1].tag_list.append("offline")
        state.game_server.user_id_to_connection[104] = connection()
        state.game_server.user_id_to_connection[104].websocket.send_json.side_effect = OSError("closed")
        state.send_to_realtime_spectators = AsyncMock(side_effect=[OSError("spectator closed"), None, None, None])
        await result.broadcast_result(state, hu_class="liuju")
        assert state.send_to_realtime_spectators.await_count == 4
        assert state._terminal_result["show_result_info"]["hu_class"] == "liuju"
    asyncio.run(scenario())
