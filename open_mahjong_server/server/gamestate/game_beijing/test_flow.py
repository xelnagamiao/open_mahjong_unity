from collections import Counter
import json

import pytest

from .BeijingGameState import BeijingGameState
from .actions import claim_actions, kong_tiles
from .bot import choose_action
from .client_context import private_context
from .state_machine import Phase as P
from ...game_calculation.beijing import rules as book

WAIT = [11,12,13,21,22,23,31,32,33,41,41,41,42]


def game(**config):
    data = BeijingGameState._default_room_data()
    data.update(tips=True, game_round=1, detailed_config=config)
    g = BeijingGameState(room_data=data, calculation_service=object())
    g.initialize_round()
    g.indicator = 46
    g.open_action_window(g.begin_turn(0))
    return g


def turn(g, index, hand):
    p = g.player_list[index]
    p.hand_tiles, p.has_draw_slot, p.draw_kind = list(hand), True, 'normal'
    g.current_player_index = index
    g.open_action_window(g.begin_turn(index))


def response(g, actor, tile):
    g.current_player_index = actor
    g.player_list[actor].discard_tiles.append(tile)
    g.open_action_window(g._window(P.RESPONSE, actor, tile, claim_actions(g, actor, tile)))


def apply(g, index=None, action='pass', **fields):
    choices = {i:dict(action_type='pass') for i,a in g.live_pending_window['actions'].items() if a}
    if index is not None:
        choices[index] = dict(action_type=action, **fields)
    return g.apply_action_results(g.live_pending_window, choices)


def test_deal_deck_seed_and_hidden_snapshot():
    g = BeijingGameState(calculation_service=object())
    g.initialize_round()
    assert [len(p.hand_tiles) for p in g.player_list] == [14,13,13,13]
    assert len(g.tiles_list) == 82 and len(g.dice) == 4
    assert Counter([g.indicator,*g.tiles_list,*(t for p in g.player_list for t in p.hand_tiles)]) == Counter(t for t in book.TILES for _ in range(4))
    other = BeijingGameState(calculation_service=object())
    other.initialize_round()
    assert g.tiles_list == other.tiles_list and g.indicator == other.indicator
    for viewer in range(4):
        packet = g.build_game_start_payload(viewer)
        json.dumps(packet)
        for i,p in enumerate(packet['game_info']['players_info']):
            assert (p['hand_tiles'] is not None) == (i == viewer)


def test_invalid_window_and_discard_identity_are_atomic():
    g = game()
    turn(g, 0, WAIT+[47])
    before = list(g.player_list[0].hand_tiles)
    for fields in (dict(TileId=11,cutIndex=0,cutClass=True),dict(TileId=47,cutIndex=13,cutClass=False),
                   dict(TileId=True,cutIndex=0),dict(TileId=11,cutIndex='0'),dict(TileId=11,cutClass='true')):
        with pytest.raises(ValueError):
            apply(g, 0, 'cut', **fields)
        assert g.player_list[0].hand_tiles == before
    with pytest.raises(ValueError):
        g.apply_action_results(dict(g.live_pending_window), {0:dict(action_type='cut',TileId=11)})


def test_tianting_declares_before_first_cut_and_tracks_original_tiles():
    g = BeijingGameState(calculation_service=object())
    g.initialize_round()
    g.indicator = 46
    g.player_list[1].hand_tiles = WAIT.copy()
    g.open_action_window(g.opening_window())
    assert g.machine.phase == P.TIANTING
    apply(g, 1, 'tianting')
    assert g.player_list[1].tian_ting
    turn(g, 1, WAIT+[11])
    assert private_context(g, 1, for_record=True)['local_request']['context']['drawn_tile'] == 11
    apply(g, 1, 'cut', TileId=11, cutIndex=13, cutClass=True)
    assert g.player_list[1].tian_ting
    apply(g)
    turn(g, 1, WAIT+[11])
    apply(g, 1, 'cut', TileId=11, cutIndex=0, cutClass=False)
    assert not g.player_list[1].tian_ting


def test_cut_hun_permanently_locks_win_and_claims_cannot_use_hun():
    g = game()
    turn(g, 0, WAIT+[45])
    apply(g, 0, 'cut', TileId=45, cutClass=True)
    assert g.player_list[0].discarded_hun
    assert all(not any(a in ('peng','gang','chi_left','chi_mid','chi_right') for a in actions) for actions in g.action_dict.values())
    apply(g)
    turn(g, 0, WAIT+[42])
    assert 'hu_self' not in g.action_dict[0]
    turn(g, 0, [45]*4+WAIT[:10])
    assert 45 not in kong_tiles(g,0,'angang')
    g.player_list[1].hand_tiles = [44,45]+WAIT[:11]
    assert 'chi_left' not in claim_actions(g,0,46)[1]


def test_ming_kong_beats_nearer_peng_and_draws_from_tail():
    g = game()
    g.player_list[1].hand_tiles = [11,11]+WAIT[2:]
    g.player_list[2].hand_tiles = [11,11,11]+WAIT[3:]
    response(g,0,11)
    tail = g.tiles_list[-1]
    results = {i:dict(action_type='pass') for i,a in g.action_dict.items() if a}
    results[1] = dict(action_type='peng')
    results[2] = dict(action_type='gang')
    g.apply_action_results(g.live_pending_window,results)
    assert g.current_player_index == 2 and g.player_list[2].hand_tiles[-1] == tail
    assert g.player_list[2].combination_tiles == ['g11'] and g.player_list[2].kong_payer == 0
    assert not g.player_list[0].discard_tiles


def test_single_call_prevents_second_open_claim_but_keeps_win_rules():
    g = game(single_call=True)
    p = g.player_list[1]
    p.hand_tiles, p.open_calls, p.combination_tiles = WAIT[3:], 1, ['s12']
    assert not claim_actions(g,0,41)[1]
    p.hand_tiles = WAIT[3:]+[42]
    p.has_draw_slot = True
    assert g.can_win(1,'self_draw',42)


def test_concealed_kong_secrecy_then_reveal_on_draw():
    g = game()
    turn(g,0,[11]*4+[21,22,23,31,32,33,41,41,41,42])
    apply(g,0,'angang',target_tile=11)
    assert g.player_list[0].combination_tiles == ['G11']
    assert g._player_view(0,1)['combination_mask'] == [[2,0]*4]
    assert g._player_view(0,0)['combination_mask'] == [[2,11]*4]
    g.end_draw()
    assert g._player_view(0,1)['combination_mask'] == [[0,11]*4]


@pytest.mark.parametrize('multi', [False, True])
def test_ron_priority_multiple_winners_and_river_recycled_once(multi):
    g = game(multi_ron=multi)
    for i in (1,2):
        g.player_list[i].hand_tiles = WAIT.copy()
    g.start_game_recording()
    g.start_round_recording()
    response(g,0,42)
    choices = {i:dict(action_type='hu' if i in (1,2) else 'pass') for i,a in g.action_dict.items() if a}
    g.apply_action_results(g.live_pending_window,choices)
    assert g.round_settlement['winners'] == ([1,2] if multi else [1])
    assert not g.player_list[0].discard_tiles
    entries = g.deferred_hu_settlements
    assert [e['recycle_discard'] for e in entries] == ([True,False] if multi else [True])
    packet = g.build_final_settlement_payload(0)
    assert [e['recycle_discard'] for e in packet['show_result_info']['beijing_settlements']] == [e['recycle_discard'] for e in entries]
    assert sum(p.score for p in g.player_list) == 0
    g.finalize_round_recording()
    before = [vars(p.record_counter).copy() for p in g.player_list]
    g.finalize_round_recording()
    assert before == [vars(p.record_counter) for p in g.player_list]
    assert g.player_list[1].record_counter.dianhe_times == 1
    assert g.player_list[0].record_counter.fangchong_times == (2 if multi else 1)
    assert all(not p.record_counter.recorded_fans for p in g.player_list)


def test_added_kong_rob_removes_one_held_tile_and_preserves_pung():
    g = game()
    p = g.player_list[0]
    p.combination_tiles, p.combination_mask = ['k42'], [[1,42,0,42,0,42]]
    g.player_list[1].hand_tiles = WAIT.copy()
    turn(g,0,[11,12,13,21,22,23,31,32,33,41,42])
    before = p.hand_tiles.copy()
    apply(g,0,'jiagang',target_tile=42)
    assert g.machine.phase == P.ROB_KONG and p.hand_tiles == before and p.combination_tiles == ['k42']
    apply(g,1,'hu')
    assert p.hand_tiles == before[:-1] and p.combination_tiles == ['k42']
    assert not g.deferred_hu_settlements[0]['recycle_discard']
    assert any(v.get('do_action_info',{}).get('cut_tile') == 42 and 'rob_kan' in v.get('do_action_info',{}).get('action_list',[]) for v in g.outbound_payloads)


def test_added_kong_commits_after_everyone_passes():
    g = game()
    p = g.player_list[0]
    p.combination_tiles, p.combination_mask = ['k42'], [[1,42,0,42,0,42]]
    turn(g,0,[11,12,13,21,22,23,31,32,33,41,42])
    tail = g.tiles_list[-1]
    apply(g,0,'jiagang',target_tile=42)
    apply(g)
    assert p.combination_tiles == ['g42'] and p.hand_tiles[-1] == tail
    assert p.after_kong and p.combination_mask[0][:4] == [3,42,1,42]


def test_dingzhuang_and_pao_conserve_scores_without_fan_statistics():
    g = game(dingzhuang=True,pao_kong=True)
    for p in g.player_list:
        p.discard_origin_tiles = [47]
    g.check_dingzhuang()
    g.check_dingzhuang()
    assert [p.score for p in g.player_list] == [-6,2,2,2]
    p = g.player_list[1]
    p.combination_tiles, p.combination_mask = ['g11'], [[1,11,0,11,0,11,0,11]]
    turn(g,1,WAIT[3:]+[42])
    p.after_kong, p.kong_payer = True, 2
    apply(g,1,'hu_self')
    e = g.deferred_hu_settlements[0]
    assert e['liability'] == 2 and e['score_changes'][2] == -3*e['points']
    assert sum(g.round_changes) == 0


def test_pao_four_calls_and_four_hun_fixed_fan():
    g = game(pao_four_calls=True)
    p = g.player_list[1]
    p.combination_tiles = ['s12','s22','s32','k41']
    p.open_calls, p.fourth_call_payer = 4, 3
    turn(g,1,[42,42])
    apply(g,1,'hu_self')
    assert g.deferred_hu_settlements[0]['liability'] == 3
    g = game(four_hun_win=True,fan_zimo=True,fan_dealer=True)
    turn(g,0,[45]*4+[11,14,17,21,24,27,31,34,37,41])
    apply(g,0,'hun_gang')
    assert g.deferred_hu_settlements[0]['fan_details']['fan'] == 2
    assert [p.score for p in g.player_list] == [12,-4,-4,-4]


def test_dealer_repeat_draw_rotation_and_last_circle():
    g = game()
    g.current_round = 4
    turn(g,0,WAIT+[42])
    apply(g,0,'hu_self')
    assert not g.match_finishing and g.next_dealer == 0
    g.machine.transition(P.READY)
    g.advance_round_after_ready()
    assert g.current_round == 4 and g.repeat == 1 and g.round_index == 2
    g.initialize_round()
    g.open_action_window(g.begin_turn(0))
    g.end_draw()
    assert g.match_finishing and g.next_dealer == 1
    assert g.build_final_settlement_payload(0)['show_result_info']['next_status'] == 'match_end'


def test_exhausted_wall_draw_occurs_after_last_discard_claim_window():
    g = game()
    g.tiles_list.clear()
    turn(g,0,WAIT+[42])
    assert g.score_win(0,'self_draw',42)['fan_values']['海底捞月'] == 1
    apply(g,0,'cut',TileId=42,cutClass=True)
    assert g.machine.phase == P.RESPONSE
    apply(g)
    assert g.machine.phase == P.END


def test_auto_cut_is_strict_and_efficiency_bot_can_decline_calls():
    g = game()
    p = g.player_list[0]
    p.user_id = 0
    turn(g,0,WAIT+[42])
    action = choose_action(g,0,['hu_self','hun_gang','angang','jiagang','cut'])
    assert action == dict(action_type='cut',TileId=42,cutIndex=13,cutClass=True)
    for actions in (['hu','peng','pass'],['tianting','pass'],['hun_gang','pass']):
        assert choose_action(g,0,actions) == dict(action_type='pass')
    p = g.player_list[1]
    p.user_id = -1
    p.hand_tiles = [11,11,12,12,13,13,21,21,22,22,31,31,41]
    response(g,0,11)
    assert 'peng' in g.action_dict[1]
    assert choose_action(g,1,['peng','pass']) == dict(action_type='pass')


def test_private_hints_include_context_without_server_enumeration(monkeypatch):
    g = game()
    monkeypatch.setattr(book,'logical_waits',lambda *a,**kw: (_ for _ in ()).throw(AssertionError('server hint search')))
    info = g.build_game_start_payload(0)['game_info']
    assert info['beijing_hints']['local_request']['context']['hun'] == 45
    assert info['beijing_hints']['local_request']['context']['drawn_tile'] == g.player_list[0].hand_tiles[-1]
