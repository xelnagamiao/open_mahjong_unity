import asyncio
import json
from collections import Counter
from dataclasses import replace

import pytest

from .DuocaiGameState import DuocaiGameState
from .actions import claim_actions, turn_actions
from .bot import choose_action
from .state_machine import Phase as P, StateMachine
from ...game_calculation.duocai import tiles as t
from ...game_calculation.duocai.solver import Meld


WAIT = [11, 12, 13, 22, 23, 24, 34, 35, 36, 36, 37, 28, 28]


def test_dragon_pao_is_detected_with_an_unrelated_open_sequence():
    g = game('riichi')
    p = g.player_list[1]
    p.melds = [Meld((11, 12, 13), declared='chi', source=0),
               Meld((45, 45, 45), source=2), Meld((46, 46, 46), source=3), Meld((47, 47, 47), source=0)]
    g.check_pao(1, 0)
    assert p.pao_liability['大三元'] == 0
    assert '大四喜' not in p.pao_liability


def game(variant='guobiao', seed=17):
    data = DuocaiGameState._default_room_data()
    data.update(sub_rule='duocai/' + variant, random_seed=seed, tips=True, game_round=2 if variant == 'riichi' else 4)
    g = DuocaiGameState(room_data=data)
    g.initialize_round()
    g.open_action_window(g.opening_window())
    return g


def apply(g, index, action, **fields):
    responses = {i:dict(action_type='pass') for i, a in g.live_pending_window['actions'].items() if a}
    responses[index] = dict(action_type=action, **fields)
    return g.apply_action_results(g.live_pending_window, responses)


def turn(g, index, hand):
    g.player_list[index].hand_tiles = list(hand)
    g.player_list[index].has_draw_slot = True
    g.player_list[index].draw_kind = 'normal'
    g.open_action_window(g.begin_turn(index))


@pytest.mark.parametrize('variant', ['guobiao', 'riichi'])
def test_deal_and_snapshot_keep_physical_supply_and_wall_secrecy(variant):
    g = game(variant)
    assert [len(p.hand_tiles) for p in g.player_list] == [14, 13, 13, 13]
    remaining = [g.wall.tiles[i] for i in range(144) if i not in g.wall.removed]
    assert Counter(remaining + [v for p in g.player_list for v in p.hand_tiles]) == Counter(g.decks.packs[g.wall.deck])
    for viewer in range(4):
        payload = g.build_game_start_payload(viewer)
        json.dumps(payload)
        info = payload['game_info']
        assert info['self_hand_tiles'] == g.player_list[viewer].hand_tiles
        assert all(p['hand_tiles'] is None for i, p in enumerate(info['players_info']) if i != viewer)
        assert all('physical_melds' not in p for i, p in enumerate(info['players_info']) if i != viewer)
        assert sum(v['tile'] is not None for v in info['duocai_info']['wall']) == (variant == 'riichi')
        assert all(len(p['duocai_hand_backs']) == p['hand_tiles_count'] for p in info['players_info'])


def test_window_rejects_stale_and_invented_physical_choice_without_mutation():
    g = game()
    p = g.player_list[0]
    before = list(p.hand_tiles)
    with pytest.raises(ValueError):
        apply(g, 0, 'cut', TileId=5039, cutIndex=999)
    assert p.hand_tiles == before
    stale = dict(g.live_pending_window)
    with pytest.raises(ValueError):
        g.apply_action_results(stale, {0:dict(action_type='cut', TileId=p.hand_tiles[-1], cutClass=True)})
    with pytest.raises(ValueError):
        apply(g, 0, 'peng', choice_id='peng:-1:11,11')


def test_meld_chooses_exact_bonus_identity_and_retains_reinterpretation():
    g = game()
    g.player_list[1].hand_tiles = [1013, 2013, 13, 11, 12, 22, 23, 24, 31, 32, 33, 41, 41]
    g.open_action_window(g._window(P.RESPONSE, 0, 13, claim_actions(g, 0, 13)))
    choices = [c for c in g.meld_choices[1] if c.kind == 'peng']
    choice = next(c for c in choices if c.take == (1013, 2013))
    g.player_list[0].discard_tiles.append(13)
    g.player_list[0].discard_riichi_flags.append(False)
    with pytest.raises(ValueError):
        apply(g, 1, 'peng')
    apply(g, 1, 'peng', choice_id=choice.key)
    assert g.game_status == P.AFTER_MELD.value
    assert g.player_list[1].melds[0].physical == (1013, 2013, 13)
    assert 13 in g.player_list[1].hand_tiles and 1013 not in g.player_list[1].hand_tiles


@pytest.mark.parametrize('variant', ['guobiao', 'riichi'])
def test_concealed_kong_and_fifth_tile_use_distinct_draw_sources(variant):
    g = game(variant)
    p = g.player_list[0]
    turn(g, 0, [11, 11, 11, 11, *WAIT[3:12], 41])
    c = next(c for c in g.meld_choices[0] if c.kind == 'angang' and c.take == (11,)*4)
    live_before, indicators = len(g.wall.live), len(g.wall.indicators())
    apply(g, 0, 'angang', choice_id=c.key)
    assert len(g.wall.live) == live_before - 1
    assert len(g.wall.indicators()) == indicators + int(variant == 'riichi')
    assert p.after_kong and len(p.melds[0].physical) == 4
    p.hand_tiles[-1] = t.EMPEROR
    g.open_action_window(g.begin_turn(0))
    c = next(c for c in g.meld_choices[0] if c.kind == 'jiagang' and c.take == (t.EMPEROR,))
    indicators = len(g.wall.indicators())
    apply(g, 0, 'jiagang', choice_id=c.key)
    assert len(g.wall.indicators()) == indicators
    assert len(p.melds[0].physical) == 5
    assert g.game_status == P.TURN.value
    payload = g.build_game_start_payload(1)['game_info']['players_info'][0]
    assert payload['duocai_melds'][0]['physical'] == ([0]*5 if variant == 'guobiao' else list(p.melds[0].physical))


def test_added_open_kong_waits_before_mutation_and_rob_removes_one_tile():
    g = game('riichi')
    p = g.player_list[0]
    p.melds = [Meld((38, 38, 38), False, 'peng', 2)]
    g.update_melds(0)
    g.player_list[1].hand_tiles = WAIT.copy()
    g.player_list[1].discard_origin_tiles = []
    turn(g, 0, [11, 12, 13, 22, 23, 24, 31, 32, 33, 41, 38])
    c = next(c for c in g.meld_choices[0] if c.kind == 'jiagang' and c.take == (38,))
    before = list(p.hand_tiles)
    apply(g, 0, 'jiagang', choice_id=c.key)
    assert g.game_status == P.ROB_KONG.value and p.hand_tiles == before and len(p.melds[0].physical) == 3
    assert 'hu' in g.action_dict[1]
    apply(g, 1, 'hu')
    assert g.game_status == P.END.value and p.hand_tiles == before[:-1]
    assert len(p.melds[0].physical) == 3
    assert g.deferred_hu_settlements[0]['source'] == 'rob_kong'
    assert sum(player.score for player in g.player_list) == 160000


def test_riichi_deposit_commits_after_ron_window_and_never_allows_hand_cut():
    g = game('riichi')
    turn(g, 0, [*WAIT, 47])
    assert 'riichi_cut' in g.action_dict[0]
    apply(g, 0, 'riichi_cut', TileId=47, cutClass=True)
    assert g.player_list[0].score == 40000 and g.riichi_sticks == 0
    responses = {i:dict(action_type='pass') for i, a in g.live_pending_window['actions'].items() if a}
    g.apply_action_results(g.live_pending_window, responses)
    assert g.player_list[0].riichi and g.player_list[0].score == 39000 and g.riichi_sticks == 1
    turn(g, 0, [*WAIT, 47])
    with pytest.raises(ValueError):
        apply(g, 0, 'cut', TileId=11, cutIndex=0, cutClass=False)


def test_passed_win_sets_furiten_only_when_an_actual_ron_was_offered():
    g = game('riichi')
    p = g.player_list[1]
    p.hand_tiles, p.discard_origin_tiles = WAIT.copy(), []
    p.riichi = 1
    g.open_action_window(g._window(P.RESPONSE, 0, 38, claim_actions(g, 0, 38)))
    assert 'hu' in g.action_dict[1]
    apply(g, 1, 'pass')
    assert p.riichi_furiten


def test_draw_only_bot_never_uses_optional_actions():
    g = game('riichi')
    p = g.player_list[0]
    p.user_id = 0
    actions = ['hu_self', 'angang', 'jiagang', 'riichi_cut', 'cut']
    result = choose_action(g, 0, actions)
    assert result['action_type'] == 'cut' and result['TileId'] == p.hand_tiles[-1] and result['cutClass']
    assert choose_action(g, 0, ['hu', 'peng', 'gang', 'pass']) == dict(action_type='pass')


def test_final_round_dealer_repeat_is_not_agari_yame():
    g = game('riichi')
    g.current_round = 8
    g.finish_round(winner=0)
    assert not g.match_finishing and g.next_dealer == 0 and g.honba == 1
    g.machine.transition(P.READY)
    g.advance_round_after_ready()
    assert g.current_round == 8 and g.round_index == 2


def test_state_machine_rejects_impossible_phase_jumps():
    m = StateMachine()
    with pytest.raises(RuntimeError):
        m.transition(P.READY)
    m.transition(P.TURN)
    m.transition(P.ROB_KONG)
    with pytest.raises(RuntimeError):
        m.transition(P.AFTER_MELD)
