"""Drive four opt-in Development Players through a loopback-only Singapore match.

Uses production routing and game state with a disposable in-memory persistence stub.
Launch each player with --verify-singapore OUTPUT ENDPOINT UID (11000001..11000004).
The endpoint is written to OUTPUT_DIR/endpoint.txt. No live database or service is used.
"""
import argparse,asyncio,json,sys,traceback,time
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock,patch
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'open_mahjong_server'))
import websockets
from server.gamestate.game_singapore.test_state import make_state
from server.gamestate.game_singapore.test_integration import choose
from server.gamestate.public.hand_slot_utils import infer_bot_cut_class,has_draw_slot
from server.gamestate.gamestate_router import handle_gamestate_message
from server.gamestate.public.game_record_manager import build_local_record_detail

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output-dir',type=Path,required=True,help='New, empty directory for evidence')
args=parser.parse_args()
OUT=args.output_dir.resolve()
if OUT.exists() and any(OUT.iterdir()):parser.error('output-dir must be empty')
OUT.mkdir(parents=True,exist_ok=True)
UIDS=list(range(11000001,11000005))

async def main():
    state=make_state();state.room_random_seed=41;state.round_time=300;state.step_time=30
    state.bot_speed='instant'
    for uid,p in zip(UIDS,state.player_list):
        p.user_id=uid;p.username=f'新加坡测试{uid-11000000}';p.remaining_time=300
    server=state.game_server
    server.players={str(uid):SimpleNamespace(user_id=uid) for uid in UIDS}
    server.gamestate_manager.get_game_state_by_gamestate_id=lambda gid:state if gid==state.gamestate_id else None
    ready=set();asserted=set();replayed=asyncio.Event();reconnected=False;rounds=[];tasks=[];submitted=[]
    async def cpu(st,fn,*args):return fn(*args)
    async def plan(uid,allowed,tick):
        nonlocal reconnected
        for _ in range(6000):
            p=next(p for p in state.player_list if p.user_id==uid)
            if p.player_index in state.waiting_players_list:break
            await asyncio.sleep(.005)
        else:raise RuntimeError('action window did not open')
        if tick!=state.server_action_tick:return
        action,tile,index,target=choose(state,p,allowed)
        if uid==UIDS[0] and action=='cut' and p.normal_draw_count>=2 and not reconnected:
            reconnected=True
            await server.user_id_to_connection[uid].websocket.raw({'type':'qa/reconnect'})
            return
        await server.user_id_to_connection[uid].websocket.raw({'type':'qa/plan','action':action,'tile':tile,'target':target,'tick':tick,
            'drawn':infer_bot_cut_class(p.hand_tiles,tile,index,draw_slot=has_draw_slot(p)) if tile else False})
    class Adapter:
        def __init__(self,ws,uid):self.ws=ws;self.uid=uid
        async def raw(self,payload):
            if payload.get('type','').startswith('qa/'):
                payload={**payload,'qa':payload['type'],'type':'pong','client_ts':int(time.time()*1000)}
            await self.ws.send(json.dumps(payload,ensure_ascii=False))
        async def send_json(self,payload):
            await self.raw(payload)
            info=payload.get('ask_hand_action_info') or payload.get('ask_other_action_info')
            if info and info.get('action_list'):tasks.append(asyncio.create_task(plan(self.uid,info['action_list'],info['action_tick'])))
            elif payload['type'].endswith('/ready_status'):
                p=next(p for p in state.player_list if p.user_id==self.uid)
                if state.action_dict.get(p.player_index):tasks.append(asyncio.create_task(plan(self.uid,['ready'],state.server_action_tick)))
            if payload['type'].endswith('/show_result'):
                await self.raw({'type':'qa/assert','scores':[p.score for p in state.player_list],
                    'rounds':len(state.player_list[0].score_history),'hu_class':payload['show_result_info']['hu_class']})
    async def handle(ws):
        uid=int(ws.request.path.strip('/').split('/')[0]);adapter=Adapter(ws,uid)
        server.user_id_to_connection[uid]=SimpleNamespace(websocket=adapter)
        try:
            async for raw in ws:
                message=json.loads(raw);kind=message.get('type')
                if kind=='qa/ready':ready.add(uid)
                elif kind=='qa/reconnected':await state.player_reconnect(uid)
                elif kind=='qa/replayed':replayed.set()
                elif kind=='qa/asserted':asserted.add((message['round'],uid))
                elif kind=='ping':await adapter.raw({**message,'type':'pong'})
                elif kind.startswith('gamestate/'):
                    submitted.append({'uid':uid,**message})
                    await handle_gamestate_message(server,str(uid),message,adapter)
        except websockets.ConnectionClosed:pass
    settle=state._settle_hand
    async def capture(before):
        result=await settle(before)
        rounds.append({'index':state.round_index,'current_round':state.current_round,'scores':[p.score for p in state.player_list]})
        (OUT/'progress.json').write_text(json.dumps({'rounds':rounds,'actions':len(submitted),'reconnected':reconnected},ensure_ascii=False,indent=2))
        return result
    state._settle_hand=capture
    async with websockets.serve(handle,'127.0.0.1',0,ping_interval=None) as listener:
        port=listener.sockets[0].getsockname()[1]
        (OUT/'endpoint.txt').write_text(f'ws://127.0.0.1:{port}')
        for _ in range(1800):
            if len(ready)==4:break
            await asyncio.sleep(.1)
        else:raise TimeoutError(f'Clients not ready: {ready}')
        with patch('server.gamestate.game_singapore.SingaporeGameState.run_room_bot_cpu',cpu),patch('server.gamestate.game_singapore.lifecycle.liuju_ready_wait_seconds',return_value=1):
            await asyncio.wait_for(state.game_loop_chinese(),650)
        detail=build_local_record_detail(state,'sgRuntime1','1/4').model_dump(mode='json',exclude_none=True)
        (OUT/'record-detail.json').write_text(json.dumps(detail,ensure_ascii=False,indent=2,default=str))
        (OUT/'submitted.json').write_text(json.dumps(submitted,ensure_ascii=False,indent=2))
        net=[next(p.score-400 for p in state.player_list if p.original_player_index==i) for i in range(4)]
        for _ in range(300):
            if all((len(rounds),uid) in asserted for uid in UIDS):break
            await asyncio.sleep(.1)
        else:raise TimeoutError('Final result was not presented on all four clients')
        await server.user_id_to_connection[UIDS[0]].websocket.raw({'type':'qa/replay','detail':detail,'rounds':rounds,'net':net})
        await asyncio.wait_for(replayed.wait(),180)
        for uid in UIDS:await server.user_id_to_connection[uid].websocket.raw({'type':'qa/done'})
        await asyncio.sleep(3)
        await asyncio.gather(*tasks)
        (OUT/'server-result.json').write_text(json.dumps({'status':'completed','rounds':rounds,'net':net,'actions':len(submitted),'reconnected':reconnected},indent=2))

try:asyncio.run(main())
except Exception:
    (OUT/'server-result.json').write_text(json.dumps({'status':'failed','error':traceback.format_exc()},indent=2));raise
