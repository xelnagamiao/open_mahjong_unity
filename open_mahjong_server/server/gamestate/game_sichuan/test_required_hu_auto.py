"""血流双打必和由服务端直接执行，队友/首次和牌仍独立选择。"""
import asyncio

import pytest

from .action_check import check_action_after_cut, check_action_jiagang
from .boardcast import reconnected_send_pending_ask
from .test_xueliu_doubles import make_state
from .test_action_window import submit
from .wait_action import wait_action
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer


def claim_state(source=1, won=True, rob=False):
    state, messages = make_state()
    state.claim_protection = state.tactical_call = False
    state.current_player_index = source
    state.game_status = "waiting_action_qianggang" if rob else "waiting_action_after_cut"
    for player in state.player_list:
        player.waiting_tiles = set()
        player.remaining_time = 120
    actor = state.player_list[0]
    actor.waiting_tiles = {25}
    actor.has_won = actor.post_hu_lock = won
    actor.huapai_list = [25] if won else []
    state.player_list[source].discard_tiles = [25]
    if rob:
        state.jiagang_tile = 25
        state.player_list[source].combination_tiles = ["g25"]
        state.player_list[source].combination_mask = [[1, 25, 3, 25, 0, 25, 0, 25]]
    state.action_dict = check_action_jiagang(state, 25) if rob else check_action_after_cut(state, 25)
    return state, messages


@pytest.mark.parametrize("source", [1, 3])
@pytest.mark.parametrize("rob", [False, True])
@pytest.mark.parametrize("connection", ["online", "offline", "bot"])
def test_enemy_win_executes_without_response_or_timeout(source, rob, connection):
    async def run():
        state, messages = claim_state(source, rob=rob)
        actor = state.player_list[0]
        before_time = actor.remaining_time
        if connection == "offline":
            actor.tag_list.append("offline")
        elif connection == "bot":
            actor.user_id = 2
        assert state.action_dict[0] == ["hu"]
        try:
            await state.broadcast_ask_other_action()
            if connection == "online":
                await drain_viewer(state, 0)
                ask = next(m["message"]["ask_other_action_info"] for m in messages
                           if m["user_id"] == actor.user_id and "ask_other_action_info" in m["message"])
                assert ask["action_list"] == []  # 不向必和玩家显示手动选择。
                await reconnected_send_pending_ask(state, actor.user_id)
                assert messages[-1]["message"]["ask_other_action_info"]["action_list"] == []
            assert state.action_queues[0].empty()
            await asyncio.wait_for(wait_action(state), .5)
            assert state.game_status == "settle_win"
            assert list(state.sichuan_hu_results) == [0]
            assert actor.remaining_time == before_time
            assert state.pending_win["discarder"] == source
            assert state.pending_win["type"] == ("qianggang" if rob else "ron")
            if rob:
                assert state.player_list[source].combination_tiles == ["k25"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("source,won", [(2, True), (1, False), (3, False)])
@pytest.mark.parametrize("rob", [False, True])
def test_teammate_and_first_win_keep_optional_hu_pass(source, won, rob):
    async def run():
        state, messages = claim_state(source, won, rob)
        assert state.action_dict[0] == ["hu", "pass"]
        try:
            await state.broadcast_ask_other_action()
            ask = next(m["message"]["ask_other_action_info"] for m in messages
                       if m["user_id"] == state.player_list[0].user_id and "ask_other_action_info" in m["message"])
            assert ask["action_list"] == ["hu", "pass"]
            await submit(state, 0, "pass")
            await asyncio.wait_for(wait_action(state), .5)
            assert state.game_status == ("deal_card_after_gang" if rob else "deal_card")
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("other_action", ["hu", "pass", "required"])
@pytest.mark.parametrize("rob", [False, True])
def test_required_hu_and_other_winners_are_resolved_independently(other_action, rob):
    async def run():
        state, _ = claim_state(1, rob=rob)
        other = state.player_list[2]
        other.waiting_tiles = {25}
        other.has_won = other.post_hu_lock = other_action == "required"
        state.action_dict = check_action_jiagang(state, 25) if rob else check_action_after_cut(state, 25)
        try:
            await state.broadcast_ask_other_action()
            task = asyncio.create_task(wait_action(state))
            if other_action != "required":
                await asyncio.sleep(.01)
                assert not task.done(), "必和不能提前截掉其他候选者的一炮多响响应"
                await submit(state, 2, other_action)
            await asyncio.wait_for(task, .5)
            assert set(state.sichuan_hu_results) == ({0} if other_action == "pass" else {0, 2})
            assert state.game_status == "settle_win"
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
