"""后手自动选牌须给玩家留出可见的三秒阶段间隔。"""
import asyncio
import time
from unittest.mock import patch

import pytest

from .boardcast import _auto_xueliu_throw_three, broadcast_xueliu_throw_three_ask, broadcast_dingque_done
from .test_action_window import submit
from .test_xueliu_doubles import make_state
from ..public.lifecycle import cancel_auxiliary_tasks, start_owned_task


@pytest.mark.parametrize('doubles,expected_delay', [(False, 1.0), (True, 1.0)])
def test_dingque_reveal_is_broadcast_before_presentation_wait(doubles, expected_delay):
    async def scenario():
        state, messages = make_state()
        state.is_xueliu_doubles = doubles
        waiting, finish = asyncio.Event(), asyncio.Event()
        start_tick = state.server_action_tick

        async def sleep(delay):
            assert delay == expected_delay
            reveals = [m for m in messages if m['type'].endswith('/dingque_done')]
            assert len(reveals) == 4
            for message in reveals:
                result = message['message']['show_result_info']
                assert set(result['player_to_dingque'].values()) == {3}
                assert result['action_tick'] == start_tick + 1
            waiting.set()
            await finish.wait()

        with patch(__package__ + '.boardcast.asyncio.sleep', sleep):
            task = asyncio.create_task(broadcast_dingque_done(state))
            try:
                await asyncio.wait_for(waiting.wait(), 1)
                assert not task.done(), '开局在定缺卡落位前就继续行牌'
                finish.set()
                await task
            finally:
                if not task.done():
                    task.cancel()
                    await asyncio.gather(task, return_exceptions=True)

    asyncio.run(scenario())


def test_real_two_stage_exchange_keeps_second_group_visible_for_three_seconds():
    async def scenario():
        state, _ = make_state()
        state.current_round = 1
        state.bot_speed = 'instant'
        # 先手为真人，后手同时包含摸切和智能机器人。
        state.player_list[2].user_id = 0
        state.player_list[3].user_id = 2
        seen = []
        chosen_at = {}
        original_choose = state._default_xueliu_throw_tiles

        def choose(index):
            if state.exchange_stage == 2:
                chosen_at[index] = time.monotonic()
            return original_choose(index)

        for index in (0, 1):
            connection = state.game_server.user_id_to_connection[state.player_list[index].user_id]
            original_send = connection.websocket.send_json

            async def send(payload, index=index, original_send=original_send):
                await original_send(payload)
                info = payload.get('ask_hand_action_info')
                if not info or not info.get('xueliu_exchange'):
                    return
                if info['xueliu_exchange']['animating']:
                    assert not info['action_list']
                    return
                phase = info['xueliu_exchange']['stage']
                seen.append((phase, index, time.monotonic()))
                if info['action_list']:
                    await submit(state, index, 'xueliu_exchange_three',
                                 tick=info['action_tick'], selected_tiles=original_choose(index))

            connection.websocket.send_json = send

        try:
            with patch.object(state, '_default_xueliu_throw_tiles', choose):
                await asyncio.wait_for(state._xueliu_throw_three_phase(), 8)
            assert [phase for phase, _, _ in seen] == [1, 1, 2, 2]
            last_prompt = max(at for phase, _, at in seen if phase == 2)
            assert set(chosen_at) == {2, 3}
            # Windows 的事件循环时钟可能有约 15ms 分辨率；配置值另由并行测试精确核对。
            assert all(at - last_prompt >= 2.98 for at in chosen_at.values())
            assert all(len(p.xueliu_throw_tiles) == 3 for p in state.player_list)
            assert state.exchange_stage == 0
        finally:
            await cancel_auxiliary_tasks(state)

    asyncio.run(scenario())


def second_group_state():
    state, _ = make_state()
    state.game_status = 'waiting_xueliu_throw'
    state.exchange_stage = 2
    state.exchange_groups = [[2, 3], [0, 1]]
    state.action_dict = {0: ['xueliu_exchange_three'], 1: ['xueliu_exchange_three'], 2: [], 3: []}
    state.server_action_tick = 12
    state.player_list[0].user_id = 0
    state.player_list[1].user_id = 2
    return state


@pytest.mark.parametrize('speed', ['instant', 'fast', 'medium', 'slow'])
def test_second_group_waits_in_parallel_after_all_human_prompts(speed):
    async def scenario():
        state = second_group_state()
        state.bot_speed = speed
        delivered, delays = [], []
        both_waiting, release = asyncio.Event(), asyncio.Event()
        real_sleep = asyncio.sleep

        async def send(current, index, response):
            delivered.append(index)
            await real_sleep(0)

        async def sleep(delay):
            assert delivered == [2, 3], '后手提示尚未发完，机器人就开始选牌'
            delays.append(delay)
            if len(delays) == 2:
                both_waiting.set()
            await release.wait()

        try:
            with patch(__package__ + '.boardcast._send_ask_response_to_viewer', send), \
                 patch(__package__ + '.boardcast.asyncio.sleep', sleep):
                await broadcast_xueliu_throw_three_ask(state)
                await asyncio.wait_for(both_waiting.wait(), 1)
                assert delays == [3.0, 3.0]
                assert all(state.action_queues[i].empty() for i in range(4))
                assert not any(event.is_set() for event in state.action_events.values())
                tasks = list(state._lifecycle_tasks)
                release.set()
                await asyncio.gather(*tasks)
            for i in (0, 1):
                action = state.action_queues[i].get_nowait()
                assert action['_action_tick'] == 12
                assert len(action['selected_tiles']) == 3
                assert state.action_events[i].is_set()
        finally:
            await cancel_auxiliary_tasks(state)

    asyncio.run(scenario())


@pytest.mark.parametrize('change', ['new_tick', 'new_phase', 'already_submitted', 'closed'])
def test_delayed_selection_cannot_submit_to_an_expired_window(change):
    async def scenario():
        state = second_group_state()
        waiting, release = asyncio.Event(), asyncio.Event()

        async def sleep(delay):
            waiting.set()
            await release.wait()

        try:
            with patch(__package__ + '.boardcast.asyncio.sleep', sleep):
                task = start_owned_task(state, _auto_xueliu_throw_three(state, 0))
                await asyncio.wait_for(waiting.wait(), 1)
                if change == 'new_tick':
                    state.server_action_tick += 1
                elif change == 'new_phase':
                    state.game_status = 'waiting_dingque'
                elif change == 'already_submitted':
                    state.action_dict[0] = []
                else:
                    state.lifecycle_state = 'closed'
                release.set()
                await task
            assert state.action_queues[0].empty()
            assert not state.action_events[0].is_set()
            assert not state.player_list[0].xueliu_throw_tiles
        finally:
            await cancel_auxiliary_tasks(state)

    asyncio.run(scenario())


def test_game_cleanup_cancels_pending_second_group_selection():
    async def scenario():
        state = second_group_state()
        waiting = asyncio.Event()

        async def sleep(delay):
            waiting.set()
            await asyncio.Event().wait()

        with patch(__package__ + '.boardcast.asyncio.sleep', sleep):
            task = start_owned_task(state, _auto_xueliu_throw_three(state, 0))
            await asyncio.wait_for(waiting.wait(), 1)
            await cancel_auxiliary_tasks(state)
        assert task.cancelled()
        assert state.action_queues[0].empty()

    asyncio.run(scenario())


@pytest.mark.parametrize('doubles,stage', [(True, 1), (False, 0), (False, 2)])
def test_first_group_and_other_blood_flow_openings_have_no_extra_wait(doubles, stage):
    async def scenario():
        state = second_group_state()
        state.is_xueliu_doubles = doubles
        state.exchange_stage = stage

        async def unexpected_wait(delay):
            raise AssertionError('Only the doubles second group should wait')

        with patch(__package__ + '.boardcast.asyncio.sleep', unexpected_wait):
            await _auto_xueliu_throw_three(state, 0)
        assert state.action_events[0].is_set()
        assert len(state.action_queues[0].get_nowait()['selected_tiles']) == 3

    asyncio.run(scenario())
