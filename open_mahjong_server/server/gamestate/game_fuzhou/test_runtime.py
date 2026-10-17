"""Real queues, clock expiry, reconnect visibility, and the full match driver."""
import asyncio
from collections import Counter
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

import pytest

from .FuzhouGameState import FuzhouGameState
from .bot import choose_action, play_bot
from .runtime import ActionClock
from .state_machine import Phase as P
from .test_state import WIN, act, open_turn, state_with
from ...game_calculation.fuzhou import rules as book


def test_timeout_cut_and_flower_use_legal_action_and_consume_only_own_bank():
    async def run():
        s = state_with(); open_turn(s)
        actions = await s.wait_action()
        assert actions[0]['action_type'] == 'cut'
        assert actions[0]['TileId'] == WIN[-1] and actions[0]['cutClass']
        assert s.player_list[0].remaining_time == 0
        t = FuzhouGameState(calculation_service=object()); t.initialize_round()
        t.open_action_window(t.opening_window())
        actor = t.live_pending_window['player']
        result = await t.wait_action()
        assert result[actor]['action_type'] == 'buhua'
        act(t, result)
        assert t.physical_inventory() == Counter(book.PHYSICAL)
    asyncio.run(run())


def test_claim_expiry_consumes_bank_and_passes_claim():
    async def run():
        s = state_with(); s.player_list[1].hand_tiles = WIN[:-1]
        s.step_time = 5
        for p in s.player_list: p.remaining_time = 17
        open_turn(s); act(s, {0: dict(action_type='cut', TileId=29, cutClass=True)})
        for i in s.waiting_players_list:
            s.action_clocks[i].start(s._timing_now()-23)
        result = await s.wait_action()
        assert result and all(a['action_type']=='pass' for a in result.values())
        assert all(s.player_list[i].remaining_time == 0 for i in result)
        assert s.player_list[0].remaining_time == 17
        act(s, result)
        assert s.machine.phase in (P.TURN, P.REPLACEMENT)
    asyncio.run(run())


def test_wait_event_receives_delayed_response_without_charging_step_time():
    async def run():
        s = state_with(); s.step_time = 2; s.player_list[0].remaining_time = 5
        open_turn(s)
        task = asyncio.create_task(s.wait_action())
        await asyncio.sleep(.005)
        await s.submit_action(0, 'hu_self')
        result = await task
        assert result[0]['action_type'] == 'hu_self'
        assert s.player_list[0].remaining_time == 5
        assert not s.waiting_players_list and not s.action_events[0].is_set()
        assert not [t for t in asyncio.all_tasks() if t is not asyncio.current_task() and not t.done()]
    asyncio.run(run())


@pytest.mark.parametrize('index', [-1, 4, True, None])
def test_submit_rejects_invalid_seats(index):
    async def run():
        s = state_with(); open_turn(s)
        with pytest.raises(ValueError): await s.submit_action(index, 'hu_self')
    asyncio.run(run())


def test_received_time_is_used_even_when_processing_is_delayed():
    async def run():
        s = state_with(); s.step_time = 2; s.player_list[0].remaining_time = 10
        open_turn(s); s.action_clocks[0].start(100)
        s._timing_now = lambda: 105
        await s.submit_action(0, 'hu_self')
        with pytest.raises(ValueError): await s.submit_action(0, 'hu_self')
        s._timing_now = lambda: 200
        await s.wait_action()
        assert s.player_list[0].remaining_time == 7
    asyncio.run(run())


def test_ready_timeout_skips_bots_and_never_consumes_clock_bank():
    async def run():
        s = state_with(); s.end_draw()
        s.player_list[2].user_id = 2
        for p in s.player_list: p.remaining_time = 17
        s.flush_outbound_payloads = AsyncMock()
        await s.run_round_ready_phase(0)
        assert s.machine.phase == P.READY and not s.waiting_players_list
        assert all(p.remaining_time==17 for p in s.player_list)
        assert all(not x for x in s.action_dict.values())
        assert s.flush_outbound_payloads.await_count == 2
    asyncio.run(run())


def test_bot_tasks_deduplicate_tick_and_ignore_stale_or_already_answered(monkeypatch):
    import server.gamestate.game_fuzhou.bot as bot
    async def run():
        s = state_with(); open_turn(s); s.player_list[0].user_id = 1
        monkeypatch.setattr(bot, 'wait_bot_delay', AsyncMock())
        await play_bot(s, 0, s.server_action_tick-1)
        assert s.action_queues[0].empty()
        s.schedule_bot_actions(); s.schedule_bot_actions()
        assert len(s.bot_tasks) == 1
        await asyncio.gather(*list(s.bot_tasks))
        assert s.action_queues[0].qsize() == 1
        await play_bot(s, 0, s.server_action_tick)
        assert s.action_queues[0].qsize() == 1
        s.action_queues[0].get_nowait()
        s.submit_action = AsyncMock(side_effect=ValueError('stale'))
        await play_bot(s, 0, s.server_action_tick)
    asyncio.run(run())


def test_reconnect_and_spectator_preserve_authority_and_only_reveal_selected_hand():
    async def run():
        s = state_with(); s.step_time = 5; open_turn(s)
        s.action_clocks[0].start(100)
        s._timing_now = lambda: 101
        sent = []
        async def send(i, payload): sent.append((i,payload))
        s.send_payload_to_player = send
        s.player_list[0].tag_list.append('offline')
        await s.player_reconnect(s.player_list[0].user_id)
        assert 'offline' not in s.player_list[0].tag_list
        assert [p['type'] for _,p in sent] == ['gamestate/fuzhou/game_start','gamestate/fuzhou/broadcast_hand_action']
        info = sent[0][1]['game_info']
        assert info['players_info'][0]['hand_tiles'] == WIN
        assert all(p['hand_tiles'] is None for p in info['players_info'][1:])
        assert info['fuzhou_info']['final_hands'] is None
        assert s.action_clocks[0].deadline == 105
        n = len(sent); await s.player_reconnect(-999)
        assert len(sent)==n
        ws = NS(send_json=AsyncMock())
        s.game_server = NS(user_id_to_connection={999:NS(websocket=ws),998:NS(websocket=None)})
        await s.send_realtime_spectator_snapshot(999,1)
        snapshot = ws.send_json.await_args_list[0].args[0]['game_info']
        assert snapshot['self_hand_tiles'] == s.player_list[1].hand_tiles
        assert snapshot['players_info'][0]['hand_tiles'] is None
        n = ws.send_json.await_count
        for uid,index in [(999,-1),(998,1),(997,1)]: await s.send_realtime_spectator_snapshot(uid,index)
        assert ws.send_json.await_count==n
        act(s,{0:dict(action_type='hu_self')})
        await s.player_reconnect(s.player_list[0].user_id)
        scores = [p.score for p in s.player_list]
        await s.player_reconnect(s.player_list[0].user_id)
        assert [p.score for p in s.player_list] == scores == [120,-40,-40,-40]
        assert sent[-1][1]['game_info']['players_info'][1]['hand_tiles'] is not None
    asyncio.run(run())


@pytest.mark.parametrize('duration',[1,2,4])
def test_full_match_driver_keeps_all_physical_tiles_and_counts_only_dealer_changes(duration):
    async def run():
        data = FuzhouGameState._default_room_data(); data['random_seed'] = 15; data['game_round'] = duration
        s = FuzhouGameState(room_data=data,calculation_service=object())
        observed, windows = [], 0
        async def flush():
            observed.extend(p.get('type') for p in s.outbound_payloads if 'type' in p)
            s.outbound_payloads.clear()
        async def decide(timeout=None):
            nonlocal windows
            windows += 1
            assert windows < 5000*duration, 'match did not converge'
            assert s.physical_inventory() == Counter(book.PHYSICAL)
            return {i:choose_action(s,i,s.action_dict[i]) for i in list(s.waiting_players_list)}
        s.flush_outbound_payloads = flush
        s.wait_action = decide
        s.complete_game_lifecycle = AsyncMock()
        await s.run_game_loop()
        assert s.machine.phase == P.FINISHED
        assert s.next_dealer_changes == duration*4 and s.match_finishing
        assert sum(p.score for p in s.player_list) == 0
        assert len(s.game_record['game_round']) >= duration*4
        assert len({len(p.score_history) for p in s.player_list}) == 1
        assert 'gamestate/fuzhou/game_start' in observed
        s.complete_game_lifecycle.assert_awaited_once()
    asyncio.run(run())


def test_match_driver_cancels_inflight_bots_on_failure():
    async def run():
        s = state_with()
        task = asyncio.create_task(asyncio.sleep(100))
        s.bot_tasks.add(task)
        with pytest.raises(RuntimeError, match='须完成上局确认阶段'):
            await s.run_game_loop()
        await asyncio.gather(task,return_exceptions=True)
        assert task.cancelled()
    asyncio.run(run())


def test_actual_delivery_starts_clock_once_and_ignores_stale_or_empty_messages(monkeypatch):
    from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
    inherited = AsyncMock()
    monkeypatch.setattr(ZhongyongGameState,'_deliver_claim_payload',inherited)
    async def run():
        s=state_with();s.step_time=5;open_turn(s)
        s._timing_now=lambda:100
        payload=s.build_pending_action_payload(0)
        assert s.action_clocks[0].start_time is None
        await s._deliver_claim_payload(0,payload)
        assert s.action_clocks[0].deadline==105
        s._timing_now=lambda:101
        await s._deliver_claim_payload(0,payload)
        assert s.action_clocks[0].deadline==105
        for packet in ({}, {'ask_hand_action_info':{'action_tick':-1,'action_list':['cut']}},
                       {'ask_other_action_info':{'action_tick':s.server_action_tick,'action_list':[]}}):
            await s._deliver_claim_payload(0,packet)
        assert inherited.await_count==5
        s.action_queues[0].put_nowait(dict(action_type='cut',TileId=29))
        open_turn(s)
        assert s.action_queues[0].empty()
    asyncio.run(run())


def test_missing_clock_does_not_fabricate_elapsed_time_or_drop_valid_response(monkeypatch):
    """Fault injection for a missing clock entry during delivery/recovery."""
    from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
    inherited = AsyncMock()
    monkeypatch.setattr(ZhongyongGameState, '_deliver_claim_payload', inherited)
    async def run():
        s = state_with(); s.step_time = 5; s.player_list[0].remaining_time = 17
        open_turn(s)
        payload = s.build_pending_action_payload(0)
        s.action_clocks.clear()
        await s._deliver_claim_payload(0, payload)
        await s.submit_action(0, 'hu_self')
        response = await s.wait_action()
        assert response[0]['action_type'] == 'hu_self'
        assert s.player_list[0].remaining_time == 17
        assert not s.waiting_players_list and not s.action_clocks
        inherited.assert_awaited_once_with(0, payload)
    asyncio.run(run())


def test_timeout_helper_can_finish_a_seat_removed_from_waiting_set():
    s = state_with(); open_turn(s)
    s.waiting_players_list.remove(0)
    other_banks = [p.remaining_time for p in s.player_list[1:]]
    response = s._build_timeout_action(0)
    assert response['player_index'] == 0 and response['action_type'] == 'cut'
    assert response['TileId'] == WIN[-1] and response['cutClass']
    assert not s.waiting_players_list and not s.action_dict[0]
    assert [p.remaining_time for p in s.player_list[1:]] == other_banks


def test_waiter_does_not_emit_phantom_action_when_timeout_window_is_cancelled():
    """A cancelled timeout must finish without reporting a nonexistent move."""
    async def run():
        s = state_with(); open_turn(s)
        def cancel_window(index):
            s.waiting_players_list.remove(index)
            s.action_dict[index] = []
            return None
        s._build_timeout_action = cancel_window
        assert await s.wait_action() == {}
        assert not s.waiting_players_list
        assert not [t for t in asyncio.all_tasks() if t is not asyncio.current_task() and not t.done()]
    asyncio.run(run())
