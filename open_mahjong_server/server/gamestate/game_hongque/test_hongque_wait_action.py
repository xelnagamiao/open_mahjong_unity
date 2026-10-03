"""虹雀新等待动作组件的行为级回归测试。"""
from __future__ import annotations

import asyncio

import pytest

from server.gamestate.game_hongque.HongqueGameState import HongqueGameState
from server.gamestate.game_hongque.action_check import check_action_after_cut
from server.gamestate.game_hongque.rules import kong_candidates
from server.gamestate.game_hongque.state_machine import HongqueStatus
from server.gamestate.game_hongque import wait_action as wait_module


def _state() -> HongqueGameState:
    room = {
        "room_id": "986",
        "game_round": 1,
        "player_list": [101, 102, 103, 104],
        "tactical_grace_seconds": 5.0,
    }
    state = HongqueGameState(None, room, gamestate_id="hongque-wait-action")
    state.Debug = True
    state.debug_scenario = "tactical_all_claims"
    return state


@pytest.mark.parametrize("supplements,wall", [(2, ["GX9"]), (0, [])])
def test_last_tile_kong_cannot_leave_onlycut_without_an_action(supplements, wall):
    async def exercise():
        state = _state()
        state.Debug = False
        state.phase = "claim"
        state._transition(HongqueStatus.ONLYCUT_AFTER_ACTION)
        player = state.players[0]
        player.hand = ["AX4"]
        player.melds = [{"kind": "sequence", "tiles": ["AX1", "AX2", "AX3"]}]
        player.supplements = supplements
        state.wall = list(wall)
        candidate = kong_candidates(player.hand, player.melds)[0]
        with pytest.raises(ValueError):
            await state.submit_action(player.user_id, "kong", candidate_id=candidate["id"])
        assert player.hand == ["AX4"]
        actions, candidates = state._legal_turn_actions(player)
        assert actions == ["discard"]
        assert candidates == []
        await state.cleanup_game_state()

    asyncio.run(exercise())


@pytest.mark.parametrize("supplements,wall", [(2, ["GX9"]), (0, [])])
def test_all_hand_claim_without_supplement_still_offers_ron(supplements, wall):
    state = _state()
    state.players[0].discards = ["AX3"]
    state.last_discard = {"player": 0, "tile": "AX3"}
    player = state.players[1]
    player.hand = ["AX1", "AX2"]
    player.supplements = supplements
    state.wall = list(wall)
    candidates = check_action_after_cut(state)[1]
    assert [candidate["kind"] for candidate in candidates] == ["win"]


@pytest.mark.parametrize("live_wait", [False, True])
def test_queued_claim_invalidated_by_another_claim_is_reasked(monkeypatch, live_wait):
    # Both actions arrive before the waiter wakes. Make seat 1's response the
    # first one consumed so seat 2's old chi expires, but its peng/ron stay legal.
    real_wait = asyncio.wait

    async def ordered_wait(tasks, **kwargs):
        done, pending = await real_wait(tasks, **kwargs)
        return sorted(done, key=lambda task: int(task.get_name().rsplit("-", 1)[-1])), pending

    monkeypatch.setattr(wait_module.asyncio, "wait", ordered_wait)

    async def exercise():
        state = await _opened_state()
        state.tactical_grace_seconds = 0.01
        state._in_wait_action = True
        tick = state.action_tick
        for seat in (1, 2):
            await state.submit_action(
                state.players[seat].user_id, "claim",
                candidate_id=_candidate(state, seat, "sequence")["id"], action_tick=tick,
            )
        try:
            if live_wait:
                await state.wait_action()
                assert state.phase == "turn"
                assert state.current_player_index == 1
            else:
                await state._drive_local()
                assert state.claim_window.active.player_index == 1
                assert 2 in state.claim_window.pending
                assert state.action_queues[2].empty()
                # The expired chi must not count as pass: seat 2 can still upgrade.
                state._in_wait_action = False
                peng = _candidate(state, 2, "triplet")
                await state.submit_action(state.players[2].user_id, "claim",
                                          candidate_id=peng["id"], action_tick=state.action_tick)
                assert state.claim_window.active.player_index == 2
                assert state.claim_window.active.candidate["kind"] == "triplet"
        finally:
            await state.cleanup_game_state()

    asyncio.run(exercise())


async def _opened_state() -> HongqueGameState:
    state = _state()
    await state._start_round()
    await state.submit_action(
        state.players[0].user_id,
        "discard",
        tile="AX1",
        action_tick=state.action_tick,
    )
    assert state.phase == "claim"
    return state


async def _pass_all_pending(state: HongqueGameState) -> None:
    while state.phase == "claim":
        window = state.claim_window
        pending = set(window.pending) if window is not None else set()
        if not pending:
            if state.phase == "claim":
                await state.wait_action()
            break
        player_index = next(iter(pending))
        await state.submit_action(
            state.players[player_index].user_id,
            "pass",
            action_tick=state.action_tick,
        )


def _candidate(state, player_index: int, kind: str) -> dict:
    return next(
        item for item in state.claim_options[player_index]
        if item["kind"] == kind
    )


def test_claim_refreshes_one_shared_five_second_clock_for_all_viewers() -> None:
    asyncio.run(_shared_clock())


async def _shared_clock() -> None:
    state = await _opened_state()
    chi = _candidate(state, 1, "sequence")
    await state.submit_action(
        state.players[1].user_id,
        "claim",
        candidate_id=chi["id"],
        action_tick=state.action_tick,
    )

    snapshots = [state.build_state(index) for index in range(4)]
    assert {item["remaining_time"] for item in snapshots} == {5}
    assert {item["step_remaining"] for item in snapshots} == {0}
    assert {item["claim_stage"] for item in snapshots} == {"tactical"}
    # 吃牌申请者本人不会被重新询问碰或和。
    assert snapshots[1]["legal_actions"] == []
    assert snapshots[1]["candidates"] == []
    await state.cleanup_game_state()


def test_claimant_can_upgrade_after_another_player_interrupts() -> None:
    asyncio.run(_upgrade_after_interrupt())


async def _upgrade_after_interrupt() -> None:
    state = await _opened_state()
    chi = _candidate(state, 2, "sequence")
    await state.submit_action(
        state.players[2].user_id, "claim",
        candidate_id=chi["id"], action_tick=state.action_tick,
    )
    assert chi.get("action_type") == "chi_second"
    assert state.claim_window.active.player_index == 2

    chi_first = _candidate(state, 1, "sequence")
    previous_deadline = state.claim_window.deadline
    await state.submit_action(
        state.players[1].user_id, "claim",
        candidate_id=chi_first["id"], action_tick=state.action_tick,
    )
    assert chi_first.get("action_type") == "chi_first"
    assert state.claim_window.active.player_index == 1
    assert 2 in state.claim_window.pending
    offered = state.build_state(2)["candidates"]
    assert {candidate["kind"] for candidate in offered} >= {"triplet", "win"}
    assert all(candidate["priority"] > chi_first["priority"] for candidate in offered)
    assert all(candidate["id"] != chi["id"] for candidate in offered)
    assert state.claim_window.deadline >= previous_deadline

    peng = next(candidate for candidate in offered if candidate["kind"] == "triplet")
    await state.submit_action(
        state.players[2].user_id, "claim",
        candidate_id=peng["id"], action_tick=state.action_tick,
    )
    assert state.claim_window.active.player_index == 2
    assert state.claim_window.active.candidate["kind"] == "triplet"
    await state.cleanup_game_state()


def test_initial_pass_is_reasked_from_snapshot_after_a_claim() -> None:
    asyncio.run(_pass_is_reasked_after_claim())


async def _pass_is_reasked_after_claim() -> None:
    state = await _opened_state()
    await state.submit_action(
        state.players[2].user_id, "pass", action_tick=state.action_tick,
    )
    assert state.claim_responses[2]["action"] == "pass"

    chi_third = _candidate(state, 3, "sequence")
    await state.submit_action(
        state.players[3].user_id, "claim",
        candidate_id=chi_third["id"], action_tick=state.action_tick,
    )

    assert 2 not in state.claim_responses
    offered = state.build_state(2)["candidates"]
    assert offered
    assert all(candidate["priority"] > chi_third["priority"] for candidate in offered)
    assert any(candidate.get("action_type") == "chi_second" for candidate in offered)
    await state.cleanup_game_state()


def test_three_chi_priority_levels_can_interrupt_in_order() -> None:
    asyncio.run(_three_chi_levels())


async def _three_chi_levels() -> None:
    state = await _opened_state()
    # 上家吃(2) → 对家吃(3) → 下家吃(4)。每位申请者提交后均不再询问自己。
    for player_index in (3, 2, 1):
        chi = _candidate(state, player_index, "sequence")
        _, offered = state.build_state(player_index)["legal_actions"], \
            state.build_state(player_index)["candidates"]
        assert chi["id"] in {item["id"] for item in offered}
        await state.submit_action(
            state.players[player_index].user_id,
            "claim",
            candidate_id=chi["id"],
            action_tick=state.action_tick,
        )

    await _pass_all_pending(state)
    assert state.phase == "turn"
    assert state.game_status == "onlycut_after_action"
    assert state.current_player_index == 1
    assert state.players[1].melds[-1]["kind"] == "sequence"
    await state.cleanup_game_state()


def test_three_peng_priority_levels_can_interrupt_in_order() -> None:
    asyncio.run(_three_peng_levels())


async def _three_peng_levels() -> None:
    state = await _opened_state()
    for player_index, action_type, priority in (
        (3, "peng_third", 6),
        (2, "peng_second", 7),
        (1, "peng_first", 8),
    ):
        peng = _candidate(state, player_index, "triplet")
        assert peng["action_type"] == action_type
        assert peng["priority"] == priority
        assert peng["id"] in {
            item["id"] for item in state.build_state(player_index)["candidates"]
        }
        await state.submit_action(
            state.players[player_index].user_id,
            "claim",
            candidate_id=peng["id"],
            action_tick=state.action_tick,
        )

    await _pass_all_pending(state)
    assert state.phase == "turn"
    assert state.game_status == "onlycut_after_action"
    assert state.current_player_index == 1
    assert state.players[1].melds[-1]["kind"] == "triplet"
    await state.cleanup_game_state()


def test_three_hong_priority_levels_can_interrupt_in_order() -> None:
    asyncio.run(_three_hong_levels())


async def _three_hong_levels() -> None:
    state = _state()
    state.Debug = False
    state.phase = "turn"
    state.current_player_index = 0
    state.wall = ["GY9"]
    state.players[0].hand = ["AX1"]
    state.players[1].hand = ["BX1", "CX1", "DX1", "EX1", "FX1", "GX1"]
    state.players[2].hand = ["BX2", "CX3", "DX4", "EX5", "FX6", "GX7"]
    state.players[3].hand = ["BX7", "CX6", "DX5", "EX4", "FX3", "GX2"]
    await state.submit_action(
        state.players[0].user_id,
        "discard",
        tile="AX1",
        action_tick=state.action_tick,
    )

    for player_index, action_type, priority in (
        (3, "hong_third", 9),
        (2, "hong_second", 10),
        (1, "hong_first", 11),
    ):
        hong = _candidate(state, player_index, "rainbow")
        assert hong["action_type"] == action_type
        assert hong["priority"] == priority
        await state.submit_action(
            state.players[player_index].user_id,
            "claim",
            candidate_id=hong["id"],
            action_tick=state.action_tick,
        )

    await _pass_all_pending(state)
    assert state.phase == "turn"
    assert state.game_status == "onlycut_after_action"
    assert state.current_player_index == 1
    assert state.players[1].melds[-1]["kind"] == "rainbow"
    await state.cleanup_game_state()


def test_game_status_is_authoritative_state_machine() -> None:
    asyncio.run(_state_machine_lifecycle())


async def _state_machine_lifecycle() -> None:
    state = _state()
    assert state.game_status == "waiting"
    await state._start_round()
    assert state.game_status == "waiting_hand_action"
    await state.submit_action(
        state.players[0].user_id,
        "discard",
        tile="AX1",
        action_tick=state.action_tick,
    )
    assert state.game_status == "waiting_action_after_cut"
    assert state.build_state(0)["game_status"] == state.game_status
    transitions = [(old.value, new.value) for old, new in state.state_machine.history]
    assert ("waiting_hand_action", "resolving_discard") in transitions
    assert ("resolving_discard", "waiting_action_after_cut") in transitions
    await state.cleanup_game_state()


def test_same_discard_collects_multiple_ron_like_riichi() -> None:
    asyncio.run(_multiple_ron())


async def _multiple_ron() -> None:
    state = await _opened_state()
    ron1 = _candidate(state, 1, "win")
    ron2 = _candidate(state, 2, "win")
    opening_tick = state.action_tick
    # 两个客户端可能在收到第一次重询广播前，几乎同时按原始询问 tick 荣和。
    await asyncio.gather(
        state.submit_action(
            state.players[1].user_id, "claim",
            candidate_id=ron1["id"], action_tick=opening_tick,
        ),
        state.submit_action(
            state.players[2].user_id, "claim",
            candidate_id=ron2["id"], action_tick=opening_tick,
        ),
    )
    assert 3 not in state.claim_responses
    await _pass_all_pending(state)

    assert state.phase == "round_end"
    assert state.round_result["winner_indices"] == [1, 2]
    assert state.round_result["multi_ron"] is True
    if state._round_task:
        state._round_task.cancel()
    await state.cleanup_game_state()
