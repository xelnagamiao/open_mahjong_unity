"""虹雀的放弃贯穿完整弃牌窗口，兼顾排队、迟到请求和多家荣和。"""
import asyncio

import pytest

from .HongqueGameState import HongqueGameState
from .wait_action import open_claim_window


async def opened_state():
    state = HongqueGameState(None, {
        "room_id": "force-pass", "game_round": 1,
        "player_list": [101, 102, 103, 104], "tactical_grace_seconds": 0.01,
    }, gamestate_id="hongque-force-pass")
    state.Debug = True
    state.debug_scenario = "tactical_all_claims"
    await state._start_round()
    await state.submit_action(101, "discard", tile="AX1", action_tick=state.action_tick)
    assert state.phase == "claim"
    return state


def candidate(state, seat, kind):
    return next(item for item in state.claim_options[seat] if item["kind"] == kind)


@pytest.mark.parametrize("decline", ["pass", "force_pass"])
def test_cancel_is_reasked_but_give_up_exits_until_next_discard(decline):
    async def exercise():
        state = await opened_state()
        try:
            assert state.build_state(2)["legal_actions"] == ["pass", "claim", "force_pass"]
            await state.submit_action(103, decline, action_tick=state.action_tick)
            await state.submit_action(104, "claim", candidate_id=candidate(state, 3, "sequence")["id"],
                                      action_tick=state.action_tick)
            assert bool(state.build_state(2)["candidates"]) is (decline == "pass")
            assert (2 in state.claim_window.pending) is (decline == "pass")
            if decline == "force_pass":
                old_window = state.claim_window
                # 开下一张相同弃牌的真实窗口，之前的退出标记不能继续影响合法选项。
                await open_claim_window(state)
                assert state.claim_window is not old_window
                assert not state.claim_window.force_passed
                assert "force_pass" in state.build_state(2)["legal_actions"]
        finally:
            await state.cleanup_game_state()
    asyncio.run(exercise())


@pytest.mark.parametrize("live_wait", [False, True])
def test_queued_give_up_is_not_lost_when_another_submission_refreshes_window(live_wait):
    async def exercise():
        state = await opened_state()
        try:
            state._in_wait_action = True
            tick = state.action_tick
            await state.submit_action(102, "claim", candidate_id=candidate(state, 1, "sequence")["id"], action_tick=tick)
            await state.submit_action(103, "force_pass", action_tick=tick)
            assert state.claim_window.force_passed == {2}
            if live_wait:
                await asyncio.wait_for(state.wait_action(), 1)
                assert state.phase == "turn"
                assert state.current_player_index == 1
            else:
                await state._drive_local()
                assert 2 not in state.claim_window.pending
                assert state.build_state(2)["legal_actions"] == []
                assert state.build_state(2)["candidates"] == []
        finally:
            await state.cleanup_game_state()
    asyncio.run(exercise())


def test_original_tick_give_up_is_accepted_after_cancel_and_a_new_tactical_ask():
    async def exercise():
        state = await opened_state()
        try:
            opening_tick = state.action_tick
            await state.submit_action(103, "pass", action_tick=opening_tick)
            await state.submit_action(104, "claim", candidate_id=candidate(state, 3, "sequence")["id"],
                                      action_tick=state.action_tick)
            assert state.action_tick > opening_tick
            await state.submit_action(103, "force_pass", action_tick=opening_tick)
            assert 2 in state.claim_window.force_passed
            assert state.build_state(2)["legal_actions"] == []
            with pytest.raises(ValueError, match="过期"):
                await state.submit_action(102, "force_pass", action_tick=opening_tick - 1)
        finally:
            await state.cleanup_game_state()
    asyncio.run(exercise())


def test_late_give_up_while_not_pending_prevents_future_rechecks():
    async def exercise():
        state = await opened_state()
        try:
            opening_tick = state.action_tick
            await state.submit_action(103, "pass", action_tick=opening_tick)
            assert 2 not in state.claim_window.pending
            await state.submit_action(103, "force_pass", action_tick=opening_tick)
            assert state.action_queues[2].empty()
            await state.submit_action(104, "claim", candidate_id=candidate(state, 3, "sequence")["id"],
                                      action_tick=state.action_tick)
            assert 2 not in state.claim_window.pending
            assert state.build_state(2)["legal_actions"] == []
        finally:
            await state.cleanup_game_state()
    asyncio.run(exercise())


def test_give_up_does_not_retract_accepted_ron_and_does_not_prevent_other_rons():
    async def exercise():
        state = await opened_state()
        try:
            await state.submit_action(104, "force_pass", action_tick=state.action_tick)
            await state.submit_action(102, "claim", candidate_id=candidate(state, 1, "win")["id"],
                                      action_tick=state.action_tick)
            assert 1 in state.claim_window.accepted_rons
            with pytest.raises(ValueError):
                await state.submit_action(102, "force_pass", action_tick=state.action_tick)
            await state.submit_action(103, "claim", candidate_id=candidate(state, 2, "win")["id"],
                                      action_tick=state.action_tick)
            assert state.phase == "round_end"
            assert set(state.round_result["winner_indices"]) == {1, 2}
        finally:
            await state.cleanup_game_state()
    asyncio.run(exercise())


def test_give_up_rejected_outside_claim_and_for_a_player_without_an_offer():
    async def exercise():
        state = await opened_state()
        try:
            with pytest.raises(ValueError):
                await state.submit_action(101, "force_pass", action_tick=state.action_tick)
            state.phase = "turn"
            with pytest.raises(ValueError):
                await state.submit_action(102, "force_pass", action_tick=state.action_tick)
        finally:
            await state.cleanup_game_state()
    asyncio.run(exercise())
