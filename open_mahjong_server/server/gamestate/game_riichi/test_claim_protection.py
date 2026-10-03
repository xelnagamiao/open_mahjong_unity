"""Exercise Riichi's real cut, declaration, dora and response wait boundaries."""
import asyncio
import time
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock
import pytest
from .test_double_riichi import make_game
from . import boardcast as b
from .wait_action import _execute_cut, wait_action
from ..public.claim_protection import finalize_claim_protection,end_claim_protection_interval
from ..public.outbound_pipe import drain_viewer,close_outbound_pipes


@pytest.mark.parametrize('enabled',[False,True])
@pytest.mark.parametrize('riichi',[False,True])
@pytest.mark.parametrize('outcome',['pass','timeout','peng','ron','none'])
def test_actual_cut_flow_keeps_declaration_dora_and_tags_in_order(monkeypatch,enabled,riichi,outcome):
    async def run():
        g,_=make_game(0);g.claim_protection=enabled;g.claim_protect_delay=.1;g.claim_meld_followup_gap=.02
        wire={i:[] for i in range(4)}
        for i,p in enumerate(g.player_list):
            async def send(payload,seat=i):wire[seat].append(payload)
            g.game_server.user_id_to_connection[p.user_id]=NS(websocket=NS(send_json=send))
        actions={0:[],1:['hu_first','pass'] if outcome=='ron' else ([] if outcome=='none' else ['peng','pass']),2:[],3:[]}
        g.player_list[1].hand_tiles=[47,47]+[11]*11
        g.send_to_realtime_spectators=AsyncMock()
        monkeypatch.setattr('server.gamestate.game_riichi.wait_action.check_action_after_cut',lambda *_:{k:list(v) for k,v in actions.items()})
        g._pending_kan_dora_count=1
        # Real reveal implementation must finish updating scoring inputs before action eligibility.
        g.dead_wall_count=14;g.rinshan_count=1
        try:
            await _execute_cut(g,0,47,True,None,riichi)
            protected=enabled and outcome in ('pass','timeout','peng')
            if protected: assert not any('do_action_info' in p for p in wire[2])
            if outcome in ('pass','timeout','peng'):
                g.step_time=0
                for p in g.player_list:p.remaining_time=0 if outcome=='timeout' else 10
                if outcome!='timeout':
                    async def respond():
                        await asyncio.sleep(.01)
                        await g.action_queues[1].put(dict(action_type='peng' if outcome=='peng' else 'pass',target_tile=47))
                        g.action_events[1].set()
                    task=asyncio.create_task(respond())
                await asyncio.wait_for(wait_action(g),1)
                if outcome!='timeout':await task
            else:await finalize_claim_protection(g,b._send_do_action_payload_to_viewer)
            await asyncio.gather(*(drain_viewer(g,i) for i in range(4)))
            for messages in wire.values():
                types=[p['type'].rsplit('/',1)[-1] for p in messages]
                cut=next(i for i,p in enumerate(messages) if p.get('do_action_info',{}).get('action_list')==['cut'])
                assert types.index('update_dora')>cut
                if riichi:assert types.index('declare_riichi')<cut
                if outcome=='peng':assert any(p.get('do_action_info',{}).get('action_list')==['peng'] for p in messages)
            assert not g._cp_active
            assert g.kan_dora_indicators
            assert g.game_status==('onlycut_after_action' if outcome=='peng' else 'waiting_action_after_cut' if outcome=='ron' else 'deal_card')
        finally:
            close_outbound_pipes(g);end_claim_protection_interval(g)
    asyncio.run(run())


def test_hidden_observer_does_not_receive_other_action_ask(monkeypatch):
    async def run():
        g,_=make_game(0);g.claim_protection=True;g.game_status='waiting_action_after_cut'
        g.player_list[0].discard_tiles=[47];g.action_dict={0:[],1:['peng','pass'],2:[],3:[]}
        sockets={p.user_id:NS(websocket=NS(send_json=AsyncMock())) for p in g.player_list}
        g.game_server.user_id_to_connection=sockets;g.send_to_realtime_spectators=AsyncMock()
        await b.broadcast_ask_other_action(g)
        assert sockets[101].websocket.send_json.await_count==1
        assert sockets[102].websocket.send_json.await_count==0
        close_outbound_pipes(g)
    asyncio.run(run())

@pytest.mark.parametrize('spectator',[False,True])
def test_reconnect_and_spectator_snapshot_wait_for_reserved_cut(spectator):
    from .test_protocol import connected
    from ..public.claim_protection import begin_claim_protection_interval
    async def run():
        g,sockets=connected();g.claim_protection=True;g.claim_protect_delay=5
        g.player_list[0].discard_tiles=[47]
        begin_claim_protection_interval(g,{1:['peng','pass']},0)
        await b.broadcast_do_action(g,['cut'],0,cut_tile=47)
        if spectator:
            sockets[999]=NS(websocket=NS(send_json=AsyncMock()))
            task=asyncio.create_task(g.send_realtime_spectator_snapshot(999,2))
            target=sockets[999].websocket
        else:
            task=asyncio.create_task(g.player_reconnect(102));target=sockets[102].websocket
        try:
            await asyncio.sleep(.01);assert not task.done()
            g.player_list[2].score=34500
            await finalize_claim_protection(g,b._send_do_action_payload_to_viewer)
            await asyncio.wait_for(task,.5)
            starts=[c.args[0] for c in target.send_json.call_args_list if c.args[0]['type'].endswith('/game_start')]
            assert len(starts)==1
            info=starts[0]['game_info']
            assert info['players_info'][2]['score']==34500
            assert info['claim_protection'] is True
            assert 'hand_tiles' not in info['players_info'][0]
        finally:close_outbound_pipes(g);end_claim_protection_interval(g)
    asyncio.run(run())


@pytest.mark.parametrize('enabled', [False, True])
@pytest.mark.parametrize('action,pair,combination', [
    ('chi_left', [13, 14], 's14'),
    ('chi_mid', [14, 16], 's15'),
    ('chi_right', [16, 17], 's16'),
    ('peng', [15, 15], 'k15'),
    ('gang', [15, 15, 15], 'g15'),
])
def test_each_real_meld_releases_cut_before_meld_and_continues(monkeypatch, enabled, action, pair, combination):
    async def run():
        g, _ = make_game(0)
        g.claim_protection = enabled
        g.claim_protect_delay = 1
        g.claim_meld_followup_gap = .01
        g.claim_meld_post_gap = .01
        g.player_list[0].hand_tiles[-1] = 15
        g.player_list[1].hand_tiles = pair + [21, 22, 23, 24, 26, 27, 31, 32, 33, 41, 42][:13-len(pair)]
        wire = {i: [] for i in range(4)}
        for i, player in enumerate(g.player_list):
            async def send(payload, seat=i): wire[seat].append(payload)
            g.game_server.user_id_to_connection[player.user_id] = NS(websocket=NS(send_json=send))
        g.send_to_realtime_spectators = AsyncMock()
        monkeypatch.setattr('server.gamestate.game_riichi.wait_action.check_action_after_cut',
                            lambda *_: {0: [], 1: [action, 'pass'], 2: [], 3: []})
        response_task = None
        try:
            assert await _execute_cut(g, 0, 15, True, None, False)
            if enabled: assert not any('do_action_info' in p for p in wire[2])
            async def respond():
                await asyncio.sleep(.001)
                await g.action_queues[1].put(dict(action_type=action, target_tile=15))
                g.action_events[1].set()
            response_task = asyncio.create_task(respond())
            await asyncio.wait_for(wait_action(g), 1)
            await response_task
            await asyncio.gather(*(drain_viewer(g, i) for i in range(4)))
            assert g.current_player_index == 1
            assert g.player_list[1].combination_tiles == [combination]
            assert g.player_list[0].discard_tiles == []
            assert not g._cp_active
            assert g.game_status == ('deal_card_after_gang' if action == 'gang' else 'onlycut_after_action')
            for messages in wire.values():
                actions = [p['do_action_info']['action_list'] for p in messages if 'do_action_info' in p]
                assert actions == [['cut'], [action]]
        finally:
            if response_task is not None and not response_task.done(): response_task.cancel()
            close_outbound_pipes(g)
            end_claim_protection_interval(g)
    asyncio.run(run())
