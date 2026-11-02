"""Reconnect is a seat-scoped snapshot: no score execution or new ready budget."""
import asyncio
from contextlib import suppress
from copy import deepcopy
import importlib
from types import SimpleNamespace

import pytest

from . import settlement_sync as sync
from .test_action_window import ADAPTERS, make_state, drain_all
from .test_ready_phase import DRIVER_RULES, until
from .lifecycle import cancel_auxiliary_tasks
from ..verifier.host import build_db_manager, build_game_server


@pytest.mark.parametrize("adapter", ADAPTERS, ids=[a[0] for a in ADAPTERS])
def test_every_legacy_rule_restores_result_when_all_humans_missed_it(adapter):
    async def run():
        state, broadcast, _ = make_state(adapter)
        state.room_id = 1
        state.game_status = "END"
        for player in state.player_list:
            player.tag_list.append("offline")
        scores = {p.player_index: p.score for p in state.player_list}
        try:
            await broadcast.broadcast_result(state, hu_class="hu_self", hepai_player_index=0,
                player_to_score=scores, hu_score=8, hu_fan=[],
                hepai_player_hand=list(state.player_list[0].hand_tiles),
                score_changes={0: 24, 1: -8, 2: -8, 3: -8})
            await drain_all(state)
            state.game_status = "waiting_ready"
            state.action_dict = {0: [], 1: ["ready"], 2: ["ready"], 3: []}
            now = [100.0]
            sync.begin_ready(state, 8, clock=lambda: now[0])
            now[0] += 5.25
            before = [(p.score, deepcopy(p.score_history)) for p in state.player_list]
            ids = set()
            for seat in range(4):
                packets = []
                async def capture(payload): packets.append(payload)
                player = state.player_list[seat]
                state.game_server.user_id_to_connection[player.user_id].websocket.send_json = capture
                await state.player_reconnect(player.user_id)
                start = next(p for p in packets if p['type'].endswith('/game_start'))
                result = start['settlement_restore'][0]
                metadata = result['settlement_sync']
                ids.add(metadata['result_id'])
                assert metadata['restore'] and metadata['remaining_ms'] == 2750
                assert metadata['player_to_ready'] == {'0': True, '1': False, '2': False, '3': True}
                assert 'game_info' not in result
                assert result['show_result_info']['player_to_score'] == scores
                assert sync.accepts_ready(state, metadata['result_id'])
            assert len(ids) == 1
            assert [(p.score, p.score_history) for p in state.player_list] == before
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


def test_sichuan_final_history_survives_partial_and_repeated_restore():
    async def run():
        state, broadcast, _ = make_state(next(a for a in ADAPTERS if a[0] == 'sichuan'))
        state.room_id = 1
        state.game_status = 'END'
        try:
            for seat, player in enumerate(state.player_list):
                player.score = 24 if seat == 0 else -8
                player.tag_list.append('offline')
            await broadcast.broadcast_result(state, hu_class='hu_self', liuju_step='settle_hu',
                hepai_player_index=0, player_to_score={i:p.score for i,p in enumerate(state.player_list)},
                score_changes={0:24,1:-8,2:-8,3:-8}, liuju_status_final=False,
                next_status='round_continue')
            assert all(not p.score_history for p in state.player_list)
            state.player_list[0].score += 8
            state.player_list[1].score -= 8
            for _ in range(2):
                await broadcast.broadcast_result(state, hu_class='liuju', liuju_step='chajiao',
                    player_to_score={i:p.score for i,p in enumerate(state.player_list)},
                    score_changes={0:8,1:-8,2:0,3:0}, liuju_status_final=True,
                    next_status='round_end_by_ready')
            assert [p.score_history for p in state.player_list] == [['+32'],['-16'],['-8'],['-8']]
            before=deepcopy([(p.score,p.score_history) for p in state.player_list])
            state.game_status='waiting_ready';state.action_dict={0:[],1:['ready'],2:[],3:['ready']}
            sync.begin_ready(state,8)
            player=state.player_list[1]
            packets=[]
            async def capture(packet):packets.append(packet)
            state.game_server.user_id_to_connection[player.user_id].websocket.send_json=capture
            for _ in range(2): await state.player_reconnect(player.user_id)
            snapshots=[p for p in packets if p['type'].endswith('/game_start')]
            assert len(snapshots)==2
            for packet in snapshots:
                assert packet['game_info']['players_info'][0]['score_history']==['+32']
                assert packet['settlement_restore'][0]['show_result_info']['liuju_step']=='chajiao'
            assert [(p.score,p.score_history) for p in state.player_list]==before
        finally: await cancel_auxiliary_tasks(state)
    asyncio.run(run())


def test_router_rejects_old_result_confirmation_before_dispatch():
    from unittest.mock import AsyncMock, patch
    from ..gamestate_router import handle_gamestate_message
    state=SimpleNamespace(gamestate_id='ready-route',current_round=1,game_status='waiting_ready',player_list=[],action_dict={})
    result=sync.prepare_packet(state,{'type':'gamestate/guobiao/show_result',
        'show_result_info':{'hu_class':'hu_self','player_to_score':{0:24}}},0)
    server=SimpleNamespace(players={},gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _:state))
    socket=SimpleNamespace(send_json=AsyncMock())
    packet=dict(type='gamestate/GB/send_action',gamestate_id=state.gamestate_id,action='ready',settlement_id='old')
    async def run():
        with patch('server.gamestate.gamestate_router.handle_send_action',new_callable=AsyncMock) as dispatch:
            await handle_gamestate_message(server,'test',packet,socket)
            dispatch.assert_not_awaited()
            packet['settlement_id']=result['settlement_sync']['result_id']
            await handle_gamestate_message(server,'test',packet,socket)
            dispatch.assert_awaited_once()
    asyncio.run(run())


DRIVERS = DRIVER_RULES + [('game_shangrao', 'ShangraoGameState'), ('game_duocai', 'DuocaiGameState')]


@pytest.mark.parametrize('package,name', DRIVERS, ids=[x[1] for x in DRIVERS])
def test_driver_delivery_restores_current_seat_and_deadline(package, name):
    async def run():
        cls = getattr(importlib.import_module(f'server.gamestate.{package}.{name}'), name)
        state = cls(calculation_service=object())
        state.initialize_round()
        if hasattr(state, 'machine'): state.machine.phase = type(state.machine.phase)('END')
        else: state.game_status = 'END'
        state.game_server = build_game_server(build_db_manager(), messages=[])
        for player, uid in zip(state.player_list, state.game_server.user_id_to_connection): player.user_id = uid
        # The real delivery method must cache the projection even without a socket.
        for seat, player in enumerate(state.player_list):
            conn = state.game_server.user_id_to_connection.pop(player.user_id)
            await state.send_payload_to_player(seat, {'type': f'gamestate/{state.room_rule}/show_result',
                'show_result_info': {'hu_class': 'hu_self', 'hepai_player_index': 0,
                    'player_to_score': {i: p.score for i,p in enumerate(state.player_list)},
                    'private_test_projection': seat}})
            state.game_server.user_id_to_connection[player.user_id] = conn
        task = asyncio.create_task(state.run_round_ready_phase(timeout=8))
        try:
            await until(lambda: state.game_status == 'waiting_ready', task)
            await state.submit_action(0, 'ready')
            await until(lambda: state.action_queues[0].empty(), task)
            for seat in range(len(state.player_list)):
                packet = sync.prepare_packet(state, state.build_game_start_payload(seat), seat)
                result = packet['settlement_restore'][0]
                assert result['show_result_info']['private_test_projection'] == seat
                assert 0 < result['settlement_sync']['remaining_ms'] <= 8000
                assert result['settlement_sync']['player_to_ready']['0'] is True
                assert result['settlement_sync']['player_to_ready']['1'] is False
            for seat in range(1,len(state.player_list)): await state.submit_action(seat,'ready')
            await asyncio.wait_for(task,.5)
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError): await task
            await state.cleanup_game_state()
    asyncio.run(run())


def test_steps_duplicates_private_projections_and_new_round():
    state = SimpleNamespace(gamestate_id='test', current_round=1, game_status='END', player_list=[])
    def result(step, seat=0):
        return {'type':'gamestate/sichuan/show_result','show_result_info':{
            'hu_class':'liuju', 'liuju_step':step, 'cha_payer_index':seat,
            'player_to_score':{0:20,1:0}, 'private':seat}}
    first=sync.prepare_packet(state,result('chajiao'),0)
    second=sync.prepare_packet(state,result('chajiao',1),1)
    assert first['settlement_sync']['result_id'] != second['settlement_sync']['result_id']
    # Earlier step replay cannot displace the current step.
    sync.prepare_packet(state,result('chajiao'),0)
    assert sync.snapshot(state)['result_id'] == second['settlement_sync']['result_id']
    assert sync.restore_result(state,0) is None
    state.game_status='waiting_ready';state.action_dict={}
    clock=[50.0];sync.begin_ready(state,8,clock=lambda:clock[0]);clock[0]+=7
    assert sync.restore_result(state,1)['settlement_sync']['remaining_ms']==1000
    assert not sync.accepts_ready(state,first['settlement_sync']['result_id'])
    clock[0]+=2
    assert sync.snapshot(state)['remaining_ms']==0
    state.current_round=2;state.game_status='waiting_hand_action'
    assert sync.restore_result(state,1) is None
    assert not sync.accepts_ready(state,second['settlement_sync']['result_id'])


@pytest.mark.parametrize('stage', ['self_draw', 'ron', 'multi_ron'])
def test_hongque_native_reconnect_uses_same_result_and_ready_clock(stage):
    from ..game_hongque.test_hongque_reconnect import stage_fixture
    async def run():
        state, server = await stage_fixture(stage)
        now = [100.0]
        state._ready_players = {0, 2}
        sync.begin_ready(state, 8, clock=lambda: now[0])
        now[0] += 5
        before = deepcopy([(p.score,p.score_history) for p in state.players])
        ids=set()
        for player in state.players:
            socket=server.user_id_to_connection[player.user_id].websocket
            socket.packets.clear()
            await state.player_reconnect(player.user_id)
            packet=socket.packets[0]; metadata=packet['settlement_sync']
            assert metadata['restore'] and metadata['remaining_ms']==3000
            assert metadata['player_to_ready']=={'0':True,'1':False,'2':True,'3':False}
            assert 'settlement_restore' not in packet
            ids.add(metadata['result_id'])
        assert len(ids)==1
        assert [(p.score,p.score_history) for p in state.players]==before
    asyncio.run(run())


def test_public_terminal_cache_and_delayed_packet_cannot_cross_rounds():
    state=SimpleNamespace(gamestate_id='derived',current_round=1,game_status='waiting_ready',
        player_list=[],action_dict={},_terminal_result={'type':'gamestate/tuidao/show_result',
            'show_result_info':{'hu_class':'hu_self','hepai_player_index':0,'player_to_score':{0:24}}})
    packet=sync.prepare_packet(state,{'type':'gamestate/tuidao/game_start'},0)
    assert packet['settlement_restore'][0]['settlement_sync']['restore']
    delayed=deepcopy(state._terminal_result)
    state.current_round+=1
    sent=[]
    async def send(payload):sent.append(payload)
    asyncio.run(sync.send_game_packet(state,SimpleNamespace(send_json=send),delayed,0))
    assert not sent


DERIVED_RULES = [('changchun', 'ChangchunGameState'), ('shenyang', 'ShenyangGameState'),
    ('jiujiang', 'JiujiangGameState'), ('singapore', 'SingaporeGameState'),
    ('guangdong', 'GuangdongGameState'), ('hongzhong', 'HongzhongGameState'),
    ('wuhan', 'WuhanGameState'), ('tuidao', 'TuidaoGameState'), ('shanxi', 'ShanxiGameState'),
    ('shanghai', 'ShanghaiGameState'), ('shanghai', 'QinghunpengGameState'), ('shanghai', 'LajiheGameState')]


def make_derived_state(rule, name):
    from ..verifier.host import build_room_data
    from .game_record_manager import init_game_record, init_game_round
    module = importlib.import_module(f'server.gamestate.game_{rule}.{name}')
    cls = getattr(module, name)
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=20261017, room_id=1, hepai_limit=0)
    room.update(room_rule=rule, sub_rule=f'{rule}/standard', round_timer=20, step_timer=5,
                tips=False, claim_protection=False)
    room.pop('sub_rule')  # Each rule selects its declared, supported default.
    state = cls(server, room, server.calculation_service, db, 'sync-'+rule)
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
        player.hand_tiles = [11,12,13,14,15,16,21,22,23,31,32,33,41]
    state.current_player_index = 0
    state.tiles_list = [19,29,39]*6
    state.server_action_tick = 12
    state.game_status = 'END'
    init_game_record(state); init_game_round(state)
    return state


@pytest.mark.parametrize('rule,name', DERIVED_RULES, ids=[x[0] for x in DERIVED_RULES])
def test_public_terminal_family_binds_clock_before_any_player_reconnects(rule, name):
    from ..game_tuidao.result import broadcast_result as public_result
    from ..game_taiwan.boardcast import broadcast_result as projected_result
    broadcast_result = projected_result if rule == 'shanghai' else public_result
    async def run():
        state = make_derived_state(rule, name)
        for player in state.player_list: player.tag_list.append('offline')
        try:
            await broadcast_result(state, hu_class='hu_self', hepai_player_index=0,
                hepai_player_hand=state.player_list[0].hand_tiles+[41], hu_score=8, hu_fan=[],
                player_to_score={i:p.score for i,p in enumerate(state.player_list)},
                score_changes={0:24,1:-8,2:-8,3:-8}, next_status='round_end_by_ready')
            state.game_status='waiting_ready'
            state.action_dict={0:[],1:['ready'],2:[],3:['ready']}
            now=[100.0];sync.begin_ready(state,8,clock=lambda:now[0]);now[0]+=6
            before=deepcopy([(p.score,p.score_history) for p in state.player_list])
            for player in state.player_list:
                packets=[]
                async def capture(packet): packets.append(packet)
                state.game_server.user_id_to_connection[player.user_id].websocket.send_json=capture
                await state.player_reconnect(player.user_id)
                packet=next(p for p in packets if p['type'].endswith('/game_start'))
                metadata=packet['settlement_restore'][0]['settlement_sync']
                assert metadata['remaining_ms']==2000
                assert metadata['player_to_ready']=={'0':True,'1':False,'2':True,'3':False}
                assert sync.accepts_ready(state,metadata['result_id'])
            assert [(p.score,p.score_history) for p in state.player_list]==before
        finally: await cancel_auxiliary_tasks(state)
    asyncio.run(run())
