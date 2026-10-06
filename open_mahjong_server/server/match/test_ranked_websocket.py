"""Real loopback WebSocket peers exercise each production ranked table size."""
import asyncio
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock
import pytest
import websockets
from .test_lifecycle import server_fixture,USERS
from .match_router import handle_match_message
from .rating_rules import RULES,QUEUES,default_rating


@pytest.mark.parametrize('rule',RULES)
def test_peers_receive_one_commit_and_observer_has_no_owner_state(rule):
    async def run():
        server=server_fixture();manager=server.match_manager
        manager.committed_users.clear();manager.winning_queues.clear();manager._start_game=AsyncMock()
        server.db_manager.get_rank_data.return_value={'guobiao_rank':'四段','ratings':{r:default_rating(r) for r in RULES}}
        queue=next(q for q,s in QUEUES.items() if s.rating_rule==rule)
        table_users=USERS[:QUEUES[queue].player_count]
        observer=990005
        server.players[str(observer)]=SimpleNamespace(user_id=observer,current_room_id=None,is_tourist=False)
        async def handle(ws):
            cid=ws.request.path.strip('/');uid=int(cid)
            class Adapter:
                async def send_json(self,data):await ws.send(json.dumps(data))
            adapter=Adapter();server.user_id_to_connection[uid]=SimpleNamespace(websocket=adapter)
            try:
                async for raw in ws:await handle_match_message(server,cid,json.loads(raw),adapter)
            finally:manager.player_disconnect(uid);server.user_id_to_connection.pop(uid,None)
        async with websockets.serve(handle,'127.0.0.1',0) as listener:
            port=listener.sockets[0].getsockname()[1]
            clients=[await websockets.connect(f'ws://127.0.0.1:{port}/{uid}') for uid in table_users+[observer]]
            try:
                for i,ws in enumerate(clients[:-1]):
                    await ws.send(json.dumps(dict(type='match/join_queue',queue_type=queue,match_request_id=str(i))))
                async def receive_commit(i,ws):
                    replies=[]
                    while not {'match/join_queue_done','match/match_found'}.issubset({r['type'] for r in replies}):
                        replies.append(json.loads(await asyncio.wait_for(ws.recv(),5)))
                    joined=next(r for r in replies if r['type']=='match/join_queue_done')
                    found=next(r for r in replies if r['type']=='match/match_found')
                    assert joined['success'] and joined['match_request_id']==str(i)
                    assert found['match_committed'] and found['match_queue_type']==queue and found['my_queues']==[]
                await asyncio.gather(*(receive_commit(i,ws) for i,ws in enumerate(clients[:-1])))
                await clients[0].send(json.dumps(dict(type='match/leave_queue',match_request_id='late-cancel')))
                late=json.loads(await clients[0].recv());assert not late['success'] and late['match_committed']
                await clients[-1].send(json.dumps(dict(type='match/get_queue_status')))
                view=json.loads(await clients[-1].recv());assert view['my_queues']==[] and not view['match_committed']
                assert view['match_player_count']==len(table_users)
                assert set(manager.winning_queues.values())=={queue}
                assert manager.committed_users==set(table_users)
                manager._start_game.assert_awaited_once()
            finally:
                await asyncio.gather(*(ws.close() for ws in clients))
    asyncio.run(run())
