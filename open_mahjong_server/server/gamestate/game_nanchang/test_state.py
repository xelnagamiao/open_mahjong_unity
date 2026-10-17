from collections import Counter
from copy import deepcopy
import asyncio
import pytest

from .NanchangGameState import NanchangGameState
from .state_machine import Phase as P
from .actions import claim_actions,kong_tiles
from .bot import choose_action
from ...game_calculation.nanchang import rules as r

NORMAL = [11,12,13,14,15,16,22,23,24,31,32,33,41,41]


def state():
    s = NanchangGameState(calculation_service=object())
    s.initialize_round()
    s.indicator = 45
    s.domain_events = [{"action":"test"}]
    s.machine.transition(P.TURN)
    return s


def cut(s,index,tile):
    p = s.player_list[index]
    s.current_player_index = index
    w=s.open_action_window(s.begin_turn(index,discard_only=s.machine.phase==P.DISCARD_ONLY))
    pos=p.hand_tiles.index(tile)
    return s.apply_action_results(w,{index:dict(action_type="cut",TileId=tile,cutIndex=pos,cutClass=bool(p.has_draw_slot and pos==len(p.hand_tiles)-1))})


def test_full_wall_indicator_remains_in_supply_and_drawable():
    s = state()
    assert len(s.tiles_list) == 83
    assert [len(p.hand_tiles) for p in s.player_list] == [14,13,13,13]
    assert Counter(s.tiles_list+[t for p in s.player_list for t in p.hand_tiles]) == Counter({t:4 for t in r.TILES})
    s.indicator_index=0
    indicator=s.tiles_list[0]
    s.draw_for(1)
    assert s.player_list[1].hand_tiles[-1] == indicator
    assert s.indicator_index == -1


def test_draw_wall_end_and_supplement_upper_lower():
    s=state(); s.tiles_list=[11,12,13,14]
    s.player_list[1].hand_tiles=NORMAL[:-1]
    s.draw_for(1,"kong")
    assert s.player_list[1].hand_tiles[-1] == 13
    s.player_list[1].hand_tiles.pop()
    s.draw_for(1,"kong")
    assert s.player_list[1].hand_tiles[-1] == 14
    s.tiles_list=[]
    s.draw_for(2)
    assert s.machine.phase == P.END
    assert sum(s.round_changes) == 0


def test_honor_chow_exact_physical_group_and_discard_only():
    s=state(); p=s.player_list[1]
    p.hand_tiles=[42,44,11,12,13,21,22,23,31,32,33,45,45]
    s.player_list[0].hand_tiles=NORMAL[:-1]+[41]
    w=cut(s,0,41)
    assert "chi_mid" in w["actions"][1]
    responses={i:dict(action_type="chi_mid" if i==1 else "pass") for i,a in w["actions"].items() if a}
    w=s.apply_action_results(w,responses)
    assert p.combination_tiles == ['s414244']
    assert p.combination_mask == [[1,41,0,42,0,44]]
    assert w["actions"][1] == ["cut"]
    assert not kong_tiles(s,1,"angang")


def test_jingdiao_declaration_survives_other_claim_and_blocks_ron():
    s=state(); p=s.player_list[1]
    p.hand_tiles=NORMAL[:12]+[45]
    s.player_list[2].hand_tiles=[41,41,21,22,23,24,25,26,31,32,33,46,46]
    s.player_list[0].hand_tiles=NORMAL[:-1]+[41]
    w=cut(s,0,41)
    assert 'nc_jingdiao' in w['actions'][1]
    responses={i:dict(action_type='nc_jingdiao' if i==1 else 'peng' if i==2 else 'pass') for i,a in w['actions'].items() if a}
    s.apply_action_results(w,responses)
    assert p.declared_jingdiao
    assert not s.can_win(1,'discard',45,payer=2)
    cut(s,2,46)
    s.draw_for(1)
    w=s.begin_turn(1)
    assert 'hu_self' in w['actions'][1] and 'cut' not in w['actions'][1]


def test_pdf_shunhe_dealer_raise_and_cap():
    s=state(); s.indicator=39
    p=s.player_list[1]
    p.hand_tiles=[32,33,12,12,12,13,13,16,17,31]
    p.combination_tiles=['k41']
    p.discard_origin_tiles=[39]
    q=s.score_win(1,'discard',34,payer=2)
    p.missed_fan=q['effective_fan']
    assert not s.can_win(1,'discard',34,payer=3)
    assert s.can_win(1,'discard',34,payer=0)
    assert s.can_win(1,'discard',15,payer=2)
    p.discard_origin_tiles=[39,39,39]
    p.missed_fan=s.score_win(1,'discard',34,payer=2)['effective_fan']
    assert not s.can_win(1,'discard',34,payer=0)
    assert not s.can_win(1,'discard',15,payer=2)


def test_bad_actions_do_not_mutate_state():
    s=state(); w=s.open_action_window(s.begin_turn(0))
    before=deepcopy(s.player_list[0].hand_tiles)
    with pytest.raises(ValueError):
        s.apply_action_results(w,{0:dict(action_type='cut',TileId=51,cutIndex=0)})
    assert s.player_list[0].hand_tiles==before
    with pytest.raises(ValueError):
        s.apply_action_results(dict(w),{0:dict(action_type='pass')})


def test_privacy_reconnect_and_concealed_kong():
    s=state(); p=s.player_list[0]
    p.hand_tiles=[21]*4+[11,12,13,31,32,33,41,41,45,45]
    s.commit_kong(0,21,'angang')
    own=s.build_game_start_payload(0)['game_info']['players_info'][0]
    other=s.build_game_start_payload(1)['game_info']['players_info'][0]
    assert own['hand_tiles'] is not None and other['hand_tiles'] is None
    assert other['combination_tiles']==['G0']
    assert other['combination_mask']==[[2,0]*4]


def test_three_ledgers_settle_once_and_winner_becomes_dealer():
    s=state(); s.current_player_index=2
    s.player_list[2].hand_tiles=NORMAL.copy(); s.player_list[2].has_draw_slot=True
    s.record_kong(1,21,'angang')
    s.settle_win(2,'self_draw',41)
    assert s.next_dealer==2
    s.apply_deferred_score_changes(); scores=[p.score for p in s.player_list]
    s.apply_deferred_score_changes(); assert scores==[p.score for p in s.player_list]
    assert sum(scores)==0
    s.machine.transition(P.READY); winner=s.player_list[2].user_id
    s.advance_round_after_ready()
    assert s.player_list[0].user_id==winner and s.current_round==2


def test_hints_priority_and_do_not_leak_other_jing():
    from server.gamestate.game_nanchang.client_context import private_context as private_hints
    from tests.local_hint_oracles.nanchang import for_hand
    s=state(); s.tips=True
    p=s.player_list[1]; p.hand_tiles=NORMAL[:12]+[45]; p.discard_origin_tiles=[45]
    a=private_hints(s,1)
    s.player_list[2].hand_tiles=[45]*2+[11]*2
    assert private_hints(s,1)==a
    request=a['__local__'][0]['local_request']
    assert request['hand']==p.hand_tiles and request['context']['discarded']==[45]
    waits=for_hand(s,1,p.hand_tiles,p.discard_origin_tiles)
    assert any(w['status']=='self_draw_only' for w in waits)
    p.tag_list=['peida']; s._reference_wait_cache={}
    assert private_hints(s,1)['__local__'][0]['local_request']['context']['peida']
    assert all(w['status']=='unmet' for w in for_hand(s,1,p.hand_tiles,p.discard_origin_tiles))


def test_seeded_action_branches_conserve_all_physical_tiles():
    for seed in range(3):
        s=NanchangGameState(room_data={**NanchangGameState._default_room_data(),'random_seed':seed+1},calculation_service=object())
        s.initialize_round(); w=s.open_action_window(s.opening_window())
        for _ in range(450):
            if s.machine.phase==P.END:
                break
            responses={i:choose_action(s,i,a) for i,a in w['actions'].items() if a}
            w=s.apply_action_results(w,responses)
            physical=s.tiles_list+[t for p in s.player_list for t in p.hand_tiles+p.discard_tiles+p.won_tiles]
            physical += [t for p in s.player_list for m in p.combination_tiles for t in r.meld_tiles(m)]
            assert Counter(physical)==Counter({t:4 for t in r.TILES})
        assert s.machine.phase==P.END
        assert sum(s.round_changes)==0
