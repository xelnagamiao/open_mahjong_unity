"""Authoritative structural hints serve either display switch, including restore packets."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock
import pytest
from server.gamestate.game_hangzhou import hints
from server.gamestate.game_hangzhou.test_flow import FLOAT, turn, act
from server.response import Response

@pytest.mark.parametrize('hand', [(), (11,) * 12, (11,) * 15])
def test_incomplete_snapshot_never_invents_structural_waits(hand):
    request=hints.HintRequest(hand, (), tuple(sorted(set(hand))), 'mil-hangzhou-2025-om1')
    wire=hints.to_wire(request, hints.compute(request))
    assert wire['waiting_tiles']==[] and wire['waiting_by_discard']=={}

@pytest.mark.parametrize('tips,count_tips', [(False,False),(False,True),(True,False),(True,True)])
@pytest.mark.parametrize('bot', [False,True])
def test_private_structural_hints_follow_either_display_switch(tips,count_tips,bot):
    s=turn(FLOAT,tips=tips,count_tips=count_tips)
    if bot: s.player_list[0].user_id=1
    value=hints.private_hints(s,0)
    enabled=(tips or count_tips) and not bot
    assert bool(value)==enabled
    if enabled:
        assert value['source_hand_tiles']==FLOAT
        assert value['waiting_by_discard'][46]
        packet=Response(**s.build_game_start_payload(0)).model_dump(exclude_none=True)
        assert packet['game_info']['hangzhou_info']['source_hand_tiles']==FLOAT
        assert packet['game_info']['tips']==tips and packet['game_info']['count_tips']==count_tips
    else:
        assert 'source_hand_tiles' not in s.build_game_start_payload(0)['game_info']['hangzhou_info']
    assert hints.current_request(s,0,for_record=True) is not None

@pytest.mark.parametrize('tips,count_tips', [(False,False),(False,True),(True,False),(True,True)])
@pytest.mark.parametrize('bot', [False,True])
def test_async_snapshot_and_record_hints_follow_either_display_switch(tips,count_tips,bot):
    async def run():
        s=turn(FLOAT,tips=tips,count_tips=count_tips)
        if bot: s.player_list[0].user_id=1
        s._async_hints_enabled=True
        payload=s.build_game_start_payload(0)
        assert await hints.prepare(s,payloads=[payload],indices=(0,))
        enabled=(tips or count_tips) and not bot
        wire=payload['game_info']['hangzhou_info']
        assert ('source_hand_tiles' in wire)==enabled
        if enabled:
            assert wire['source_hand_tiles']==FLOAT
            assert wire['waiting_by_discard'][46]
            assert hints.private_hints(s,0)['waiting_by_discard']==wire['waiting_by_discard']
        events=s.game_record['game_round']['round_index_1']['action_ticks']
        recorded=[e[3] for e in events if e[:3]==['hangzhou','hints',0]]
        assert recorded[-1]['source_hand_tiles']==FLOAT and recorded[-1]['waiting_by_discard'][46]
    asyncio.run(run())

@pytest.mark.parametrize('tips,count_tips', [(False,True),(True,False),(True,True)])
def test_inactive_viewer_spectator_and_player_restore_have_ready_hints(tips,count_tips):
    async def run():
        hand=[11,12,13,14,15,16,21,22,23,31,32,33,45,45]
        s=turn(hand,tips=tips,count_tips=count_tips,round_timer=20,step_timer=5)
        act(s,0,'cut',TileId=45,cutClass=True); act(s)
        assert s.current_player_index==1 and not s.player_list[0].has_draw_slot
        s._async_hints_enabled=True
        websocket=SimpleNamespace(send_json=AsyncMock())
        s.game_server=SimpleNamespace(user_id_to_connection={999:SimpleNamespace(websocket=websocket)})
        await s.send_realtime_spectator_snapshot(999,0)
        packets=[Response(**call.args[0]).model_dump(exclude_none=True) for call in websocket.send_json.await_args_list]
        start=next(p for p in packets if p['type']=='gamestate/hangzhou/game_start')
        wire=start['game_info']['hangzhou_info']
        assert wire['waiting_tiles']==[45,46] and len(wire['source_hand_tiles'])==13
        assert all(p.get('hand_tiles') is None for p in start['game_info']['players_info'] if p['player_index']!=0)
        s.send_payload_to_player=AsyncMock()
        await s.player_reconnect(101)
        restored=[call.args[1] for call in s.send_payload_to_player.await_args_list]
        start=next(p for p in restored if p['type']=='gamestate/hangzhou/game_start')
        assert start['game_info']['hangzhou_info']['waiting_tiles']==[45,46]
    asyncio.run(run())
