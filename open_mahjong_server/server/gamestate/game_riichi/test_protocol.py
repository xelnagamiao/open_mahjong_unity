"""Exercise the production action router and serialized reconnect messages."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_rule_branches import game
from .test_double_riichi import declare, make_game
from .boardcast import broadcast_game_start, broadcast_do_action, reconnected_send_pending_ask, broadcast_refresh_player_tag_list, broadcast_game_end, broadcast_switch_seat, broadcast_ready_status
from ..public.ai.get_action import get_action
from .wait_action import _is_valid_cut_action, _commit_pending_riichi


def connected(preset='tenhou'):
    g=game(preset)
    g.room_id=824001
    sockets={p.user_id:SimpleNamespace(websocket=AsyncMock(),user_id=p.user_id) for p in g.player_list}
    g.game_server.user_id_to_connection=sockets
    g.game_server.players={str(uid):conn for uid,conn in sockets.items()}
    g.send_to_realtime_spectators=AsyncMock()
    return g,sockets


@pytest.mark.parametrize('preset',['majsoul','tenhou','jpml_a','mleague'])
def test_start_message_preserves_custom_rules_and_hides_other_hands_and_furiten(preset,caplog):
    async def run():
        g,sockets=connected(preset);g.hepai_limit=4
        for p in g.player_list:p.hand_tiles=[11]*13;p.tag_list=['riichi','furiten']
        await broadcast_game_start(g)
        await broadcast_refresh_player_tag_list(g)
        for p in g.player_list:
            calls=sockets[p.user_id].websocket.send_json.call_args_list
            info=calls[0].args[0]['game_info']
            assert info['detailed_config']==g.detailed_config and info['hepai_limit']==4
            for other in info['players_info']:
                own=other['user_id']==p.user_id
                assert ('hand_tiles' in other) is own
                assert ('furiten' in other['tag_list']) is own
            tags=calls[1].args[0]['refresh_player_tag_list_info']['player_to_tag_list']
            assert sum('furiten' in v for v in tags.values())==1
        assert not any(r.levelname=='ERROR' for r in caplog.records)
    asyncio.run(run())


@pytest.mark.parametrize('kind',['jiagang','angang'])
def test_reconnect_rob_kan_restores_claim_context_with_empty_river(kind):
    async def run():
        g,sockets=connected();g.game_status='waiting_action_qianggang';g.current_player_index=0
        g.jiagang_tile=305 if kind=='jiagang' else 47
        g._pending_kan={'kind':kind};g.action_dict={0:[],1:['hu_first','pass'],2:[],3:[]}
        await reconnected_send_pending_ask(g,101)
        calls=sockets[101].websocket.send_json.call_args_list
        assert len(calls)==2
        claim=calls[0].args[0]['do_action_info'];ask=calls[1].args[0]['ask_other_action_info']
        assert claim['is_claim'] and claim['action_list']==[kind]
        assert ask['cut_tile']==g.jiagang_tile==claim['cut_tile']
        assert ask['action_tick']==g.server_action_tick
    asyncio.run(run())


def test_action_draw_tile_is_private_and_declaration_does_not_advance_tick():
    async def run():
        g,sockets=connected();before=g.server_action_tick
        await broadcast_do_action(g,['jiagang'],0,cut_tile=305,is_claim=True)
        assert g.server_action_tick==before
        await broadcast_do_action(g,['deal'],0,deal_tile=305)
        assert g.server_action_tick==before+1
        for uid,conn in sockets.items():
            payload=conn.websocket.send_json.call_args_list[-1].args[0]['do_action_info']
            assert payload.get('deal_tile')==(305 if uid==100 else None)
    asyncio.run(run())


@pytest.mark.parametrize('invalid',['stale_tick','foreign_user','wrong_turn','absent_tile','kuikae','disallowed'])
def test_production_action_entry_rejects_invalid_requests(invalid):
    async def run():
        g,sockets=connected();g.game_status='waiting_hand_action';g.current_player_index=0
        g.waiting_players_list=[0];g.action_dict={0:['cut'],1:[],2:[],3:[]};g.server_action_tick=10
        uid='100';tile=g.player_list[0].hand_tiles[-1];tick=10;action='cut'
        if invalid=='stale_tick':tick=9
        if invalid=='foreign_user':uid='unknown'
        if invalid=='wrong_turn':uid='101'
        if invalid=='absent_tile':tile=99
        if invalid=='kuikae':g.player_list[0].kuikae_forbidden_tiles=[tile]
        if invalid=='disallowed':action='hu_self'
        await get_action(g,uid,action,True,tile,None,None,action_tick=tick)
        assert all(q.empty() for q in g.action_queues.values())
    asyncio.run(run())


def test_production_queue_accepts_valid_cut_then_executor_checks_riichi_lock():
    async def run():
        g,_=connected();g.game_status='waiting_hand_action';g.waiting_players_list=[0]
        g.action_dict={0:['cut','riichi_cut'],1:[],2:[],3:[]};p=g.player_list[0]
        tile=p.hand_tiles[-1]
        await get_action(g,'100','cut',True,tile,None,None,action_tick=g.server_action_tick)
        payload=await g.action_queues[0].get()
        assert payload['TileId']==tile and _is_valid_cut_action(g,0,payload)
        p.tag_list=['riichi'];p.pending_riichi=False
        payload['TileId']=p.hand_tiles[0]
        assert not _is_valid_cut_action(g,0,payload)
    asyncio.run(run())


def test_unaccepted_riichi_never_creates_a_paid_record_tick():
    g,_=make_game(0)
    g.player_list[1].hand_tiles=[47,47,11,12,13,21,22,23,31,32,33,45,45]
    declare(g)
    ticks=g.game_record['game_round']['round_index_1']['action_ticks']
    assert g.player_list[0].pending_riichi and g.player_list[0].score==25000
    assert not any(t[0]=='riichi' for t in ticks)
    _commit_pending_riichi(g)
    assert ticks[-1]==['riichi',0,1] and g.player_list[0].score==24000


@pytest.mark.parametrize('status',['waiting_hand_action','onlycut_after_action'])
def test_reconnect_restores_hand_actions_and_kuikae_after_call(status,caplog):
    async def run():
        g,sockets=connected();g.game_status=status;g.current_player_index=1
        p=g.player_list[1];p.tag_list=['offline'];p.kuikae_forbidden_tiles=[15]
        p.riichi_candidate_cuts={47:[16]}
        g.action_dict={0:[],1:['cut'] if status=='onlycut_after_action' else ['cut','riichi_cut'],2:[],3:[]}
        await g.player_reconnect(p.user_id)
        assert 'offline' not in p.tag_list
        messages=[c.args[0] for c in sockets[p.user_id].websocket.send_json.call_args_list]
        state=next(m['game_info'] for m in messages if m['type']=='gamestate/riichi/game_start')
        ask=messages[-1]['ask_hand_action_info']
        assert ask['action_list']==g.action_dict[1]
        assert ask['forbidden_cut_tiles']==[15] and ask['player_index']==1
        assert state['detailed_config']==g.detailed_config
        assert sum('hand_tiles' in info for info in state['players_info'])==1
        assert not any(r.levelname=='ERROR' for r in caplog.records)
    asyncio.run(run())


def test_final_points_ready_and_seat_rotation_survive_real_response_serialization(caplog):
    async def run():
        from .rule_logic import finalize_scores
        g,sockets=connected('mleague')
        for p,score in zip(g.player_list,[40000,30000,20000,10000]):p.score=score
        finalize_scores(g)
        for p in g.player_list:p.record_counter.rank_result=p.original_player_index+1
        g._rotate_seats()
        await broadcast_game_end(g)
        await broadcast_switch_seat(g)
        g.action_dict={0:['ready'],1:[],2:[],3:[]}
        await broadcast_ready_status(g)
        for conn in sockets.values():
            messages=[c.args[0] for c in conn.websocket.send_json.call_args_list]
            final=next(m['game_end_info']['player_final_data'] for m in messages if m['type']=='gamestate/riichi/game_end')
            for p in g.player_list:
                item=final[str(p.player_index)]
                assert item['original_player_index']==p.original_player_index
                assert item['pt']==p.riichi_points and item['score']==p.score
            assert messages[-2]['type']=='switch_seat'
            assert messages[-1]['ready_status_info']['player_to_ready']=={0:False,1:True,2:True,3:True}
        assert not any(r.levelname=='ERROR' for r in caplog.records)
    asyncio.run(run())
