"""Observable room/ask/reply/deadline contracts, driven without real 25s waits."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_flow import make_state, hand, draw, HAND
from .timing import collect_responses
from ..game_taiwan.boardcast import (
    reconnected_send_pending_ask_for_viewer, send_realtime_spectator_snapshot,
)
from ..public.ask_timing import begin_ask_round, note_ask_delivered


class Rig:
    def __init__(self, bank=20, step=5):
        self.state = make_state(round_timer=bank, step_timer=step, allow_spectator=True, room_id=987023)
        self.at = 100.0
        self.state.action_clock.now = lambda: self.at
        self.state.game_server.players = {
            f'connection-{i}': SimpleNamespace(user_id=p.user_id)
            for i, p in enumerate(self.state.player_list)
        }
        self.messages = {i: [] for i in range(4)}
        for index, player in enumerate(self.state.player_list):
            async def send(message, index=index):
                self.messages[index].append(deepcopy(message))
            self.state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(
                websocket=SimpleNamespace(send_json=send))
        self.state.send_to_realtime_spectators = AsyncMock()
        self.state._warm_hints = AsyncMock()
        self.state.spectator_manager.record_ask_hand = lambda *_: None
        self.state.spectator_manager.record_ask_other = lambda *_: None

    def hand(self, tiles=None, melds=()):
        s = self.state
        player = hand(s, 0, HAND if tiles is None else tiles, melds)
        draw(player, 45)
        s.current_player_index = 0
        s.game_status = 'waiting_hand_action'
        s.action_dict = s.check_hand_actions(0)
        return player

    def claim(self, extra=False, count=2):
        s = self.state
        s.current_player_index = 0
        s.player_list[0].discard_tiles = [11]
        hand(s, 1, [11] * count + HAND[count:])
        if extra:
            hand(s, 2, [11, 11] + HAND[2:])
        s.game_status = 'waiting_action_after_cut'
        s.action_dict = s.check_discard_actions(11)

    async def broadcast(self):
        s = self.state
        s.prepare_action_window()
        if s.game_status == 'waiting_hand_action':
            await s.broadcast_ask_hand_action()
        else:
            await s.broadcast_ask_other_action()

    async def submit(self, index, action, **data):
        await self.state.action_queues[index].put({'action_type': action, **data})
        self.state.action_events[index].set()

    async def submit_through_protocol(self, index, action):
        from ..public.ai.get_action import get_action
        player = self.state.player_list[index]
        await get_action(self.state, f'connection-{index}', action,
                         True if action == 'cut' else None,
                         player.hand_tiles[-1] if action == 'cut' else None,
                         len(player.hand_tiles)-1 if action == 'cut' else None,
                         None, action_tick=self.state.server_action_tick)

    def wake(self):
        for event in self.state.action_events.values():
            event.set()


async def pump():
    for _ in range(12):
        await asyncio.sleep(0)


def wire(messages, kind):
    return [m[kind] for m in messages if kind in m]


@pytest.mark.parametrize('settings,expected', [
    ({}, (20, 5)), ({'roundTimerValue': 0, 'stepTimerValue': 7}, (0, 7)),
    ({'roundTimerValue': 13, 'stepTimerValue': 11}, (13, 11)),
    ({'roundTimerValue': 0, 'stepTimerValue': 0}, (0, 0)),
])
def test_real_room_request_saved_config_and_state_initial_bank(settings, expected):
    async def scenario():
        from ...room.test_hongzhong_room import manager
        from ...room.hongzhong_room import handle_create_hongzhong_room
        from .HongzhongGameState import HongzhongGameState
        m = manager(); socket = SimpleNamespace(send_json=AsyncMock())
        await handle_create_hongzhong_room(m.game_server, 'conn', {'roomname': '红中计时', **settings}, socket)
        result = socket.send_json.call_args.args[0]
        assert result['success']
        room = m.rooms['hz-unit-room']
        assert (room['round_timer'], room['step_timer']) == expected
        server = SimpleNamespace(user_id_to_connection={}, room_manager=m, gamestate_manager=SimpleNamespace())
        state = HongzhongGameState(server, room | {'player_list': [101,102,103,104]}, None, None, 'hz-timing-entry')
        assert (state.round_time, state.step_time) == expected
        assert [p.remaining_time for p in state.player_list] == [expected[0]] * 4
    asyncio.run(scenario())


@pytest.mark.parametrize('bank,step', [(20, 5), (8, 2), (0, 7), (0, 0), (7, 0), (13, 11)])
@pytest.mark.parametrize('phase', ['hand', 'claim'])
def test_initial_wire_and_actual_deadline_use_configured_bank_and_step(bank, step, phase):
    async def scenario():
        r = Rig(bank, step)
        r.hand() if phase == 'hand' else r.claim()
        await r.broadcast()
        index = 0 if phase == 'hand' else 1
        kind = 'ask_hand_action_info' if phase == 'hand' else 'ask_other_action_info'
        info = wire(r.messages[index], kind)[-1]
        assert (info['remaining_time'], info['step_remaining']) == (bank, step)
        assert r.state.action_clock.deadline(index) == 100 + bank + step
    asyncio.run(scenario())


@pytest.mark.parametrize('phase', ['hand', 'claim'])
@pytest.mark.parametrize('bank,step,elapsed,expected', [
    (20, 5, 2, 20), (20, 5, 5, 20), (20, 5, 8, 17),
    (20, 5, 8.25, 16.75), (8, 2, 4.25, 5.75), (0, 7, 6, 0), (7, 0, 2, 5),
])
def test_reply_consumes_step_first_and_keeps_fractional_bank(phase, bank, step, elapsed, expected):
    async def scenario():
        r = Rig(bank, step)
        r.hand() if phase == 'hand' else r.claim()
        await r.broadcast()
        index = 0 if phase == 'hand' else 1
        r.at += elapsed
        if phase == 'hand':
            await r.submit(index, 'cut', TileId=45, cutClass='mo', hand_card_index=13)
        else:
            await r.submit(index, 'pass')
        await r.state.wait_action()
        assert r.state.action_clock.balances[index] == pytest.approx(expected)
        assert r.state.player_list[index].remaining_time == __import__('math').ceil(expected)
        assert r.state.player_list[3].remaining_time == bank
    asyncio.run(scenario())


@pytest.mark.parametrize('phase', ['hand', 'claim'])
@pytest.mark.parametrize('bank,step', [(20, 5), (3, 1), (0, 5), (0, 0)])
def test_expiration_uses_full_budget_then_correct_default_action(phase, bank, step):
    async def scenario():
        r = Rig(bank, step)
        r.hand() if phase == 'hand' else r.claim()
        await r.broadcast()
        task = asyncio.create_task(r.state.wait_action())
        await pump()
        if bank + step:
            r.at = 100 + bank + step - 0.1
            r.wake(); await pump()
            assert not task.done()
        r.at = 100 + bank + step
        r.wake()
        await asyncio.wait_for(task, 0.5)
        index = 0 if phase == 'hand' else 1
        assert r.state.player_list[index].remaining_time == 0
        if phase == 'hand':
            assert r.state.player_list[0].discard_tiles == [45]
        else:
            assert not r.state.player_list[1].combination_tiles
            assert r.state.game_status == 'deal_card'
        assert r.state.waiting_players_list == []
    asyncio.run(scenario())


@pytest.mark.parametrize('phase', ['hand', 'claim'])
def test_reply_exactly_at_deadline_is_late(phase):
    async def scenario():
        r = Rig(0, 5)
        r.hand() if phase == 'hand' else r.claim()
        await r.broadcast()
        r.at = 105
        if phase == 'hand':
            await r.submit(0, 'hu_self')
        else:
            await r.submit(1, 'peng')
        await r.state.wait_action()
        assert not r.state.pending_winners
        assert not r.state.player_list[1].combination_tiles
    asyncio.run(scenario())


def test_pung_at_six_seconds_then_new_discard_step_with_remaining_bank():
    async def scenario():
        r = Rig(); r.claim(count=3); await r.broadcast()
        r.at = 106
        await r.submit(1, 'peng')
        await r.state.wait_action()
        p = r.state.player_list[1]
        assert p.combination_tiles == ['k11'] and p.remaining_time == 19
        assert r.state.game_status == 'onlycut_after_action'
        r.state.action_dict = r.state.after_claim_actions()
        r.state.game_status = 'waiting_hand_action'
        await r.broadcast()
        info = wire(r.messages[1], 'ask_hand_action_info')[-1]
        assert (info['remaining_time'], info['step_remaining']) == (19, 5)
        assert info['action_list'] == ['cut']
        assert r.state.action_clock.deadline(1) == 130
        r.at = 110
        await r.submit(1, 'cut', TileId=p.hand_tiles[0], cutClass='hand', hand_card_index=0)
        await r.state.wait_action()
        assert p.remaining_time == 19
    asyncio.run(scenario())


@pytest.mark.parametrize('action', ['hu_self', 'angang', 'jiagang', 'gang'])
def test_hu_and_each_kong_share_room_timing_then_forced_completion_does_not_charge(action):
    async def scenario():
        r = Rig()
        if action == 'gang':
            r.claim(count=3); index = 1
        else:
            tiles = ([11]*4 + HAND[4:] if action == 'angang' else
                     [11,22,23,24,31,32,33,27,28,29] if action == 'jiagang' else HAND)
            r.hand(tiles, ['k11'] if action == 'jiagang' else ())
            index = 0
        await r.broadcast(); r.at = 107
        assert action in r.state.action_dict[index]
        await r.submit(index, action, target_tile=11)
        await r.state.wait_action()
        assert r.state.player_list[index].remaining_time == 18
        if action == 'hu_self':
            assert r.state.pending_winners and r.state.game_status == 'END'
        elif action == 'jiagang':
            assert not any(r.state.check_added_kong_actions(11).values())
            if r.state.game_status == 'waiting_action_qianggang':
                r.at = 140
                await r.state.wait_action()
            assert r.state.game_status == 'deal_card_after_gang'
            assert r.state.player_list[index].remaining_time == 18
        else:
            assert r.state.game_status == 'deal_card_after_gang'
        if action != 'hu_self':
            r.at = 150
            await r.state._deal_supplement()
            await r.broadcast()
            info = wire(r.messages[index], 'ask_hand_action_info')[-1]
            assert (info['remaining_time'], info['step_remaining']) == (18, 5)
    asyncio.run(scenario())


def test_fractional_overtime_is_not_returned_between_kong_and_replacement_discard():
    async def scenario():
        r = Rig(); r.hand([11] * 4 + HAND[4:]); await r.broadcast()
        r.at = 105.6
        await r.submit(0, 'angang', target_tile=11)
        await r.state.wait_action()
        assert r.state.action_clock.balances[0] == pytest.approx(19.4)
        assert r.state.player_list[0].remaining_time == 20  # Integer display only.
        await r.state._deal_supplement(); await r.broadcast()
        assert r.state.action_clock.window.banks[0] == pytest.approx(19.4)
        assert r.state.action_clock.deadline(0) == pytest.approx(130)
        r.at = 111.2
        await r.submit_through_protocol(0, 'cut')
        await r.state.wait_action()
        assert r.state.action_clock.balances[0] == pytest.approx(18.8)
        assert r.state.player_list[0].remaining_time == 19
    asyncio.run(scenario())


def test_pending_reconnect_and_spectator_do_not_restart_or_mutate_bank():
    async def scenario():
        r = Rig(0, 5); r.hand(); await r.broadcast(); r.at = 104
        uid = r.state.player_list[0].user_id
        for _ in range(3):
            await reconnected_send_pending_ask_for_viewer(r.state, uid, 0)
            info = wire(r.messages[0], 'ask_hand_action_info')[-1]
            assert (info['remaining_time'], info['step_remaining']) == (0, 1)
        r.state.game_server.user_id_to_connection[999] = SimpleNamespace(
            websocket=SimpleNamespace(send_json=AsyncMock()))
        await send_realtime_spectator_snapshot(r.state, 999, 0)
        frames = r.state.game_server.user_id_to_connection[999].websocket.send_json.call_args_list
        ask = next(call.args[0]['ask_hand_action_info'] for call in frames if 'ask_hand_action_info' in call.args[0])
        assert (ask['remaining_time'], ask['step_remaining']) == (0, 1)
        assert r.state.action_clock.deadline(0) == 105
        assert r.state.player_list[0].remaining_time == 0
    asyncio.run(scenario())


def test_already_replied_player_is_not_reasked_or_debited_while_other_player_pending():
    async def scenario():
        r = Rig(); r.claim(extra=True); await r.broadcast()
        task = asyncio.create_task(r.state.wait_action()); await pump()
        r.at = 108; await r.submit_through_protocol(1, 'pass'); await pump()
        assert not task.done() and r.state.player_list[1].remaining_time == 17
        r.at = 109
        uid = r.state.player_list[1].user_id
        before = len(r.messages[1])
        await reconnected_send_pending_ask_for_viewer(r.state, uid, 1)
        assert len(r.messages[1]) == before
        assert r.state.action_clock(r.state.player_list[1], reconnecting=True) == (17, 0)
        assert r.state.player_list[1].remaining_time == 17
        await r.submit_through_protocol(1, 'pass')
        assert r.state.action_queues[1].empty(), 'repeat response must not reopen a submitted seat'
        await r.submit_through_protocol(2, 'pass'); await asyncio.wait_for(task, .5)
        assert r.state.player_list[1].remaining_time == 17
        assert r.state.player_list[2].remaining_time == 16
    asyncio.run(scenario())


def test_claims_expire_by_each_players_own_remaining_bank():
    async def scenario():
        r = Rig(); r.claim(extra=True)
        r.state.player_list[1].remaining_time = 3
        await r.broadcast()
        assert r.state.action_clock.deadline(1) == 108
        assert r.state.action_clock.deadline(2) == 125
        task = asyncio.create_task(r.state.wait_action()); await pump()
        r.at = 108; r.wake(); await pump()
        assert not task.done()
        assert r.state.waiting_players_list == [2]
        assert r.state.player_list[1].remaining_time == 0
        assert r.state.player_list[2].remaining_time == 20
        r.at = 110
        await r.submit_through_protocol(2, 'pass')
        await asyncio.wait_for(task, .5)
        assert r.state.player_list[1].remaining_time == 0
        assert r.state.player_list[2].remaining_time == 15
    asyncio.run(scenario())


def test_rebroadcast_and_forged_tile_keep_original_deadline_and_charge_once():
    async def scenario():
        r = Rig(); r.hand(); await r.broadcast()
        r.at = 108
        await r.submit(0, 'cut', TileId=99, cutClass='hand', hand_card_index=0)
        await r.state.wait_action()
        assert r.state.game_status == 'waiting_hand_action'
        assert not r.state.player_list[0].discard_tiles
        await r.broadcast()
        info = wire(r.messages[0], 'ask_hand_action_info')[-1]
        assert (info['remaining_time'], info['step_remaining']) == (17, 0)
        assert r.state.action_clock.deadline(0) == 125
        r.at = 109
        await r.submit(0, 'cut', TileId=45, cutClass='mo', hand_card_index=13)
        await r.state.wait_action()
        assert r.state.player_list[0].remaining_time == 16
    asyncio.run(scenario())


def test_illegal_action_word_does_not_restart_the_window():
    async def scenario():
        r = Rig(); r.claim(); await r.broadcast()
        task = asyncio.create_task(r.state.wait_action()); await pump()
        r.at = 102; await r.submit(1, 'hu_other'); await pump()
        assert not task.done()
        r.at = 108; await r.submit(1, 'pass'); await asyncio.wait_for(task, .5)
        assert r.state.player_list[1].remaining_time == 17
    asyncio.run(scenario())


@pytest.mark.parametrize('phase', ['hand', 'claim'])
def test_timely_reply_survives_slow_spectator_broadcast(phase):
    async def scenario():
        r = Rig()
        r.hand() if phase == 'hand' else r.claim()
        index = 0 if phase == 'hand' else 1
        async def delayed(view, _response):
            if view == index:
                r.at = 102
                if phase == 'hand':
                    await r.submit(index, 'cut', TileId=45, cutClass='mo', hand_card_index=13)
                else:
                    await r.submit(index, 'pass')
                r.at = 130
        r.state.send_to_realtime_spectators = delayed
        await r.broadcast()
        await r.state.wait_action()
        assert r.state.player_list[index].remaining_time == 20
        if phase == 'hand':
            assert r.state.player_list[0].discard_tiles == [45]
    asyncio.run(scenario())


@pytest.mark.parametrize('phase', ['hand', 'claim'])
def test_real_submission_entry_accepts_timely_reply_during_broadcast(phase):
    async def scenario():
        r = Rig()
        r.hand() if phase == 'hand' else r.claim()
        index = 0 if phase == 'hand' else 1
        async def delayed(view, _response):
            if view == index:
                r.at = 102
                await r.submit_through_protocol(index, 'cut' if phase == 'hand' else 'pass')
                r.at = 130
        r.state.send_to_realtime_spectators = delayed
        await r.broadcast()
        assert not r.state.action_queues[index].empty(), 'timely reply rejected before collector started'
        await r.state.wait_action()
        assert r.state.player_list[index].remaining_time == 20
        if phase == 'hand':
            assert r.state.player_list[index].discard_tiles == [45]
    asyncio.run(scenario())


def test_malformed_or_stale_protocol_reply_does_not_close_or_restart_claim_window():
    async def scenario():
        from ..public.ai.get_action import get_action
        r = Rig(); r.claim(); await r.broadcast()
        task = asyncio.create_task(r.state.wait_action()); await pump()
        r.at = 102
        await r.state.action_queues[1].put(['pass'])
        r.state.action_events[1].set()
        await get_action(r.state, 'connection-1', 'peng', None, None, None, None,
                         action_tick=r.state.server_action_tick - 1)
        await pump()
        assert not task.done()
        assert r.state.action_clock.deadline(1) == 125
        r.at = 108
        await r.submit_through_protocol(1, 'pass')
        await asyncio.wait_for(task, .5)
        assert r.state.player_list[1].remaining_time == 17
    asyncio.run(scenario())


def test_cancelled_collector_clears_waiting_list_and_owned_event_tasks():
    async def scenario():
        r = Rig(); r.claim(extra=True); await r.broadcast()
        task = asyncio.create_task(r.state.wait_action()); await pump()
        assert r.state.waiting_players_list == [1, 2]
        tasks_before = set(asyncio.all_tasks())
        task.cancel()
        with pytest.raises(asyncio.CancelledError):
            await task
        await pump()
        assert r.state.waiting_players_list == []
        assert r.state.player_list[1].remaining_time == 20
        assert all(t.done() for t in tasks_before if t not in (asyncio.current_task(), task))
    asyncio.run(scenario())


def test_initial_send_delay_starts_the_action_budget_at_delivery():
    async def scenario():
        r = Rig(); r.hand()
        original = r.state.game_server.user_id_to_connection[101].websocket.send_json
        async def slow(message):
            r.at = 109
            await original(message)
        r.state.game_server.user_id_to_connection[101].websocket.send_json = slow
        await r.broadcast()
        assert r.state.action_clock.deadline(0) == 134
        r.at = 113
        await r.submit(0, 'cut', TileId=45, cutClass='mo', hand_card_index=13)
        await r.state.wait_action()
        assert r.state.player_list[0].remaining_time == 20
    asyncio.run(scenario())


def test_each_claimants_first_frame_matches_their_own_delivery_deadline():
    async def scenario():
        r = Rig(); r.claim(extra=True)
        async def slow_spectator(view, _response):
            if view == 1:
                r.at = 102
                await r.submit_through_protocol(1, 'pass')
                r.at = 130
        r.state.send_to_realtime_spectators = slow_spectator
        await r.broadcast()
        first = wire(r.messages[2], 'ask_other_action_info')[-1]
        assert (first['remaining_time'], first['step_remaining']) == (20, 5)
        assert r.state.action_clock.deadline(1) == 125
        assert r.state.action_clock.deadline(2) == 155
        r.at = 132
        await r.submit_through_protocol(2, 'pass')
        await r.state.wait_action()
        assert [r.state.player_list[i].remaining_time for i in (1, 2)] == [20, 20]
    asyncio.run(scenario())


@pytest.mark.parametrize('phase', ['hand', 'claim'])
def test_first_ask_via_player_reconnect_does_not_renew_on_original_broadcast(phase):
    async def scenario():
        r = Rig()
        r.hand() if phase == 'hand' else r.claim()
        s = r.state
        s.prepare_action_window()
        index = 0 if phase == 'hand' else 1
        kind = 'ask_hand_action_info' if phase == 'hand' else 'ask_other_action_info'
        await s.player_reconnect(s.player_list[index].user_id)
        first = wire(r.messages[index], kind)[-1]
        assert (first['remaining_time'], first['step_remaining']) == (20, 5)
        assert s.action_clock.window.delivered[index] == 100
        assert index in s.waiting_players_list
        r.at = 108
        await r.broadcast()
        repeated = wire(r.messages[index], kind)[-1]
        assert (repeated['remaining_time'], repeated['step_remaining']) == (17, 0)
        assert s.action_clock.deadline(index) == 125
        await r.submit_through_protocol(index, 'cut' if phase == 'hand' else 'pass')
        await s.wait_action()
        assert s.player_list[index].remaining_time == 17
    asyncio.run(scenario())


@pytest.mark.parametrize('phase', ['hand', 'claim'])
@pytest.mark.parametrize('first_via_reconnect', [False, True])
def test_rebroadcast_preserves_reply_already_received_for_same_physical_window(phase, first_via_reconnect):
    async def scenario():
        r = Rig()
        r.hand() if phase == 'hand' else r.claim()
        index = 0 if phase == 'hand' else 1
        if first_via_reconnect:
            await r.state.player_reconnect(r.state.player_list[index].user_id)
        else:
            await r.broadcast()
        r.at = 102
        await r.submit_through_protocol(index, 'cut' if phase == 'hand' else 'pass')
        assert not r.state.action_queues[index].empty()
        r.at = 130  # Only the collector is delayed; the original reply was timely.
        await r.broadcast()
        assert not r.state.action_queues[index].empty(), 'same-window preparation discarded a timely reply'
        await r.state.wait_action()
        assert r.state.player_list[index].remaining_time == 20
        if phase == 'hand':
            assert r.state.player_list[index].discard_tiles == [45]
    asyncio.run(scenario())


def test_hand_sort_is_not_a_new_action_window():
    async def scenario():
        r = Rig(); r.hand(); await r.broadcast(); r.at = 108
        r.state.player_list[0].hand_tiles.reverse()
        await r.broadcast()
        assert r.state.action_clock.deadline(0) == 125
        info = wire(r.messages[0], 'ask_hand_action_info')[-1]
        assert (info['remaining_time'], info['step_remaining']) == (17, 0)
    asyncio.run(scenario())


def test_real_new_decision_clears_stale_response_from_previous_claim():
    async def scenario():
        r = Rig(); r.claim(count=3); await r.broadcast()
        r.at = 102
        await r.submit_through_protocol(1, 'peng')
        await r.state.wait_action()
        assert r.state.game_status == 'onlycut_after_action'
        await r.submit(1, 'pass')  # In-flight response belonging to the closed claim.
        r.state.action_dict = r.state.after_claim_actions()
        r.state.game_status = 'waiting_hand_action'
        await r.broadcast()
        assert r.state.action_queues[1].empty()
        assert r.state.action_clock.deadline(1) == 127
        assert r.state.player_list[1].remaining_time == 20
    asyncio.run(scenario())


def test_round_reset_discards_old_timing_and_restores_configured_bank():
    async def scenario():
        from .test_lifecycle import round_fixture
        from ..public.ai.get_action import get_ai_action
        r = Rig(9, 2)
        s = r.state
        s.init_tiles = lambda: round_fixture(s)
        s.game_server.gamestate_manager.cleanup_game_state_complete = AsyncMock()
        s.spectator_manager.send_final_record_and_close = AsyncMock()
        s.run_hu_result_ready_phase = AsyncMock()
        original_wait = s.wait_action
        first_banks = {}
        charged = []
        async def decisions():
            if s.round_index not in first_banks:
                first_banks[s.round_index] = [p.remaining_time for p in s.player_list]
                assert first_banks[s.round_index] == [9] * 4
            s.waiting_players_list = [i for i, actions in s.action_dict.items() if actions]
            initial = {i: s.action_clock.balances.get(i, s.player_list[i].remaining_time)
                       for i in s.waiting_players_list}
            r.at += 3  # Real action window, one second beyond the configured step.
            for index in s.waiting_players_list:
                actions = s.action_dict[index]
                winner = s.round_index - 1
                action = 'hu_self' if index == winner < 3 and 'hu_self' in actions else 'cut' if 'cut' in actions else 'pass'
                p = s.player_list[index]
                await get_ai_action(s, index, action, p.has_draw_slot if action == 'cut' else None,
                                    p.hand_tiles[-1] if action == 'cut' else None,
                                    len(p.hand_tiles)-1 if action == 'cut' else None, None)
            await original_wait()
            for index, before in initial.items():
                assert s.action_clock.balances[index] == pytest.approx(max(0, before - 1))
                charged.append((s.round_index, index))
        s.wait_action = decisions
        with patch('server.gamestate.game_hongzhong.HongzhongGameState.liuju_ready_wait_seconds', return_value=0):
            await s.game_loop_chinese()
        assert list(first_banks) == [1, 2, 3, 4]
        assert len(charged) >= 4
        assert len(s._local_record_detail.record['game_round']) == 4
    asyncio.run(scenario())


def test_red_book_does_not_open_ready_chow_flower_or_rob_response_windows():
    r = Rig(); r.hand()
    assert r.state.ready_candidate_cuts(0) == {}
    assert r.state.flower_tiles == ()
    assert not any(r.state.check_added_kong_actions(11).values())
    r.claim()
    assert all(not action.startswith('chi') and action != 'hu_other'
               for actions in r.state.action_dict.values() for action in actions)


def test_inherited_state_keeps_tactical_call_and_claim_protection_disabled():
    # Even an invalid saved room cannot turn a normal Hongzhong claim into recheck.
    state = make_state(tactical_call=True, claim_protection=True)
    assert state.tactical_call is False
    assert state.claim_protection is False
    assert state.claim_response_seconds is None
