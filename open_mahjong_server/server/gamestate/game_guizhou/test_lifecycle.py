"""Asynchronous FSM tests with an in-memory outbox, no sockets or persistence."""
import asyncio
from copy import deepcopy
from unittest.mock import AsyncMock

import pytest

from .bot import play_bot
from .state_machine import Phase as P
from .test_contracts import attach_record, prepare_added_kong
from .test_flow import PLAIN, PUNGS, configured_turn, physical, state


def test_queued_action_wakes_waiter_and_consumes_only_its_time_bank():
    async def run():
        s = configured_turn()
        s.player_list[0].remaining_time = 10
        s.step_time = 0
        task = asyncio.create_task(s.wait_action())
        await asyncio.sleep(0)
        await s.submit_action(0, "cut", TileId=28, cutIndex=13, cutClass=True)
        responses = await asyncio.wait_for(task, 1)
        assert responses[0]["TileId"] == 28
        assert not s.waiting_players_list and not s.action_events[0].is_set()
        assert 0 <= s.player_list[0].remaining_time <= 10
        s.apply_action_results(s.live_pending_window, responses)
        assert s.player_list[0].discard_tiles[-1] == 28
    asyncio.run(run())


def test_timeout_preserves_ready_cut_constraints_and_forces_rob_win():
    async def run():
        s = configured_turn()
        s.player_list[0].ready_kind = "soft_ready"
        s.open_action_window(s.begin_turn(0))
        response = await s.wait_action(0)
        assert response[0]["cutClass"] and response[0]["cutIndex"] == 13
        assert response[0]["is_timeout_action"]
        s.apply_action_results(s.live_pending_window, response)
        s = prepare_added_kong()
        s.apply_action_results(s.live_pending_window, {0: {"action_type": "jiagang", "target_tile": 29}})
        response = await s.wait_action(0)
        assert response[1]["action_type"] == "hu"
        s.apply_action_results(s.live_pending_window, response)
        assert s.machine.phase == P.END
    asyncio.run(run())


def test_ready_phase_timeout_changes_tick_and_allows_next_hand():
    async def run():
        s = attach_record(configured_turn())
        s.end_draw()
        old_tick = s.server_action_tick
        s._send_claim_protection_payload = AsyncMock()
        await s.run_round_ready_phase(0)
        assert s.machine.phase == P.READY and not s.waiting_players_list
        assert s.server_action_tick == old_tick + 1
        assert all(s.build_ready_status_payload(0)["ready_status_info"]["player_to_ready"].values())
        restored = s.restore_payloads(1)
        assert [p["type"] for p in restored] == ["gamestate/guizhou/game_start", "gamestate/guizhou/show_result", "gamestate/guizhou/ready_status"]
        assert all(p["hand_tiles"] for p in restored[0]["game_info"]["players_info"])
        s.advance_round_after_ready()
        assert s.machine.phase == P.START
    asyncio.run(run())


def test_bot_stale_or_racing_reply_cannot_replace_a_new_window():
    async def run():
        s = configured_turn(PUNGS)
        s.bot_speed = "instant"
        await play_bot(s, 0, s.server_action_tick - 1)
        assert s.action_queues[0].empty()
        s.submit_action = AsyncMock(side_effect=ValueError("window closed"))
        await play_bot(s, 0, s.server_action_tick)
        s.submit_action.assert_awaited_once()
        assert s.action_queues[0].empty()
    asyncio.run(run())


def test_four_hand_async_match_has_one_finalization_and_physical_conservation(monkeypatch):
    async def run():
        s = state(19)
        for i, p in enumerate(s.player_list):
            p.user_id = i + 1
        s.bot_speed = "instant"
        sent = []
        async def capture(index, payload):
            sent.append(deepcopy(payload))
        s._send_claim_protection_payload = capture
        s.complete_game_lifecycle = AsyncMock()  # Persistence is outside this unit.
        # No presentation delay is useful in this pure, in-memory test.
        from ..public import round_end_timing
        monkeypatch.setattr(round_end_timing, "sichuan_settle_hu_panel_wait_seconds", lambda _: 0)
        await asyncio.wait_for(s.run_game_loop(), 12)
        assert s.machine.phase == P.FINISHED and s.is_match_end()
        assert len(s.game_record["game_round"]) == 4
        assert all(r["action_ticks"][-1] == ["end"] for r in s.game_record["game_round"].values())
        assert len([p for p in sent if p["type"] == "gamestate/guizhou/game_start"]) == 16
        assert all(p["show_result_info"]["guizhou_end_hands"] for p in sent if p["type"].endswith("/show_result"))
        assert sum(p.score for p in s.player_list) == 0
        assert sum(physical(s).values()) == 108 and all(n == 4 for n in physical(s).values())
        s.complete_game_lifecycle.assert_awaited_once()
        await asyncio.gather(*s.bot_tasks, return_exceptions=True)
    asyncio.run(run())


@pytest.mark.parametrize("change", [{"sub_rule": "guizhou/joker"}, {"open_cuohe": True}])
def test_constructor_refuses_other_variants(change):
    from .GuizhouGameState import GuizhouGameState
    room = GuizhouGameState._default_room_data()
    room.update(change)
    with pytest.raises(ValueError):
        GuizhouGameState(room_data=room, calculation_service=object())
