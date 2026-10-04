"""Action admission, timeout, bot, private reconnect and robbery edge cases."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock,patch
import pytest
from .test_state import make_state,hand,special_ready_for_add,ticks
from . import bot
from ...game_calculation.changchun.test_rules import READY
from ...game_calculation.changchun import rules as book


def run_bot(state,available,status='waiting_hand_action',current=True,wait=True):
    with patch.object(bot,'_wait_until_actionable',new=AsyncMock(return_value=wait)), \
         patch.object(bot,'bot_action_is_current',return_value=current), \
         patch.object(bot,'submit_bot_action',new=AsyncMock()) as submit, \
         patch.object(bot,'run_room_bot_cpu',new=AsyncMock(return_value=(28,13))):
        asyncio.run(bot.changchun_bot_action(state,0,available,status))
        return submit.call_args.args[3:] if submit.called else None


@pytest.mark.parametrize('action',['hu_self','cc_change_bao','cc_draw','cc_pass'])
def test_bot_prioritizes_win_and_special_windows(action):
    state=make_state(bot_speed='instant');hand(state,0,READY+[28],drawn=28)
    assert run_bot(state,[action,'cut'])[0]==action


@pytest.mark.parametrize('available,expected',[(['gang','peng','pass'],'gang'),(['peng','pass'],'peng'),(['pass'],'pass')])
def test_bot_claim_responses(available,expected):
    assert run_bot(make_state(bot_speed='instant'),available,'waiting_action')[0]==expected


@pytest.mark.parametrize('wait,current',[(False,True),(True,False)])
def test_bot_stale_work_never_submits(wait,current):
    assert run_bot(make_state(bot_speed='instant'),['cc_draw'],current=current,wait=wait) is None


def test_bot_special_target_token_and_added():
    state,player,candidate=special_ready_for_add();state.bot_speed='instant'
    assert run_bot(state,['cc_added','cut'])[-1]==0
    hand(state,0,[11,21,31,12,13,14,22,23,24,35,36,37,45,45],drawn=45)
    assert run_bot(state,['cc_special','cut'])[0]=='cc_special'


@pytest.mark.parametrize('kind,code,tiles,action',[
    ('concealed',[],[11]*4+[22,23,24,31,32,33,45,45,45,19],'angang'),
    ('added',['k11'],[11,22,23,24,31,32,33,45,45,45,19],'jiagang'),
])
def test_bot_ordinary_kong_and_execution(kind,code,tiles,action):
    state=make_state(bot_speed='instant');hand(state,0,tiles,code,drawn=11)
    assert run_bot(state,[action,'cut'])[-1]==11
    asyncio.run(getattr(state,'execute_'+action)(0,11))
    assert state.game_status=='deal_card_after_gang'
    assert state.player_list[0].combination_tiles==['G11' if kind=='concealed' else 'g11']
    assert len(state.kong_ledger)==1


def test_bot_ready_locked_and_fallback_discard():
    state=make_state(bot_speed='instant');p=hand(state,0,READY+[28],drawn=28)
    p.riichi_candidate_cuts=state.ready_candidate_cuts(0)
    result=run_bot(state,['riichi_cut','cut']);assert result[0]=='riichi_cut' and result[2] in p.riichi_candidate_cuts
    p.ready_locked=True
    assert run_bot(state,['cut'])[2:4]==(28,13)
    p.ready_locked=False
    assert run_bot(state,['cut'])[2:4]==(28,13)


def test_bot_visible_counts_uses_physical_not_represented_or_concealed():
    state=make_state();hand(state,0,READY,ready=True)
    state.player_list[1].combination_tiles=['Cwind:41,42,31:41,42,43','G11']
    state.player_list[2].discard_tiles=[22]
    state.cc_bao_tile=19;state.cc_bao_revision=1;state.player_list[0].cc_bao_seen=1
    counts=bot.visible_counts(state,0)
    assert counts[bot.tile_to_34(31)]==1 and counts[bot.tile_to_34(43)]==0
    assert counts[bot.tile_to_34(11)]==0 and counts[bot.tile_to_34(19)]==1
    assert bot.visible_counts(state,1)[bot.tile_to_34(11)]==4


def test_ordinary_added_kong_rob_pung_restores_correct_triplet_no_score():
    state=make_state();p=hand(state,0,[11,22,23,24,31,32,33,45,45,45,19],['k11'],drawn=11)
    hand(state,1,[11,11,12,13,14,22,23,24,35,36,37,45,45])
    asyncio.run(state.execute_jiagang(0,11))
    assert state.game_status=='waiting_action_qianggang'
    asyncio.run(state.resolve_rob_kong_responses({1:{'action_type':'peng'}},state.action_dict))
    assert p.combination_tiles==['k11'] and not state.kong_ledger
    event=next(t[1] for t in ticks(state) if t[0]=='cc' and t[1]['kind']=='added_robbed')
    assert event['tile']==11 and event['special'] is False


@pytest.mark.parametrize('mode',['timeout','change','draw','tail','special','added','angang','jiagang','win'])
def test_wait_action_dispatches_authoritative_windows(mode):
    state=make_state();state.game_status='waiting_hand_action';hand(state,0,READY+[28],drawn=28)
    name={'timeout':'execute_timeout_cut','change':'change_bao','draw':'draw_current_player','tail':'pass_final_tile',
          'special':'execute_special','added':'execute_special_added','angang':'execute_angang','jiagang':'execute_jiagang','win':'accept_self_draw'}[mode]
    action={'timeout':None,'change':'cc_change_bao','draw':'cut','tail':'cut','special':'cc_special','added':'cc_added','win':'hu_self'}.get(mode,mode)
    state.cc_window='before_draw' if mode in ('change','draw') else 'final_four' if mode=='tail' else 'normal'
    mock=AsyncMock() if mode!='win' else __import__('unittest.mock',fromlist=['Mock']).Mock()
    with patch('server.gamestate.game_changchun.ChangchunGameState._collect_responses',new=AsyncMock(return_value=({0:{'action_type':action,'target_tile':0}},{0:['hu_self']}))), \
         patch.object(state,'can_change_bao',return_value=True),patch.object(state,name,mock):
        asyncio.run(state.wait_action())
    mock.assert_called_once()


@pytest.mark.parametrize('method,args',[('execute_special',(0,-1)),('execute_special',(1,0)),('execute_special_added',(0,99)),('execute_angang',(0,99)),('execute_jiagang',(0,99)),('execute_claim',(1,'peng'))])
def test_forged_action_no_mutation(method,args):
    state=make_state();hand(state,0,READY+[28],drawn=28)
    before=list(state.player_list[0].hand_tiles)
    asyncio.run(getattr(state,method)(*args))
    assert state.player_list[0].hand_tiles==before and not state.kong_ledger


def test_bao_hidden_snapshot_and_change_guard_and_tail_guard():
    state=make_state();assert state.public_bao_count()==0
    assert asyncio.run(state.change_bao()) is False
    asyncio.run(state.pass_final_tile())
    state.cc_window='final_four'
    with pytest.raises(RuntimeError):asyncio.run(state.pass_final_tile())
    state.cc_bao_slot=3;state.cc_wall_ids=[]
    with pytest.raises(RuntimeError):state._sync_wall_ids()


def test_all_public_bao_copies_required_concealed_kong_excluded():
    state=make_state();state.cc_bao_tile=31
    state.player_list[0].combination_tiles=['G31']
    assert state.public_bao_count()==0
    state.player_list[0].combination_tiles=['Cyao:31,31,31:11,21,31']
    assert state.public_bao_count()==3


def test_bao_supplement_resume_and_exhaustion():
    state=make_state();hand(state,0,READY,ready=True)
    state.cc_bao_exhausted=True
    asyncio.run(state._deal_supplement());assert state.cc_window=='before_draw'
    state.next_supplement_kind='angang'
    asyncio.run(state.draw_current_player());assert len(state.player_list[0].hand_tiles)==14
    for method in ('_deal_normal','draw_current_player'):
        state.tiles_list=[];state.cc_tail_remaining=0
        asyncio.run(getattr(state,method)());assert state.game_status=='END'
    assert state.playable_wall_count()==0


def test_final_pass_event_never_exposes_another_players_tile():
    state=make_state();state.cc_window='final_four';state.cc_tail_remaining=1
    hand(state,0,READY+[28],drawn=28)
    asyncio.run(state.pass_final_tile())
    assert state.view_state(1)['tail_tiles']==[{'player':0,'tile':0}]
    assert state.view_state(0)['tail_tiles']==[{'player':0,'tile':28}]


def test_score_mo_bao_peida_and_other_seat_predraw_rejected():
    state=make_state();p=hand(state,0,READY+[28],ready=True,drawn=28)
    state.cc_bao_tile=28;state.cc_bao_revision=p.cc_bao_seen=1
    assert '摸宝' in state.score_candidate(0,'self_draw')['fan_names']
    p.tag_list.append('peida');assert state.score_candidate(0,'self_draw') is None
    state.cc_window='before_draw';assert state.score_candidate(1,'self_draw') is None
    assert state.score_candidate(1,'discard') is None


def test_reconnect_resends_terminal_result_only_after_round_ended():
    from ..game_taiwan.TaiwanGameState import TaiwanGameState
    state=make_state();socket=SimpleNamespace(send_json=AsyncMock())
    state.game_server.user_id_to_connection[101]=SimpleNamespace(websocket=socket)
    with patch.object(TaiwanGameState,'player_reconnect',new=AsyncMock()):
        asyncio.run(state.player_reconnect(101));socket.send_json.assert_not_called()
        state._terminal_result={'type':'show_result'}
        asyncio.run(state.player_reconnect(101));socket.send_json.assert_awaited_once_with(state._terminal_result)
        asyncio.run(state.player_reconnect(999))


@pytest.mark.parametrize('call',[
    lambda:book.payments(-1,0),lambda:book.payments(0,-1),lambda:book.payments(0,0,discarder=0),
    lambda:book.kong_payments(5,'direct',11),lambda:book.kong_payments(0,'direct',51),
    lambda:book.add_special('k11',11),lambda:book.add_special('Cwind:41,42,43:41,42,43',True),
])
def test_invalid_scoring_inputs_rejected(call):
    with pytest.raises(ValueError):call()


def test_invalid_hand_and_special_candidates():
    assert book.initial_special_candidates(READY,['broken'])==()
    assert book.initial_special_candidates([11]*5)==()
    assert book.score(READY+[28],winning_tile=28,bao='mo')
    assert book.score([11]*14,winning_tile=11) is None
    assert book.score([11]*13+[22],winning_tile=22,bao='mo') is None
    assert book.parse_meld('Cwind:41,42,43:41,42,43').kong


def test_clock_reconnect_deducts_elapsed_and_rejects_profile():
    with pytest.raises(ValueError):make_state(sub_rule='changchun/unknown')
    state=make_state();p=state.player_list[0];p.remaining_time=20
    state._ask_broadcast_time=100
    with patch('server.gamestate.game_changchun.ChangchunGameState.time.time',return_value=102):
        assert state.claim_clock(p)==(0,3)
        assert state.claim_clock(p,True)==(0,1)
    state._ask_broadcast_time=None
    assert state.claim_clock(p,True)==(0,3)
    assert state._liability_payer_for_win() is None


def test_metadata_reconnect_hooks_and_non_secret_record():
    state=make_state();hand(state,0,READY)
    assert state.build_private_game_info_fields(0)['changchun']['bao_tile']==0
    assert state.build_record_round_fields()['rule_version']==book.EDITION
    assert state.build_game_info_fields()['detailed_config']['fan_cap']==6
    assert state.spectator_record_tick(['d',11])==['d',11]
    asyncio.run(state.resolve_rob_kong_responses({},{}))
    asyncio.run(state.execute_claim(1,'gang'))


def test_malformed_special_and_chow_singleton_cannot_be_created():
    state=make_state();hand(state,0,[11,21,31])
    assert not state.special_candidates(0)
    hand(state,0,[11,21,31,45,45],['s12','k22','k35'])
    assert not state.special_candidates(0)
    hand(state,0,[11]*4)
    assert not state.kong_allowed(0,11,'concealed')
    hand(state,0,[11]*4+[45],['s12','k22','k35'])
    assert not state.kong_allowed(0,11,'concealed')


def test_ready_direct_kong_preserves_wait_and_rejects_wrong_draw():
    state=make_state();state.current_player_index=1
    ready=[11]*3+[22,23,24,31,32,33,45,45,45,19]
    p=hand(state,0,ready,ready=True)
    assert state.kong_allowed(0,11,'direct')
    assert 'gang' in state.check_discard_actions(11)[0]
    p.hand_tiles.append(11);p.last_drawn_tile=19
    assert not state.kong_allowed(0,11,'concealed')
    state.player_list[1].discard_tiles=[18]
    asyncio.run(state.execute_claim(0,'peng'))
    assert not p.combination_tiles


def test_stale_bot_options_fall_back_safely():
    state=make_state(bot_speed='instant');hand(state,0,READY+[28],drawn=28).discard_count=1
    assert run_bot(state,['cc_special','cc_added','angang','jiagang','cut'])[0]=='cut'


@pytest.mark.parametrize('mask',range(32))
def test_all_room_switches_and_fixed_false_readback(mask):
    from ...room.changchun_room import ChangchunRoomValidator
    keys=['tips','count_tips','pointer_tips','tourist_limit','allow_spectator']
    options={key:bool(mask & 1<<i) for i,key in enumerate(keys)}
    config=ChangchunRoomValidator(room_name='长春',round_timer=20,step_timer=5,
        use_flowers=False,hepai_limit=0,claim_protection=False,open_cuohe=False,
        tactical_call=False,tian_di_ren_he=False,**options).model_dump()
    assert all(config[key]==value for key,value in options.items())


def test_missing_win_and_unready_bao_do_not_score():
    assert book.score(READY,winning_tile=99) is None
    assert book.score(READY+[19],winning_tile=19,bao='wildcard') is None
    assert book.score([12,13,14,22,23,24,32,33,34,16,16,16,18,18],winning_tile=18,bao='mo') is None
    state=make_state();state.cc_window='before_draw';hand(state,0,READY)
    state.accept_self_draw(0);assert not state.pending_winners
    state._mark_eight_flowers_if_ready(0)
    state.enter_water(0);state.enter_water(1)
    assert state.player_list[0].passed_fan==state.player_list[1].passed_fan==-1


def test_discarding_a_winning_tile_sets_same_or_lower_ron_threshold():
    state=make_state();hand(state,0,READY+[19],drawn=19)
    asyncio.run(state.execute_cut(0,{'TileId':19,'cutIndex':13,'cutClass':True}))
    assert state.player_list[0].passed_fan==2


def test_previous_chow_singleton_restriction_counts_concealed_quad_as_hand():
    state=make_state();hand(state,0,[19]*4+[45],['s12','k22','k35'])
    assert state.kong_allowed(0,19,'concealed')
    # MIL retains concealed quads in the standing-hand count for this rule.
    hand(state,0,[19]*3+[45],['s12','k22','k35'])
    assert not state.kong_allowed(0,19,'direct')


def test_special_addition_must_preserve_declared_wait_and_cardinality():
    state,p,candidate=special_ready_for_add()
    p.ready_locked=True;p.locked_waits={19}
    assert not state.special_added_candidates(0)
    p.locked_waits={45};assert state.special_added_candidates(0)
    p.ready_locked=False;p.hand_tiles.pop(0)
    assert not state.special_added_candidates(0)


def test_event_snapshots_preserve_draw_slot_without_disclosing_other_draws():
    state=make_state();hand(state,0,READY+[28],drawn=28);hand(state,1,READY+[18],drawn=18)
    state.cc_event={'kind':'special'}
    view=state.view_state(0)
    assert view['players'][0]['has_draw_slot'] and view['players'][0]['last_drawn_tile']==28
    assert view['players'][1]['has_draw_slot'] and 'last_drawn_tile' not in view['players'][1]
    state.game_status='END'
    assert state.view_state(0)['players'][1]['last_drawn_tile']==18


def test_delayed_spectator_has_unknown_wall_and_seed_until_complete_record():
    from ..public.spectator_manager import SpectatorManager
    from ..public.game_record_manager import init_game_record,init_game_round
    state=make_state();state.master_seed=42;state.init_tiles();init_game_record(state);init_game_round(state)
    spectator=SpectatorManager(state,enabled=True);state.spectator_manager=spectator
    spectator.record_game_title();spectator.record_round_start()
    hand(state,0,READY,ready=True);state.cc_window='before_draw'
    asyncio.run(state.change_bao(initial=True))
    delayed=spectator._build_spectator_record(float('inf'));rd=delayed['game_round']['round_index_1']
    assert len(rd['tiles_list'])==83 and set(rd['tiles_list'])=={0}
    assert 'round_random_seed' not in rd
    indicator=next(t[1] for t in rd['action_ticks'] if t[0]=='cc')
    assert indicator['tile']==0 and 'dice' not in indicator
    assert any(state.game_record['game_round']['round_index_1']['tiles_list'])
    assert 'round_random_seed' not in state.spectator_round_header({'tiles_list':[11],'round_random_seed':123})
    full=spectator._build_full_record();complete=full['game_round']['round_index_1']
    assert complete['tiles_list']==state.game_record['game_round']['round_index_1']['tiles_list']
    assert next(t[1] for t in complete['action_ticks'] if t[0]=='cc')['tile']==state.cc_bao_tile
    full['game_round'].clear();assert state.game_record['game_round']
