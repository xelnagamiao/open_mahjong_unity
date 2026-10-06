"""Focused feature acceptance tests using real Tuidao adapters and controlled clocks."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch
import pytest

from server.gamestate.game_tuidao.test_state import make_state, hand, HAND
from server.gamestate.game_tuidao.test_timing import ControlledClock, window
from server.gamestate.game_tuidao.test_adapters import room_manager, Socket
from server.gamestate.game_tuidao import timing
from server.gamestate.game_taiwan import boardcast
from server.gamestate.game_tuidao.tactical import broadcast_recheck
from server.gamestate.public.ai.get_action import get_action
from server.room.tuidao_room import TuidaoRoomValidator, enforce_tuidao_tactical_room, create_tuidao_room
from server.room.guangdong_room import handle_create_guangdong_room
from server.room.room_manager import RoomManager
from server.gamestate.gamestate_manager import GameStateManager


class FeatureClock(ControlledClock):
    def __init__(self):
        super().__init__()
        self.callbacks = []

    def later(self, delay, callback):
        self.callbacks.append((self.now + delay, callback))
        self.callbacks.sort(key=lambda item: item[0])

    async def wait(self, tasks, timeout):
        until = self.now + timeout
        if self.callbacks:
            until = min(until, self.callbacks[0][0])
        self.now = until
        while self.callbacks and self.callbacks[0][0] <= until:
            _, callback = self.callbacks.pop(0)
            result = callback()
            if asyncio.iscoroutine(result):
                await result
        await asyncio.sleep(0)
        return set(), set(tasks)

    def submit(self, state, delay, index, action, tick=None):
        def send():
            data = dict(action_type=action, _action_tick=state.server_action_tick if tick is None else tick)
            state.action_queues[index].put_nowait(data)
            state.action_events[index].set()
        self.later(delay, send)


@pytest.fixture
def clock(monkeypatch):
    value = FeatureClock()
    monkeypatch.setattr(timing, 'monotonic', lambda: value.now)
    monkeypatch.setattr(timing, 'wait_for_events', value.wait)
    return value


def setup_state(clock, actions, *, phase='waiting_action_after_cut', callback=None, **config):
    state = make_state(**({'tactical_call': True} | config))
    state.tactical_pre_grace_delay = 0
    state.player_list[0].discard_tiles = [28]
    state.jiagang_tile = 28 if phase == 'waiting_action_qianggang' else None
    asks, claims = [], []
    class Viewer(Socket):
        async def send_json(self, message):
            await super().send_json(message)
            info = message.get('ask_other_action_info')
            if info:
                asks.append(dict(info))
                if callback:
                    await callback(state, info)
            do = message.get('do_action_info')
            if do:
                claims.append(dict(do))
    for player in state.player_list:
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=Viewer())
    state.game_status = phase
    state.action_dict = {i: list(actions.get(i, [])) for i in range(4)}
    state.prepare_action_window()
    asyncio.run(boardcast.broadcast_ask_other_action(state))
    return state, asks, claims


@pytest.mark.parametrize('phase', ['waiting_action_after_cut', 'waiting_action_qianggang'])
@pytest.mark.parametrize('fanout_delay', [1.25, 6.0])
def test_recheck_spectators_project_same_independent_deadline(clock, phase, fanout_delay):
    async def scenario():
        state = make_state(round_timer=42, step_timer=11, tactical_call=True)
        state.game_status = phase
        state.player_list[0].discard_tiles = [28]
        state.jiagang_tile = 28
        state.action_dict = {i: ['hu_first', 'pass'] if i == 1 else [] for i in range(4)}
        host_uid = state.player_list[1].user_id
        host, following = Socket(), Socket()

        class SlowSpectator(Socket):
            async def send_json(self, message):
                await super().send_json(message)
                clock.advance(fanout_delay)

        slow = SlowSpectator()
        state.game_server.user_id_to_connection = {
            host_uid: SimpleNamespace(websocket=host),
            998: SimpleNamespace(websocket=slow),
            999: SimpleNamespace(websocket=following),
        }
        state.realtime_spectators = [
            SimpleNamespace(user_id=uid, host_user_id=host_uid) for uid in (998, 999)]
        await broadcast_recheck(state, remaining_time_override=5, is_tactical_recheck=True)
        first = host.messages[-1]['ask_other_action_info']
        last = following.messages[-1]['ask_other_action_info']
        assert first['is_tactical_recheck'] is True
        assert (first['remaining_time_ms'], first['step_remaining_ms']) == (5000, 0)
        assert last['is_tactical_recheck'] is True
        assert (last['remaining_time_ms'], last['step_remaining_ms']) == (max(0, 5000 - fanout_delay * 1000), 0)
        assert first['action_tick'] == last['action_tick']
        assert state.action_clock_manager.windows[1].deadline == 105
        assert state.action_clock_manager.bank(state.player_list[1]) == 42
        await boardcast.reconnected_send_pending_ask_for_viewer(state, 999, 1)
        restored = following.messages[-1]['ask_other_action_info']
        assert restored['is_tactical_recheck'] is True
        assert restored['remaining_time_ms'] == last['remaining_time_ms']
        assert restored['step_remaining_ms'] == 0
        assert state.action_clock_manager.windows[1].deadline == 105
        assert state.action_clock_manager.bank(state.player_list[1]) == 42

    asyncio.run(scenario())


@pytest.mark.parametrize('choice', [True, False])
def test_validator_preserves_explicit_tactical_and_rejects_protection(choice):
    config = TuidaoRoomValidator(room_name='推倒和', game_round=1, round_timer=20, step_timer=5, tactical_call=choice)
    assert config.tactical_call is choice and config.claim_protection is False
    with pytest.raises(ValueError):
        TuidaoRoomValidator(room_name='推倒和', game_round=1, round_timer=20, step_timer=5, claim_protection=True)


@pytest.mark.parametrize('choice', [None, True, False])
def test_actual_guangdong_creation_route_preserves_switch(choice):
    manager, socket = room_manager(), Socket()
    message = {'roomname': '推倒和', 'sub_rule': 'guangdong/tuidao_mil2024'}
    if choice is not None:
        message['tactical_call'] = choice
    asyncio.run(handle_create_guangdong_room(SimpleNamespace(room_manager=manager), 'unit', message, socket))
    assert socket.messages[0]['success']
    assert manager.rooms['123456']['tactical_call'] is (choice is not False)


@pytest.mark.parametrize('bad', ['false', 1, None])
def test_switch_type_coercion_is_rejected_by_route(bad):
    manager, socket = room_manager(), Socket()
    asyncio.run(handle_create_guangdong_room(SimpleNamespace(room_manager=manager), 'unit',
        {'roomname': '推倒和', 'tactical_call': bad}, socket))
    assert not socket.messages[0]['success'] and not manager.rooms


@pytest.mark.parametrize('players,seats,spectators,expected', [
    ([101,102], [101,102,-1,-1], [], True),
    ([101,102,103,104], None, [0,2,9], True),
    ([101,10], None, [], True),
    ([101,0], None, [], False), ([101,2], None, [], False), ([101,9], None, [], False),
    ([101], [101,2,-1,-1], [], False),
])
def test_real_bot_predicate_only_counts_occupied_player_seats(players, seats, spectators, expected):
    room = dict(room_rule='guangdong', sub_rule='guangdong/tuidao_mil2024', player_list=players,
                tactical_call=True, spectators=spectators)
    if seats is not None:
        room['seat_list'] = seats
    enforce_tuidao_tactical_room(room)
    assert room['tactical_call'] is expected
    room['player_list'], room['seat_list'] = [101,102,103,104], [101,102,103,104]
    enforce_tuidao_tactical_room(room)
    assert room['tactical_call'] is expected  # bot removal never silently re-enables


@pytest.mark.parametrize('players,choice,expected', [
    ([101,102,103,104], True, True), ([101,102,103,104], False, False),
    ([101,0,102,103], True, False), ([101,2,102,103], True, False),
])
def test_state_initialization_enforces_robot_fallback(players, choice, expected):
    state = make_state(player_list=players, tactical_call=choice)
    assert state.tactical_call is expected and not state.tactical_commit_lock and not state.claim_protection


def test_room_broadcast_and_reconnect_sync_use_server_effective_switch():
    async def scenario():
        socket = Socket()
        player = SimpleNamespace(user_id=101, current_room_id='123456')
        room = dict(room_rule='guangdong', sub_rule='guangdong/tuidao_mil2024', room_id='123456',
                    player_list=[101,2], seat_list=[101,2,-1,-1], tactical_call=True)
        manager = SimpleNamespace(rooms={'123456': room}, _sync_room_host=lambda _: None,
            game_server=SimpleNamespace(players={'unit': player}, user_id_to_connection={101: SimpleNamespace(websocket=socket)}))
        await RoomManager._broadcast_room_info(manager, '123456')
        assert socket.messages[-1]['room_info']['tactical_call'] is False
        room['tactical_call'] = True
        response = await RoomManager.sync_my_room(manager, 'unit')
        assert response.room_info['tactical_call'] is False
    asyncio.run(scenario())


@pytest.mark.parametrize('bot_id',[0,2])
def test_actual_bot_addition_disables_room_option_and_broadcasts_it(bot_id):
    async def scenario():
        socket=Socket()
        player=SimpleNamespace(user_id=101,current_room_id='123456')
        room=dict(room_rule='guangdong',sub_rule='guangdong/tuidao_mil2024',room_id='123456',
                  host_user_id=101,player_list=[101],seat_list=[101,-1,-1,-1],tactical_call=True)
        manager=SimpleNamespace(rooms={'123456':room},_sync_room_host=lambda _:None,
            game_server=SimpleNamespace(players={'unit':player},user_id_to_connection={101:SimpleNamespace(websocket=socket)}))
        async def broadcast(room_id):await RoomManager._broadcast_room_info(manager,room_id)
        manager._broadcast_room_info=broadcast
        result=await RoomManager._add_room_bot(manager,'unit','123456',bot_id,None)
        assert result.success and bot_id in room['player_list']
        assert not room['tactical_call'] and not socket.messages[-1]['room_info']['tactical_call']
    asyncio.run(scenario())


@pytest.mark.parametrize('players,choice,expected',[
    ([101,102,103,104],True,True),([101,102,103,104],False,False),
    ([101,0,102,103],True,False),([101,2,102,103],True,False),
])
def test_actual_start_path_and_game_reconnect_broadcast_effective_option(players,choice,expected):
    async def scenario():
        room=TuidaoRoomValidator(room_name='推倒和',game_round=1,round_timer=20,step_timer=5,tactical_call=choice).model_dump()
        room.update(room_rule='guangdong',room_id='123456',room_type='custom',player_list=players,
                    seat_list=players,host_user_id=101,allow_spectator=False)
        socket=Socket()
        server=SimpleNamespace(players={'unit':SimpleNamespace(user_id=101,current_room_id='123456')},
            user_id_to_connection={101:SimpleNamespace(websocket=socket)},calculation_service=None,
            db_manager=SimpleNamespace(get_user_settings=lambda _:{}))
        server.room_manager=SimpleNamespace(rooms={'123456':room},active_game_room_ids={},
            _sync_room_host=lambda _:None,all_players_ready=lambda _:True)
        manager=server.gamestate_manager=GameStateManager(server)
        manager.run_game=AsyncMock()
        with patch('server.gamestate.gamestate_manager.attach_game_frames'):
            response=await manager.start_game('unit','123456')
        assert response is None
        state=manager.get_game_state_by_room_id('123456')
        await state.game_task
        assert room['tactical_call'] is expected and state.tactical_call is expected
        state.master_seed=1
        state.init_tiles()
        await boardcast.send_reconnect_game_state(state,state.player_list[0])
        assert socket.messages[-1]['game_info']['tactical_call'] is expected
    asyncio.run(scenario())


@pytest.mark.parametrize('bank,step,answer,expected', [(20,5,8,17), (42,11,14.5,38.5), (0,9,7,0)])
@pytest.mark.parametrize('phase', ['waiting_action_after_cut','waiting_action_qianggang'])
def test_enabled_main_ask_retains_room_clock_and_overtime(clock, bank, step, answer, expected, phase):
    state, asks, _ = setup_state(clock, {1:['hu_first','pass']}, phase=phase, round_timer=bank, step_timer=step)
    assert asks[0]['remaining_time_ms'] == bank*1000 and asks[0]['step_remaining_ms'] == step*1000
    assert not asks[0].get('is_tactical_recheck')
    clock.submit(state, answer, 1, 'pass')
    responses, _ = asyncio.run(state.collect_action_responses())
    assert responses[1]['action_type']=='pass'
    assert state.action_clock_manager.bank(state.player_list[1]) == expected
    assert all(not info.get('is_tactical_recheck') for info in asks)


@pytest.mark.parametrize('step,bank', [(5,20),(11,42),(9,0)])
def test_valid_low_claim_alone_triggers_five_second_recheck_without_bank_charge(clock, step, bank):
    state, asks, claims = setup_state(clock, {1:['chi_left','pass'], 2:['peng','pass']}, round_timer=bank, step_timer=step)
    clock.submit(state, 2, 1, 'chi_left')
    responses, _ = asyncio.run(state.collect_action_responses())
    grace = [info for info in asks if info.get('is_tactical_recheck')]
    assert len(grace)==1 and grace[0]['remaining_time_ms']==5000 and grace[0]['step_remaining_ms']==0
    assert clock.now==107 and responses[1]['action_type']=='chi_left'
    assert all(state.action_clock_manager.bank(p)==bank for p in state.player_list)
    assert [c['action_list'] for c in claims if c.get('is_claim')]==[['chi_left']]*4


def test_main_pass_can_compete_and_initial_chi_can_upgrade_to_hu(clock):
    async def recheck(state, info):
        if info.get('is_tactical_recheck'):
            clock.submit(state, 1, info['player_index'], 'peng' if info['player_index']==2 else 'hu_first')
    state, asks, claims = setup_state(clock, {1:['chi_left','hu_first','pass'],2:['peng','pass']}, callback=recheck)
    clock.submit(state, 1, 2, 'pass')
    clock.submit(state, 2, 1, 'chi_left')
    responses, allowed = asyncio.run(state.collect_action_responses())
    assert responses[1]['action_type']=='hu_first'
    assert responses[2]['action_type']=='peng'
    assert [a['player_index'] for a in asks if a.get('is_tactical_recheck')]==[2,1]
    assert clock.now==104 and not state.tactical_commit_lock
    assert 'hu_first' in allowed[1] and all(p.remaining_time==20 for p in state.player_list)
    records=state.game_record['game_round']['round_index_1']['action_ticks']
    assert ['ca',1,'cl',28] in records and ['ca',2,'p',28] in records
    assert not state._claim_apply_pending


def test_claim_announcement_preserves_opening_tick_for_inflight_response(clock):
    state,asks,claims=setup_state(clock,{1:['chi_left','pass'],2:['peng','pass']})
    opening=state.server_action_tick
    clock.submit(state,2,1,'chi_left')
    asyncio.run(state.collect_action_responses())
    assert all(info['action_tick']==opening for info in claims if info.get('is_claim'))
    assert [info['action_tick'] for info in asks if info.get('is_tactical_recheck')]==[opening+1]


def test_force_pass_permanently_removes_competitor(clock):
    state, asks, _ = setup_state(clock, {1:['chi_left','pass'],2:['peng','pass']})
    clock.submit(state, 1, 2, 'force_pass')
    clock.submit(state, 2, 1, 'chi_left')
    responses, _ = asyncio.run(state.collect_action_responses())
    assert responses[2]['action_type']=='force_pass' and clock.now==102
    assert not any(info.get('is_tactical_recheck') for info in asks)


@pytest.mark.parametrize('action,tick_delta,delay,accepted', [('peng',0,4.999,True), ('peng',0,5,False), ('peng',-1,1,False), ('hu_first',0,1,False)])
def test_recheck_rejects_late_stale_and_illegal_requests(clock, action, tick_delta, delay, accepted):
    async def recheck(state, info):
        if info.get('is_tactical_recheck'):
            clock.submit(state, delay, 2, action, tick=state.server_action_tick+tick_delta)
    state, asks, _ = setup_state(clock, {1:['chi_left','pass'],2:['peng','pass']}, callback=recheck)
    clock.submit(state, 8, 1, 'chi_left')
    responses, _ = asyncio.run(state.collect_action_responses())
    assert responses[1]['action_type']=='chi_left'
    assert (responses.get(2,{}).get('action_type')=='peng') is accepted
    assert state.player_list[1].remaining_time==17 and state.player_list[2].remaining_time==17


def test_recheck_reconnect_keeps_exact_budget_and_phase_without_bank_cost(clock):
    recorded=[]
    async def recheck(state, info):
        if info.get('is_tactical_recheck') and info['remaining_time_ms']==5000:
            async def reconnect():
                before = state.action_clock_manager.bank(state.player_list[2])
                socket=state.game_server.user_id_to_connection[103].websocket
                await boardcast.reconnected_send_pending_ask(state,103)
                recorded.append(socket.messages[-1]['ask_other_action_info'])
                assert state.action_clock_manager.bank(state.player_list[2])==before
            clock.later(1.25,reconnect)
    state, asks, _ = setup_state(clock, {1:['chi_left','pass'],2:['peng','pass']}, callback=recheck)
    clock.submit(state, 2, 1, 'chi_left')
    responses,_=asyncio.run(state.collect_action_responses())
    assert recorded[0]['is_tactical_recheck'] is True
    assert recorded[0]['remaining_time_ms']==3750 and recorded[0]['step_remaining_ms']==0
    assert clock.now==107 and state.player_list[2].remaining_time==20


def test_disabled_option_waits_for_normal_higher_priority_response(clock):
    state, asks, claims=setup_state(clock,{1:['chi_left','pass'],2:['peng','pass']}, tactical_call=False)
    clock.submit(state,2,1,'chi_left')
    clock.submit(state,8,2,'peng')
    responses,_=asyncio.run(state.collect_action_responses())
    assert clock.now==108 and responses[2]['action_type']=='peng'
    assert state.player_list[1].remaining_time==20 and state.player_list[2].remaining_time==17
    assert not any(a.get('is_tactical_recheck') for a in asks) and not claims


@pytest.mark.parametrize('phase',['waiting_action_after_cut','waiting_action_qianggang'])
def test_closest_hu_may_steal_farther_hu_according_to_head_bump(clock,phase):
    async def recheck(state,info):
        if info.get('is_tactical_recheck'):
            clock.submit(state,1,1,'hu_first')
    state,asks,_=setup_state(clock,{1:['hu_first','pass'],3:['hu_third','pass']},phase=phase,callback=recheck)
    clock.submit(state,1,1,'pass')
    clock.submit(state,2,3,'hu_third')
    responses,_=asyncio.run(state.collect_action_responses())
    assert responses[1]['action_type']=='hu_first' and responses[3]['action_type']=='hu_third'
    assert [a['player_index'] for a in asks if a.get('is_tactical_recheck')]==[1]
    assert state.player_list[1].remaining_time==20 and state.player_list[3].remaining_time==20


def test_actual_rule_actions_upgrade_claim_then_resolve_single_winner(clock):
    async def recheck(state,info):
        if info.get('is_tactical_recheck'):
            clock.submit(state,1,info['player_index'],'peng' if info['player_index']==2 else 'hu_first')
    state,asks,_=setup_state(clock,{},callback=recheck)
    hand(state,1,[11,12,13,21,22,23,26,27,29,29,29,35,35])
    hand(state,2,[28,28,11,12,13,21,22,23,34,35,36,41,41])
    state.action_dict=state.check_discard_actions(28)
    assert 'chi_left' in state.action_dict[1] and 'hu_first' in state.action_dict[1]
    assert 'peng' in state.action_dict[2]
    state.prepare_action_window()
    asyncio.run(boardcast.broadcast_ask_other_action(state))
    clock.submit(state,1,2,'pass')
    clock.submit(state,2,1,'chi_left')
    asyncio.run(state.wait_action())
    assert len(state.pending_winners)==1 and state.pending_winners[0]['index']==1
    assert state.pending_winners[0]['source']=='discard'
    assert state.player_list[2].combination_tiles==[]


def test_old_opening_force_pass_remains_effective_during_recheck(clock):
    opening=[]
    async def recheck(state,info):
        if info.get('is_tactical_recheck'):
            clock.submit(state,1,2,'force_pass',tick=opening[0])
    state,asks,_=setup_state(clock,{1:['chi_left','pass'],2:['peng','pass']},callback=recheck)
    opening.append(state.server_action_tick)
    clock.submit(state,2,1,'chi_left')
    responses,_=asyncio.run(state.collect_action_responses())
    assert responses[2]['action_type']=='force_pass' and clock.now==103
    assert state.player_list[2].remaining_time==20


def test_declared_hu_is_not_cancelled_by_later_force_pass(clock):
    opening=[]
    async def recheck(state,info):
        if info.get('is_tactical_recheck'):
            clock.submit(state,1,3,'force_pass',tick=opening[0])
            clock.submit(state,2,1,'pass')
    state,asks,_=setup_state(clock,{1:['hu_first','pass'],3:['hu_third','pass']},callback=recheck)
    opening.append(state.server_action_tick)
    clock.submit(state,1,3,'hu_third')
    responses,_=asyncio.run(state.collect_action_responses())
    assert responses[3]['action_type']=='hu_third' and responses[1]['action_type']=='pass'
    assert state._tactical_silent_action is True


def test_no_valid_claim_does_not_open_recheck_or_shorten_main_clock(clock):
    state,asks,_=setup_state(clock,{1:['chi_left','pass'],2:['peng','pass']})
    clock.submit(state,2,1,'hu_first')  # illegal
    responses,_=asyncio.run(state.collect_action_responses())
    assert responses=={} and clock.now==125
    assert state.player_list[1].remaining_time==0 and state.player_list[2].remaining_time==0
    assert not any(info.get('is_tactical_recheck') for info in asks)
