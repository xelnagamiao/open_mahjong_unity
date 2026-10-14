import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock
import pytest
from .test_state import make_state,drawn
from ...game_calculation.singapore.test_rules import PLAIN
from ..gamestate_router import handle_gamestate_message


@pytest.mark.parametrize('case',['valid','wrong_game','wrong_rule','unknown_connection','foreign_user','wrong_player','stale','bool_tick','missing_tick','paused','unoffered_hu'])
def test_real_router_identity_window_and_action_guards(case):
    async def run():
        state=make_state();drawn(state.player_list[0],PLAIN)
        state.server_action_tick=7;state.current_player_index=0;state.game_status='waiting_hand_action'
        state.waiting_players_list=[0];state.action_dict={0:['cut'],1:[],2:[],3:[]}
        socket=SimpleNamespace(send_json=AsyncMock())
        uid=999 if case=='foreign_user' else 102 if case=='wrong_player' else 101
        state.game_server.players={'user':SimpleNamespace(user_id=uid,websocket=socket)}
        state.game_server.gamestate_manager.get_game_state_by_gamestate_id=lambda gid:state if gid==state.gamestate_id else None
        if case=='wrong_rule':state.room_rule='taiwan'
        if case=='paused':state.vote_manager=SimpleNamespace(phase='paused')
        message={'type':'gamestate/singapore/cut_tile','gamestate_id':state.gamestate_id,'action_tick':7,
                 'TileId':PLAIN[-1],'cutClass':True,'cutIndex':13,'player_index':3}
        if case=='wrong_game':message['gamestate_id']='missing'
        if case=='stale':message['action_tick']=6
        if case=='bool_tick':message['action_tick']=True
        if case=='missing_tick':message.pop('action_tick')
        if case=='unoffered_hu':message.update(type='gamestate/singapore/send_action',action='hu_self')
        await handle_gamestate_message(state.game_server,'missing' if case=='unknown_connection' else 'user',message,socket)
        assert (not state.action_queues[0].empty())==(case=='valid')
        assert all(state.action_queues[i].empty() for i in (1,2,3))
        assert state.player_list[0].hand_tiles==list(PLAIN)
    asyncio.run(run())
