"""两人选齐、交换动画、下一组开放的服务端顺序及重连可见性。"""
import asyncio
import time
from unittest.mock import patch

import pytest

from .test_xueliu_doubles import make_state
from .test_action_window import submit
from .boardcast import broadcast_xueliu_exchange_animation, reconnected_send_pending_ask


@pytest.mark.parametrize('round_number', [1, 2, 3, 4])
def test_both_selections_then_animation_then_next_pair(round_number):
    async def scenario():
        state, messages = make_state()
        state.current_round = round_number
        first = (round_number - 1) % 4
        groups = [[first, (first+1)%4], [(first+2)%4, (first+3)%4]]
        prompts, animations = asyncio.Queue(), asyncio.Queue()
        finish = {1: asyncio.Event(), 2: asyncio.Event()}

        async def ask(current):
            await prompts.put((current.exchange_stage, list(current.waiting_players_list)))

        async def animate(current):
            await broadcast_xueliu_exchange_animation(current)
            await animations.put(current.exchange_stage)
            await finish[current.exchange_stage].wait()

        async def choose(index):
            await submit(state, index, 'xueliu_exchange_three',
                         selected_tiles=state._default_xueliu_throw_tiles(index))

        with patch(__package__+'.boardcast.broadcast_xueliu_throw_three_ask', ask), \
             patch(__package__+'.boardcast.broadcast_xueliu_exchange_animation', animate):
            task = asyncio.create_task(state._xueliu_throw_three_phase())
            try:
                for stage, group in enumerate(groups, 1):
                    assert await asyncio.wait_for(prompts.get(), 2) == (stage, group)
                    assert not state.exchange_view(group[0])['animating']
                    await choose(group[0])
                    for _ in range(100):
                        if state.player_list[group[0]].xueliu_throw_tiles:
                            break
                        await asyncio.sleep(.001)
                    assert state.player_list[group[0]].xueliu_throw_tiles
                    assert animations.empty(), '一人完成就抢先播放了动画'
                    assert state.exchange_view(group[0])['active_players'] == group
                    assert state.exchange_view(group[1])['active_players'] == group
                    assert state.game_status == 'waiting_xueliu_throw'
                    await choose(group[1])
                    assert await asyncio.wait_for(animations.get(), 2) == stage
                    assert prompts.empty() and not any(state.action_dict.values())
                    assert state.game_status == 'xueliu_exchange_animation'
                    # 真正经过协议入口，提前点击下一组/重复点击本组都不能消费牌。
                    before = [p.hand_tiles.copy() for p in state.player_list]
                    for index in range(4):
                        await choose(index)
                    assert [p.hand_tiles for p in state.player_list] == before
                    assert all(queue.empty() for queue in state.action_queues.values())
                    for index in range(4):
                        state.exchange_animation_deadline = time.time()+.7
                        await reconnected_send_pending_ask(state, state.player_list[index].user_id)
                        info = messages[-1]['message']['ask_hand_action_info']
                        view = info['xueliu_exchange']
                        assert view['animating'] and view['active_players'] == group
                        assert 0 < view['animation_seconds'] <= .701
                        assert not info['action_list'] and info['remaining_time'] == 0
                        incoming = state.player_list[(index+2)%4].xueliu_throw_tiles
                        assert view['teammate_suit'] == (incoming[0]//10 if incoming else 0)
                        assert 'tiles' not in view and 'hand_tiles' not in view
                    finish[stage].set()
                await asyncio.wait_for(task, 2)
                assert state.exchange_stage == 0
                assert all(len(p.hand_tiles)==13 for p in state.player_list)
            finally:
                if not task.done():
                    task.cancel()
                    await asyncio.gather(task, return_exceptions=True)

    asyncio.run(scenario())


def test_cancel_during_animation_does_not_open_second_group():
    async def scenario():
        state, _ = make_state()
        reached = asyncio.Event()
        asks = []

        async def ask(current):
            asks.append(current.exchange_stage)
            for index in current.waiting_players_list:
                await submit(current, index, 'xueliu_exchange_three',
                             selected_tiles=current._default_xueliu_throw_tiles(index))

        async def animate(current):
            reached.set()
            await asyncio.Event().wait()

        with patch(__package__+'.boardcast.broadcast_xueliu_throw_three_ask', ask), \
             patch(__package__+'.boardcast.broadcast_xueliu_exchange_animation', animate):
            task=asyncio.create_task(state._xueliu_throw_three_phase())
            await asyncio.wait_for(reached.wait(), 2)
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            assert asks == [1] and not any(state.action_dict.values())
    asyncio.run(scenario())
