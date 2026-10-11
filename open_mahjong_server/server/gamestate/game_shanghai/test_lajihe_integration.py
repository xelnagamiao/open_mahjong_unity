"""四个协议客户端驱动真实动作队列，完成随机整场及建房/重连验证。"""
import asyncio
import json
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest
import websockets

from .test_lajihe_state import make_state, ticks
from .LajiheGameState import LajiheGameState
from ..public.ai.get_action import get_ai_action
from ..public.ai.smart_bot_logic import count_visible_tiles, find_best_cut
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class
from ..game_taiwan.boardcast import send_reconnect_game_state, reconnected_send_pending_ask_for_viewer, broadcast_do_action
from ..gamestate_router import handle_gamestate_message


@pytest.mark.parametrize("seed", [1,7,13,41,47,144,1729,2024,2026,31337,65535,8675309])
def test_four_protocol_clients_complete_entire_match(seed):
    async def run():
        state=make_state();state.room_random_seed=seed;state.round_time=30;state.step_time=5
        state.game_server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
        messages=[];tasks=[];submitted=Counter()
        async def respond(uid,allowed,tick):
            for _ in range(1000):
                p=next(p for p in state.player_list if p.user_id==uid)
                if p.player_index in state.waiting_players_list: break
                await asyncio.sleep(0)
            else: raise AssertionError("响应窗口未开放")
            assert tick==state.server_action_tick
            tile=index=target=None
            action=next((a for a in ["ready","buhua","lajihe_flower_continue","hu_self","hu_first","hu_second","hu_third"] if a in allowed),None)
            if action is None and "angang" in allowed:
                target=next(t for t,n in Counter(p.hand_tiles).items() if n==4);action="angang"
            if action is None and "jiagang" in allowed:
                target=next(t for t in p.hand_tiles if f"k{t}" in p.combination_tiles);action="jiagang"
            if action is None and "cut" in allowed:
                tile,index=find_best_cut(p.hand_tiles,len(p.combination_tiles),count_visible_tiles(state),p.kuikae_forbidden_tiles)
                action="cut"
            if action is None:
                action=next((a for a in ["gang","peng","chi_left","chi_mid","chi_right"] if a in allowed),"pass")
            submitted[action]+=1
            await get_ai_action(state,p.player_index,action,
                infer_bot_cut_class(p.hand_tiles,tile,index,draw_slot=has_draw_slot(p)) if tile else False,
                tile,index,target)
        class Socket:
            def __init__(self,uid): self.uid=uid
            async def send_json(self,payload):
                messages.append((self.uid,payload))
                info=payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
                if info and info.get("action_list"):
                    tasks.append(asyncio.create_task(respond(self.uid,info["action_list"],info["action_tick"])))
                elif payload["type"].endswith("/ready_status"):
                    p=next(p for p in state.player_list if p.user_id==self.uid)
                    if state.action_dict.get(p.player_index):
                        tasks.append(asyncio.create_task(respond(self.uid,["ready"],state.server_action_tick)))
        for p in state.player_list:
            state.game_server.user_id_to_connection[p.user_id]=SimpleNamespace(websocket=Socket(p.user_id))
        with patch("server.gamestate.game_shanghai.QinghunpengGameState.liuju_ready_wait_seconds",return_value=0):
            await asyncio.wait_for(state.game_loop_chinese(),90)
        await asyncio.gather(*tasks)
        rounds=state.game_record["game_round"]
        assert len(rounds)==4 and state.huangfan_count==0
        assert sum(p.score for p in state.player_list)==0
        assert submitted["buhua"]>0 and submitted["cut"]>0 and submitted["riichi_cut"]==0
        assert all(p.round_number_history for p in state.player_list)
        starts=[p for _,p in messages if p["type"]=="gamestate/shanghai/game_start"]
        assert len(starts)==16 and all(p["game_info"]["sub_rule"]=="shanghai/lajihe" for p in starts)
        assert len([p for _,p in messages if p["type"]=="gamestate/shanghai/game_end"])==4
        for data in rounds.values():
            total=Counter(data["tiles_list"])
            for i in range(4): total.update(data[f"p{i}_tiles"])
            assert sum(total.values())==144
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()
    asyncio.run(run())


def test_creation_dispatch_and_lifecycle():
    from ..test_room_lifecycle import make_server, USERS, parked_loop
    async def run():
        server=make_server(USERS)
        server.db_manager.get_user_settings.return_value={"username":"lajihe-test"}
        manager=server.room_manager;manager._broadcast_room_info=AsyncMock()
        result=await manager.create_Shanghai_room(str(USERS[0]),"上海垃圾和",1,"",30,3,True,sub_rule="shanghai/lajihe")
        assert result.success
        room=next(iter(manager.rooms.values()));room["player_list"]=USERS.copy();room["ready_list"]=USERS.copy()
        with patch.object(LajiheGameState,"run_game_loop",parked_loop):
            assert await server.gamestate_manager.start_game(str(USERS[0]),room["room_id"]) is None
            state=server.gamestate_manager.get_game_state_by_room_id(room["room_id"])
            assert type(state) is LajiheGameState and state.sub_rule=="shanghai/lajihe"
            await asyncio.sleep(0)
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
            assert not server.gamestate_manager.gamestate_id_to_game_state
    asyncio.run(run())


def test_reconnect_preserves_private_hand_public_kong_and_pending_flower_choice():
    async def run():
        state=make_state();state.master_seed=144;state.init_tiles()
        for p in state.player_list:
            state.game_server.user_id_to_connection[p.user_id]=SimpleNamespace(websocket=AsyncMock())
        state.player_list[0].combination_mask=[[0,45]*4];state.player_list[0].combination_tiles=["G45"]
        state.player_list[0].huapai_list=list(range(51,59));state.player_list[0].eight_flowers_paid=True
        state.player_list[0].score=90
        state.game_status="waiting_flower_choice"
        state.action_dict={0:["lajihe_flower_end","lajihe_flower_continue"],1:[],2:[],3:[]}
        state.waiting_players_list=[0]
        await send_reconnect_game_state(state,state.player_list[0])
        await reconnected_send_pending_ask_for_viewer(state,101,0)
        socket=state.game_server.user_id_to_connection[101].websocket
        packets=[c.args[0] for c in socket.send_json.await_args_list]
        start=next(p["game_info"] for p in packets if "game_info" in p)
        assert start["sub_rule"]=="shanghai/lajihe"
        assert any(p.get("ask_hand_action_info",{}).get("action_list")==state.action_dict[0] for p in packets)
        await broadcast_do_action(state,["angang"],0,combination_mask=[0,45]*4,combination_target="G45")
        other=state.game_server.user_id_to_connection[102].websocket.send_json.await_args.args[0]
        assert other["do_action_info"]["combination_mask"]==[0,45]*4
        assert other["do_action_info"]["combination_target"]=="G45"
    asyncio.run(run())


def test_four_real_websocket_peers_finish_match_via_production_action_router():
    async def run():
        state=make_state();state.room_random_seed=41;state.round_time=30;state.step_time=5
        server=state.game_server
        server.players={str(p.user_id):SimpleNamespace(user_id=p.user_id) for p in state.player_list}
        server.gamestate_manager.get_game_state_by_gamestate_id=lambda gid: state if gid==state.gamestate_id else None
        server.gamestate_manager.cleanup_game_state_complete=AsyncMock()
        submitted=Counter();received=Counter()
        async def handle(ws):
            uid=int(ws.request.path.strip('/'))
            class Adapter:
                async def send_json(self,data): await ws.send(json.dumps(data))
            adapter=Adapter();server.user_id_to_connection[uid]=SimpleNamespace(websocket=adapter)
            async for raw in ws:
                await handle_gamestate_message(server,str(uid),json.loads(raw),adapter)
        async def client(uid,ws):
            async for raw in ws:
                payload=json.loads(raw);received[payload['type']]+=1
                if payload['type'].endswith('/game_end'): return
                info=payload.get('ask_hand_action_info') or payload.get('ask_other_action_info')
                p=next(p for p in state.player_list if p.user_id==uid)
                allowed=info.get('action_list',[]) if info else []
                if payload['type'].endswith('/ready_status') and state.action_dict.get(p.player_index): allowed=['ready']
                if not allowed: continue
                tick=info['action_tick'] if info else state.server_action_tick
                for _ in range(1000):
                    if p.player_index in state.waiting_players_list: break
                    await asyncio.sleep(0)
                action=next((a for a in ['ready','buhua','lajihe_flower_continue','hu_self','hu_first','hu_second','hu_third','angang','jiagang','cut','gang','peng','chi_left','chi_mid','chi_right'] if a in allowed),'pass')
                request={'type':'gamestate/GB/send_action','gamestate_id':state.gamestate_id,'action':action,'action_tick':tick}
                if action=='cut':
                    tile,index=find_best_cut(p.hand_tiles,len(p.combination_tiles),count_visible_tiles(state),p.kuikae_forbidden_tiles)
                    request.update(type='gamestate/GB/cut_tile',TileId=tile,cutIndex=index,cutClass=infer_bot_cut_class(p.hand_tiles,tile,index,draw_slot=has_draw_slot(p)))
                elif action=='angang': request['targetTile']=next(t for t,n in Counter(p.hand_tiles).items() if n==4)
                elif action=='jiagang': request['targetTile']=next(t for t in p.hand_tiles if f'k{t}' in p.combination_tiles)
                submitted[action]+=1
                await ws.send(json.dumps(request))
        async with websockets.serve(handle,'127.0.0.1',0) as listener:
            port=listener.sockets[0].getsockname()[1]
            peers=[await websockets.connect(f'ws://127.0.0.1:{port}/{uid}') for uid in (101,102,103,104)]
            tasks=[asyncio.create_task(client(uid,ws)) for uid,ws in zip((101,102,103,104),peers)]
            try:
                with patch('server.gamestate.game_shanghai.QinghunpengGameState.liuju_ready_wait_seconds',return_value=0):
                    await asyncio.wait_for(state.game_loop_chinese(),60)
                await asyncio.wait_for(asyncio.gather(*tasks),5)
                assert received['gamestate/shanghai/game_start']==16
                assert received['gamestate/shanghai/game_end']==4
                assert submitted['cut']>0 and submitted['buhua']>0
                assert len(state.game_record['game_round'])==4 and sum(p.score for p in state.player_list)==0
            finally:
                for task in tasks: task.cancel()
                await asyncio.gather(*tasks,return_exceptions=True)
                await asyncio.gather(*(ws.close() for ws in peers))
    asyncio.run(run())
