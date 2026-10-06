"""Room-clock contract, independent of MIL's offline spoken-call limits."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_state import make_state, hand, draw, HAND
from . import timing
from ..game_taiwan import boardcast
from ..public.ask_timing import begin_ask_round, note_ask_delivered
from ..public.ai.get_action import get_action
from ...room.tuidao_room import handle_create_tuidao_room, TuidaoRoomValidator
from .test_adapters import room_manager, Socket


class ControlledClock:
    def __init__(self):
        self.now = 100.0
        self.events = []
        self.wait_budgets = []

    def advance(self, seconds):
        self.now += seconds

    def schedule(self, state, after, index, action, **fields):
        self.events.append((self.now + after, state, index,
                            dict(action_type=action, _action_tick=state.server_action_tick) | fields))
        self.events.sort(key=lambda event: event[0])

    async def wait(self, tasks, timeout):
        self.wait_budgets.append(timeout)
        until = self.now + timeout
        if self.events:
            until = min(until, self.events[0][0])
        self.now = until
        while self.events and self.events[0][0] <= until:
            _, state, index, data = self.events.pop(0)
            state.action_queues[index].put_nowait(data)
            state.action_events[index].set()
        await asyncio.sleep(0)
        return set(), set(tasks)


@pytest.fixture
def clock(monkeypatch):
    value = ControlledClock()
    monkeypatch.setattr(timing, 'monotonic', lambda: value.now)
    monkeypatch.setattr(timing, 'wait_for_events', value.wait)
    return value


def window(state, phase='waiting_action_after_cut', actions=None, delivered=True):
    state.game_status = phase
    state.action_dict = {i: [] for i in range(4)}
    state.action_dict.update(actions or {1: ['peng', 'pass']})
    state.prepare_action_window()
    state.server_action_tick += 1
    begin_ask_round(state)
    if delivered:
        for i, allowed in state.action_dict.items():
            if allowed:
                note_ask_delivered(state, i)
    return state.action_clock_manager


@pytest.mark.parametrize('bank,step', [(20, 5), (42, 11), (0, 5), (0, 9), (7, 0), (0, 0)])
@pytest.mark.parametrize('phase', ['waiting_action_after_cut', 'waiting_action_qianggang'])
def test_claim_uses_room_bank_and_step(bank, step, phase):
    state = make_state(round_timer=bank, step_timer=step)
    state.game_status = phase
    assert state.claim_clock(state.player_list[1]) == (bank, step)


@pytest.mark.parametrize('phase', ['waiting_hand_action', 'waiting_action_after_cut', 'waiting_action_qianggang'])
@pytest.mark.parametrize('bank,step,elapsed,expected', [
    (20, 5, 4, (20, 1)), (20, 5, 8, (17, 0)),
    (0, 11, 7, (0, 4)), (17, 9, 12, (14, 0)),
    (7, 0, 3, (4, 0)), (0, 0, 0, (0, 0)),
])
def test_live_clock_and_deadline_follow_room(clock, phase, bank, step, elapsed, expected):
    state = make_state(round_timer=bank, step_timer=step)
    manager = window(state, phase, {0: ['cut']} if phase == 'waiting_hand_action' else None)
    index = 0 if phase == 'waiting_hand_action' else 1
    assert manager.windows[index].deadline == 100 + bank + step
    clock.advance(elapsed)
    assert state.action_clock(state.player_list[index], reconnecting=True) == expected
    assert state.player_list[index].remaining_time == bank  # displays never settle the bank


@pytest.mark.parametrize('phase', ['waiting_hand_action', 'waiting_action_after_cut', 'waiting_action_qianggang'])
@pytest.mark.parametrize('bank,step,elapsed,expected', [
    (20, 5, 4, 20), (20, 5, 8, 17), (0, 11, 7, 0),
    (17, 9, 12, 14), (7, 0, 3, 4), (20, 5, 5.4, 19.6),
])
def test_response_charges_only_overtime(clock, phase, bank, step, elapsed, expected):
    state = make_state(round_timer=bank, step_timer=step)
    index = 0 if phase == 'waiting_hand_action' else 1
    action = 'cut' if index == 0 else 'pass'
    manager = window(state, phase, {index: [action]})
    clock.schedule(state, elapsed, index, action)
    responses, _ = asyncio.run(state.collect_action_responses())
    assert responses[index]['action_type'] == action
    assert manager.bank(state.player_list[index]) == pytest.approx(expected)
    assert state.player_list[index].remaining_time == timing.display_seconds(expected)
    assert clock.now == pytest.approx(100 + elapsed)


def test_fractional_overtime_accumulates_across_operations(clock):
    state = make_state()
    for _ in range(3):
        window(state)
        clock.schedule(state, 5.4, 1, 'pass')
        asyncio.run(state.collect_action_responses())
    assert state.action_clock_manager.bank(state.player_list[1]) == pytest.approx(18.8)
    assert state.player_list[1].remaining_time == 19
    window(state)
    assert state.action_clock_manager.windows[1].deadline == pytest.approx(clock.now + 23.8)


@pytest.mark.parametrize('bank,step', [(20, 5), (42, 11), (0, 5), (7, 0), (0, 0)])
def test_expiration_consumes_entire_budget_not_three_seconds(clock, bank, step):
    state = make_state(round_timer=bank, step_timer=step)
    window(state)
    responses, allowed = asyncio.run(state.collect_action_responses())
    assert responses == {} and allowed[1] == ['peng', 'pass']
    assert clock.now == pytest.approx(100 + bank + step)
    assert state.player_list[1].remaining_time == 0


def test_on_time_queued_reply_survives_slow_other_transport(clock):
    state = make_state()
    window(state)
    clock.advance(4)
    state.action_queues[1].put_nowait(dict(action_type='peng', _action_tick=state.server_action_tick))
    clock.advance(30)  # collector starts after another slow send
    responses, _ = asyncio.run(state.collect_action_responses())
    assert responses[1]['action_type'] == 'peng'
    assert state.player_list[1].remaining_time == 20


@pytest.mark.parametrize('elapsed', [25, 26])
def test_late_ingress_cannot_restore_expired_window(clock, elapsed):
    state = make_state()
    window(state)
    clock.advance(elapsed)
    state.action_queues[1].put_nowait(dict(action_type='peng', _action_tick=state.server_action_tick))
    responses, _ = asyncio.run(state.collect_action_responses())
    assert responses == {} and state.player_list[1].remaining_time == 0


def test_invalid_and_old_tick_do_not_end_or_reset_window(clock):
    state = make_state()
    manager = window(state)
    clock.schedule(state, 1, 1, 'peng', _action_tick=state.server_action_tick - 1)
    clock.schedule(state, 2, 1, 'cut')
    clock.schedule(state, 8, 1, 'pass')
    deadline = manager.windows[1].deadline
    responses, _ = asyncio.run(state.collect_action_responses())
    assert responses[1]['action_type'] == 'pass'
    assert manager.windows[1].deadline == deadline
    assert state.player_list[1].remaining_time == 17


def test_each_seat_starts_at_delivery_and_failed_send_keeps_full_budget(clock):
    state = make_state()
    manager = window(state, actions={1: ['pass'], 2: ['pass'], 3: ['pass']}, delivered=False)
    note_ask_delivered(state, 1)
    clock.advance(8)
    note_ask_delivered(state, 2)
    assert manager.windows[1].deadline == 125
    assert manager.windows[2].deadline == 133
    assert state.action_clock(state.player_list[3]) == (20, 5)
    assert manager.windows[3].started_at is None
    clock.schedule(state, 1, 1, 'pass')
    clock.schedule(state, 1, 2, 'pass')
    clock.schedule(state, 1, 3, 'pass')
    asyncio.run(state.collect_action_responses())
    assert manager.windows[3].deadline == 133  # fallback after broadcast, not t=100
    assert [p.remaining_time for p in state.player_list] == [20, 16, 20, 20]


def test_reconnect_repeat_is_read_only_and_completed_seat_is_not_asked(clock):
    async def scenario():
        state = make_state()
        player = state.player_list[1]
        player.discard_tiles = [11]
        state.player_list[0].discard_tiles = [11]
        socket = Socket()
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
        manager = window(state, actions={1: ['pass'], 2: ['pass']})
        clock.advance(8)
        deadline = manager.windows[1].deadline
        for _ in range(3):
            await boardcast.reconnected_send_pending_ask(state, player.user_id)
        clocks = [m['ask_other_action_info'] for m in socket.messages]
        assert [(m['remaining_time'], m['step_remaining']) for m in clocks] == [(17, 0)] * 3
        assert player.remaining_time == 20 and manager.windows[1].deadline == deadline
        state.action_queues[1].put_nowait(dict(action_type='pass', _action_tick=state.server_action_tick))
        async def wait_after_first_reply(tasks, timeout):
            assert player.remaining_time == 17 and 1 not in state.waiting_players_list
            before = len(socket.messages)
            await boardcast.reconnected_send_pending_ask(state, player.user_id)
            assert len(socket.messages) == before
            return await clock.wait(tasks, timeout)
        clock.schedule(state, 2, 2, 'pass')
        with patch.object(timing, 'wait_for_events', wait_after_first_reply):
            await state.collect_action_responses()
        assert player.remaining_time == 17
        assert manager.bank(player) == 17
    asyncio.run(scenario())


def test_repeated_delivery_and_wall_clock_jump_do_not_reset_deadline(clock):
    state = make_state()
    manager = window(state)
    deadline = manager.windows[1].deadline
    clock.advance(8)
    note_ask_delivered(state, 1)
    state._ask_delivered_at[1] -= 10000
    assert manager.windows[1].deadline == deadline
    assert state.claim_clock(state.player_list[1], reconnecting=True) == (17, 0)


@pytest.mark.parametrize('phase', ['waiting_action_after_cut', 'waiting_action_qianggang'])
def test_timeout_dispatch_is_pass_and_advances_normally(clock, phase):
    state = make_state()
    state.player_list[0].discard_tiles = [11]
    window(state, phase)
    method = 'resolve_discard_responses' if phase == 'waiting_action_after_cut' else 'resolve_rob_kong_responses'
    with patch.object(state, method, new=AsyncMock()) as resolve:
        asyncio.run(state.wait_action())
    assert resolve.await_args.args[0] == {}
    assert state.player_list[1].remaining_time == 0 and clock.now == 125


@pytest.mark.parametrize('ready', [False, True])
@pytest.mark.parametrize('bank,step', [(20, 5), (0, 0)])
def test_timeout_default_cut_keeps_ready_lock(clock, ready, bank, step):
    state = make_state(round_timer=bank, step_timer=step)
    player = hand(state, 0, HAND, ready=ready)
    draw(player, 19)
    window(state, 'waiting_hand_action', {0: ['cut']})
    with patch.object(state, 'check_discard_actions', return_value={i: [] for i in range(4)}):
        asyncio.run(state.wait_action())
    assert player.discard_tiles == [19] and player.hand_tiles == HAND
    assert player.remaining_time == 0
    assert player.ready_locked is ready
    assert state.game_status == 'deal_card'


def test_ready_declaration_uses_same_hand_clock(clock):
    state = make_state()
    player = hand(state, 0, HAND)
    draw(player, 19)
    window(state, 'waiting_hand_action', {0: ['cut', 'riichi_cut']})
    clock.schedule(state, 8, 0, 'riichi_cut', TileId=19, cutIndex=13, cutClass=True)
    asyncio.run(state.wait_action())
    assert player.declared_ready and player.ready_locked and player.remaining_time == 17


@pytest.mark.parametrize('claim', ['peng', 'chi_right'])
def test_claim_then_discard_gets_new_step_but_same_bank(clock, claim):
    state = make_state()
    state.player_list[0].discard_tiles = [11]
    player = hand(state, 1, [11, 11, 12, 13, 14, 21, 22, 23, 31, 32, 33, 47, 47])
    window(state, actions={1: ['peng', 'chi_right', 'pass']})
    clock.schedule(state, 8, 1, claim)
    asyncio.run(state.wait_action())
    assert state.game_status == 'onlycut_after_action'
    assert player.remaining_time == 17 and player.last_drawn_tile is None
    state.action_dict = state.after_claim_actions()
    window(state, 'waiting_hand_action', state.action_dict)
    assert state.action_clock(player) == (17, 5)
    clock.schedule(state, 4, 1, 'cut', TileId=47, cutIndex=10, cutClass=False)
    asyncio.run(state.wait_action())
    assert player.remaining_time == 17 and player.discard_tiles[-1] == 47


def test_next_round_resets_all_banks_and_old_windows(clock):
    state = make_state(round_timer=42, step_timer=11)
    window(state)
    clock.schedule(state, 14.5, 1, 'pass')
    asyncio.run(state.collect_action_responses())
    assert state.action_clock_manager.bank(state.player_list[1]) == 38.5
    state._reset_hand_runtime()
    assert [p.remaining_time for p in state.player_list] == [42] * 4
    assert state.action_clock_manager.windows == {}
    window(state)
    assert state.action_clock(state.player_list[1]) == (42, 11)


@pytest.mark.parametrize('bank,step', [(20, 5), (42, 11), (0, 9), (7, 0), (0, 0)])
def test_room_request_persistence_and_state_initialization(bank, step):
    manager, socket = room_manager(), Socket()
    message = dict(roomname='计时回归', roundTimerValue=bank, stepTimerValue=step)
    asyncio.run(handle_create_tuidao_room(SimpleNamespace(room_manager=manager), 'unit', message, socket))
    room = socket.messages[0]['room_info']
    assert room['round_timer'] == bank and room['step_timer'] == step
    state = make_state(**{key: room[key] for key in ('round_timer', 'step_timer')})
    assert state.round_time == bank and state.step_time == step
    assert [p.remaining_time for p in state.player_list] == [bank] * 4


def test_default_room_request_is_twenty_plus_five():
    manager, socket = room_manager(), Socket()
    asyncio.run(handle_create_tuidao_room(SimpleNamespace(room_manager=manager), 'unit', {'roomname': '默认计时'}, socket))
    room = socket.messages[0]['room_info']
    assert (room['round_timer'], room['step_timer']) == (20, 5)


@pytest.mark.parametrize('phase', ['waiting_hand_action', 'waiting_action_after_cut', 'waiting_action_qianggang'])
@pytest.mark.parametrize('bank,step', [(20, 5), (42, 11), (0, 9)])
def test_actual_serialized_first_and_reconnect_clocks(clock, phase, bank, step):
    async def scenario():
        state = make_state(round_timer=bank, step_timer=step)
        player = state.player_list[0 if phase == 'waiting_hand_action' else 1]
        socket = Socket()
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
        state.current_player_index = 0
        state.player_list[0].discard_tiles = [11]
        state.jiagang_tile = 11
        state.game_status = phase
        state.action_dict = {i: ['cut'] if i == 0 and phase == 'waiting_hand_action'
                             else ['pass'] if i == 1 and phase != 'waiting_hand_action'
                             else [] for i in range(4)}
        state.prepare_action_window()
        send = boardcast.broadcast_ask_hand_action if phase == 'waiting_hand_action' else boardcast.broadcast_ask_other_action
        await send(state)
        field = 'ask_hand_action_info' if phase == 'waiting_hand_action' else 'ask_other_action_info'
        first = socket.messages[-1][field]
        assert (first['remaining_time'], first['step_remaining']) == (bank, step)
        deadline = state.action_clock_manager.windows[player.player_index].deadline
        assert deadline == 100 + bank + step
        clock.advance(7)
        await boardcast.reconnected_send_pending_ask(state, player.user_id)
        reconnect = socket.messages[-1][field]
        expected = (max(0, bank - max(0, 7 - step)), max(0, step - 7))
        assert (reconnect['remaining_time'], reconnect['step_remaining']) == expected
        assert reconnect['action_tick'] == first['action_tick']
        assert state.action_clock_manager.windows[player.player_index].deadline == deadline
    asyncio.run(scenario())


def test_ingress_during_fanout_is_accepted_and_not_timed_at_processing(clock):
    async def scenario():
        state = make_state()
        state.game_server.players = {'c1': SimpleNamespace(user_id=102)}
        state.game_status = 'waiting_action_after_cut'
        state.action_dict = {0: [], 1: ['pass'], 2: ['pass'], 3: []}
        state.player_list[0].discard_tiles = [11]
        first = Socket()
        class SlowOtherSocket(Socket):
            async def send_json(self, message):
                await super().send_json(message)
                clock.advance(4)
                await get_action(state, 'c1', 'pass', None, None, None, None,
                                 action_tick=state.server_action_tick)
                assert state.action_queues[1].qsize() == 1
                clock.advance(30)
        slow = SlowOtherSocket()
        state.game_server.user_id_to_connection = {
            102: SimpleNamespace(websocket=first), 103: SimpleNamespace(websocket=slow)}
        await boardcast.broadcast_ask_other_action(state)
        manager = state.action_clock_manager
        assert manager.windows[1].deadline == 125
        assert manager.windows[2].deadline == 159
        clock.schedule(state, 4, 2, 'pass')
        responses, _ = await state.collect_action_responses()
        assert set(responses) == {1, 2}
        assert state.player_list[1].remaining_time == state.player_list[2].remaining_time == 20
    asyncio.run(scenario())


def test_realtime_spectator_sees_host_clock_without_starting_a_new_window(clock):
    async def scenario():
        state = make_state()
        host, spectator = Socket(), Socket()
        state.game_server.user_id_to_connection = {
            102: SimpleNamespace(websocket=host), 999: SimpleNamespace(websocket=spectator)}
        state.realtime_spectators = [SimpleNamespace(user_id=999, host_user_id=102)]
        state.game_status = 'waiting_action_after_cut'
        state.action_dict = {0: [], 1: ['pass'], 2: [], 3: []}
        state.player_list[0].discard_tiles = [11]
        await boardcast.broadcast_ask_other_action(state)
        manager = state.action_clock_manager
        assert host.messages[-1] == spectator.messages[-1]
        clock.advance(8)
        await boardcast.reconnected_send_pending_ask_for_viewer(state, 999, 1)
        ask = spectator.messages[-1]['ask_other_action_info']
        assert (ask['remaining_time'], ask['step_remaining']) == (17, 0)
        assert manager.windows[1].deadline == 125
        assert state.player_list[1].remaining_time == 20
    asyncio.run(scenario())


def test_concealed_kong_replacement_gets_new_step_and_preserves_bank(clock):
    async def scenario():
        state = make_state()
        player = hand(state, 0, [11, 11, 11, 22, 23, 24, 31, 32, 33, 41, 41, 41, 47])
        draw(player, 11)
        window(state, 'waiting_hand_action', {0: ['cut', 'angang']})
        clock.schedule(state, 8, 0, 'angang', target_tile=11)
        await state.wait_action()
        assert state.game_status == 'deal_card_after_gang' and player.remaining_time == 17
        await state._deal_supplement()
        assert state.game_status == 'waiting_hand_action' and player.last_drawn_tile == 19
        window(state, 'waiting_hand_action', state.action_dict)
        assert state.action_clock(player) == (17, 5)
        assert state.action_clock_manager.windows[0].deadline == clock.now + 22
    asyncio.run(scenario())


def test_rob_kong_response_after_three_seconds_is_still_a_valid_win(clock):
    async def scenario():
        state = make_state()
        player = hand(state, 0, [22, 23, 24, 31, 32, 33, 41, 41, 47, 47], ['k28'])
        draw(player, 28)
        hand(state, 1, HAND)
        state.current_player_index = 0
        await state.execute_jiagang(0, 28)
        assert state.game_status == 'waiting_action_qianggang'
        window(state, 'waiting_action_qianggang', state.action_dict)
        clock.schedule(state, 8, 1, 'hu_first')
        await state.wait_action()
        assert state.pending_winners[0]['index'] == 1
        assert state.pending_winners[0]['source'] == 'robbing_kong'
        assert state.player_list[1].remaining_time == 17
    asyncio.run(scenario())


def test_self_draw_acceptance_uses_the_same_full_clock(clock):
    state = make_state()
    player = hand(state, 0, HAND)
    draw(player, 28)
    state.result_dict = {'hu_self': state.score_candidate(0, 'self_draw')}
    window(state, 'waiting_hand_action', {0: ['hu_self', 'cut']})
    clock.schedule(state, 8, 0, 'hu_self')
    asyncio.run(state.wait_action())
    assert state.pending_winners[0]['source'] == 'self_draw'
    assert state.pending_winners[0]['index'] == 0 and player.remaining_time == 17


def test_real_round_loop_restores_custom_bank_after_seat_rotation(clock):
    import importlib
    module = importlib.import_module('server.gamestate.game_tuidao.TuidaoGameState')
    async def scenario():
        state = make_state(round_timer=42, step_timer=11)
        initial_banks = []
        async def play_hand():
            initial_banks.append((state.current_round, [(p.user_id, p.remaining_time) for p in state.player_list]))
            window(state)
            clock.schedule(state, 14.5, 1, 'pass')
            await state.collect_action_responses()
            assert state.action_clock_manager.bank(state.player_list[1]) == 38.5
        async def settle(_):
            state.pending_winners = [{'index': 1}]
            return state.current_round == 4
        state.game_server.gamestate_manager.cleanup_game_state_complete = AsyncMock()
        with patch.object(module, 'broadcast_game_start', new=AsyncMock()), \
             patch.object(module, 'broadcast_game_end', new=AsyncMock()), \
             patch.object(state, '_run_hand', new=play_hand), \
             patch.object(state, '_settle_hand', new=settle), \
             patch.object(state.spectator_manager, 'send_final_record_and_close', new=AsyncMock()):
            await state.game_loop_chinese()
        assert [number for number, _ in initial_banks] == [1, 2, 3, 4]
        assert all(bank == 42 for _, players in initial_banks for _, bank in players)
        assert initial_banks[0][1][0][0] != initial_banks[1][1][0][0]
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()
    asyncio.run(scenario())


def test_real_async_event_wait_ignores_malformed_queue_and_accepts_reply_once():
    async def scenario():
        state = make_state()
        window(state)
        collector = asyncio.create_task(state.collect_action_responses())
        await asyncio.sleep(0)
        state.action_queues[1].put_nowait(None)
        state.action_events[1].set()
        await asyncio.sleep(0)
        state.action_queues[1].put_nowait(dict(action_type='pass', _action_tick=state.server_action_tick))
        state.action_events[1].set()
        responses, _ = await asyncio.wait_for(collector, 1)
        assert responses[1]['action_type'] == 'pass'
        assert state.player_list[1].remaining_time == 20
        assert not state.waiting_players_list
    asyncio.run(scenario())


def test_uncontested_added_kong_does_not_create_an_extra_response_wait(clock):
    state = make_state()
    player = hand(state, 0, [22, 23, 24, 31, 32, 33, 41, 41, 47, 47], ['k28'])
    draw(player, 28)
    window(state, 'waiting_hand_action', {0: ['cut', 'jiagang']})
    clock.schedule(state, 2, 0, 'jiagang', target_tile=28)
    asyncio.run(state.wait_action())
    assert state.game_status == 'deal_card_after_gang'
    assert clock.now == 102 and player.remaining_time == 20
    assert state._pending_jiagang is None and state.kong_ledger[-1]['kind'] == 'added'


def test_rob_kong_timeout_pass_finalizes_kong_with_default_room_budget(clock):
    state = make_state()
    player = hand(state, 0, [22, 23, 24, 31, 32, 33, 41, 41, 47, 47], ['k28'])
    draw(player, 28)
    hand(state, 1, HAND)
    asyncio.run(state.execute_jiagang(0, 28))
    window(state, 'waiting_action_qianggang', state.action_dict)
    asyncio.run(state.wait_action())
    assert clock.now == 125 and not state.pending_winners
    assert state.game_status == 'deal_card_after_gang'
    assert state.player_list[1].remaining_time == 0 and player.remaining_time == 20
    assert state.kong_ledger[-1]['kind'] == 'added'


@pytest.mark.parametrize('overrides', [
    {'claim_protection': True},
    {'tactical_call': True, 'claim_protection': True},
])
def test_mil_profile_still_rejects_protection_with_or_without_tactical(overrides):
    with pytest.raises(ValueError, match='MIL推倒和标准本不支持'):
        TuidaoRoomValidator(room_name='计时边界', game_round=1, **overrides)


def test_tactical_enabled_keeps_normal_claim_clock_and_protection_disabled(clock):
    state = make_state(round_timer=42, step_timer=11,
                       tactical_call=True, claim_protection=True)
    assert state.tactical_call is True and state.claim_protection is False
    window(state)
    assert state.claim_clock(state.player_list[1]) == (42, 11)
    clock.advance(8)
    assert state.claim_clock(state.player_list[1]) == (42, 3)


@pytest.mark.parametrize('phase', [
    'waiting_hand_action', 'waiting_action_after_cut', 'waiting_action_qianggang',
])
@pytest.mark.parametrize('elapsed,expected_bank_ms,expected_step_ms', [
    (2.4, 18800, 2600), (5.4, 18400, 0),
])
def test_serialized_exact_budget_survives_fractional_bank_and_reconnect(
        clock, phase, elapsed, expected_bank_ms, expected_step_ms):
    async def scenario():
        state = make_state()
        index = 0 if phase == 'waiting_hand_action' else 1
        player = state.player_list[index]
        state.action_clock_manager._set_bank(player, 18.8)
        socket = Socket()
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
        state.game_status = phase
        state.player_list[0].discard_tiles = [11]
        state.jiagang_tile = 11
        state.action_dict = {i: ['cut' if index == 0 else 'pass'] if i == index else []
                             for i in range(4)}
        state.prepare_action_window()
        broadcast = boardcast.broadcast_ask_hand_action if index == 0 else boardcast.broadcast_ask_other_action
        await broadcast(state)
        field = 'ask_hand_action_info' if index == 0 else 'ask_other_action_info'
        initial = socket.messages[-1][field]
        assert initial['remaining_time'] == 19 and initial['step_remaining'] == 5
        assert initial['remaining_time_ms'] == 18800
        assert initial['step_remaining_ms'] == 5000
        assert initial.get('is_tactical_recheck', False) is False
        deadline = state.action_clock_manager.windows[index].deadline
        assert deadline == pytest.approx(123.8)
        clock.advance(elapsed)
        for _ in range(2):
            await boardcast.reconnected_send_pending_ask(state, player.user_id)
            reply = socket.messages[-1][field]
            assert reply['remaining_time_ms'] == expected_bank_ms
            assert reply['step_remaining_ms'] == expected_step_ms
            assert reply['action_tick'] == initial['action_tick']
            assert state.action_clock_manager.windows[index].deadline == deadline
            assert state.action_clock_manager.bank(player) == pytest.approx(18.8)
    asyncio.run(scenario())


@pytest.mark.parametrize('phase', ['waiting_hand_action', 'waiting_action_after_cut', 'waiting_action_qianggang'])
def test_each_realtime_spectator_gets_current_host_budget_after_slow_fanout(clock, phase):
    async def scenario():
        state = make_state()
        index = 0 if phase == 'waiting_hand_action' else 1
        host = Socket()
        class SlowSpectator(Socket):
            async def send_json(self, message):
                await super().send_json(message)
                clock.advance(8)
        slow, following = SlowSpectator(), Socket()
        host_uid = state.player_list[index].user_id
        state.game_server.user_id_to_connection = {
            host_uid: SimpleNamespace(websocket=host), 998: SimpleNamespace(websocket=slow),
            999: SimpleNamespace(websocket=following),
        }
        state.realtime_spectators = [SimpleNamespace(user_id=uid, host_user_id=host_uid) for uid in (998,999)]
        state.game_status = phase
        state.player_list[0].discard_tiles = [11]
        state.jiagang_tile = 11
        state.action_dict = {i: ['cut' if index == 0 else 'pass'] if i == index else [] for i in range(4)}
        broadcast = boardcast.broadcast_ask_hand_action if index == 0 else boardcast.broadcast_ask_other_action
        await broadcast(state)
        field = 'ask_hand_action_info' if index == 0 else 'ask_other_action_info'
        assert (host.messages[-1][field]['remaining_time'], host.messages[-1][field]['step_remaining']) == (20,5)
        assert (slow.messages[-1][field]['remaining_time'], slow.messages[-1][field]['step_remaining']) == (20,5)
        last = following.messages[-1][field]
        assert (last['remaining_time'], last['step_remaining']) == (17,0)
        assert (last['remaining_time_ms'], last['step_remaining_ms']) == (17000,0)
        assert last['action_tick'] == host.messages[-1][field]['action_tick']
        assert state.action_clock_manager.windows[index].deadline == 125
        assert state.player_list[index].remaining_time == 20
    asyncio.run(scenario())
