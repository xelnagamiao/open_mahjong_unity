"""Match-only entitlement and replay output stay aligned."""
from copy import deepcopy
import asyncio
from types import SimpleNamespace

import pytest

from server.database import commerce
from server.database.game_rewards import build_currency_reward, attach_currency_to_end_packet
from server.gamestate.public.match_result import build_match_result, attach_match_result
from test_round_progress import record
from test_commerce import shop


@pytest.mark.parametrize('room',['custom','events'])
def test_nonmatching_tables_never_credit_even_with_four_registered_humans(shop,room):
    replay=record(hands=16)
    replay['game_title']['room_type']=room
    for i in range(4): replay['game_title'][f'p{i}_uid']=101+i
    shop.sql('INSERT INTO game_player_records SELECT %s,generate_series(101,104)',(room,))
    conn=shop.pool.getconn()
    try:
        with conn.cursor() as cur:
            assert commerce.settle_game_reward(cur,room,replay)['credited_minor']==0
        conn.commit()
    finally:shop.pool.putconn(conn)
    assert shop.sql('SELECT COUNT(*) FROM wallet_ledger')==[(0,)]


def test_snapshot_counts_repeats_not_schedule_and_preserves_saved_ratings():
    replay=record(hands=11,limit=2)
    title=replay['game_title']
    title.update(room_type='match',end_time='2026-11-09 10:00:00')
    ids=[title[f'p{i}_uid'] for i in range(4)]
    players=[dict(user_id=uid,username=str(uid),rank=i+1,score=10-i,original_player_index=i) for i,uid in enumerate(ids)]
    title['rating_result']={str(uid):dict(rating_system='grade',rating_pt=delta)
                            for uid,delta in zip(ids,[25.5,5,-10,-20.5])}
    replay['currency_reward']=build_currency_reward(replay,ids,ids[:3])
    replay['match_result']=build_match_result(replay,players)
    saved=deepcopy(replay['match_result'])
    live=[SimpleNamespace(**{k:v for k,v in p.items() if k!='rank'},record_counter=SimpleNamespace(rank_result=p['rank'])) for p in players]
    attach_match_result(replay,live)  # Local replay attachment must retain authoritative currency.
    assert replay['match_result']==saved
    assert [p['currency_delta_minor'] for p in saved['players']]==[55,55,55,0]
    assert [p['rating_pt'] for p in saved['players']]==[25.5,5,-10,-20.5]
    packet=dict(type='gamestate/riichi/game_end',game_end_info=dict(player_final_data={str(i):p for i,p in enumerate(players)}))
    output=attach_currency_to_end_packet(SimpleNamespace(game_record=replay,player_list=live),packet)
    assert [p['currency_delta_minor'] for p in output['game_end_info']['player_final_data'].values()]==[55,55,55,0]
    assert 'currency_delta_minor' not in packet['game_end_info']['player_final_data']['0']
    from server.gamestate.public.settlement_sync import send_game_packet
    sent=[]
    class Socket:
        async def send_json(self,value): sent.append(value)
    asyncio.run(send_game_packet(SimpleNamespace(game_record=replay,player_list=live),Socket(),packet))
    assert sent[0]['game_end_info']['player_final_data']['0']['currency_delta_minor']==55
