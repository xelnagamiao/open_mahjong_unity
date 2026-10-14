"""MCR realtime hand ordering across the actual transport and flower lifecycle."""
import asyncio
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_sanma import sanma
from . import transparent
from .boardcast import _build_game_start_payload_for_viewer, broadcast_result
from .buhua_broadcast import perform_buhua_and_broadcast
from ..game_riichi import realtime_hand as live
from ..game_riichi.test_realtime_hand import request
from ..gamestate_router import handle_gamestate_message
from ..public.game_record_manager import build_game_title_data, player_action_record_cut
from ..public.hand_action_notify import apply_player_cut


def setup():
    game = sanma(sub_rule=transparent.REALTIME_SUB_RULE)
    game.game_status = 'waiting_hand_action'
    game.game_record = {'game_round': {f'round_index_{game.round_index}': {'action_ticks': []}}}
    game.send_to_realtime_spectators = AsyncMock()
    game.game_server.players = {str(p.user_id): SimpleNamespace(user_id=p.user_id) for p in game.player_list}
    game.game_server.gamestate_manager.get_game_state_by_gamestate_id = lambda gid: game if gid == game.gamestate_id else None
    for i, p in enumerate(game.player_list):
        p.hand_tiles = [11+i, 2019-i, 2019-i, 22+i]
        p.has_draw_slot = False
        live.initialize_hand(game, p)
    return game


def command(g, p, tiles=None, **changes):
    return request(g, p, tiles, type='gamestate/guobiao/reorder_hand', **changes)


def test_start_keeps_deal_order_and_record_declares_new_initial_layout():
    g = setup()
    assert build_game_title_data(g)['realtime_initial_order'] == 'deal'
    for viewer in range(4):
        start = _build_game_start_payload_for_viewer(g, viewer)
        for seat, layout in start['washizu_layouts'].items():
            hand = g.player_list[seat].hand_tiles
            assert layout['tiles'] == (hand if seat == viewer else [0, hand[1], hand[2], 0])
            assert layout['keys'] == [1, 2, 3, 4]


def test_drag_broadcast_ack_spectator_reconnect_and_record_share_order():
    async def run():
        g = setup(); p = g.player_list[0]
        conn = g.game_server.user_id_to_connection
        order = [2019, 22, 2019, 11]
        message = command(g, p, order, keys=[3,4,2,1])
        await handle_gamestate_message(g.game_server, str(p.user_id), message, conn[p.user_id].websocket)
        for viewer in g.player_list:
            response = conn[viewer.user_id].websocket.send_json.call_args.args[0]
            assert response['type'] == 'gamestate/guobiao/hand_order' and response['success']
            layout = response['washizu_layouts'][0]
            assert layout['tiles'] == (order if viewer == p else [2019,0,2019,0])
            assert layout['keys'] == [3,4,2,1]
            assert response['hand_order_request_id'] == (7 if viewer == p else None)
            await g.player_reconnect(viewer.user_id)
            starts = [c.args[0]['game_info'] for c in conn[viewer.user_id].websocket.send_json.call_args_list if c.args[0].get('game_info')]
            assert starts[-1]['washizu_layouts'][0] == layout
        assert g.send_to_realtime_spectators.await_count >= 4
        tick = g.game_record['game_round']['round_index_1']['action_ticks'][-1]
        assert tick[0] == 'wo' and json.loads(tick[2]) == order and json.loads(tick[5]) == [3,4,2,1]
    asyncio.run(run())


@pytest.mark.parametrize('changes,reason', [({'revision':-1},'stale'), ({'round_index':2},'round'),
    ({'tiles':[11,19,2019,22]},'tiles'), ({'keys':[1,2,2,4]},'keys'), ({'draw_index':0},'draw')])
def test_invalid_drag_preserves_hand_and_does_not_record(changes, reason):
    async def run():
        g = setup(); p = g.player_list[0]
        before = live.layout_for_viewer(g,p,0)
        msg = command(g,p,before['tiles']); msg.update(changes)
        ws = g.game_server.user_id_to_connection[p.user_id].websocket
        await handle_gamestate_message(g.game_server,str(p.user_id),msg,ws)
        assert ws.send_json.call_args.args[0]['reason'] == reason
        assert live.layout_for_viewer(g,p,0) == before
        assert not g.game_record['game_round']['round_index_1']['action_ticks']
    asyncio.run(run())


@pytest.mark.parametrize('foreign_family', [False, True])
def test_spectators_and_wrong_family_cannot_reorder(foreign_family):
    async def run():
        g=setup(); p=g.player_list[0]; ws=AsyncMock()
        g.game_server.players['observer']=SimpleNamespace(user_id=999)
        message=command(g,p)
        if foreign_family: message['type']='gamestate/riichi/reorder_hand'
        await handle_gamestate_message(g.game_server,str(p.user_id) if foreign_family else 'observer',message,ws)
        assert ws.send_json.call_args.args[0]['reason']=='unauthorized'
    asyncio.run(run())


def test_cut_removes_selected_identical_copy_and_records_key():
    async def run():
        g=setup(); p=g.player_list[0]
        assert live.reorder(g,p,command(g,p,[2019,22,2019,11],keys=[3,4,2,1]))[0]
        result=await apply_player_cut(g,0,{'TileId':2019,'cutClass':False,'cutIndex':2})
        assert result==(2019,False,2)
        player_action_record_cut(g,2019)
        assert live.layout_for_viewer(g,p,0)['keys']==[3,4,1]
        assert g.game_record['game_round']['round_index_1']['action_ticks'][-1][-2:]==['wk',2]
    asyncio.run(run())


@pytest.mark.parametrize('instant',[False,True])
def test_flower_steps_keep_order_keys_and_hide_replacement_until_draw(instant):
    async def run():
        g=setup(); p=g.player_list[0]
        p.hand_tiles=[11,2019,58,22]; live.initialize_hand(g,p)
        live.reorder(g,p,command(g,p,[22,58,11,2019]))
        g.tiles_list=[2013,2041]
        with patch('server.gamestate.game_guobiao.buhua_broadcast.HAND_SETTLE_GAP_SEC',0):
            tile=await perform_buhua_and_broadcast(g,0,instant=instant)
        for viewer in g.player_list:
            frames=[c.args[0]['do_action_info'] for c in g.game_server.user_id_to_connection[viewer.user_id].websocket.send_json.call_args_list]
            if not instant:
                initial=frames[0]['washizu_layouts'][0]
                assert initial['keys']==[4,1,2] and len(initial['tiles'])==3
                assert tile not in initial['tiles']
            final=frames[-1]['washizu_layouts'][0]
            assert final['keys']==[4,1,2,5] and final['drawn_key']==5 and final['draw_pinned']
            assert final['tiles']==([22,11,2019,tile] if viewer==p else [0,0,2019,tile])
    asyncio.run(run())


def test_result_preserves_mixed_positions_and_separate_winning_tile():
    async def run():
        g=setup();p=g.player_list[0]
        live.reorder(g,p,command(g,p,[22,2019,11,2019]))
        await broadcast_result(g,hepai_player_index=0,hepai_player_hand=p.hand_tiles+[2041],hu_class='hu_first')
        for conn in g.game_server.user_id_to_connection.values():
            result=conn.websocket.send_json.call_args.args[0]['show_result_info']
            assert result['hepai_player_hand']==[22,2019,11,2019,2041]
    asyncio.run(run())
