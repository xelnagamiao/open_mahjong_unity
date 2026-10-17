"""Mandatory flower-win choices, including the confirmed final-four exception."""
import asyncio
from collections import Counter

import pytest

from .test_state import WIN, state_with, act
from .state_machine import Phase as P
from .bot import choose_action
from ...game_calculation.fuzhou import rules as book


def flowers(count=20):
    return [t for t in book.FLOWERS for _ in range(4 if t < 50 else 1)][:count]


def waiting(golds=0, count=20):
    hand = WIN[:12] + ([39] * golds + [28] * (3-golds)) + [29]
    state = state_with(hand)
    player = state.player_list[0]
    player.huapai_list = flowers(count)
    state.update_reserve()
    state.machine.phase = P.RESPONSE
    return state


@pytest.mark.parametrize('golds', [0, 2, 3])
@pytest.mark.parametrize('kind', ['normal', 'flower', 'kong'])
def test_completed_draw_opens_only_forced_win_choices(golds, kind):
    state = waiting(golds)
    state.tiles_list = [29] * 50
    state.open_action_window(state.draw_for(0, kind))
    assert state.machine.phase == P.FLOWER_WIN
    assert state.round_settlement is None and state.round_changes == [0]*4
    expected = ['fuzhou_flower_win'] + (['sanjin'] if golds == 3 else [])
    assert state.action_dict == {0: expected, 1: [], 2: [], 3: []}
    assert len(state.player_list[0].hand_tiles) == 17


@pytest.mark.parametrize('choice,special,points', [('fuzhou_flower_win', 'flower', 103), ('sanjin', 'three_gold', 83)])
@pytest.mark.parametrize('last_tile,last_four', [(29, False), (29, True), (47, True)])
def test_player_selects_flower_or_three_gold_with_separate_scoring(choice, special, points, last_tile, last_four):
    state = waiting(3)
    state.tiles_list = [last_tile] + [21, 22, 23] + [11] * (state.dead_wall_count if last_four else 50)
    state.open_action_window(state.draw_for(0))
    assert state.action_dict[0] == ['fuzhou_flower_win', 'sanjin']
    wall = list(state.tiles_list)
    act(state, {0: dict(action_type=choice)})
    assert state.machine.phase == P.END
    score = state.round_settlement['score']
    assert score['special'] == special and score['points'] == points
    assert state.round_changes == [points*3, -points, -points, -points]
    assert state.tiles_list == wall
    assert state.player_list[0].huapai_list == flowers()
    assert state.player_list[0].hand_tiles[-1] == last_tile


@pytest.mark.parametrize('invalid', ['pass', 'cut', 'hu_self', 'buhua', 'angang', 'jiagang', 'youjin', 'tingjin'])
def test_mandatory_window_rejects_every_non_win_action(invalid):
    state = waiting(3); state.tiles_list = [29] * 50
    window = state.open_action_window(state.draw_for(0))
    hand = list(state.player_list[0].hand_tiles)
    with pytest.raises(ValueError, match='行动不在当前窗口'):
        act(state, {0: dict(action_type=invalid, TileId=29, target_tile=29)})
    assert state.live_pending_window is window and state.round_settlement is None
    assert state.player_list[0].hand_tiles == hand


@pytest.mark.parametrize('last_tile', [29, 47])
@pytest.mark.parametrize('double', [False, True])
def test_last_four_full_flowers_leave_unexposed_draw_and_remaining_wall_intact(last_tile, double):
    state = waiting(3)
    state.double_youjin_owner = 0 if double else None
    if double:
        state.player_list[0].youjin = 2
        state.player_list[0].locked_waits = {39}
    state.tiles_list = [last_tile, 21, 22, 23] + [11]*state.dead_wall_count
    state.open_action_window(state.draw_for(0))
    assert state.machine.phase == P.FLOWER_WIN and state.in_last_four
    assert state.action_dict[0] == ['fuzhou_flower_win', 'sanjin']
    assert state.sea_queue == ([] if double else [1, 2, 3])
    assert len(state.tiles_list) == state.dead_wall_count + 3
    act(state, {0: dict(action_type='fuzhou_flower_win')})
    assert len(state.player_list[0].huapai_list) == 20
    assert state.dead_wall_count == 28
    assert not [e for e in state.domain_events if e['action'] == 'buhua']


def test_very_last_draw_can_be_a_flower_and_still_open_both_choices():
    state = waiting(3)
    state.in_last_four = True; state.sea_queue = [0]; state.machine.phase = P.SEA
    state.tiles_list = [47] + [11]*state.dead_wall_count
    state.open_action_window(state.next_sea_draw())
    assert state.sea_queue == [] and len(state.tiles_list) == state.dead_wall_count
    assert state.machine.phase == P.FLOWER_WIN
    act(state, {0: dict(action_type='sanjin')})
    assert state.round_settlement['score']['special'] == 'three_gold'


def test_twentieth_exposed_flower_extends_reserve_into_the_last_four():
    state = waiting(3, 19)
    # Replacing the fourth 45 exposes flower #20 and extends the reserve by 2.
    # The remaining wall is now reserve+4, so the next tile is an unreplaced
    # final-four draw from the front, not another tail replacement.
    state.tiles_list = [45, 47, 21, 22, 23] + [11]*28
    state.open_action_window(state.draw_for(0))
    assert state.machine.phase == P.REPLACEMENT
    act(state, {0: dict(action_type='buhua')})
    assert state.machine.phase == P.FLOWER_WIN and state.in_last_four
    assert state.player_list[0].huapai_list == flowers(20)
    assert state.player_list[0].hand_tiles[-1] == 47
    assert [e['action'] for e in state.domain_events] == ['deal_tile', 'buhua', 'deal_tile']
    assert len(state.tiles_list) == state.dead_wall_count + 3


def test_nineteen_exposed_flowers_do_not_count_a_final_unreplaced_flower_as_twenty():
    state = waiting(3, 19)
    state.double_youjin_owner = 0
    state.tiles_list = [47, 21, 22, 23] + [11]*state.dead_wall_count
    state.open_action_window(state.draw_for(0))
    assert state.machine.phase == P.SEA and state.action_dict[0] == ['pass']
    act(state, {0: dict(action_type='pass')})
    assert state.round_settlement['winner'] is None
    assert len(state.player_list[0].huapai_list) == 19
    assert len(state.tiles_list) == state.dead_wall_count + 3


def test_normal_flower_after_twenty_is_still_replaced_before_any_win_choice():
    state = waiting(3)
    state.tiles_list = [47] + [11]*50 + [29]
    state.open_action_window(state.draw_for(0))
    assert state.machine.phase == P.REPLACEMENT and state.action_dict[0] == ['buhua']
    assert not state.can_flower_win(0) and not state.can_three_gold(0)
    act(state, {0: dict(action_type='buhua')})
    assert state.machine.phase == P.FLOWER_WIN and len(state.player_list[0].huapai_list) == 21


def test_choice_reconnect_restores_private_actions_and_blocks_every_tile():
    state = waiting(3); state.tiles_list = [29]*50
    state.open_action_window(state.draw_for(0))
    for viewer in range(4):
        packets = state.restore_payloads(viewer)
        assert len(packets) == 2
        info = packets[-1]['ask_hand_action_info']
        assert info['fuzhou_info']['phase'] == P.FLOWER_WIN.value
        assert info['action_list'] == (['fuzhou_flower_win', 'sanjin'] if viewer == 0 else [])
        if viewer == 0:
            assert set(info['forbidden_cut_tiles']) == set(state.player_list[0].hand_tiles)
            assert info['kong_candidates'] == info['fuzhou_declaration_candidates'] == {}
        else:
            assert packets[0]['game_info']['players_info'][0]['hand_tiles'] is None


def test_timeout_and_bot_default_to_flower_win_instead_of_hanging_or_passing():
    async def run():
        state = waiting(3); state.tiles_list = [29]*50
        state.open_action_window(state.draw_for(0))
        assert choose_action(state, 0, state.action_dict[0]) == dict(action_type='fuzhou_flower_win')
        response = await asyncio.wait_for(state.wait_action(), .5)
        assert response[0]['action_type'] == 'fuzhou_flower_win'
        act(state, response)
        assert state.machine.phase == P.END and state.round_settlement['score']['special'] == 'flower'
    asyncio.run(run())


def test_flower_choice_uses_real_144_tiles_without_moving_the_last_flower():
    state = state_with(WIN[:12] + [39]*3 + [29]); player = state.player_list[0]
    for tile in flowers():
        state.tiles_list.remove(tile); player.huapai_list.append(tile)
    state.update_reserve()
    state.tiles_list.remove(47); state.tiles_list.insert(0, 47)
    while len(state.tiles_list) > state.dead_wall_count + 4:
        tile = state.tiles_list.pop(4)
        state.player_list[1].discard_tiles.append(tile)
    state.machine.phase = P.RESPONSE
    assert state.physical_inventory() == Counter(book.PHYSICAL)
    state.open_action_window(state.draw_for(0))
    assert state.action_dict[0] == ['fuzhou_flower_win', 'sanjin']
    act(state, {0: dict(action_type='fuzhou_flower_win')})
    assert state.physical_inventory() == Counter(book.PHYSICAL)


@pytest.mark.parametrize('special', ['flower', 'three_gold'])
@pytest.mark.parametrize('case', ['not_last_four', 'only_nineteen', 'two_unreplaced_flowers', 'flower_not_last', 'discard', 'short_hand'])
def test_final_flower_scoring_exception_does_not_accept_other_incomplete_states(special, case):
    hand = WIN[:12] + [39]*3 + [29, 47]
    exposed = flowers()
    kwargs = dict(last_four=True, source='self_draw')
    if case == 'not_last_four': kwargs['last_four'] = False
    if case == 'only_nineteen': exposed.pop()
    if case == 'two_unreplaced_flowers': hand[-2] = 46
    if case == 'flower_not_last': hand[-1], hand[-2] = hand[-2], hand[-1]
    if case == 'discard': kwargs['source'] = 'discard'
    if case == 'short_hand': hand.pop(0)
    assert book.score_hand(hand, flowers=exposed, jin=39, special=special, context=book.WinContext(**kwargs)) is None
