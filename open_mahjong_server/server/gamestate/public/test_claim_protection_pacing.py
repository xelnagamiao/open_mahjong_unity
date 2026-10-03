"""Wire-level matrix for every broadcaster using shared claim protection."""
import asyncio
import importlib
import time
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .ai import pacing
from .claim_protection import (begin_claim_protection_interval, finalize_claim_protection,
    init_claim_protection_state, end_claim_protection_interval, room_claim_protection_enabled,
    supports_claim_protection)
from .outbound_pipe import close_outbound_pipes, drain_viewer
from server.response import Response

ADAPTERS = [('guobiao','game_guobiao'), ('qingque','game_mmcr'),
            ('changsha','game_changsha'), ('sichuan','game_sichuan'), ('riichi','game_riichi')]
SPEEDS = {'instant':0, 'fast':.02, 'medium':.04, 'slow':.06}


def wire_state(monkeypatch, rule, speed='fast', enabled=True, bot=False):
    for k,v in SPEEDS.items(): monkeypatch.setitem(pacing.BOT_SPEEDS,k,v)
    players=[NS(user_id=uid,username=str(uid),player_index=i,tag_list=[],hand_tiles=[11],
                has_draw_slot=True,discard_tiles=[11],chi_candidates=[],remaining_time=30)
             for i,uid in enumerate([0 if bot else 100,101,102,2 if bot else 103])]
    wire={i:[] for i in range(4)}; conns={}
    for i,p in enumerate(players):
        async def send(payload,seat=i): wire[seat].append((time.monotonic(),payload))
        conns[p.user_id]=NS(websocket=NS(send_json=send))
    s=NS(room_rule=rule,player_list=players,bot_speed=speed,claim_protection=enabled,
         claim_protect_delay=.1,claim_meld_followup_gap=.04,claim_meld_post_gap=.02,
         server_action_tick=1,current_player_index=0,game_status='waiting_hand_action',
         waiting_players_list=[0],game_server=NS(user_id_to_connection=conns),
         send_to_realtime_spectators=AsyncMock(),lifecycle_state='running')
    init_claim_protection_state(s)
    return s,wire


async def begin(s,b):
    response=Response(type=f'gamestate/{s.room_rule}/broadcast_hand_action',success=True,message="test")
    for i in [1,2,3]: await b._send_ask_response_to_viewer(s,i,response,block=False)
    await asyncio.gather(*(drain_viewer(s,i) for i in [1,2,3]))


@pytest.mark.parametrize('rule,module',ADAPTERS)
@pytest.mark.parametrize('enabled',[True,False])
@pytest.mark.parametrize('speed',SPEEDS)
@pytest.mark.parametrize('outcome',['none','pass','meld','hu'])
@pytest.mark.parametrize('bot',[False,True])
def test_wire_timing_matrix(monkeypatch,caplog,rule,module,enabled,speed,outcome,bot):
    async def run():
        b=importlib.import_module('server.gamestate.'+module+'.boardcast')
        s,w=wire_state(monkeypatch,rule,speed,enabled,bot=bot)
        try:
            await begin(s,b)
            actions={} if outcome=='none' else {1:['hu_first','pass'] if outcome=='hu' else ['peng','pass']}
            begin_claim_protection_interval(s,actions,0)
            await b.broadcast_do_action(s,['cut'],0,cut_tile=11)
            protected=enabled and not bot and outcome in ('pass','meld')
            if protected:
                assert len(w[2])==1
                assert w[1][-1][1]['do_action_info']['cut_tile']==11
            if outcome=='meld':
                await b.broadcast_do_action(s,['peng'],1,cut_tile=11)
            else:
                await finalize_claim_protection(s,b._send_do_action_payload_to_viewer)
            await asyncio.gather(*(drain_viewer(s,i) for i in [1,2,3]))
            cuts=[(t,p['do_action_info']) for t,p in w[2] if p.get('do_action_info',{}).get('action_list')==['cut']]
            assert len(cuts)==1
            if bot:
                assert not s._cp_active and not s._cp_pending_cut and s._cp_timer_task is None
                assert cuts[0][0]-w[2][0][0] < .04  # No presentation wait after actual AI submission.
            if protected and outcome=='meld':
                assert w[2][-1][0]-cuts[0][0] >= .023
            assert not any(rec.levelname=='ERROR' for rec in caplog.records)
        finally:
            end_claim_protection_interval(s);close_outbound_pipes(s)
    asyncio.run(run())


@pytest.mark.parametrize('rule,module',ADAPTERS)
@pytest.mark.parametrize('bot',[False,True])
def test_timer_reconnect_and_cleanup(monkeypatch,rule,module,bot):
    async def run():
        b=importlib.import_module('server.gamestate.'+module+'.boardcast')
        s,w=wire_state(monkeypatch,rule,bot=bot)
        try:
            await begin(s,b)
            begin_claim_protection_interval(s,{1:['peng','pass']},0)
            await b.broadcast_do_action(s,['cut'],0,cut_tile=11)
            reconnect=asyncio.create_task(drain_viewer(s,2))
            await asyncio.sleep(.005)
            assert reconnect.done() if bot else not reconnect.done()
            await asyncio.wait_for(reconnect,.5)
            await finalize_claim_protection(s,b._send_do_action_payload_to_viewer)
            assert sum('do_action_info' in p for _,p in w[2])==1
            # Closing with a reserved cut must release all queue work.
            begin_claim_protection_interval(s,{1:['peng','pass']},0)
            await b.broadcast_do_action(s,['cut'],0,cut_tile=11)
            close_outbound_pipes(s);end_claim_protection_interval(s)
            await asyncio.sleep(.005)
        finally:
            close_outbound_pipes(s);end_claim_protection_interval(s)
    asyncio.run(run())


@pytest.mark.parametrize('rule',['guobiao','qingque','changsha','sichuan','zhongyong','nanque','jiandan','riichi','taiwan','classical','hongque','hongkong','shanghai','free'])
@pytest.mark.parametrize('enabled',[False,True])
@pytest.mark.parametrize('uid',[0,2,3,100])
def test_bot_gate_is_explicit_and_all_bot_types_share_it(rule,enabled,uid):
    room=dict(room_rule=rule,claim_protection=enabled,player_list=[uid])
    assert room_claim_protection_enabled(room) == (supports_claim_protection(rule) and enabled and uid>=10)


@pytest.mark.parametrize('rule,module',ADAPTERS)
def test_human_metadata_after_meld_retains_one_post_gap(monkeypatch,rule,module):
    from .outbound_pipe import schedule_viewer_send
    from .claim_protection import take_post_meld_gap_delay
    async def run():
        b=importlib.import_module('server.gamestate.'+module+'.boardcast')
        s,w=wire_state(monkeypatch,rule)
        monkeypatch.setitem(pacing.BOT_SPEEDS,'fast',.10)
        s.claim_meld_post_gap=.07
        try:
            await begin(s,b)
            begin_claim_protection_interval(s,{1:['peng','pass']},0)
            await b.broadcast_do_action(s,['cut'],0,cut_tile=11)
            await b.broadcast_do_action(s,['peng'],1,cut_tile=11)
            # Riichi tags/dora and Zhongyong draws can occupy this queue slot.
            async def metadata():w[2].append((time.monotonic(),{'type':'metadata'}))
            schedule_viewer_send(s,2,metadata,delay_before=take_post_meld_gap_delay(s,2))
            s.current_player_index=1;s.server_action_tick+=1
            response=Response(type=f'gamestate/{rule}/broadcast_hand_action',success=True,message='test')
            await b._send_ask_response_to_viewer(s,2,response,block=False)
            begin_claim_protection_interval(s,{},1)
            await b.broadcast_do_action(s,['cut'],1,cut_tile=12)
            await drain_viewer(s,2)
            meld=next(t for t,p in w[2] if p.get('do_action_info',{}).get('action_list')==['peng'])
            cut=next(t for t,p in w[2] if p.get('do_action_info',{}).get('cut_tile')==12)
            assert .052 <= cut-meld < .115  # One human post gap; no bot clock involved.
        finally:close_outbound_pipes(s);end_claim_protection_interval(s)
    asyncio.run(run())
