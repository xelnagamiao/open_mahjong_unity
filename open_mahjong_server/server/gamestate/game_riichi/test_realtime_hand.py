"""Realtime order authority, secrecy, lifecycle, transport and legacy parity."""
import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from . import realtime_hand as live
from .test_protocol import connected
from .washizu import public_hands
from .init_tiles import init_riichi_tiles
from .boardcast import broadcast_game_start, broadcast_do_action
from ...game_calculation.riichi.washizu_tiles import REALTIME_SUB_RULE, make_tiles
from ..gamestate_router import handle_gamestate_message


def setup():
    game, sockets = connected()
    game.sub_rule = REALTIME_SUB_RULE
    game.game_status = 'waiting_hand_action'
    for i, player in enumerate(game.player_list):
        player.hand_tiles = [11+i, 2019-i, 2019-i, 22+i]
        player.has_draw_slot = False
        live.initialize_hand(game, player)
    game.game_server.gamestate_manager = SimpleNamespace(get_game_state_by_gamestate_id=lambda gid: game if gid == game.gamestate_id else None)
    game.game_record = {'game_round': {f'round_index_{game.round_index}': {'action_ticks': []}}}
    return game, sockets


def request(game, player, tiles=None, **changes):
    state = live.sync_hand(game, player)
    result = dict(type='gamestate/riichi/reorder_hand', gamestate_id=game.gamestate_id,
                  round_index=game.round_index, revision=state['revision'], request_id=7,
                  tiles=tiles or list(reversed([tile for _, tile in state['entries']])),
                  draw_index=-1, draw_pinned=False)
    result.update(changes)
    return result


def test_initial_deal_order_and_private_logic_untouched():
    game, _ = setup(); player = game.player_list[0]
    original = list(player.hand_tiles)
    owner = live.layout_for_viewer(game, player, 0)
    assert owner['tiles'] == original == [11,2019,2019,22]
    assert live.layout_for_viewer(game, player, 1)['tiles'] == [0,2019,2019,0]
    assert player.hand_tiles == original
    message = request(game, player, [22,2019,11,2019])
    assert live.reorder(game, player, message) == (True, '', True)
    assert player.hand_tiles == original
    assert public_hands(game)[0] == [0,2019,0,2019]
    assert Counter(live.layout_for_viewer(game, player, 0)['keys']) == Counter(owner['keys'])


@pytest.mark.parametrize('changes,reason', [
    ({'revision':-1},'stale'), ({'revision':True},'stale'),
    ({'round_index':-1},'round'), ({'round_index':True},'round'),
    ({'tiles':[11,19,2019,22]},'tiles'), ({'tiles':[11,2019,2019]},'tiles'),
    ({'tiles':[11,2019,2019,2019]},'tiles'), ({'tiles':[True,2019,2019,22]},'tiles'),
    ({'tiles':'bad'},'tiles'), ({'tiles':[11]*15},'tiles'),
    ({'draw_index':0},'draw'), ({'draw_pinned':True},'draw'),
    ({'draw_index':False},'draw'), ({'draw_pinned':1},'draw'),
])
def test_invalid_orders_are_atomic(changes, reason):
    game, _ = setup(); player=game.player_list[0]
    before=live.layout_for_viewer(game,player,0)
    assert live.reorder(game,player,request(game,player,**changes)) == (False,reason,False)
    assert live.layout_for_viewer(game,player,0) == before


def test_draw_merge_discard_meld_replacement_and_new_round():
    game,_=setup();p=game.player_list[0]
    live.reorder(game,p,request(game,p,[22,2019,11,2019]))
    p.hand_tiles.append(2031);p.has_draw_slot=True
    layout=live.layout_for_viewer(game,p,0)
    assert layout['tiles']==[22,2019,11,2019,2031] and layout['draw_pinned']
    assert live.reorder(game,p,request(game,p,[22,2031,2019,11,2019],draw_index=1))==(True,'',True)
    assert p.hand_tiles[-1]==2031
    p.hand_tiles.remove(11);p.has_draw_slot=False
    layout=live.layout_for_viewer(game,p,0)
    assert layout['tiles']==[22,2031,2019,2019] and not layout['draw_pinned']
    p.hand_tiles.remove(2019);p.hand_tiles.remove(2019)
    assert live.layout_for_viewer(game,p,0)['tiles']==[22,2031]
    old_keys=set(live.layout_for_viewer(game,p,0)['keys'])
    p.hand_tiles.append(2041);p.has_draw_slot=True
    layout=live.layout_for_viewer(game,p,0)
    assert layout['tiles']==[22,2031,2041] and layout['keys'][-1] not in old_keys
    game.round_index+=1;p.hand_tiles=[11,2012];p.has_draw_slot=False
    assert live.layout_for_viewer(game,p,0)['tiles']==[11,2012]


def test_duplicate_draw_tracks_its_key_when_merged():
    game,_=setup();p=game.player_list[0];p.hand_tiles.append(2019);p.has_draw_slot=True
    before=live.layout_for_viewer(game,p,0)
    assert live.reorder(game,p,request(game,p,[2019,11,2019,22,2019],draw_index=0))==(True,'',True)
    after=live.layout_for_viewer(game,p,0)
    assert after['keys'][0]==before['drawn_key'] and after['drawn_key']==before['drawn_key']
    assert p.hand_tiles[-1]==2019


def test_merged_duplicate_draw_and_selected_hand_copy_are_removed_at_exact_positions():
    game,_=setup();p=game.player_list[0];p.hand_tiles.append(2019);p.has_draw_slot=True
    live.reorder(game,p,request(game,p,[2019,22,2019,11,2019],draw_index=0))
    state=live.layout_for_viewer(game,p,0);drawn=state['drawn_key']
    assert live.ordered_reveal(game,0,[11,2019,2019,22,2019],True)==[22,2019,11,2019,2019]
    p.hand_tiles.pop();p.has_draw_slot=False
    assert live.note_cut(game,p,2019,True,0)==drawn
    layout=live.layout_for_viewer(game,p,0)
    assert layout['tiles']==[22,2019,11,2019] and drawn not in layout['keys']
    selected=layout['keys'][-1];p.hand_tiles.remove(2019)
    assert live.note_cut(game,p,2019,False,3)==selected
    assert live.layout_for_viewer(game,p,0)['tiles']==[22,2019,11]


def test_meld_consumes_first_matching_copies_in_display_order():
    game,_=setup();p=game.player_list[0]
    live.reorder(game,p,request(game,p,[2019,22,2019,11]))
    p.hand_tiles.remove(2019)
    assert live.layout_for_viewer(game,p,0)['tiles']==[22,2019,11]


def test_identical_copies_can_exchange_physical_positions_and_invalid_keys_are_rejected():
    game,_=setup();p=game.player_list[0]
    before=live.layout_for_viewer(game,p,0);keys=before['keys'][:];keys[1],keys[2]=keys[2],keys[1]
    assert live.reorder(game,p,request(game,p,before['tiles'],keys=keys))==(True,'',True)
    after=live.layout_for_viewer(game,p,0)
    assert after['tiles']==before['tiles'] and after['keys']==keys
    for invalid in ([keys[0]]*4,[True,*keys[1:]],[*keys[:-1],999],list(reversed(keys))):
        assert live.reorder(game,p,request(game,p,after['tiles'],keys=invalid))[1]=='keys'
        assert live.layout_for_viewer(game,p,0)==after


@pytest.mark.parametrize('status',['waiting','waiting_ready','END'])
def test_no_reorder_during_settlement(status):
    game,_=setup();game.game_status=status;p=game.player_list[0]
    assert live.reorder(game,p,request(game,p))[1]=='inactive'


def test_claim_protection_does_not_leak_queued_gameplay():
    game,_=setup();game._cp_active=True;p=game.player_list[0]
    assert live.reorder(game,p,request(game,p))[1]=='busy'


def test_network_route_and_record_hide_every_opponents_opaque_face():
    async def run():
        game,sockets=setup();p=game.player_list[0]
        await handle_gamestate_message(game.game_server,str(p.user_id),request(game,p,[22,2019,11,2019]),sockets[p.user_id].websocket)
        for viewer in game.player_list:
            msg=sockets[viewer.user_id].websocket.send_json.call_args.args[0]
            assert msg['success'] and msg['type']=='gamestate/riichi/hand_order'
            assert msg['washizu_layouts'][0]['tiles']==([22,2019,11,2019] if viewer==p else [0,2019,0,2019])
            assert msg['hand_order_request_id']==(7 if viewer==p else None)
        tick=game.game_record['game_round'][f'round_index_{game.round_index}']['action_ticks'][-1]
        assert tick[:5]==['wo',0,'[22, 2019, 11, 2019]',-1,'F']
        assert __import__('json').loads(tick[5])==live.layout_for_viewer(game,p,0)['keys']
        assert game.send_to_realtime_spectators.await_count==4
    asyncio.run(run())


def test_spectator_cannot_reorder_another_hand():
    async def run():
        game,sockets=setup();websocket=AsyncMock()
        game.game_server.players['observer']=SimpleNamespace(user_id=99999)
        await handle_gamestate_message(game.game_server,'observer',request(game,game.player_list[0]),websocket)
        assert websocket.send_json.call_args.args[0]['reason']=='unauthorized'
        assert all(not conn.websocket.send_json.called for conn in sockets.values())
    asyncio.run(run())


def test_start_and_action_snapshots_are_view_scoped(caplog):
    async def run():
        game,sockets=setup()
        await broadcast_game_start(game)
        await broadcast_do_action(game,['cut'],0,cut_tile=11)
        for viewer in game.player_list:
            for call in sockets[viewer.user_id].websocket.send_json.call_args_list:
                message=call.args[0];info=message.get('game_info') or message.get('do_action_info')
                for seat,layout in info['washizu_layouts'].items():
                    assert all(t==0 or t>=2000 for t in layout['tiles']) if seat!=viewer.player_index else 0 not in layout['tiles']
        assert not any(r.levelname=='ERROR' for r in caplog.records)
    asyncio.run(run())


def test_variant_uses_the_same_physical_pool_and_room_constraints():
    from ...room.room_validators import RiichiRoomValidator
    room=RiichiRoomValidator(sub_rule=REALTIME_SUB_RULE,room_name='test',game_round=1,round_timer=20,step_timer=5,red_dora=True,open_xiru=True,hepai_way='multi_ron')
    assert not room.red_dora and not room.open_xiru and room.hepai_way=='head_bump'
    game,_=setup()
    for p in game.player_list:p.hand_tiles=[];p.has_draw_slot=False
    init_riichi_tiles(game)
    assert Counter(game.tiles_list+game.dora_indicators+sum((p.hand_tiles for p in game.player_list),[]))==Counter(make_tiles())
    assert len(game.tiles_list)-game.dead_wall_count==70


def test_settlement_reveals_in_current_order_with_independent_winning_tile():
    game,_=setup();p=game.player_list[0]
    live.reorder(game,p,request(game,p,[22,2019,11,2019]))
    assert live.ordered_reveal(game,0,[11,2019,22,2019,2041],True)==[22,2019,11,2019,2041]
    assert live.ordered_reveal(game,0,[11,2019,22,2019])==[22,2019,11,2019]
    game.sub_rule='riichi/washizu'
    assert live.ordered_reveal(game,0,[11,2019,22,2019])==[11,2019,22,2019]


def test_noop_ack_does_not_add_record_or_restart_action_clock():
    async def run():
        game,sockets=setup();p=game.player_list[0]
        layout=live.layout_for_viewer(game,p,0)
        before=game.server_action_tick
        await handle_gamestate_message(game.game_server,str(p.user_id),request(game,p,layout['tiles']),sockets[p.user_id].websocket)
        assert game.server_action_tick==before
        assert not game.game_record['game_round'][f'round_index_{game.round_index}']['action_ticks']
        assert sockets[p.user_id].websocket.send_json.call_args.args[0]['success']
        assert all(not sockets[v.user_id].websocket.send_json.called for v in game.player_list[1:])
    asyncio.run(run())
