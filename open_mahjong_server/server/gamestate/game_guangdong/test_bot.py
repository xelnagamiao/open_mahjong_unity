"""广东机器人分支；牌效和万能牌结构始终执行实际求解器。"""

import asyncio
from copy import deepcopy
from unittest.mock import AsyncMock, patch

import pytest

from . import bot
from .test_state import hand, make_state
from ...game_calculation.guangdong.config import GHOSTS
from ...game_calculation.guangdong.rules import structural_waits


MODULE = "server.gamestate.game_guangdong.bot"
MELDS = ("k11", "k22", "k33")


def bot_state(tiles=(41, 41, 41, 45, 47), melds=MELDS, *, draw_slot=True):
    state = make_state()
    player = hand(state, 0, tiles, melds)
    player.has_draw_slot = draw_slot
    player.last_drawn_tile = tiles[-1] if tiles and draw_slot else None
    state.game_status = "waiting_hand_action"
    state.current_player_index = 0
    state.server_action_tick = 12
    state.waiting_players_list = [0]
    return state


async def actual_cpu(_state, function, *args):
    return function(*args)


def run_bot(state, available, status=None, *, cpu_function=actual_cpu, submit=None, ready=None, decorated=False):
    state.action_dict = {i: list(available) if i == 0 else [] for i in range(4)}
    state.game_status = status or state.game_status
    with patch(f"{MODULE}._wait_until_actionable", new=AsyncMock(return_value=True, side_effect=ready)) as wait, \
         patch(f"{MODULE}.run_room_bot_cpu", new=AsyncMock(side_effect=cpu_function)) as cpu, \
         patch(f"{MODULE}.submit_bot_action", new=AsyncMock(side_effect=submit)) as sender:
        action = bot.guangdong_bot_action if decorated else bot.guangdong_bot_action.__wrapped__
        asyncio.run(action(state, 0, available, state.game_status))
    return sender, cpu, wait


@pytest.mark.parametrize("win", ("hu_self", "hu_first", "hu_second", "hu_third"))
def test_legal_win_precedes_all_kongs_claims_and_discard(win):
    state = bot_state()
    sender, cpu, _ = run_bot(state, ["cut", "gang", "peng", "pass", win])
    sender.assert_awaited_once_with(bot.get_ai_action, state, 0, win, None, None, None, None)
    cpu.assert_not_awaited()


@pytest.mark.parametrize("available,expected", [
    (["gang", "peng", "pass"], "gang"),
    (["peng", "pass"], "peng"),
    (["pass"], "pass"),
    ([], None),
    (["cut"], None),
])
@pytest.mark.parametrize("status", ("waiting_action_after_cut", "waiting_action_qianggang"))
def test_claim_order_and_no_invented_action(available, expected, status):
    sender, cpu, _ = run_bot(bot_state(), available, status)
    if expected is None:
        sender.assert_not_awaited()
    else:
        assert sender.await_args.args[3:] == (expected, None, None, None, None)
    cpu.assert_not_awaited()


def test_concealed_kong_finds_actual_legal_tile_before_added_kong():
    state = bot_state((11, 41, 41, 41, 41))
    sender, cpu, _ = run_bot(state, ["angang", "jiagang", "cut"])
    assert sender.await_args.args[3:] == ("angang", None, None, None, 41)
    assert state.kong_allowed(0, 41, "concealed")
    cpu.assert_not_awaited()


def test_added_kong_after_unavailable_concealed_kong():
    state = bot_state((11, 41, 41, 45, 47))
    sender, cpu, _ = run_bot(state, ["angang", "jiagang", "cut"])
    assert sender.await_args.args[3:] == ("jiagang", None, None, None, 11)
    assert state.kong_allowed(0, 11, "added")
    cpu.assert_not_awaited()


@pytest.mark.parametrize("tiles", ([41, 41, 41, 45, 47], [41, 41, 41, 45, 55]))
def test_stale_kong_options_without_candidate_fall_back_to_legal_discard(tiles):
    state = bot_state(tiles)
    sender, cpu, _ = run_bot(state, ["angang", "jiagang", "cut"])
    assert sender.await_args.args[3] == "cut"
    assert sender.await_args.args[5] in tiles
    cpu.assert_awaited_once()


@pytest.mark.parametrize("status", ("waiting_hand_action", "onlycut_after_action"))
@pytest.mark.parametrize("draw_slot", (True, False))
def test_actual_cut_selects_isolated_honor_and_preserves_draw_slot_semantics(status, draw_slot):
    state = bot_state(draw_slot=draw_slot)
    before = deepcopy(state.player_list[0].hand_tiles)
    sender, cpu, _ = run_bot(state, ["cut"], status)
    assert sender.await_args.args[3:] == ("cut", draw_slot, 47, 4, None)
    assert state.player_list[0].hand_tiles == before
    assert structural_waits(before[:-1], MELDS)
    cpu.assert_awaited_once()


@pytest.mark.parametrize("available,tiles", [([], [41, 45]), (["cut"], []), (["angang", "jiagang"], [41, 45])])
def test_no_cut_when_absent_or_no_tiles(available, tiles):
    sender, cpu, _ = run_bot(bot_state(tiles, ()), available)
    sender.assert_not_awaited()
    cpu.assert_not_awaited()


@pytest.mark.parametrize("change", ("wait_rejected", "tick", "seat"))
def test_inactive_wait_or_stale_tick_before_decision_makes_no_submission(change):
    state = bot_state()

    async def ready(_state, _index):
        if change == "tick":
            state.server_action_tick += 1
        elif change == "seat":
            state.waiting_players_list.clear()
        return change != "wait_rejected"

    sender, cpu, _ = run_bot(state, ["cut"], ready=ready)
    sender.assert_not_awaited()
    cpu.assert_not_awaited()


@pytest.mark.parametrize("change", ("tick", "seat"))
def test_cpu_discard_result_is_dropped_after_authoritative_action_advances(change):
    state = bot_state()

    async def changed_cpu(_state, function, *args):
        result = function(*args)
        if change == "tick":
            state.server_action_tick += 1
        else:
            state.waiting_players_list.clear()
        return result

    sender, cpu, _ = run_bot(state, ["cut"], cpu_function=changed_cpu)
    cpu.assert_awaited_once()
    sender.assert_not_awaited()


@pytest.mark.parametrize("tiles,expected", [
    ([41, 41, 41, 45, 47], 47),
    ([41, 41, 41, 45, 55], 45),
    ([41, 41, 45, 55, 56], 45),
    ([41, 45, 55, 56, 57], 45),
    ([41, 55, 56, 57, 58], 41),
])
def test_zero_to_four_ghosts_keep_jokers_when_natural_discard_exists(tiles, expected):
    original = list(tiles)
    assert bot.choose_cut(tiles, MELDS) == expected
    assert expected not in GHOSTS and tiles == original
    tiles.remove(expected)
    assert structural_waits(tiles, MELDS)


@pytest.mark.parametrize("ghost", GHOSTS)
def test_one_pair_wait_keeps_each_physical_ghost(ghost):
    melds = (*MELDS, "k44")
    assert bot.choose_cut([41, ghost], melds) == 41
    assert bot.choose_cut([ghost, 41], melds) == 41


@pytest.mark.parametrize("ghosts", [(55, 56), (56, 57), (57, 58), (58, 55)])
def test_all_ghost_residual_hand_still_selects_legal_drawn_ghost(ghosts):
    melds = (*MELDS, "k44")
    state = bot_state(ghosts, melds)
    sender, _, _ = run_bot(state, ["cut"])
    assert sender.await_args.args[3:] == ("cut", True, ghosts[-1], 1, None)
    assert structural_waits([ghosts[0]], melds)


@pytest.mark.parametrize("tiles,expected", [
    ([11, 12, 13, 45, 47], 47),  # 保留完整顺子。
    ([11, 13, 19, 45, 47], 47),  # 尚未听时仍保留边嵌搭子。
    ([11, 13, 19, 47, 45], 45),  # 同价值弃牌优先摸切。
])
def test_natural_shape_and_tied_discard_prefer_drawn_honor(tiles, expected):
    assert bot.choose_cut(tiles, ("k21", "k22", "k23")) == expected


@pytest.mark.parametrize("mode", ("cut", "win", "angang", "jiagang", "peng", "gang", "pass", "ghost_cut"))
def test_real_ai_queue_receives_complete_current_tick_action(mode):
    state = bot_state()
    if mode == "win":
        action, status = "hu_self", "waiting_hand_action"
    elif mode == "angang":
        hand(state, 0, [11, 41, 41, 41, 41], MELDS)
        state.player_list[0].last_drawn_tile = 41
        action, status = mode, "waiting_hand_action"
    elif mode == "jiagang":
        hand(state, 0, [11, 41, 41, 45, 47], MELDS)
        action, status = mode, "waiting_hand_action"
    elif mode == "ghost_cut":
        hand(state, 0, [55, 56], (*MELDS, "k44"))
        state.player_list[0].last_drawn_tile = 56
        action, status = "cut", "waiting_hand_action"
    elif mode in ("peng", "gang", "pass"):
        action, status = mode, "waiting_action_after_cut"
    else:
        action, status = mode, "waiting_hand_action"

    async def actual_submit(callback, *args):
        await callback(*args)

    sender, _, _ = run_bot(state, [action], status, submit=actual_submit, decorated=True)
    sender.assert_awaited_once()
    assert state.action_events[0].is_set()
    assert state.action_queues[0].qsize() == 1
    queued = state.action_queues[0].get_nowait()
    assert queued["action_type"] == action and queued["_action_tick"] == 12
    if action == "cut":
        assert queued["TileId"] == (56 if mode == "ghost_cut" else 47)
        assert queued["cutClass"] is True
    elif action in ("angang", "jiagang"):
        assert queued["target_tile"] == (41 if action == "angang" else 11)
