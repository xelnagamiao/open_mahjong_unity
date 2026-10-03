"""Queue timing, dealer progression, bots and replay finalization."""

import asyncio
from collections import Counter
from copy import deepcopy
from unittest.mock import AsyncMock

import pytest

from .bot import choose_action, play_bot
from .test_flow import PLAIN, FLOAT, turn, state, act, physical, river
from .state_machine import Phase as P
from ...game_calculation.hangzhou.rules import TILES


def test_early_queue_reply_and_later_event_both_survive_wait_entry():
    async def run():
        for early in (False, True):
            s = turn()
            s.player_list[0].remaining_time = 5
            if early:
                await s.submit_action(0, "cut", TileId=42, cutClass=True, cutIndex=13)
            task = asyncio.create_task(s.wait_action())
            await asyncio.sleep(0)
            if not early:
                await s.submit_action(0, "cut", TileId=42, cutClass=True, cutIndex=13)
            responses = await asyncio.wait_for(task, 1)
            assert responses[0]["TileId"] == 42
            assert not s.action_events[0].is_set() and not s.waiting_players_list
            s.apply_action_results(s.live_pending_window, responses)
            assert s.player_list[0].discard_tiles[-1] == 42
        with pytest.raises(ValueError):
            await s.submit_action(True, "pass")
    asyncio.run(run())


def test_timeouts_obey_cai_lock_pass_ten_winds_and_keep_claim_bank():
    async def run():
        s = turn()
        s.cai_discard_locks = {1}
        s.open_action_window(s.begin_turn(0))
        responses = await s.wait_action(0)
        assert responses[0]["TileId"] == 42 and responses[0]["cutClass"] and responses[0]["is_timeout_action"]
        s.apply_action_results(s.live_pending_window, responses)
        s = turn()
        s.player_list[1].hand_tiles = [41, 41] + PLAIN[:11]
        s.player_list[1].remaining_time = 20
        river(s, 0, 41)
        responses = await s.wait_action(0)
        assert all(d["action_type"] == "pass" for d in responses.values())
        assert s.player_list[1].remaining_time == 20
        s = turn(PLAIN[:-1] + [43])
        s.player_list[0].discard_origin_tiles = [41,42,42,43,43,44,44,45,47]
        act(s, 0, "cut", TileId=43, cutClass=True)
        assert s.machine.phase == P.TEN_WINDS
        responses = await s.wait_action(0)
        assert responses[0]["action_type"] == "pass"
        s.apply_action_results(s.live_pending_window, responses)
        assert s.machine.phase == P.RESPONSE
    asyncio.run(run())


def test_ready_timeout_dealer_draw_streak_and_different_winner_rotation():
    async def run():
        s = turn()
        s._send_claim_protection_payload = AsyncMock()
        with pytest.raises(RuntimeError):
            s.advance_round_after_ready()
        for expected in (2, 3, 3):
            s.end_draw()
            await s.run_round_ready_phase(0)
            assert s.machine.phase == P.READY and not s.waiting_players_list
            assert all(s.build_ready_status_payload(0)["ready_status_info"]["player_to_ready"].values())
            s.advance_round_after_ready()
            assert s.dealer_streak == expected
            s.initialize_round()
            s.open_action_window(s.begin_turn(0))
        s.max_round = 2
        s.player_list[2].hand_tiles = list(PLAIN)
        s.player_list[2].pre_draw_tiles = PLAIN[:-1]
        s.player_list[2].has_draw_slot = True
        s.open_action_window(s.begin_turn(2))
        act(s, 2, "hu_self")
        s.apply_deferred_score_changes()
        scores = {p.user_id: p.score for p in s.player_list}
        assert s.next_dealer == 2 and s.round_changes == [-16,-2,20,-2]
        await s.run_round_ready_phase(0)
        s.advance_round_after_ready()
        assert [p.original_player_index for p in s.player_list] == [2,3,0,1]
        assert s.dealer_streak == 1 and s.current_round == 5
        assert all(p.score == scores[p.user_id] for p in s.player_list)
        s.initialize_round();s.open_action_window(s.begin_turn(0));s.end_draw()
        payload = s.build_final_settlement_payload(0)["show_result_info"]
        assert payload["score_changes"] == {2:0,3:0,0:0,1:0}
    asyncio.run(run())


def test_bot_uses_only_public_information_and_preserves_joker_unless_floating():
    s = turn(FLOAT)
    cut = choose_action(s, 0, ["cut"])
    assert cut["TileId"] == 46
    for p in s.player_list[1:]:
        p.hand_tiles = [99] * 13
    s.tiles_list = list(reversed(s.tiles_list))
    assert choose_action(s, 0, ["cut"]) == cut
    s.cai_discard_locks = {1}
    s.player_list[0].hand_tiles[-1] = 43
    assert choose_action(s, 0, ["cut"])["TileId"] == 43
    assert choose_action(s, 0, ["hu_self", "cut"])["action_type"] == "hu_self"
    assert choose_action(s, 0, ["ready"])["action_type"] == "ready"
    assert choose_action(s, 0, ["gang", "pass"])["action_type"] == "gang"


def test_bot_stale_duplicate_and_takeover_paths_do_not_enqueue():
    async def run():
        s = turn(bot_speed="instant")
        await play_bot(s, 0, s.server_action_tick - 1)
        assert s.action_queues[0].empty()
        s.submit_action = AsyncMock(side_effect=ValueError("closed"))
        await play_bot(s, 0, s.server_action_tick)
        s.submit_action.assert_awaited_once()
        await s.action_queues[0].put({"action_type": "cut"})
        s.submit_action.reset_mock()
        await play_bot(s, 0, s.server_action_tick)
        s.submit_action.assert_not_awaited()
    asyncio.run(run())


def test_wait_cancellation_cleans_event_tasks_and_bot_scheduler_is_idempotent():
    async def run():
        s = turn()
        s.player_list[0].remaining_time = 99
        waiter = asyncio.create_task(s.wait_action())
        await asyncio.sleep(0)
        waiter.cancel()
        with pytest.raises(asyncio.CancelledError):
            await waiter
        assert not [task for task in asyncio.all_tasks() if task is not asyncio.current_task() and not task.done()]
        s.player_list[0].user_id = 1
        s.bot_speed = "instant"
        s.schedule_bot_actions();s.schedule_bot_actions()
        assert len(s.bot_tasks) == 1
        await asyncio.gather(*s.bot_tasks)
        assert s.action_queues[0].qsize() == 1
    asyncio.run(run())


def test_seeded_async_match_has_four_real_rounds_zero_sum_and_final_records():
    async def run():
        s = state(19, bot_speed="instant")
        # run_game_loop owns initialization; start from a freshly allocated shell.
        from .HangzhouGameState import HangzhouGameState
        s = HangzhouGameState(room_data=dict(HangzhouGameState._default_room_data(), random_seed=19, bot_speed="instant",
                                           player_list=[1,2,3,4]))
        captured=[]
        async def capture(index, payload):
            captured.append(deepcopy(payload))
        s._send_claim_protection_payload = capture
        s.complete_game_lifecycle = AsyncMock()
        await asyncio.wait_for(s.run_game_loop(), 30)
        assert s.machine.phase == P.FINISHED and s.is_match_end()
        assert s.current_round == 4 and len(s.game_record["game_round"]) == 4
        assert all(r["action_ticks"][-1] == ["end"] for r in s.game_record["game_round"].values())
        assert all(r["hangzhou"]["joker_tiles"] == [46] for r in s.game_record["game_round"].values())
        assert sum(p.score for p in s.player_list) == 0
        assert physical(s) == Counter({t:4 for t in TILES})
        assert not s.bot_tasks
        assert len([p for p in captured if p["type"] == "gamestate/hangzhou/show_result"]) == 16
        s.complete_game_lifecycle.assert_awaited_once()
        with pytest.raises(RuntimeError):
            s.advance_round_after_ready()
    asyncio.run(run())
