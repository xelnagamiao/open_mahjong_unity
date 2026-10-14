import asyncio
import json
from collections import Counter
import pytest
from .MalaysiaGameState import MalaysiaGameState
from .phases import P
from . import actions as A
from ...game_calculation.malaysia import rules as R

def state():
    s=MalaysiaGameState(calculation_service=object())
    s.start_game_recording();s.initialize_round();s.start_round_recording()
    return s

def apply(s, **choices):
    w=s.live_pending_window
    responses={i:choices.get(str(i),{'action_type':'pass'}) for i,a in w['actions'].items() if a}
    return s.apply_action_results(w,responses)

def inventory(s):
    c=Counter(s.tiles_list)
    for p in s.player_list:
        c.update(p.hand_tiles);c.update(p.huapai_list);c.update(p.discard_tiles);c.update(p.won_tiles)
        for m in p.melds:c.update(m.physical)
    return c

def arrange(s,hands,bonuses=None):
    for i,p in enumerate(s.player_list):
        p.reset_for_round(s.round_time);p.hand_tiles=list(hands[i]);p.huapai_list=list((bonuses or [[],[],[]])[i])
    s.tiles_list=[21,22,23,24,25,26,27,28,29]
    s.opening=False;s.opening_interrupted=True;s.discard_log=[];s.sea_discard=False
    s.machine.phase=P.TURN;s.current_player_index=0
    s.player_list[0].has_draw_slot=True;s.player_list[0].last_drawn_tile=s.player_list[0].hand_tiles[-1]

@pytest.mark.parametrize('seed',range(20))
def test_complete_three_hand_match_conserves_every_physical_tile(seed):
    s=MalaysiaGameState(room_data={**MalaysiaGameState._default_room_data(),'random_seed':seed},calculation_service=object())
    s.start_game_recording();total=Counter(R.tile_pool())
    for round_index in range(3):
        s.initialize_round();s.start_round_recording();s.open_action_window(s.opening_window())
        for step in range(600):
            assert inventory(s)==total
            if s.machine.phase==P.END:break
            actions={i:s.automatic_action(i,bot=True) for i,a in s.action_dict.items() if a}
            s.apply_action_results(s.live_pending_window,actions)
        else:pytest.fail('non-terminating round')
        s.finalize_round_recording();s.finalize_round_recording()
        payload=s.build_final_settlement_payload(0)
        assert sum(p.score for p in s.player_list)==0
        assert all(len(p.score_history)==round_index+1 for p in s.player_list)
        assert len(payload['game_info']['players_info'])==3
        json.dumps(s.game_record,default=str)
        s.machine.transition(P.READY)
        if not s.match_finishing:s.advance_round_after_ready()
    assert s.match_finishing and sorted(p.original_player_index for p in s.player_list)==[0,1,2]

def test_natural_pung_beats_nearer_fly_pung():
    s=state();arrange(s,[[21,22],[21,81,23,24],[21,21,25,26]])
    s.player_list[0].hand_tiles.remove(21);s.player_list[0].discard_tiles=[21]
    s.open_action_window(s.window(P.RESPONSE,0,21,A.responses(s,0,21)))
    apply(s,**{'1':{'action_type':'malaysia_peng:1'},'2':{'action_type':'malaysia_peng:0'}})
    assert s.current_player_index==2 and s.machine.phase==P.CUT
    assert s.player_list[2].melds[0].physical==(21,21,21)

@pytest.mark.parametrize('chow', [True,False])
def test_exact_chow_choices_and_no_two_fly_open_chow(chow):
    s=state();p=s.player_list[1];p.hand_tiles=[81,81,22,23]
    choices=A.claims(p,21,chow,[29])
    assert ('malaysia_chi:22:0' in choices)==chow
    assert all(m.physical.count(81)<=1 for m in choices.values() if m.kind=='sequence')
    assert 'malaysia_peng:2' in choices

def test_redemption_keeps_inventory_and_cannot_repeat():
    s=state();arrange(s,[[21,22,21],[23],[24]])
    p=s.player_list[0];p.melds=[R.Meld('triplet',(21,81,81),(21,)*3,1)]
    p.combination_tiles=['k21'];p.combination_mask=[[1,21,0,81,0,81]]
    before=inventory(s);s.open_action_window(s.begin_turn(0))
    apply(s,**{'0':{'action_type':'malaysia_redeem:0'}})
    assert inventory(s)==before and p.hand_tiles[-1]==81
    assert p.melds[0].physical==(21,21,81)
    assert 'malaysia_redeem:0' not in s.action_dict[0]

def test_added_kong_can_be_robbed_before_commit_and_payment():
    s=state();arrange(s,[[29,21],[22,23,24,25,26,27,28,28,28,29,29,29,21],[24]])
    p=s.player_list[0];p.melds=[R.Meld('triplet',(21,)*3,(21,)*3,2)]
    p.combination_tiles=['k21'];p.combination_mask=[[1,21,0,21,0,21]]
    s.open_action_window(s.begin_turn(0));apply(s,**{'0':{'action_type':'jiagang','target_tile':21}})
    assert s.machine.phase==P.KONG and not s.ledger
    apply(s,**{'1':{'action_type':'hu'}})
    assert s.machine.phase==P.END and p.melds[0].kind=='triplet'
    assert p.hand_tiles==[29] and not any(x['reason']=='加杠' for x in s.ledger)
    packets=[x['do_action_info'] for x in s.outbound_payloads if x.get('do_action_info',{}).get('action_list')==['rob_kan']]
    assert len(packets)==3
    assert all(x['action_player']==0 and x['cut_tile']==21 and x['is_mo_gang'] for x in packets)
    ticks=s.game_record['game_round']['round_index_1']['action_ticks']
    assert not any(t[0]=='rk' for t in ticks)
    assert sum(t[:2]==['malaysia','win_source'] for t in ticks)==1

@pytest.mark.parametrize('kind', ['angang','jiagang'])
def test_kong_commits_once_after_pass(kind):
    s=state();arrange(s,[[21]*4+[29] if kind=='angang' else [21,29],[24],[25]])
    p=s.player_list[0]
    if kind=='jiagang':
        p.melds=[R.Meld('triplet',(21,)*3,(21,)*3,1)];p.combination_tiles=['k21'];p.combination_mask=[[1,21,0,21,0,21]]
    s.open_action_window(s.begin_turn(0));apply(s,**{'0':{'action_type':kind,'target_tile':21}})
    apply(s)
    assert p.melds[0].kind=='kong' and p.hand_tiles[-1]==29
    assert [p.score for p in s.player_list]==([200,-100,-100] if kind=='angang' else [100,-50,-50])
    assert not A.kongs(s,0,'jiagang')

def test_draw_preserves_instant_payment_and_finalizes_once():
    s=state();s.pay_instant(1,[0,2],50,'成套花');s.machine.phase=P.TURN;s.end_draw()
    s.finalize_round_recording();s.finalize_round_recording()
    s.build_final_settlement_payload(0);s.build_final_settlement_payload(0)
    assert [p.score for p in s.player_list]==[-50,100,-50]
    assert all(len(p.score_history)==1 for p in s.player_list)

def test_invalid_and_stale_windows_are_atomic():
    s=state();s.open_action_window(s.opening_window());w=s.live_pending_window
    before=inventory(s);tick=s.server_action_tick
    with pytest.raises(ValueError):s.apply_action_results(w,{4:{'action_type':'hu_self'}})
    assert inventory(s)==before and tick==s.server_action_tick
    actions={i:s.automatic_action(i) for i,a in s.action_dict.items() if a}
    s.apply_action_results(w,actions)
    with pytest.raises(ValueError):s.apply_action_results(w,actions)

def test_response_queued_before_wait_is_not_lost():
    async def run():
        s=state();s.open_action_window(s.opening_window())
        for i in list(s.waiting_players_list):
            await s.submit_action(i,**s.automatic_action(i))
            with pytest.raises(ValueError):await s.submit_action(i,**s.automatic_action(i))
        responses=await s.wait_action(.1)
        assert not any(x.get('is_timeout_action') for x in responses.values())
        s.apply_action_results(s.live_pending_window,responses)
    asyncio.run(run())

def test_private_snapshots_and_ready_reconnect():
    s=state();s.open_action_window(s.opening_window())
    for viewer in range(3):
        p=s.build_game_start_payload(viewer);assert p['player_index']==viewer
        assert len(p['game_info']['players_info'])==3
    s.machine.phase=P.TURN;s.end_draw();s.finalize_round_recording();s.machine.transition(P.READY)
    assert [p['type'].split('/')[-1] for p in s.restore_payloads(1)]==['game_start','show_result','ready_status']
