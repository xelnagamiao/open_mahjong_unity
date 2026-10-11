import asyncio
from types import SimpleNamespace
import pytest

from .test_state import state,NORMAL
from .NanchangGameState import NanchangGameState
from .get_action import handle_action
from .state_machine import Phase as P
from ...response import Response
from ...database.shared_record_metrics import analyze_shared_record_for_player


class Socket:
    def __init__(self):self.sent=[]
    async def send_json(self,payload):self.sent.append(payload)


@pytest.mark.parametrize('case',['wrong_game','wrong_rule','unknown_connection','foreign_user','stale','bool_tick','invalid_cut','valid_cut','paused'])
def test_socket_authorizes_identity_tick_and_action_before_mutation(case):
    async def check():
        s=state();s.open_action_window(s.begin_turn(0));socket=Socket();p=s.player_list[0]
        connection=SimpleNamespace(user_id=9999 if case=='foreign_user' else p.user_id,websocket=socket)
        server=SimpleNamespace(players={'test':connection},gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda gid:s if gid==s.gamestate_id else None))
        if case=='wrong_rule':s.room_rule='wenzhou'
        if case=='paused':s.vote_manager=SimpleNamespace(phase='paused')
        message=dict(type='gamestate/nanchang/cut_tile',gamestate_id='missing' if case=='wrong_game' else s.gamestate_id,
                     action_tick=True if case=='bool_tick' else s.server_action_tick-1 if case=='stale' else s.server_action_tick,
                     TileId=51 if case=='invalid_cut' else p.hand_tiles[-1],cutClass=True,cutIndex=len(p.hand_tiles)-1,player_index=3)
        before=list(p.hand_tiles)
        await handle_action(server,'unknown' if case=='unknown_connection' else 'test',message,socket)
        assert p.hand_tiles==before
        assert (not s.action_queues[0].empty())==(case=='valid_cut')
        assert s.action_queues[3].empty()
        if case in ('stale','bool_tick'):
            assert socket.sent[-1]['action_tick']==s.server_action_tick
        elif case in ('invalid_cut','paused'):
            assert socket.sent[-1]['type']=='tips' and not socket.sent[-1]['success']
        else:assert not socket.sent
    asyncio.run(check())


def test_reconnect_spectator_and_pydantic_envelopes_preserve_private_fields():
    async def check():
        s=state();s.tips=True;s.count_tips=True;socket=Socket();p=s.player_list[0]
        connection=SimpleNamespace(user_id=p.user_id,websocket=socket)
        observer=Socket()
        s.game_server=SimpleNamespace(user_id_to_connection={p.user_id:connection,900:SimpleNamespace(websocket=observer)})
        s.open_action_window(s.begin_turn(0));p.tag_list.append('offline')
        await s.player_reconnect(p.user_id)
        assert 'offline' not in p.tag_list
        assert socket.sent[0]['game_info']['nanchang_info']['indicator']==45
        assert socket.sent[1]['ask_hand_action_info']['action_tick']==s.server_action_tick
        for raw in socket.sent:
            model=Response.model_validate(raw).model_dump(exclude_none=True)
            assert model['game_info']['nanchang_info']==raw['game_info']['nanchang_info']
            assert model['game_info']['nanchang_hints']==raw['game_info']['nanchang_hints']
        await s.send_realtime_spectator_snapshot(900,0)
        assert len(observer.sent)==2 and observer.sent[0]['game_info']['self_hand_tiles']==p.hand_tiles
        assert observer.sent[0]['game_info']['players_info'][1]['hand_tiles'] is None
        s.current_player_index=0;p.hand_tiles=NORMAL.copy();p.has_draw_slot=True
        s.settle_win(0,'self_draw',41)
        socket.sent.clear();await s.player_reconnect(p.user_id)
        assert socket.sent[-1]['show_result_info']['nanchang_end_hands'][0]==NORMAL
        assert socket.sent[-1]['show_result_info']['score_changes']=={i:s.round_changes[i] for i in range(4)}
    asyncio.run(check())


@pytest.mark.parametrize('seed',[811,812])
def test_complete_async_bot_games_have_four_scored_rounds(monkeypatch,seed):
    from . import bot
    from ..public.ai import pacing
    async def immediate(*a,**kw):return None
    monkeypatch.setattr(bot,'wait_bot_delay',immediate)
    monkeypatch.setattr(pacing,'bot_delay',lambda *a,**kw:0)
    async def play():
        data={**NanchangGameState._default_room_data(),'player_list':[1,2,3,4],'random_seed':seed,'game_round':1}
        s=NanchangGameState(room_data=data,calculation_service=object())
        await asyncio.wait_for(s.run_game_loop(),30)
        assert s.machine.phase==P.FINISHED and len(s.game_record['game_round'])==4
        assert len(s.player_list[0].score_history)==4
        assert sum(p.score for p in s.player_list)==0
        assert all(r['action_ticks'][-1][0]=='end' for r in s.game_record['game_round'].values())
        for p in s.player_list:
            metrics=analyze_shared_record_for_player(s.game_record,p.original_player_index)
            assert metrics['total_rounds']==4 and metrics['total_round_score']==p.score
    asyncio.run(play())
