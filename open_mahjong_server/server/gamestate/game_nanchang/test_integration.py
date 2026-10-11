import asyncio
from copy import deepcopy
from types import SimpleNamespace
import pytest

from .test_state import state,cut,NORMAL
from .NanchangGameState import NanchangGameState
from .state_machine import Phase as P
from .hints import private_hints,hand_key
from .bot import choose_action
from .timing import ActionClock
from ...database.shared_record_metrics import analyze_shared_record_for_player


@pytest.mark.parametrize('context',['heaven','earth','kong'])
def test_future_waits_do_not_borrow_special_current_event(context):
    s=state();s.tips=True;index=0 if context=='heaven' else 1
    p=s.player_list[index];p.hand_tiles=NORMAL.copy();p.has_draw_slot=True
    if context=='heaven':s.domain_events=[]
    if context=='earth':s.discard_log=[(0,41)]
    if context=='kong':p.after_kong=True
    expected=s.score_win(index,'self_draw',41,private=True,preview=True)
    hints=private_hints(s,index)[hand_key(NORMAL[:-1])]
    row=next(r for r in hints if r['tile']==41)
    assert row['self_draw_points']==expected['points']
    actual=s.score_win(index,'self_draw',41,private=True)
    if context=='heaven':assert actual['fixed']==20 and expected['fixed']==0
    if context=='kong':assert '杠开' in actual['fan_names'] and '杠开' not in expected['fan_names']
    if context=='earth':
        assert s.score_win(index,'discard',41,payer=0,hand=NORMAL[:-1],private=True)['fixed']==20
        assert row['ron_points'][0]!=60


def test_delivery_clock_and_timeout_predraw():
    c=ActionClock(2.4,1)
    assert c.deadline is None
    c.start(100);c.start(200)
    assert c.deadline==103.4 and c.remaining_bank(101.5)==pytest.approx(1.9)
    s=state();p=s.player_list[0];p.hand_tiles=NORMAL[:12]+[45]
    w=s.open_action_window(s.before_kong_draw(0))
    assert w.get('predraw')=='kong'
    response=s._build_timeout_action(0)
    assert response['action_type']=='nc_draw'
    # The published window remains the immutable validation source after timeout clears live offers.
    result=s.apply_action_results(w,{0:response})
    assert result['status']==P.TURN.value and p.after_kong
    p.hand_tiles=NORMAL[:12]+[45,46];p.declared_jingdiao=True;p.has_draw_slot=True
    w=s.open_action_window(s.begin_turn(0))
    assert 'hu_self' in w['actions'][0] and 'cut' not in w['actions'][0]
    response=s._build_timeout_action(0)
    assert response['action_type']=='hu_self'
    s.apply_action_results(w,{0:response})
    assert s.machine.phase==P.END


def test_queue_auth_and_late_response_never_mutate_hand():
    async def check():
        s=state();s.step_time=1;s.player_list[0].remaining_time=2
        w=s.open_action_window(s.begin_turn(0));s._timing_now=lambda:10.0
        s._start_action_clock(0,{'action_tick':s.server_action_tick})
        p=s.player_list[0];before=list(p.hand_tiles)
        tile=p.hand_tiles[-1]
        s._timing_now=lambda:13.0
        with pytest.raises(ValueError,match='耗尽'):
            await s.submit_action(0,'cut',TileId=tile,cutClass=True)
        assert p.hand_tiles==before and s.action_queues[0].empty()
        s._timing_now=lambda:12.99
        await s.submit_action(0,'cut',TileId=tile,cutClass=True)
        with pytest.raises(ValueError,match='重复'):
            await s.submit_action(0,'cut',TileId=tile,cutClass=True)
        responses=await s.wait_action(3)
        assert responses[0]['_receipt_time']==12.99
        s.apply_action_results(w,responses)
        assert len(p.hand_tiles)==len(before)-1
    asyncio.run(check())


def test_registered_room_profiles_and_all_live_room_indexes():
    from ...room.nanchang_room import NanchangRoomValidator
    from ..gamestate_manager import GameStateManager
    manager=GameStateManager(SimpleNamespace())
    for rule in ('nanchang','nanjing','wuhan','shenyang','changchun','free','jiujiang'):
        assert rule in manager._rule_room_indexes()
    assert len({id(v) for v in manager._rule_room_indexes().values()})==len(manager._rule_room_indexes())
    v=NanchangRoomValidator(room_name='南昌四人测试')
    assert v.sub_rule=='nanchang/mil2026' and v.detailed_config=={'rule_version':'mil-nanchang-2026'}
    for bad in ({'sub_rule':'nanchang/doubles'},{'use_flowers':True},{'detailed_config':{'allow_2v2':True}}):
        with pytest.raises(ValueError):NanchangRoomValidator(room_name='南昌四人测试',**bad)


def test_actual_three_round_records_include_jing_kong_and_rotated_seats():
    s=state();s.start_game_recording()
    winners=[]
    for number in range(3):
        if number:
            s.machine.transition(P.READY);s.advance_round_after_ready();s.initialize_round();s.domain_events=[{'action':'test'}]
        s.start_round_recording()
        starts={p.original_player_index:p.score for p in s.player_list}
        if number==0:
            p=s.player_list[2];p.hand_tiles=NORMAL.copy();p.has_draw_slot=True
            s.current_player_index=2
            s.settle_win(2,'self_draw',41)
        elif number==1:
            # Real kong transition, followed by an exhausted wall.
            s.machine.transition(P.TURN);p=s.player_list[1]
            p.hand_tiles=[21]*4+[11,12,13,31,32,33,41,41,45,45];p.has_draw_slot=True
            s.commit_kong(1,21,'angang');s.tiles_list=[];s.draw_for(1)
        else:
            s.machine.transition(P.TURN)
            s.player_list[1].hand_tiles=NORMAL[:-1];s.player_list[1].has_draw_slot=False
            s.player_list[0].hand_tiles=NORMAL.copy();s.player_list[0].has_draw_slot=True
            s.discard_log=[(3,11)]
            w=cut(s,0,41)
            responses={i:{'action_type':'hu' if i==1 else 'pass'} for i,a in w['actions'].items() if a}
            s.apply_action_results(w,responses)
        if s.deferred_hu_settlements:
            h=s.deferred_hu_settlements[-1]
            winners.append((s.player_list[h['winner']].original_player_index,h['source']))
        s.finalize_round_recording()
        rd=s.game_record['game_round'][f'round_index_{s.round_index}']
        one={'game_title':s.game_record['game_title'],'game_round':{'one':rd}}
        for p in s.player_list:
            metrics=analyze_shared_record_for_player(one,p.original_player_index)
            assert metrics['total_rounds']==1
            assert metrics['total_round_score']==p.score-starts[p.original_player_index]
    for p in s.player_list:
        metrics=analyze_shared_record_for_player(s.game_record,p.original_player_index)
        assert metrics['total_rounds']==3
        assert metrics['total_round_score']==p.score
        assert metrics['win_count']==sum(w==p.original_player_index for w,_ in winners)
        assert metrics['self_draw_count']==sum(w==p.original_player_index and source=='self_draw' for w,source in winners)
    assert sum(analyze_shared_record_for_player(s.game_record,i)['deal_in_count'] for i in range(4))==1
