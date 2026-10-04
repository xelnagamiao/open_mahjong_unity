"""血流状态机拆分的行为回归。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .SichuanGameState import SichuanGameState
from .XueliuGameState import XueliuGameState


def make_state(cls):
    state = cls.__new__(cls)
    state.sub_rule = "sichuan/xueliu" if cls is XueliuGameState else "sichuan/standard"
    state._configure_rule()
    state.blood_battle = not state.is_xueliu
    state.paofen_watch = None
    state.player_list = [SimpleNamespace(player_index=i, score=100, is_hu=False,
        hu_order=0, gang_score_records=[]) for i in range(4)]
    return state


def test_rule_configuration_does_not_cross_states():
    standard = make_state(SichuanGameState)
    flow = make_state(XueliuGameState)
    assert standard.xueliu_rule_profile is None
    assert flow.xueliu_rule_profile["sub_rule"] == "sichuan/xueliu"
    standard.sub_rule = "sichuan/xueliu"
    with pytest.raises(ValueError):
        standard._configure_rule()


def test_opening_phases_use_their_own_rule():
    standard = make_state(SichuanGameState)
    standard._dingque_phase = AsyncMock()
    with patch(__package__ + ".SichuanGameState.broadcast_dingque_done", new_callable=AsyncMock) as done:
        asyncio.run(standard._opening_phase())
        standard._dingque_phase.assert_awaited_once()
        done.assert_awaited_once_with(standard)
    from .test_xueliu_integration import full_state
    flow, _ = full_state()
    flow._xueliu_throw_three_phase = AsyncMock()
    flow.broadcast_game_start = AsyncMock()
    asyncio.run(flow._opening_phase())
    flow._xueliu_throw_three_phase.assert_awaited_once()
    flow.broadcast_game_start.assert_awaited_once()


def test_gang_scoring_is_isolated():
    standard = make_state(SichuanGameState)
    flow = make_state(XueliuGameState)
    assert standard._record_gang_score(0, 11, "xiayu2") == {0: 6, 1: -2, 2: -2, 3: -2}
    assert flow._record_gang_score(0, 11, "xiayu2") == {0: 0, 1: 0, 2: 0, 3: 0}
    assert flow.player_list[0].gang_score_records[0]["xueliu_fan"] == 2


def test_self_draw_scoring_is_isolated_and_balanced():
    standard = make_state(SichuanGameState)
    flow = make_state(XueliuGameState)
    assert standard._apply_hu_score_changes(0, 4, True) == {0: 15, 1: -5, 2: -5, 3: -5}
    flow.player_list[1].gang_score_records = [{"xueliu_fan": 2}]
    changes = flow._apply_hu_score_changes(0, 4, True, fan=4)
    assert changes == {0: 14, 1: -6, 2: -4, 3: -4}
    assert sum(changes.values()) == 0


def test_wall_end_dispatches_to_flow_settlement():
    flow = make_state(XueliuGameState)
    flow._settle_xueliu_wall_end = AsyncMock()
    asyncio.run(flow._settle_liuju())
    flow._settle_xueliu_wall_end.assert_awaited_once()
