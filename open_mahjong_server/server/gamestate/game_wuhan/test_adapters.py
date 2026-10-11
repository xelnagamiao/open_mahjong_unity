import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock,patch

import pytest

from .test_flow import make_state,indicator,hand,draw,HAND,PURE
from . import bot
from ...room.wuhan_room import WuhanRoomValidator
from ..public.ai.get_action import get_ai_action,get_action
from ..game_taiwan.boardcast import broadcast_game_start,send_realtime_spectator_snapshot,_build_do_action_payload


@pytest.mark.parametrize('kwargs',[{'sub_rule':'wuhan/standard'},{'use_flowers':True},{'open_cuohe':True},
    {'tactical_call':True},{'claim_protection':True},{'tian_di_ren_he':True},{'tips':'false'},
    {'detailed_config':{'rule_version':'mil2024'}},{'detailed_config':{'fan_cap':10}}])
def test_room_rejects_foreign_options(kwargs):
    with pytest.raises(ValueError):WuhanRoomValidator(room_name='武汉测试',game_round=1,round_timer=20,step_timer=5,**kwargs)


def test_room_defaults():
    d=WuhanRoomValidator(room_name='武汉测试',game_round=1,round_timer=20,step_timer=5).model_dump()
    assert d['sub_rule']=='wuhan/mil2025' and d['detailed_config']=={'rule_version':'mil2025'}
    assert not d['use_flowers'] and not d['claim_protection']


@pytest.mark.parametrize('special',[37,38,39,45])
def test_public_submission_path_accepts_single_kong(special):
    async def scenario():
        s=make_state();indicator(s);hand(s,0,HAND+[special],['s26']);s.game_status='waiting_hand_action'
        s.action_dict=s.check_hand_actions(0);s.waiting_players_list=[0]
        await get_ai_action(s,0,'angang',None,None,None,special)
        assert s.action_queues[0].get_nowait()['target_tile']==special
        await get_ai_action(s,0,'angang',None,None,None,25)
        assert s.action_queues[0].empty()
    asyncio.run(scenario())


@pytest.mark.parametrize('actions,status,expected',[(['hu_self','cut'],'waiting_hand_action','hu_self'),
    (['peng','pass'],'waiting_action_after_cut','peng'),(['pass'],'waiting_hand_action','pass'),
    (['angang','cut'],'waiting_hand_action','angang'),(['cut'],'waiting_hand_action','cut')])
def test_bot_submits_real_queue(actions,status,expected):
    async def scenario():
        s=make_state();indicator(s);p=hand(s,0,HAND+[45],['s26']);p.user_id=0
        s.game_status=status;s.action_dict={0:actions,1:[],2:[],3:[]};s.waiting_players_list=[0];s.bot_speed='instant'
        await bot.wuhan_bot_action(s,0,actions,status)
        assert s.action_queues[0].get_nowait()['action_type']==expected
    asyncio.run(scenario())


def conn():return SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))


def test_network_privacy_reconnect_and_terminal_idempotence():
    async def scenario():
        s=make_state(tips=True,allow_spectator=True);indicator(s)
        for i in range(4):
            hand(s,i,HAND,['s26']);s.game_server.user_id_to_connection[101+i]=conn()
        s.player_list[0].combination_tiles=['G26'];s.player_list[0].combination_mask=[[2,26]*4]
        s.game_server.user_id_to_connection[999]=conn();s.send_to_realtime_spectators=AsyncMock()
        await broadcast_game_start(s)
        for i in range(4):
            info=s.game_server.user_id_to_connection[101+i].websocket.send_json.call_args.args[0]['game_info']
            assert [p['player_index'] for p in info['players_info'] if 'hand_tiles' in p]==[i]
            assert info['wuhan_hints']['source_hand_tiles']==s.player_list[i].hand_tiles
            if i:assert info['players_info'][0]['combination_mask']==[[2,0]*4]
        await send_realtime_spectator_snapshot(s,999,2)
        info=s.game_server.user_id_to_connection[999].websocket.send_json.call_args.args[0]['game_info']
        assert [p['player_index'] for p in info['players_info'] if 'hand_tiles' in p]==[2]
        assert _build_do_action_payload(s,viewer_index=2,action_list=['deal_tile'],action_player=0,deal_tile=39).get('deal_tile') in (0,None)
        await s._broadcast_result(hu_class='liuju',score_changes={i:0 for i in range(4)})
        old=deepcopy(s._terminal_result)
        s.player_list[1].tag_list.append('offline');await s.player_reconnect(102)
        assert 'offline' not in s.player_list[1].tag_list
        assert s.game_server.user_id_to_connection[102].websocket.send_json.call_args.args[0]==old
    asyncio.run(scenario())


def test_terminal_send_failure_does_not_skip_other_peers():
    s=make_state();s.game_server.user_id_to_connection={101+i:conn() for i in range(4)}
    s.game_server.user_id_to_connection[101].websocket.send_json.side_effect=OSError('disconnected')
    s.send_to_realtime_spectators=AsyncMock(side_effect=[OSError('gone'),None,None,None])
    asyncio.run(s._broadcast_result(hu_class='liuju'))
    assert s.game_server.user_id_to_connection[104].websocket.send_json.await_count==1
    assert s._terminal_result['show_result_info']['hu_class']=='liuju'


@pytest.mark.parametrize('rotation',range(4))
def test_bonus_then_draw_statistics_rotation(rotation):
    from ...database.shared_record_metrics import analyze_shared_record_for_player
    from ..public.game_record_manager import init_game_round
    s=make_state();indicator(s);s.current_round=4
    for i,p in enumerate(s.player_list):p.original_player_index=(i+rotation)%4
    init_game_round(s);s.player_list[0].huapai_list=[37,37,37,38,38,38,45]
    asyncio.run(s._check_bonus(0))
    with patch('server.gamestate.game_wuhan.WuhanGameState.asyncio.sleep',new=AsyncMock()):
        asyncio.run(s._settle_hand({i:0 for i in range(4)}))
    for p in s.player_list:
        stats=analyze_shared_record_for_player(s.game_record,p.original_player_index)
        assert stats['total_round_score']==p.score and stats['win_count']==0 and stats['deal_in_count']==0


@pytest.mark.parametrize('seed',[7,26,2025])
def test_full_match_rotations_and_record_metrics(seed):
    from ...database.shared_record_metrics import analyze_shared_record_for_player
    from ..public.hand_slot_utils import infer_bot_cut_class
    s=make_state(random_seed=seed)
    stored=[]
    s.db_manager=SimpleNamespace(store_wuhan_game_record=lambda *args:stored.append(args) or None)
    s.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
    s.run_hu_result_ready_phase=AsyncMock()
    steps=0
    async def auto():
        nonlocal steps
        steps+=1;assert steps<1500
        i=s.current_player_index;p=s.player_list[i]
        if s.game_status=='waiting_hand_action':
            a=s.action_dict[i]
            if 'hu_self' in a:s.accept_self_draw(i)
            elif s.sea_phase:s.game_status='deal_card'
            elif 'angang' in a:await s.execute_angang(i,next(t for t in p.hand_tiles if s.kong_allowed(i,t,'concealed')))
            elif 'jiagang' in a:await s.execute_jiagang(i,next(t for t in p.hand_tiles if s.kong_allowed(i,t,'added')))
            else:
                from collections import Counter
                c=Counter(p.hand_tiles)
                tile=min(p.hand_tiles,key=lambda t:100 if t==s.joker else -100 if t in s.skins else 4*c[t]+sum((3-abs(d))*c[t+d] for d in (-2,-1,1,2) if t<40 and (t+d)//10==t//10))
                position=p.hand_tiles.index(tile)
                await s.execute_cut(i,dict(TileId=tile,cutIndex=position,cutClass=infer_bot_cut_class(p.hand_tiles,tile,position,draw_slot=p.has_draw_slot)))
        else:
            response={j:{'action_type':next((x for x in ('hu_first','hu_second','hu_third','gang','peng','chi_left','chi_mid','chi_right') if x in a),'pass')} for j,a in s.action_dict.items() if a}
            if s.game_status=='waiting_action_after_cut':await s.resolve_discard_responses(response,s.action_dict)
            else:await s.resolve_rob_kong_responses(response,s.action_dict)
        return True
    s.wait_action=auto
    with patch('server.gamestate.game_wuhan.WuhanGameState.asyncio.sleep',new=AsyncMock()):asyncio.run(s.game_loop_chinese())
    assert len(stored)==1 and s.current_round==4
    for p in s.player_list:
        metrics=analyze_shared_record_for_player(s.game_record,p.original_player_index)
        assert metrics['total_rounds']==4 and metrics['total_round_score']==p.score
        assert metrics['win_count']==p.record_counter.zimo_times+p.record_counter.dianhe_times
        assert metrics['deal_in_count']==p.record_counter.fangchong_times
        assert len(p.score_history)==4
    assert sum(p.score for p in s.player_list)==0
