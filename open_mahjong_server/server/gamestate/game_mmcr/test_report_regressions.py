"""Reproductions from the MMCR author's 2026-10-28 comparison report."""
import asyncio
import importlib
from unittest.mock import AsyncMock, Mock

import pytest

from ...game_calculation.game_calculation_service import GameCalculationService
from .test_ask_timing import game
from .action_check import (
    check_action_after_cut, check_action_hand_action, check_action_jiagang,
    check_only_cut,
)
from .wait_action import wait_action
from ..public.lifecycle import cancel_auxiliary_tasks


@pytest.fixture
def calculator():
    return GameCalculationService()


@pytest.mark.parametrize("hand,melds,way,tile,fan,base", [
    ([21, 21, 21, 25, 26, 26, 26, 27], ["s16", "G11"],
     ["岭上开花", "自风南"], 27, "镜同", 322),
    ([11, 11, 11, 11, 19, 19, 31, 31, 31, 31, 39, 39, 44, 44], [],
     ["自摸", "自风西"], 39, "镜同三对", 1180),
])
def test_report_scores_use_deployed_calculator(calculator, hand, melds, way, tile, fan, base):
    score, names = calculator.Qingque_hepai_check(hand, melds, way, tile)
    assert fan in names
    assert round(score * score) == base


@pytest.mark.parametrize("hand,melds,excluded", [
    ([11, 11, 11, 11, 13, 14, 15, 16, 17, 18, 19, 19, 19], [], 11),
    ([21, 22, 23, 31, 32, 33, 41, 41, 41, 11], ["k11"], 11),
    ([21, 22, 23, 31, 32, 33, 41, 41, 41, 11], ["K11"], 11),
    ([12, 12, 12, 13, 14, 21, 22, 23, 41, 41], ["s12"], 12),
    ([12, 13, 21, 22, 23, 31, 32, 33, 41, 41], ["g11"], 11),
    ([12, 13, 21, 22, 23, 31, 32, 33, 41, 41], ["G11"], 11),
])
def test_waits_never_require_a_fifth_copy(calculator, hand, melds, excluded):
    assert excluded not in calculator.Qingque_tingpai_check(hand, melds)


def test_legal_wait_is_preserved(calculator):
    hand = [11, 11, 11, 11, 13, 14, 15, 16, 17, 18, 19, 19, 19]
    assert calculator.Qingque_tingpai_check(hand, []) == {12}


@pytest.mark.parametrize("action,seat,tiles", [
    ("chi_left", 1, [11, 12]), ("chi_mid", 1, [12, 14]),
    ("chi_right", 1, [14, 15]), ("peng", 2, [13, 13]),
    ("gang", 3, [13, 13, 13]),
])
@pytest.mark.parametrize("wall", [[], [47]])
def test_claims_on_last_discard(action, seat, tiles, wall):
    state = game()
    state.tiles_list = wall.copy()
    for player in state.player_list:
        player.hand_tiles = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 41]
    state.player_list[seat].hand_tiles = tiles + state.player_list[seat].hand_tiles[len(tiles):]
    actions = check_action_after_cut(state, 13)
    assert action in actions[seat]
    assert "pass" in actions[seat]
    assert actions[0] == []


@pytest.mark.parametrize("checker", [check_only_cut, check_action_hand_action])
@pytest.mark.parametrize("action,hand,melds", [
    ("angang", [11, 11, 11, 11, 21, 22, 23, 31, 32, 33, 41, 42, 43, 44], []),
    ("jiagang", [11, 21, 22, 23, 31, 32, 33, 41, 42, 43, 44], ["k11"]),
])
def test_self_kongs_available_without_replacement_tile(checker, action, hand, melds):
    state = game()
    state.tiles_list = []
    state.player_list[0].hand_tiles = hand
    state.player_list[0].combination_tiles = melds
    actions = checker(state, 0)[0]
    assert action in actions
    assert "cut" in actions
    assert "hu_self" not in actions


def test_last_tile_robbed_kong_combines_bonuses(calculator):
    state = game()
    state.calculation_service = calculator
    state.tiles_list = []
    winner = state.player_list[1]
    winner.hand_tiles = [12, 13, 14, 22, 23, 24, 33, 34, 35, 36, 37, 38, 28]
    state.refresh_waiting_tiles(1)
    actions = check_action_jiagang(state, 28)
    assert "hu_first" in actions[1]
    _, fans = state.result_dict["hu_first"]
    assert {"抢杠", "河底捞鱼"} <= set(fans)


def test_empty_wall_kong_replacement_ends_round_without_drawing(monkeypatch):
    module = importlib.import_module("server.gamestate.game_mmcr.QingqueGameState")
    state = game()
    state.game_record["game_title"] = {}
    monkeypatch.setattr(module, "init_qingque_tiles", Mock())
    monkeypatch.setattr(module, "init_game_record", Mock())
    monkeypatch.setattr(module, "init_game_round", Mock())
    state.broadcast_game_start = AsyncMock()
    state.broadcast_do_action = AsyncMock()
    state.broadcast_ask_hand_action = AsyncMock()

    async def declare_kong():
        state.tiles_list = []
        state.game_status = "deal_card_after_gang"

    state.wait_action = declare_kong
    for player in state.player_list:
        player.get_gang_tile = Mock(side_effect=AssertionError("empty wall must not be popped"))

    class ReachedSettlement(Exception):
        pass

    state.unlimited_session.should_end_after_round = Mock(side_effect=ReachedSettlement)
    with pytest.raises(ReachedSettlement):
        asyncio.run(state.game_loop_qingque())
    assert state.game_status == "END"
    for player in state.player_list:
        player.get_gang_tile.assert_not_called()


async def choose(state, seat, action, **data):
    for index, options in state.action_dict.items():
        if options:
            state.action_queues[index].put_nowait({
                "action_type": action if index == seat else "pass",
                "_action_tick": state.server_action_tick,
                **(data if index == seat else {}),
            })
    await asyncio.wait_for(wait_action(state), 1)


def test_last_discard_chow_can_discard_again_and_be_won(calculator):
    async def run():
        state = game()
        state.calculation_service = calculator
        state.tiles_list = []
        state.player_list[1].hand_tiles = [11, 12, 14, 15, 16, 17, 18, 19, 21, 22, 23, 41, 42]
        state.player_list[2].hand_tiles = [21, 22, 23, 24, 25, 26, 31, 32, 33, 34, 35, 36, 41]
        state.refresh_waiting_tiles(2)
        state.action_dict = check_action_after_cut(state, 13)
        try:
            await choose(state, 1, "chi_left")
            assert state.game_status == "onlycut_after_action"
            assert state.player_list[1].combination_tiles == ["s12"]
            state.action_dict = check_only_cut(state, 1)
            assert "hu_self" not in state.action_dict[1]
            state.game_status = "waiting_hand_action"
            await choose(state, 1, "cut", TileId=41, cutClass=False,
                         cutIndex=state.player_list[1].hand_tiles.index(41))
            assert state.game_status == "waiting_action_after_cut"
            assert "hu_first" in state.action_dict[2]
            assert "河底捞鱼" in state.result_dict["hu_first"][1]
            await choose(state, 2, "hu_first")
            assert state.game_status == "END"
            assert state.hu_class == "hu_first"
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("rob", [False, True])
def test_last_tile_added_kong_waits_for_rob_before_exhaustion(calculator, rob):
    async def run():
        state = game()
        state.calculation_service = calculator
        state.tiles_list = []
        state.game_status = "waiting_hand_action"
        state.player_list[0].hand_tiles = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 28]
        state.player_list[0].combination_tiles = ["k28"]
        state.player_list[0].combination_mask = [[1, 28, 0, 28, 0, 28]]
        state.player_list[1].hand_tiles = [26, 27, 21, 22, 23, 31, 32, 33, 34, 35, 36, 41, 41]
        state.refresh_waiting_tiles(1)
        state.action_dict = check_only_cut(state, 0)
        try:
            await choose(state, 0, "jiagang", target_tile=28)
            assert state.game_status == "waiting_action_qianggang"
            assert "hu_first" in state.action_dict[1]
            assert {"抢杠", "河底捞鱼"} <= set(state.result_dict["hu_first"][1])
            await choose(state, 1, "hu_first" if rob else "pass")
            assert state.game_status == ("END" if rob else "deal_card_after_gang")
            assert state.jiagang_tile is None
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
