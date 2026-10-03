"""蓝十四人：超时、错和续局、机器人门槛和墙尾取牌边界。"""
import asyncio
from unittest.mock import AsyncMock

import pytest

from .test_lanshi_actions import prepared
from .test_lanshi_v4_integration import state
from . import wait_action as action_module
from ..public.ai.smart_bot_logic import should_accept_hu
from ...game_calculation.test_lanshi_v4 import tiles


@pytest.mark.parametrize('draw_slot', [False, True])
def test_timeout_cuts_one_tile_and_preserves_draw_slot_meaning(monkeypatch, draw_slot):
    gs, sent = prepared(monkeypatch)
    gs.current_player_index = 0
    gs.game_status = 'waiting_hand_action'
    gs.step_time = 0
    p = gs.player_list[0]
    p.hand_tiles = tiles('123456m345p678s1z2m')
    p.has_draw_slot = draw_slot
    p.last_drawn_tile = 12 if draw_slot else None
    p.remaining_time = 0
    before = p.hand_tiles.copy()
    gs.action_dict = {i: ['cut'] if i == 0 else [] for i in range(4)}
    asyncio.run(asyncio.wait_for(action_module.wait_action(gs), 1))
    cut = p.discard_tiles[-1]
    assert cut in before and len(p.hand_tiles) == len(before) - 1
    if draw_slot:
        assert cut == 12
    assert not p.has_draw_slot
    assert sent.await_args.kwargs['is_timeout_action'] is True
    assert sent.await_args.kwargs['cut_class'] is draw_slot


@pytest.mark.parametrize('phase,after', [
    ('waiting_action_after_cut', 'deal_card'),
    ('waiting_action_qianggang', 'deal_card_after_gang'),
])
def test_timeout_declines_claims_and_resumes_correct_draw(monkeypatch, phase, after):
    gs, _ = prepared(monkeypatch)
    gs.current_player_index = 0
    gs.game_status = phase
    gs.step_time = 0
    gs.player_list[0].discard_tiles = [22]
    gs.jiagang_tile = 22 if phase.endswith('qianggang') else None
    for p in gs.player_list:
        p.remaining_time = 0
    gs.action_dict = {i: ['hu_first', 'pass'] if i == 1 else [] for i in range(4)}
    asyncio.run(asyncio.wait_for(action_module.wait_action(gs), 1))
    assert gs.game_status == after and gs.jiagang_tile is None
    assert gs.hu_class not in ('hu_self', 'hu_first', 'hu_second', 'hu_third')


@pytest.mark.parametrize('kind,rob,expected', [
    ('hu_self', False, 'waiting_hand_action'),
    ('hu_first', False, 'deal_card'),
    ('hu_second', False, 'deal_card'),
    ('hu_third', True, 'deal_card_after_gang'),
])
def test_wrong_win_resumes_same_round_without_adding_claimed_tile(kind, rob, expected):
    gs = state()
    gs.current_player_index = 0
    gs.tiles_list = []  # 除和牌外不得继续鸣牌。
    gs.player_list[0].discard_tiles = [29]
    gs.broadcast_refresh_player_tag_list = AsyncMock()
    gs.hu_class = kind
    gs.jiagang_tile = 29 if rob else None
    winner = gs.resolve_hepai_player_index(kind)
    before = [p.hand_tiles.copy() for p in gs.player_list]
    round_before = gs.current_round
    asyncio.run(gs.apply_cuohe_resume_after_ready(winner, kind, rob))
    assert gs.game_status == expected
    assert gs.current_round == round_before
    assert [p.hand_tiles for p in gs.player_list] == before
    assert 'peida' in gs.player_list[winner].tag_list
    assert not gs.result_dict and not gs.hu_class
    assert all(not a.startswith('hu_') for a in gs.action_dict.get(winner, []))
    if rob:
        assert gs.jiagang_tile is None


@pytest.mark.parametrize('action', ['hu_self', 'hu_first', 'hu_second', 'hu_third'])
@pytest.mark.parametrize('points,expected', [(0, False), (4, False), (5, True), (100, True)])
def test_bot_obeys_five_point_threshold_even_with_wrong_win_enabled(action, points, expected):
    gs = state()
    gs.result_dict[action] = (points, ['一般高*1'])
    assert should_accept_hu(gs, 0, action) is expected


@pytest.mark.parametrize('wall,order', [
    ([11], [11]), ([11, 12], [11, 12]),
    ([11, 12, 13, 14, 15], [14, 15, 12, 13, 11]),
])
def test_kong_tail_alternates_lower_upper_and_handles_last_tile(wall, order):
    gs = state()
    p = gs.player_list[0]
    p.hand_tiles = []
    gs.backward_tiles_list_type = 'double'
    remaining = wall.copy()
    for tile in order:
        p.get_gang_tile(remaining, gs)
        assert p.hand_tiles[-1] == tile
        assert p.last_drawn_tile == tile and p.has_draw_slot
    assert not remaining and p.hand_tiles == order
