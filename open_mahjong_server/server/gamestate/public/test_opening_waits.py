"""Every eligible seat can ron before its first draw, using the real game loops."""
import asyncio
import importlib
from unittest.mock import AsyncMock, Mock

import pytest

from ...game_calculation.game_calculation_service import GameCalculationService
from ..game_mmcr.test_ask_timing import game as qingque_game
from ..game_riichi.test_double_riichi import make_game as riichi_game
from ..game_sichuan.test_hepai_limit import state as sichuan_game
from ..game_classical.ClassicalGameState import ClassicalGameState


@pytest.mark.parametrize("rule", ["mmcr", "riichi", "sichuan", "classical"])
@pytest.mark.parametrize("seat", [1, 2, 3])
def test_opening_discard_is_winnable_before_first_draw(rule, seat, monkeypatch):
    name = {"mmcr": "Qingque", "riichi": "Riichi", "sichuan": "Sichuan", "classical": "Classical"}[rule]
    module = importlib.import_module(f"server.gamestate.game_{rule}.{name}GameState")
    actions = importlib.import_module(f"server.gamestate.game_{rule}.action_check")
    state = qingque_game() if rule == "mmcr" else riichi_game(0)[0] if rule == "riichi" else sichuan_game()
    if rule == "classical":
        state = ClassicalGameState(state.game_server, dict(
            player_list=[100, 101, 102, 103], room_id=1, room_rule="classical",
            room_type="custom", sub_rule="classical/standard", round_timer=20,
            step_timer=5, game_round=1, tips=False, allow_spectator=False),
            GameCalculationService(), Mock(), "classical-opening")
    state.calculation_service = GameCalculationService()
    state.game_record = {"game_title": {}, "game_round": {"round_index_1": {"action_ticks": []}}}
    # Each player's hand is a separate legal shape; the probe replaces dealing only.
    hand = ([11, 12, 13, 14, 15, 16, 21, 22, 23, 26, 27, 28, 25]
            if rule in ("sichuan", "classical") else [11, 19, 19, 21, 29, 31, 39, 42, 43, 44, 45, 46, 47])
    win_tile = 25 if rule in ("sichuan", "classical") else 41

    def deal(_):
        state.tiles_list = [39] * 60
        for player in state.player_list:
            player.hand_tiles = hand.copy()
            player.combination_tiles = []
            player.waiting_tiles = set()
        if rule == "sichuan":
            state.player_list[state.dealer_index].hand_tiles.append(39)

    monkeypatch.setattr(module, f"init_{name.lower()}_tiles", deal)
    monkeypatch.setattr(module, "init_game_record", Mock())
    monkeypatch.setattr(module, "init_game_round", Mock())
    state.broadcast_game_start = AsyncMock()
    state.broadcast_do_action = AsyncMock()
    state.broadcast_ask_hand_action = AsyncMock()
    if rule == "sichuan":
        async def opening():
            for player in state.player_list:
                player.dingque_suit = 3
        state._opening_phase = opening

    class FirstDecision(Exception):
        pass

    state.wait_action = AsyncMock(side_effect=FirstDecision)
    loop = getattr(state, f"game_loop_{name.lower()}")
    with pytest.raises(FirstDecision):
        asyncio.run(loop())
    assert win_tile in state.player_list[seat].waiting_tiles
    offered = actions.check_action_after_cut(state, win_tile)[seat]
    assert any(action.startswith("hu") for action in offered)
