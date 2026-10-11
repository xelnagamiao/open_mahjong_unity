import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_flow import make_state, indicator, hand, draw, PURE, HAND
from .hints import compute_hand, describe
from ...room.wuhan_room import create_wuhan_room, handle_create_wuhan_room
from ...response import Response


def room_manager():
    player = SimpleNamespace(user_id=10000101, username='武汉测试', current_room_id=None)
    return SimpleNamespace(
        game_server=SimpleNamespace(players={'client':player}, db_manager=SimpleNamespace(get_user_settings=lambda uid:{'username':'武汉测试'})),
        _reject_room_entry_conflicts=lambda uid, action:None, _normalize_event_id=lambda value:value,
        _validate_event_for_room=lambda event, uid:None, _generate_room_id=lambda:880199,
        _apply_event_fields=lambda room,event:room.update(event_id=event),
        _broadcast_room_info=AsyncMock(), rooms={}, room_passwords={})


@pytest.mark.parametrize('failure',['missing','anonymous','already_joined','entry_conflict','event_conflict','invalid','settings'])
def test_room_create_rejection_is_atomic(failure):
    manager=room_manager(); options={}
    denied=Response(type='tips',success=False,message='已有进行中的对局')
    if failure=='missing':manager.game_server.players.clear()
    elif failure=='anonymous':manager.game_server.players['client'].user_id=0
    elif failure=='already_joined':manager.game_server.players['client'].current_room_id=880100
    elif failure=='entry_conflict':manager._reject_room_entry_conflicts=lambda *args:denied
    elif failure=='event_conflict':manager._validate_event_for_room=lambda *args:denied
    elif failure=='invalid':options['gameround']=0
    elif failure=='settings':manager.game_server.db_manager.get_user_settings=lambda uid:None
    response=asyncio.run(create_wuhan_room(manager,'client',room_name='武汉测试',**options))
    assert not response.success and not manager.rooms and not manager.room_passwords
    manager._broadcast_room_info.assert_not_awaited()


@pytest.mark.parametrize('password',['','local-test'])
def test_create_message_persists_wuhan_contract(password):
    manager=room_manager();ws=SimpleNamespace(send_json=AsyncMock())
    message=dict(roomname='武汉测试',password=password,gameround=4,tips=False,count_tips=True,pointer_tips=False,
        tourist_limit=True,allow_spectator=False,random_seed=format(2025,'064x'),event_id=5)
    asyncio.run(handle_create_wuhan_room(SimpleNamespace(room_manager=manager),'client',message,ws))
    response=ws.send_json.call_args.args[0]
    assert response['success'] and response['type']=='room/create_room_done'
    room=manager.rooms[880199]
    assert room['room_rule']=='wuhan' and room['sub_rule']=='wuhan/mil2025' and room['hepai_limit']==6
    assert room['detailed_config']=={'rule_version':'mil2025'} and room['game_round']==4
    assert not room['tips'] and room['count_tips'] and not room['pointer_tips']
    assert room['tourist_limit'] and not room['allow_spectator'] and room['event_id']==5
    assert room['has_password']==bool(password) and (880199 in manager.room_passwords)==bool(password)
    assert room['is_player_set_random_seed']
    assert manager.game_server.players['client'].current_room_id==880199
    manager._broadcast_room_info.assert_awaited_once_with(880199)


def test_hint_preindicator_closed_sea_and_malformed_hand():
    s=make_state();indicator(s);p=hand(s,0,PURE,['s12'])
    assert not compute_hand(p.hand_tiles,p.combination_tiles,None,s.table_snapshot(),0)['waiting_tiles']
    assert not compute_hand([11],[],38,s.table_snapshot(),0)['waiting_tiles']
    assert describe(HAND,[],38,s.table_snapshot(),0,25)['label']=='未满足'
    assert describe(HAND,['s26'],38,s.table_snapshot(),0,17)['label']=='未满足'
    assert describe(PURE,['s12'],38,s.table_snapshot(),0,15,sea=True)['label']=='仅自摸'


def test_hint_point_range_and_discard_preview_use_bilateral_payments():
    s=make_state();indicator(s);hand(s,0,PURE,['s12'])
    for i in range(1,4):s.player_list[i].huapai_list=[45]*i
    row=describe(PURE,['s12'],38,s.table_snapshot(),0,15)
    assert row=={'kind':'ron','label':'80～90点','points':80}
    preview=compute_hand(PURE+[25],['s12'],38,s.table_snapshot(),0)
    assert preview['waiting_details_by_discard'][25][15]==row


def test_claim_collector_wakes_on_revision_then_closes_without_charging_bank():
    async def run():
        s=make_state();indicator(s);s.game_status='waiting_action_after_cut'
        s.claim_response_seconds=.08;s.action_dict={0:[],1:['peng','pass'],2:[],3:[]}
        s.prepare_action_window();s.on_action_window_broadcast();s.on_action_window_delivered(1)
        task=asyncio.create_task(s.collect_action_responses())
        await asyncio.sleep(.01);s.action_queues[1].put_nowait({'action_type':'peng'});s.action_events[1].set()
        await asyncio.sleep(.01);s.action_queues[1].put_nowait({'action_type':'pass'});s.action_events[1].set()
        response,_=await asyncio.wait_for(task,1)
        assert response=={1:{'action_type':'pass'}} and s.player_list[1].remaining_time==20
        assert not s.waiting_players_list
    asyncio.run(run())


def test_cancelled_claim_collector_releases_waiters():
    async def run():
        s=make_state();s.game_status='waiting_action_qianggang';s.action_dict={0:[],1:['pass'],2:[],3:[]}
        s.prepare_action_window();s.on_action_window_broadcast();s.on_action_window_delivered(1)
        task=asyncio.create_task(s.collect_action_responses())
        await asyncio.sleep(.01);task.cancel()
        with pytest.raises(asyncio.CancelledError):await task
        assert not s.waiting_players_list
    asyncio.run(run())


@pytest.mark.parametrize('action',['hu_self','pass'])
def test_real_sea_action_window_resolves_win_or_pass(action):
    async def run():
        s=make_state();indicator(s);p=hand(s,0,PURE,['s12']);draw(p,15)
        s.sea_phase=True;s.game_status='waiting_hand_action';s.action_dict=s.check_hand_actions(0)
        s.prepare_action_window();s.on_action_window_broadcast();s.on_action_window_delivered(0)
        s.action_queues[0].put_nowait({'action_type':action})
        assert await asyncio.wait_for(s.wait_action(),1)
        assert s.game_status==('END' if action=='hu_self' else 'deal_card')
        assert bool(s.pending_winners)==(action=='hu_self')
        if action=='pass':
            before=list(p.hand_tiles);await s.execute_timeout_cut(0)
            assert p.hand_tiles==before and s.game_status=='deal_card'
    asyncio.run(run())


def test_added_kong_robbery_uses_original_meld_fan_and_liability():
    async def run():
        s=make_state();indicator(s)
        actor=hand(s,0,[13],['k13'])
        winner=hand(s,1,[11,12,21,22,23,31,32,33,25,25],['s26'])
        for p in s.player_list[1:]:p.huapai_list=[45]
        actor.huapai_list=[45]
        await s.execute_jiagang(0,13)
        assert s.game_status=='waiting_action_qianggang' and s._pending_jiagang
        assert s.table_snapshot()[0]['melds']==['k13']
        actions=s.check_added_kong_actions(13);win=next(a for a in actions[1] if a.startswith('hu_'))
        detail=s.result_dict[win]
        assert detail['liabilities']==[0] and detail['changes'][0]==-detail['total']
        assert detail['fan_by_player'][0]==9
        await s.resolve_rob_kong_responses({1:{'action_type':win}},actions)
        assert actor.combination_tiles==['k13'] and not s._pending_jiagang and s.pending_winners[0]['index']==1
    asyncio.run(run())


def test_cut_guards_and_public_push_gold_liability():
    async def run():
        s=make_state();indicator(s);p=hand(s,0,HAND+[45],['s26'])
        before=list(p.hand_tiles)
        await s.execute_cut(1,{'TileId':11});await s.execute_cut(0,{'TileId':11},declare_ready=True)
        await s.execute_cut(0,{'TileId':99});assert p.hand_tiles==before
        hand(s,1,[],['g14','g24','g34']);s.player_list[1].huapai_list=[39]
        assert s.push_gold_active()
        await s.execute_cut(0,{'TileId':11,'CutIndex':0,'IsMoCut':False})
        assert s.push_liability=={0} and p.discard_tiles==[11]
        s.tiles_list=s.tiles_list[:13];before=list(p.hand_tiles)
        await s.execute_cut(0,{'TileId':45});assert p.hand_tiles==before
        await s._special_kong(0,47,draw=True);assert p.hand_tiles==before
        await s.finalize_jiagang();await s.execute_claim(2,'peng')
        assert not s.player_list[2].combination_tiles
        await s._reveal_indicator();assert s.indicator==38
    asyncio.run(run())


def test_private_worker_cannot_publish_a_result_for_an_old_table():
    async def run():
        s=make_state(tips=True);indicator(s);hand(s,0,PURE,['s12'])
        async def changed_table(*args):
            s.player_list[1].huapai_list.append(45)
            return {'source_hand_tiles':list(PURE),'source_melds':['s12']}
        with patch('server.gamestate.game_wuhan.WuhanGameState.run_room_bot_cpu',side_effect=changed_table):
            await s.prepare_private_fields([0])
        assert 'wuhan_hints' not in s.build_private_game_info_fields(0)
        await s.prepare_private_fields([0]);assert 'wuhan_hints' in s.build_private_game_info_fields(0)
    asyncio.run(run())


def test_own_action_budget_and_timeout_use_the_normal_cut_path():
    from ..public.ai.get_action import get_ai_action
    from ..public.hand_slot_utils import infer_bot_cut_class
    async def run():
        for timeout in (False,True):
            s=make_state();indicator(s);p=hand(s,0,HAND,['s26']);draw(p,15)
            s.game_status='waiting_hand_action';s.action_dict=s.check_hand_actions(0)
            s.prepare_action_window();s.on_action_window_broadcast();s.on_action_window_delivered(0)
            assert s.claim_clock(p)[0]==20
            if timeout:await s.execute_timeout_cut(0)
            else:
                cut_class=infer_bot_cut_class(p.hand_tiles,15,10,draw_slot=p.has_draw_slot)
                await get_ai_action(s,0,'cut',cut_class,15,10,None)
                assert await asyncio.wait_for(s.wait_action(),1)
            assert p.discard_tiles==[15] and p.hand_tiles==HAND
    asyncio.run(run())


def test_invalid_room_and_stopped_player_cannot_act():
    with pytest.raises(ValueError):make_state(sub_rule='wuhan/unknown')
    s=make_state();indicator(s);p=hand(s,1,PURE,['s12']);p.tag_list.append('peida')
    assert not s.kong_allowed(1,45,'concealed') and not s.check_discard_actions(15)[1]
    p.tag_list.clear();assert s.score_candidate(1,'self_draw',15) is None
    hand(s,1,[11],['s12']);assert s.score_candidate(1,'discard',13) is None


def test_disabled_live_hints_remain_available_to_replay_without_leaking():
    async def run():
        s=make_state(tips=False,count_tips=False);indicator(s);hand(s,0,PURE,['s12'])
        await s.prepare_private_fields([0])
        assert 'wuhan_hints' not in s.build_private_game_info_fields(0)
        ticks=s.game_record['game_round']['round_index_1']['action_ticks']
        recorded=[tick[3] for tick in ticks if tick[:3]==['wuhan','hints',0]]
        assert recorded and recorded[-1]['waiting_details'][15]['label']=='仅自摸'
        s.count_tips=True
        assert s.build_private_game_info_fields(0)['wuhan_hints']==recorded[-1]
    asyncio.run(run())
