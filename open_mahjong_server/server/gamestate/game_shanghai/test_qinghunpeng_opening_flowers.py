"""起手花：上家舍牌先等其他家响应，再补花并重新开放下家的响应。"""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch

import pytest

from .test_qinghunpeng_state import make_state
from .bot import shanghai_smart_bot_action
from ..game_taiwan.boardcast import (
    broadcast_ask_hand_action, broadcast_ask_other_action,
    reconnected_send_pending_ask_for_viewer, send_reconnect_game_state,
    send_realtime_spectator_snapshot,
)


FLOWER_CLAIM = [11,11,12,13,14,15,16,17,41,41,45,51,52]
FLOWER_CHOW = [12,13,14,15,16,17,18,19,41,41,45,51,52]
FLOWER_RON = [11,12,13,14,15,16,17,18,19,45,45,41,51]
OTHER_PUNG = [11,11,21,22,23,24,25,26,31,32,33,41,41]


def opening_state(hand=FLOWER_CLAIM, *, tile=11, discarder=0, wall=(22,23,24,42,43)):
    state = make_state()
    state.current_player_index = discarder
    state.player_list[discarder].discard_tiles = [tile]
    state.player_list[(discarder + 1) % 4].hand_tiles = list(hand)
    state.tiles_list = list(wall)
    state.game_status = "waiting_action_after_cut"
    state.action_dict = state.check_discard_actions(tile)
    return state


async def finish_other_responses(state, responses=None):
    await state.resolve_discard_responses(responses or {}, state.action_dict)
    assert state.game_status == "deal_card"
    await state._deal_normal()


async def finish_flowers(state):
    index = state.current_player_index
    for _ in range(8):
        if state.game_status == "END" or not any(t in state.flower_tiles for t in state.player_list[index].hand_tiles):
            return
        assert state.action_dict[index] == ["buhua"]
        await state.execute_buhua(index)
    raise AssertionError("补花超过八花上限")


def ticks(state):
    return state.game_record["game_round"][f"round_index_{state.round_index}"]["action_ticks"]


def sockets(state):
    result = {}
    for player in state.player_list:
        result[player.player_index] = AsyncMock()
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=result[player.player_index])
    return result


@pytest.mark.parametrize("discarder", [0,1,2])
def test_upper_discard_does_not_offer_existing_claim_before_opening_flowers(discarder):
    state = opening_state(discarder=discarder)
    next_index = (discarder + 1) % 4
    assert state.action_dict[next_index] == []
    assert state.player_list[next_index].normal_draw_count == 0
    assert state.opening_before_draw is None


@pytest.mark.parametrize("action,hand", [
    ("peng", FLOWER_CLAIM), ("chi_right", FLOWER_CLAIM),
    ("gang", [11,11,11,12,13,14,15,16,41,41,45,51,52]),
])
def test_early_upper_claim_cannot_consume_discard_or_hand(action, hand):
    async def run():
        state = opening_state(hand)
        before = list(state.player_list[1].hand_tiles)
        await state.execute_claim(1, action)
        assert state.player_list[1].hand_tiles == before
        assert state.player_list[1].combination_tiles == []
        assert state.player_list[0].discard_tiles == [11]
        assert state.opening_before_draw is None
    asyncio.run(run())


@pytest.mark.parametrize("action,hand", [
    ("peng", OTHER_PUNG),
    ("gang", [11,11,11,21,22,23,24,25,26,31,32,33,41]),
    ("hu_second", [12,13,14,15,16,17,18,19,11,11,11,15,15]),
])
def test_other_seat_claim_or_win_preempts_opening_flower_phase(action, hand):
    async def run():
        state = opening_state(FLOWER_CHOW)
        state.player_list[2].hand_tiles = list(hand)
        state.action_dict = state.check_discard_actions(11)
        assert state.action_dict[1] == [] and action in state.action_dict[2]
        await state.resolve_discard_responses({2: {"action_type": action}}, state.action_dict)
        assert state.player_list[1].hand_tiles == FLOWER_CHOW
        assert state.player_list[1].huapai_list == [] and state.opening_before_draw is None
        if action.startswith("hu_"):
            assert [item["index"] for item in state.pending_winners] == [2]
        else:
            assert state.current_player_index == 2
            assert state.player_list[0].discard_tiles == []
    asyncio.run(run())


def test_all_other_responses_finish_before_next_player_is_asked_to_replace_flowers():
    async def run():
        state = opening_state(FLOWER_CHOW)
        state.player_list[2].hand_tiles = list(OTHER_PUNG)
        state.player_list[2].remaining_time = 30
        state.action_dict = state.check_discard_actions(11)
        task = asyncio.create_task(state.wait_action())
        await asyncio.sleep(0)
        assert not task.done()
        assert state.waiting_players_list == [2]
        assert state.opening_before_draw is None and state.player_list[1].huapai_list == []
        state.action_queues[2].put_nowait({"action_type": "pass", "_action_tick": state.server_action_tick})
        state.action_events[2].set()
        assert await asyncio.wait_for(task, 2)
        await state._deal_normal()
        assert state.current_player_index == state.opening_before_draw == 1
        assert state.action_dict[1] == ["buhua"]
        assert state.player_list[1].normal_draw_count == 0
        await finish_flowers(state)
        assert state.opening_claim_window == 1
        assert "chi_right" in state.action_dict[1]
        assert all(not state.action_dict[i] for i in (0,2,3))
    asyncio.run(run())


@pytest.mark.parametrize("action,tile,hand,replacement", [
    ("chi_right", 11, [12,14,15,16,17,18,19,41,41,45,45,47,51], 13),
    ("peng", 11, [11,14,15,16,17,18,19,41,41,45,45,47,51], 11),
    ("gang", 11, [11,11,14,15,16,17,18,19,41,41,45,47,51], 11),
    ("hu_first", 41, FLOWER_RON, 45),
])
def test_flower_replacement_can_create_new_claim_or_ron_on_same_upper_discard(action, tile, hand, replacement):
    async def run():
        state = opening_state(hand, tile=tile, wall=(22,23,24,replacement))
        assert state.action_dict[1] == []
        await finish_other_responses(state)
        assert state.action_dict[1] == ["buhua"]
        await finish_flowers(state)
        assert state.game_status == "waiting_action_after_cut"
        assert action in state.action_dict[1]
        assert state.player_list[0].discard_tiles == [tile]
        assert not state.player_list[1].has_draw_slot
        assert state.player_list[1].normal_draw_count == 0
        assert state.tiles_list == [22,23,24]
        await state.resolve_discard_responses({1: {"action_type": action}}, state.action_dict)
        if action.startswith("hu_"):
            assert state.pending_winners[0]["tile"] == tile
            assert state.pending_winners[0]["payer"] == 0
            assert state.pending_winners[0]["source"] == "discard"
        else:
            assert state.player_list[0].discard_tiles == []
            assert state.current_player_index == 1
            if action == "gang":
                assert state.game_status == "deal_card_after_gang"
                await state._deal_supplement()
                assert state.player_list[1].last_drawn_tile == 24
                assert len(state.player_list[1].hand_tiles) == 11
            else:
                assert state.game_status == "onlycut_after_action"
                assert state.after_claim_actions()[1] == ["cut"]
    asyncio.run(run())


def test_passing_reopened_response_draws_normally_once_and_does_not_repeat_flowers():
    async def run():
        state = opening_state()
        await finish_other_responses(state)
        await finish_flowers(state)
        await state.resolve_discard_responses({1: {"action_type": "pass"}}, state.action_dict)
        assert state.game_status == "deal_card"
        await state._deal_normal()
        player = state.player_list[1]
        assert state.current_player_index == 1 and player.normal_draw_count == 1
        assert len(player.hand_tiles) == 14 and player.last_drawn_tile == 22 and player.has_draw_slot
        assert state.opening_claim_window is None and state.opening_before_draw is None
        assert [tick[0] for tick in ticks(state)].count("d") == 1
        assert [tick[0] for tick in ticks(state)].count("bd") == 2
        assert state.player_list[0].discard_tiles == [11]
    asyncio.run(run())


@pytest.mark.parametrize("index", [2,3])
@pytest.mark.parametrize("action", ["peng", "gang"])
def test_opposite_and_lower_discard_still_allows_claim_before_initial_flowers(index, action):
    async def run():
        hand = FLOWER_CLAIM if action == "peng" else [11,11,11,12,13,14,15,16,41,41,45,51,52]
        state = opening_state((), wall=(21,22,23,24,25))
        state.player_list[index].hand_tiles = list(hand)
        state.action_dict = state.check_discard_actions(11)
        assert action in state.action_dict[index]
        await state.resolve_discard_responses({index: {"action_type": action}}, state.action_dict)
        assert state.current_player_index == index
        if action == "gang":
            await state._deal_supplement()
        else:
            state.action_dict = state.after_claim_actions()
        assert state.action_dict[index] == ["buhua"]
        await finish_flowers(state)
        assert state.player_list[index].normal_draw_count == 0
        assert state.player_list[0].discard_tiles == []
        if action == "gang":
            assert state.game_status == "deal_card_after_gang"
            await state._deal_supplement()
            assert state.player_list[index].last_drawn_tile == 23
        else:
            assert "cut" in state.action_dict[index]
    asyncio.run(run())


@pytest.mark.parametrize("mode", ["completed", "already_drew", "reopened", "no_flowers"])
def test_shared_defer_helper_only_applies_to_unprocessed_first_upper_response(mode):
    state = opening_state()
    player = state.player_list[1]
    if mode == "completed":
        player.opening_flowers_done = True
    elif mode == "already_drew":
        player.normal_draw_count = 1
    elif mode == "reopened":
        state.opening_claim_window = 1
    else:
        player.hand_tiles = [t for t in player.hand_tiles if t not in state.flower_tiles]
    assert not state.should_defer_opening_discard_response(1)
    assert "peng" in state.check_discard_actions(11)[1]


@pytest.mark.parametrize("wall,expected_flowers,ended", [
    ((22,45,52), [51,52], False),
    ((), [51], True),
    ((52,), [51,52], True),
])
def test_chained_flowers_and_missing_tail_never_reopen_an_incomplete_hand(wall, expected_flowers, ended):
    async def run():
        state = opening_state(FLOWER_RON, tile=41, wall=wall)
        await finish_other_responses(state)
        await finish_flowers(state)
        player = state.player_list[1]
        assert player.huapai_list == expected_flowers
        assert player.normal_draw_count == 0 and state.player_list[0].discard_tiles == [41]
        if ended:
            assert state.game_status == "END" and state.pending_winners == []
            assert state.opening_claim_window is None
            assert len(player.hand_tiles) == 12
        else:
            assert state.action_dict[1] == ["hu_first","pass"]
            assert len(player.hand_tiles) == 13 and not player.has_draw_slot
            assert state.tiles_list == [22]
    asyncio.run(run())


def test_last_tail_replacement_can_ron_but_pass_ends_without_phantom_normal_draw():
    async def run():
        state = opening_state(FLOWER_RON, tile=41, wall=(45,))
        await finish_other_responses(state)
        await finish_flowers(state)
        assert state.action_dict[1] == ["hu_first","pass"]
        assert state.result_dict["hu_first"]["base_score"] == 10
        await state.resolve_discard_responses({1: {"action_type": "pass"}}, state.action_dict)
        await state._deal_normal()
        assert state.game_status == "END" and state.player_list[1].normal_draw_count == 0
        assert not any(tick[0] == "d" for tick in ticks(state))
    asyncio.run(run())


def test_reconnect_and_spectator_follow_deferred_flower_and_reopened_asks_with_private_tiles():
    async def run():
        state = opening_state(FLOWER_CHOW)
        state.player_list[2].hand_tiles = list(OTHER_PUNG)
        state.action_dict = state.check_discard_actions(11)
        connections = sockets(state)
        observer = AsyncMock()
        state.game_server.user_id_to_connection[501] = SimpleNamespace(websocket=observer)
        recorded = []
        state.spectator_manager = SimpleNamespace(enabled=True, record_tick=lambda t: recorded.append(deepcopy(t)),
                                                  record_ask_hand=Mock(), record_ask_other=Mock())

        await broadcast_ask_other_action(state)
        assert not connections[1].send_json.await_args_list
        connections[1].send_json.reset_mock()
        await send_reconnect_game_state(state, state.player_list[1])
        await reconnected_send_pending_ask_for_viewer(state, 102, 1)
        assert all("ask_other_action_info" not in call.args[0] for call in connections[1].send_json.await_args_list)
        await send_realtime_spectator_snapshot(state, 501, 1)
        assert all("ask_other_action_info" not in call.args[0] for call in observer.send_json.await_args_list)

        await finish_other_responses(state, {2: {"action_type": "pass"}})
        connections[1].send_json.reset_mock()
        await reconnected_send_pending_ask_for_viewer(state, 102, 1)
        assert connections[1].send_json.await_args.args[0]["ask_hand_action_info"]["action_list"] == ["buhua"]
        observer.send_json.reset_mock()
        await send_realtime_spectator_snapshot(state, 501, 1)
        assert observer.send_json.await_args.args[0]["ask_hand_action_info"]["action_list"] == ["buhua"]

        await finish_flowers(state)
        await broadcast_ask_other_action(state)
        connections[1].send_json.reset_mock()
        await send_reconnect_game_state(state, state.player_list[1])
        await reconnected_send_pending_ask_for_viewer(state, 102, 1)
        ask = connections[1].send_json.await_args.args[0]["ask_other_action_info"]
        assert "chi_right" in ask["action_list"] and ask["cut_tile"] == 11
        observer.send_json.reset_mock()
        await send_realtime_spectator_snapshot(state, 501, 1)
        assert observer.send_json.await_args.args[0]["ask_other_action_info"]["action_list"] == ask["action_list"]
        snapshot = observer.send_json.await_args_list[0].args[0]["game_info"]
        assert snapshot["current_player_index"] == 0 and snapshot["view_player_index"] == 1
        assert snapshot["players_info"][1]["hand_tiles_count"] == 13
        assert snapshot["players_info"][1]["hand_tiles"] == state.player_list[1].hand_tiles
        assert all("hand_tiles" not in p for p in snapshot["players_info"] if p["player_index"] != 1)
        assert recorded == ticks(state)
        assert [tick[0] for tick in recorded] == ["bh","bd","bh","bd"]
        assert [tick[2] for tick in recorded] == [1,1,1,1]
        for call in connections[2].send_json.await_args_list:
            info = call.args[0].get("do_action_info", {})
            if info.get("action_list") == ["deal_buhua_tile"]:
                assert "deal_tile" not in info
    asyncio.run(run())


def test_real_bot_submits_flower_before_newly_available_ron():
    async def run():
        state = opening_state(FLOWER_RON, tile=41, wall=(22,45))
        await finish_other_responses(state)
        state.waiting_players_list = [1]
        with patch('server.gamestate.game_shanghai.bot._wait_until_actionable', new=AsyncMock(return_value=True)), \
             patch('server.gamestate.game_shanghai.bot.get_ai_action', new=AsyncMock()) as submit:
            await shanghai_smart_bot_action.__wrapped__(state, 1, state.action_dict[1], state.game_status)
            assert submit.await_args.args[2] == "buhua"
            await finish_flowers(state)
            await shanghai_smart_bot_action.__wrapped__(state, 1, state.action_dict[1], state.game_status)
            assert [call.args[2] for call in submit.await_args_list] == ["buhua","hu_first"]
        assert state.player_list[1].normal_draw_count == 0
    asyncio.run(run())


def test_no_post_flower_response_proceeds_to_normal_draw_without_empty_ask():
    async def run():
        state = opening_state(FLOWER_RON, tile=41, wall=(22,46))
        await finish_other_responses(state)
        await finish_flowers(state)
        assert state.game_status == "deal_card"
        assert all(not actions for actions in state.action_dict.values())
        await state._deal_normal()
        assert state.player_list[1].normal_draw_count == 1
        assert state.player_list[1].last_drawn_tile == 22
        assert len(state.player_list[1].hand_tiles) == 14
    asyncio.run(run())


def test_bot_is_not_launched_for_deferred_ask_but_gets_flower_then_reopened_ask():
    async def run():
        state = opening_state(FLOWER_RON, tile=41, wall=(22,45))
        state.player_list[1].user_id = 2
        state.smart_bot_action = AsyncMock()
        await broadcast_ask_other_action(state)
        await asyncio.sleep(0)
        state.smart_bot_action.assert_not_awaited()
        await finish_other_responses(state)
        await broadcast_ask_hand_action(state)
        await asyncio.sleep(0)
        assert state.smart_bot_action.await_args.args[2] == ["buhua"]
        await finish_flowers(state)
        await broadcast_ask_other_action(state)
        await asyncio.sleep(0)
        assert state.smart_bot_action.await_count == 2
        assert state.smart_bot_action.await_args.args[2] == ["hu_first","pass"]
        assert state.smart_bot_action.await_args.args[3] == "waiting_action_after_cut"
    asyncio.run(run())


@pytest.mark.parametrize("action,tile,hand,replacement", [
    ("pass", 41, FLOWER_RON, 45),
    ("chi_right", 11, [12,14,15,16,17,18,19,41,41,45,45,47,51], 13),
    ("peng", 11, [11,14,15,16,17,18,19,41,41,45,45,47,51], 11),
    ("gang", 11, [11,11,14,15,16,17,18,19,41,41,45,47,51], 11),
    ("hu_first", 41, FLOWER_RON, 45),
])
def test_actual_record_replays_deferred_flowers_then_response_or_normal_draw(action, tile, hand, replacement):
    from collections import Counter
    from ...game_calculation.shanghai.qinghunpeng import TILES, FLOWERS
    from ..public.game_record_manager import init_game_round
    from ..verifier.record_sim.goto_action import RecordSim

    async def run():
        state = make_state()
        complete_wall = [t for t in TILES for _ in range(4)] + list(FLOWERS)
        remaining = list(complete_wall)
        state.player_list[0].hand_tiles = [21,22,23,24,25,26,27,28,29,31,32,33,34,tile]
        state.player_list[1].hand_tiles = list(hand)
        for player in state.player_list[:2]:
            for used in player.hand_tiles:
                remaining.remove(used)
        remaining.remove(replacement)
        if action == "gang":
            remaining.remove(24)
        for player in state.player_list[2:]:
            player.hand_tiles = [t for t in remaining if t // 10 in (2,3)][:13]
            for used in player.hand_tiles:
                remaining.remove(used)
        state.tiles_list = remaining + ([24] if action == "gang" else []) + [replacement]
        assert Counter(state.tiles_list + [t for p in state.player_list for t in p.hand_tiles]) == Counter(complete_wall)
        state.current_player_index = 0
        state.opening_dealer_action = True
        state.player_list[0].has_draw_slot = True
        state.player_list[0].last_drawn_tile = tile
        init_game_round(state)
        await state.execute_cut(0, {"TileId": tile, "cutIndex": 13, "cutClass": True})
        assert state.game_status == "deal_card"
        await state._deal_normal()
        await finish_flowers(state)
        assert action in state.action_dict[1]
        await state.resolve_discard_responses({1: {"action_type": action}}, state.action_dict)
        if action == "pass":
            await state._deal_normal()
        elif action == "gang":
            await state._deal_supplement()
        elif action.startswith("hu_"):
            state.run_hu_result_ready_phase = AsyncMock()
            before = {p.original_player_index: p.score for p in state.player_list}
            await state._settle_hand(before)

        key = f"round_index_{state.round_index}"
        replay = RecordSim(state.game_record)
        frames = replay.apply_all(key)
        assert RecordSim(state.game_record).goto_action(key, len(ticks(state))) == frames[-1]
        for index, player in enumerate(state.player_list):
            expected_hand = list(player.hand_tiles)
            expected_discards = list(player.discard_tiles)
            if action.startswith("hu_"):
                if index == 1:
                    expected_hand.append(tile)
            actual = replay.players[index]
            assert Counter(actual.tile_list) == Counter(expected_hand)
            assert actual.discard_tiles == expected_discards
            assert actual.combination_tiles == player.combination_tiles
            assert actual.combination_masks == player.combination_mask
            assert actual.huapai_list == player.huapai_list
            assert actual.score == player.score
        assert replay.current_tiles_list == state.tiles_list
        sequence = [t[0] for t in ticks(state)]
        assert sequence[:3] == ["c","bh","bd"]
        assert not any(t[0] == "d" for t in ticks(state)[:3])
        if action != "pass":
            assert state.player_list[1].normal_draw_count == 0
    asyncio.run(run())
