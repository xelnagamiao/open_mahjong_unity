"""四个独立协议端驱动真实动作队列，验证完整四副、路由和落盘契约。"""
import asyncio
import json
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock,patch

import pytest
import websockets

from .test_state import make_state
from .JiujiangGameState import JiujiangGameState
from ..public.ai.get_action import get_ai_action
from ..public.ai.smart_bot_logic import count_visible_tiles,find_best_cut
from ..public.hand_slot_utils import has_draw_slot,infer_bot_cut_class
from ..gamestate_router import handle_gamestate_message


def choose(state,p,allowed):
    action=next((a for a in ['ready','hu_self','hu_first','hu_second','hu_third','buhua','riichi_cut','angang','jiagang','cut','gang','peng','chi_left','chi_mid','chi_right'] if a in allowed),'pass')
    tile=index=target=None
    if action in ('cut','riichi_cut'):
        if p.ready_locked: tile,index=p.last_drawn_tile,len(p.hand_tiles)-1
        elif action=='riichi_cut': tile=sorted(p.riichi_candidate_cuts)[0];index=p.hand_tiles.index(tile)
        else: tile,index=find_best_cut(p.hand_tiles,len(p.combination_tiles),count_visible_tiles(state),set())
    elif action in ('angang','jiagang'): target=state._kong_candidates(p.player_index,action=='jiagang')[0]
    return action,tile,index,target


async def await_window(state,p):
    for _ in range(2000):
        if p.player_index in state.waiting_players_list: return
        await asyncio.sleep(0)
    raise AssertionError('动作窗口未开放')


@pytest.mark.parametrize('seed',[1,7,13,41,1729,2026])
def test_four_clients_complete_match(seed):
    async def run():
        state=make_state();state.room_random_seed=seed;state.round_time=30;state.step_time=5
        state.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
        messages=[];tasks=[];submitted=Counter()
        async def respond(uid,allowed,tick):
            p=next(p for p in state.player_list if p.user_id==uid)
            await await_window(state,p)
            assert tick==state.server_action_tick
            action,tile,index,target=choose(state,p,allowed);submitted[action]+=1
            await get_ai_action(state,p.player_index,action,
                infer_bot_cut_class(p.hand_tiles,tile,index,draw_slot=has_draw_slot(p)) if tile else False,tile,index,target)
        class Socket:
            def __init__(self,uid): self.uid=uid
            async def send_json(self,payload):
                messages.append((self.uid,payload))
                info=payload.get('ask_hand_action_info') or payload.get('ask_other_action_info')
                if info and info.get('action_list'):
                    tasks.append(asyncio.create_task(respond(self.uid,info['action_list'],info['action_tick'])))
                elif payload['type'].endswith('/ready_status'):
                    p=next(p for p in state.player_list if p.user_id==self.uid)
                    if state.action_dict.get(p.player_index): tasks.append(asyncio.create_task(respond(self.uid,['ready'],state.server_action_tick)))
        for p in state.player_list: state.game_server.user_id_to_connection[p.user_id]=SimpleNamespace(websocket=Socket(p.user_id))
        with patch('server.gamestate.game_jiujiang.lifecycle.liuju_ready_wait_seconds',return_value=0):
            await asyncio.wait_for(state.game_loop_chinese(),90)
        await asyncio.gather(*tasks)
        assert len(state.game_record['game_round'])==4 and sum(p.score for p in state.player_list)==0
        assert submitted['buhua']>0 and submitted['cut']>0
        assert sum(p['type']=='gamestate/jiujiang/game_start' for _,p in messages)==16
        assert sum(p['type']=='gamestate/jiujiang/game_end' for _,p in messages)==4
        for rd in state.game_record['game_round'].values():
            total=Counter(rd['tiles_list'])
            for i in range(4): total.update(rd[f'p{i}_tiles'])
            assert sum(total.values())==120 and all(n==4 for n in total.values())
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()
    asyncio.run(run())


def test_creation_dispatch_and_cleanup():
    from ..test_room_lifecycle import make_server,USERS,parked_loop
    async def run():
        server=make_server(USERS);server.db_manager.get_user_settings.return_value={'username':'jiujiang-test'}
        for connection in server.players.values(): connection.current_room_id=None
        manager=server.room_manager;manager._broadcast_room_info=AsyncMock()
        result=await manager.create_Jiujiang_room(str(USERS[0]),room_name='九江测试',gameround=1)
        assert result.success
        room=next(iter(manager.rooms.values()));room['player_list']=USERS.copy();room['ready_list']=USERS.copy()
        with patch.object(JiujiangGameState,'run_game_loop',parked_loop):
            assert await server.gamestate_manager.start_game(str(USERS[0]),room['room_id']) is None
            state=server.gamestate_manager.get_game_state_by_room_id(room['room_id'])
            assert type(state) is JiujiangGameState
            await asyncio.sleep(0)
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
            assert not server.gamestate_manager.room_id_to_JiujiangGameState
    asyncio.run(run())


def test_four_actual_bots_complete_match(monkeypatch):
    async def cpu(state,fn,*args): return fn(*args)
    monkeypatch.setattr('server.gamestate.game_jiujiang.bot.run_room_bot_cpu',cpu)
    async def run():
        state=make_state();state.room_random_seed=13;state.bot_speed='instant';state.round_time=30;state.step_time=5
        for p in state.player_list: p.user_id=2
        state.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
        with patch('server.gamestate.game_jiujiang.lifecycle.liuju_ready_wait_seconds',return_value=0):
            await asyncio.wait_for(state.game_loop_chinese(),45)
        assert len(state.game_record['game_round'])==4 and sum(p.score for p in state.player_list)==0
        assert any(t[0]=='bh' for r in state.game_record['game_round'].values() for t in r['action_ticks'])
    asyncio.run(run())


def test_real_websockets_through_production_router():
    async def run():
        state=make_state();state.room_random_seed=41;state.round_time=30;state.step_time=5
        server=state.game_server;received=Counter();submitted=Counter()
        server.players={str(p.user_id):SimpleNamespace(user_id=p.user_id) for p in state.player_list}
        server.gamestate_manager.get_game_state_by_gamestate_id=lambda gid:state if gid==state.gamestate_id else None
        server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
        class Adapter:
            def __init__(self,ws): self.ws=ws
            async def send_json(self,payload): await self.ws.send(json.dumps(payload))
        async def handle(ws):
            uid=int(ws.request.path.strip('/'));adapter=Adapter(ws)
            server.user_id_to_connection[uid]=SimpleNamespace(websocket=adapter)
            async for raw in ws: await handle_gamestate_message(server,str(uid),json.loads(raw),adapter)
        async def client(uid,ws):
            async for raw in ws:
                payload=json.loads(raw);received[payload['type']]+=1
                if payload['type'].endswith('/game_end'): return
                p=next(p for p in state.player_list if p.user_id==uid)
                info=payload.get('ask_hand_action_info') or payload.get('ask_other_action_info')
                allowed=info.get('action_list',[]) if info else []
                if payload['type'].endswith('/ready_status') and state.action_dict.get(p.player_index): allowed=['ready']
                if not allowed: continue
                await await_window(state,p)
                action,tile,index,target=choose(state,p,allowed)
                request={'type':'gamestate/jiujiang/send_action','gamestate_id':state.gamestate_id,'action':action,
                         'action_tick':info['action_tick'] if info else state.server_action_tick}
                if action in ('cut','riichi_cut'):
                    request.update(type='gamestate/jiujiang/'+('riichi_cut' if action=='riichi_cut' else 'cut_tile'),TileId=tile,
                        cutIndex=index,cutClass=infer_bot_cut_class(p.hand_tiles,tile,index,draw_slot=has_draw_slot(p)))
                elif target is not None: request['targetTile']=target
                submitted[action]+=1;await ws.send(json.dumps(request))
        async with websockets.serve(handle,'127.0.0.1',0) as listener:
            port=listener.sockets[0].getsockname()[1]
            peers=[await websockets.connect(f'ws://127.0.0.1:{port}/{uid}') for uid in (101,102,103,104)]
            tasks=[asyncio.create_task(client(uid,ws)) for uid,ws in zip((101,102,103,104),peers)]
            try:
                with patch('server.gamestate.game_jiujiang.lifecycle.liuju_ready_wait_seconds',return_value=0):
                    await asyncio.wait_for(state.game_loop_chinese(),90)
                await asyncio.wait_for(asyncio.gather(*tasks),5)
                assert received['gamestate/jiujiang/game_start']==16 and received['gamestate/jiujiang/game_end']==4
                assert submitted['cut']>0 and submitted['buhua']>0
            finally:
                for task in tasks: task.cancel()
                await asyncio.gather(*tasks,return_exceptions=True)
                await asyncio.gather(*(ws.close() for ws in peers))
    asyncio.run(run())
