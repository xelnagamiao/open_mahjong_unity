"""Actual action, ron priority, chombo and reconnect paths use both gates."""
import asyncio
from unittest.mock import AsyncMock

import pytest

from .test_rule_branches import game
from .action_check import check_hepai
from .ron_resolution import valid_ron_result, should_interrupt_wait_for_action
from .boardcast import _build_base_game_info


def ready(minimum_han=1, minimum_score=8000, chombo=False):
    g = game()
    g.current_player_index = 0
    g.hepai_limit = minimum_han
    g.detailed_config['minimum_win_score'] = minimum_score
    g.open_cuohe = chombo
    g.dora_indicators = [27, 27]
    p = g.player_list[1]
    p.hand_tiles = [22,23,24,33,34,35,36,37,28,28]
    p.combination_tiles = ['s13']
    p.combination_mask = []
    return g


@pytest.mark.parametrize('minimum_han,score,allowed', [(1,8000,True),(2,8000,False),(1,12000,False),(3,0,False)])
def test_ron_button_requires_both_gates(minimum_han, score, allowed):
    g = ready(minimum_han, score)
    actions = {1: []}
    check_hepai(g, actions, 38, 1, 'ron')
    assert ('hu_first' in actions[1]) is allowed
    if allowed:
        assert valid_ron_result(g, 'hu_first')
        assert g.result_dict['hu_first']['yaku_han'] == 1


@pytest.mark.parametrize('minimum_han,score', [(2,8000),(1,12000)])
def test_failed_gate_uses_chombo_only_when_enabled(minimum_han, score):
    g = ready(minimum_han, score, chombo=True)
    actions = {1: []}
    check_hepai(g, actions, 38, 1, 'ron')
    assert actions[1] == ['hu_first']
    assert not valid_ron_result(g, 'hu_first')
    g.hu_class = 'hu_first'
    g.ron_player_index = 1
    g._settle_cuohe = AsyncMock()
    g._settle_hu = AsyncMock()
    asyncio.run(g._settle_round())
    g._settle_cuohe.assert_awaited_once()
    g._settle_hu.assert_not_awaited()


def test_score_gate_also_blocks_tsumo():
    g = ready(minimum_score=12000)
    g.current_player_index = 1
    g.player_list[1].hand_tiles.append(38)
    actions = {1: []}
    check_hepai(g, actions, 38, 1, 'tsumo')
    assert actions[1] == []
    g.detailed_config['minimum_win_score'] = 8000
    check_hepai(g, actions, 38, 1, 'tsumo')
    assert actions[1] == ['hu_self']


def test_gate_failure_cannot_steal_head_bump_priority():
    g = ready(minimum_han=2)
    g.result_dict = {
        'hu_first': dict(han=5, yaku_han=1, standard_ron_score=8000),
        'hu_second': dict(han=5, yaku_han=2, standard_ron_score=8000),
    }
    assert not should_interrupt_wait_for_action(g, 'hu_first', [2], {2: ['hu_second', 'pass']})
    g.hepai_limit = 1
    g.result_dict['hu_first']['standard_ron_score'] = 7700
    assert not valid_ron_result(g, 'hu_first')
    assert valid_ron_result(g, 'hu_second')


def test_reconnect_info_preserves_both_requirements():
    g = ready(minimum_han=3, minimum_score=12000)
    info = _build_base_game_info(g)
    assert info['hepai_limit'] == 3
    assert info['detailed_config']['minimum_win_score'] == 12000
