"""Real Hangzhou state/transport/replay tactical regressions with controlled clocks."""

import asyncio
from collections import Counter
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .actions import claim_actions
from .get_action import handle_action
from .HangzhouGameState import HangzhouGameState
from .state_machine import Phase as P
from .test_contracts import fake_room_manager
from .test_flow import PLAIN, act, physical, turn
from .test_timing import Clock, claim_state, prompt, spin
from ...game_calculation.hangzhou.rules import TILES
from ...room.hangzhou_room import (HangzhouRoomValidator, create_hangzhou_room,
                                   enforce_hangzhou_tactical_room, handle_create_hangzhou_room)
from ..public.tactical_claim import get_higher_priority_snapshot, tactical_player_has_force_passed


def tactical_state(bank=20, step=5, *, enabled=True):
    state, clock = claim_state(bank, step, multiple=True)
    state.tactical_call = enabled
    state.tactical_pre_grace_delay = 0
    window = state.live_pending_window
    state.open_action_window(state._window(P.RESPONSE, 0, window["tile"], claim_actions(state, 0, window["tile"])))
    return state, clock


async def until(predicate):
    for _ in range(100):
        if predicate():
            return
        await asyncio.sleep(0)
    raise AssertionError("Expected async phase was not reached")


async def begin_recheck(state, clock, *, elapsed=2):
    task = asyncio.create_task(state.resolve_action_window())
    await spin()
    tick = state.server_action_tick
    clock.advance(elapsed)
    await state.submit_action(1, "chi_right")
    await until(lambda: state._tactical_recheck_active and state.server_action_tick > tick)
    await spin()
    assert not task.done()
    return task


@pytest.mark.parametrize("enabled", [False, True])
@pytest.mark.parametrize("bank,step", [(20, 5), (40, 8), (0, 8), (20, 0)])
def test_switch_does_not_shorten_normal_claim_clock(enabled, bank, step):
    async def run():
        state, clock = tactical_state(bank, step, enabled=enabled)
        task = asyncio.create_task(state.resolve_action_window())
        await spin()
        assert prompt(state, 1) == (bank, step)
        assert state._action_deadlines[1] == 1000 + bank + step
        assert not state.build_pending_action_payload(1)["ask_other_action_info"].get("is_tactical_recheck")
        clock.advance(6)
        clock.wake(state)
        await spin()
        assert not task.done(), "Enabling tactical must not impose a 5s main ask"
        await state.submit_action(1, "pass")
        await state.submit_action(2, "pass")
        await asyncio.wait_for(task, .5)
        expected = max(0, bank - max(0, 6 - step))
        assert state.player_list[1].remaining_time == expected
        assert state.player_list[2].remaining_time == expected
    asyncio.run(run())


@pytest.mark.parametrize("bank,step,elapsed", [(20, 5, 8), (40, 8, 10), (0, 8, 2), (20, 0, 2)])
@pytest.mark.parametrize("answer", ["peng", "pass", "timeout"])
def test_actual_interrupt_is_independent_five_seconds_without_bank_charge(bank, step, elapsed, answer):
    async def run():
        state, clock = tactical_state(bank, step)
        task = await begin_recheck(state, clock, elapsed=elapsed)
        expected = max(0, bank - max(0, elapsed - step))
        assert state.player_list[1].remaining_time == expected
        assert state.player_list[2].remaining_time == expected
        packet = state.build_pending_action_payload(2)
        ask = packet["ask_other_action_info"]
        assert (ask["remaining_time"], ask["step_remaining"], ask["is_tactical_recheck"]) == (5, 0, True)
        assert packet["game_info"]["players_info"][2]["remaining_time"] == expected
        assert state._action_deadlines[2] == clock.now + 5
        # Application is presentation only, with no physical meld or replay mutation.
        assert not state.player_list[1].combination_tiles
        assert state.player_list[0].discard_tiles[-1] == 13
        if answer == "timeout":
            clock.advance(4.99)
            clock.wake(state)
            await spin()
            assert not task.done()
            clock.advance(.01)
            with pytest.raises(ValueError, match="耗尽"):
                await state.submit_action(2, "peng")
            clock.wake(state)
        else:
            clock.advance(3)
            await state.submit_action(2, answer)
        await asyncio.wait_for(task, .5)
        winner = 2 if answer == "peng" else 1
        assert state.current_player_index == winner and state.machine.phase == P.DISCARD_ONLY
        assert prompt(state, winner) == (expected, step)
        assert all(state.player_list[i].remaining_time == expected for i in (1, 2))
        assert physical(state) == Counter({tile: 4 for tile in TILES})
        melds = [event for event in state.domain_events if event["action"] in ("chi_right", "peng")]
        assert len(melds) == 1 and melds[0]["player"] == winner
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        assert sum(tick[0] in ("cl", "cm", "cr", "p", "g") for tick in ticks) == 1
        ca = [tick for tick in ticks if tick[0] == "ca"]
        assert ca == ([["ca", 1, "cr", 13]] if answer == "peng" else [])
        final = [p for p in state.outbound_payloads if p.get("action") in ("chi_right", "peng")
                 and not p.get("do_action_info", {}).get("is_claim")]
        assert len(final) == 4 and all(p["do_action_info"]["silent"] for p in final)
    asyncio.run(run())


def test_ordinary_off_waits_all_responses_and_does_not_emit_applications():
    async def run():
        state, clock = tactical_state(enabled=False)
        task = asyncio.create_task(state.resolve_action_window())
        await spin()
        clock.advance(2)
        await state.submit_action(1, "chi_right")
        await spin()
        assert not task.done() and not state._tactical_recheck_active
        clock.advance(8)
        await state.submit_action(2, "peng")
        await asyncio.wait_for(task, .5)
        assert state.current_player_index == 2 and state.player_list[2].remaining_time == 15
        assert not any(p.get("do_action_info", {}).get("is_claim") for p in state.outbound_payloads)
        assert not any(p.get("do_action_info", {}).get("silent") for p in state.outbound_payloads)
    asyncio.run(run())


@pytest.mark.parametrize("decline", ["pass", "force_pass"])
def test_main_pass_may_compete_again_but_force_pass_exits_this_discard(decline):
    async def run():
        state, clock = tactical_state()
        task = asyncio.create_task(state.resolve_action_window())
        await spin()
        await state.submit_action(2, decline)
        await spin()
        await state.submit_action(1, "chi_right")
        if decline == "pass":
            await until(lambda: state._tactical_recheck_active)
            await state.submit_action(2, "peng")
        await asyncio.wait_for(task, .5)
        assert state.current_player_index == (2 if decline == "pass" else 1)
        assert state.tactical_commit_lock is False
    asyncio.run(run())


def test_batch_receipts_win_without_new_grace_or_double_charge():
    async def run():
        state, clock = tactical_state()
        task = asyncio.create_task(state.resolve_action_window())
        await spin()
        clock.advance(8)
        await state.submit_action(1, "chi_right")
        await state.submit_action(2, "peng")
        # Processing/forwarding delay cannot invalidate already timely receipts.
        clock.advance(30)
        await asyncio.wait_for(task, .5)
        assert state.current_player_index == 2
        assert state.player_list[1].remaining_time == state.player_list[2].remaining_time == 17
        assert not any((p.get("ask_other_action_info") or {}).get("is_tactical_recheck") for p in state.outbound_payloads)
    asyncio.run(run())


def test_recheck_reconnect_duplicate_delivery_and_spectator_do_not_restart_clock():
    async def run():
        state, clock = tactical_state()
        task = await begin_recheck(state, clock)
        deadline = state._action_deadlines[2]
        clock.advance(2)
        ws = SimpleNamespace(send_json=AsyncMock())
        state.game_server = SimpleNamespace(user_id_to_connection={103: SimpleNamespace(websocket=ws),
                                                                   999: SimpleNamespace(websocket=ws)})
        await state.player_reconnect(103)
        await state.send_realtime_spectator_snapshot(999, 2)
        packet = state.build_pending_action_payload(2)
        await state._send_timed_payload(2, packet, ws)
        assert prompt(state, 2) == (3, 0) and state._action_deadlines[2] == deadline
        asks = [c.args[0]["ask_other_action_info"] for c in ws.send_json.call_args_list
                if c.args[0].get("ask_other_action_info")]
        assert len(asks) == 3 and all(a["is_tactical_recheck"] and a["step_remaining"] == 0 for a in asks)
        await state.submit_action(2, "peng")
        await asyncio.wait_for(task, .5)
        assert state.player_list[2].remaining_time == 20
    asyncio.run(run())


def test_recheck_stale_nonpass_rejected_but_same_discard_force_pass_accepted():
    async def run():
        state, clock = tactical_state()
        opening = state.server_action_tick
        task = await begin_recheck(state, clock)
        ws = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: state),
                                 players={"c": SimpleNamespace(user_id=103)})
        message = dict(type="gamestate/hangzhou/send_action", gamestate_id=state.gamestate_id,
                       action_tick=opening, action="peng")
        await handle_action(server, "c", message, ws)
        assert state.action_queues[2].empty()
        assert ws.send_json.call_args.args[0]["ask_other_action_info"]["is_tactical_recheck"]
        await handle_action(server, "c", {**message, "action": "force_pass"}, ws)
        assert tactical_player_has_force_passed(state, 2)
        await asyncio.wait_for(task, .5)
        assert state.current_player_index == 1
        count = len(state.domain_events)
        await handle_action(server, "c", {**message, "action": "force_pass"}, ws)
        assert len(state.domain_events) == count and not tactical_player_has_force_passed(state, 2)
    asyncio.run(run())


def test_expired_force_pass_neither_queues_nor_marks_player_and_forged_queue_is_ignored():
    async def run():
        state, clock = tactical_state()
        task = await begin_recheck(state, clock)
        clock.advance(5)
        with pytest.raises(ValueError, match="耗尽"):
            await state.submit_action(2, "force_pass")
        assert not tactical_player_has_force_passed(state, 2) and state.action_queues[2].empty()
        clock.wake(state)
        await asyncio.wait_for(task, .5)
        assert state.current_player_index == 1
    asyncio.run(run())
    state, _ = tactical_state()
    assert not state.tactical_action_receipt_valid(2, dict(action_type="peng", _receipt_tick=state.server_action_tick - 1,
                                                         _receipt_time=1000, _receipt_deadline=1025))
    assert not state.tactical_action_receipt_valid(2, dict(action_type="peng", _receipt_tick=state.server_action_tick,
                                                         _receipt_time=1025, _receipt_deadline=1025))


def test_equal_rank_competitor_uses_seat_distance_and_no_guobiao_commit_lock():
    # Abstract priority fixture: two pungs are not both physically possible for a four-copy discard.
    state, _ = tactical_state()
    state._tactical_action_snapshot = {0: [], 1: ["peng", "pass", "force_pass"],
                                      2: ["peng", "pass", "force_pass"], 3: []}
    state._tactical_committed_players = {1}
    higher, any_higher = get_higher_priority_snapshot(state, "gang", 2)
    assert any_higher and higher[1] == ["peng", "pass", "force_pass"]
    assert not get_higher_priority_snapshot(state, "peng", 1)[1]


@pytest.mark.parametrize("enabled", [False, True])
def test_room_request_default_and_explicit_choice_roundtrip(enabled):
    async def run():
        manager, _ = fake_room_manager()
        manager.create_Hangzhou_room = lambda *a, **kw: create_hangzhou_room(manager, *a, **kw)
        ws = SimpleNamespace(send_json=AsyncMock())
        await handle_create_hangzhou_room(SimpleNamespace(room_manager=manager), "c",
                                         dict(roomname="杭州", tactical_call=enabled), ws)
        assert manager.rooms[123456]["tactical_call"] is enabled
        assert ws.send_json.call_args.args[0]["room_info"]["tactical_call"] is enabled
    asyncio.run(run())
    assert HangzhouRoomValidator(room_name="x", game_round=4, round_timer=20, step_timer=5).tactical_call is True
    with pytest.raises(ValueError):
        HangzhouRoomValidator(room_name="x", game_round=4, round_timer=20, step_timer=5, tactical_call=1)


def test_omitted_room_request_stores_default_on():
    async def run():
        manager, _ = fake_room_manager()
        result = await create_hangzhou_room(manager, "c", room_name="default")
        assert result.success and result.room_info["tactical_call"] is True
        assert manager.rooms[123456]["tactical_call"] is True
    asyncio.run(run())


@pytest.mark.parametrize("bot", [0, 1, 2, 3, 10])
def test_start_and_gameinfo_authoritatively_disable_bot_request(bot):
    room = {**HangzhouGameState._default_room_data(), "player_list": [101, bot, 103, 104], "tactical_call": True}
    state = HangzhouGameState(room_data=room)
    assert state.tactical_call is False and room["tactical_call"] is False
    state.initialize_round()
    assert state.build_game_start_payload(0)["game_info"]["tactical_call"] is False
    state.player_list[1].user_id = 102
    assert state.sync_tactical_enabled() is False


def test_lobby_policy_ignores_empty_seats_and_spectators_and_preserves_host_false():
    room = dict(room_rule="hangzhou", player_list=[101], seat_list=[101, -1, None, -1],
                spectators=[0, 2], tactical_call=True)
    enforce_hangzhou_tactical_room(room)
    assert room["tactical_call"] is True
    room["tactical_call"] = False
    enforce_hangzhou_tactical_room(room)
    assert room["tactical_call"] is False
    other = {**room, "room_rule": "guobiao", "player_list": [0], "tactical_call": True}
    enforce_hangzhou_tactical_room(other)
    assert other["tactical_call"] is True


@pytest.mark.parametrize("bot", [0, 2])
def test_actual_bot_join_broadcast_sync_and_removal_are_sticky_off(bot):
    async def run():
        from ...room.room_manager import RoomManager
        from ...room.room_seats import unseat_player
        fake, host = fake_room_manager()
        result = await create_hangzhou_room(fake, "c", room_name="bot room", tactical_call=True)
        assert result.success
        ws = SimpleNamespace(send_json=AsyncMock())
        fake.game_server.user_id_to_connection = {101: SimpleNamespace(websocket=ws)}
        manager = RoomManager(fake.game_server)
        manager.rooms = fake.rooms
        assert (await manager._add_room_bot("c", 123456, bot, None)).success
        room = manager.rooms[123456]
        assert room["tactical_call"] is False and bot in room["player_list"]
        assert ws.send_json.call_args.args[0]["room_info"]["tactical_call"] is False
        # A stale saved true is corrected independently on sync and broadcasts.
        room["tactical_call"] = True
        assert (await manager.sync_my_room("c")).room_info["tactical_call"] is False
        room["tactical_call"] = True
        await manager._broadcast_room_info(123456)
        assert ws.send_json.call_args.args[0]["room_info"]["tactical_call"] is False
        unseat_player(room, bot)
        await manager._broadcast_room_info(123456)
        assert room["tactical_call"] is False
        assert host.current_room_id == 123456
    asyncio.run(run())


@pytest.mark.parametrize("chow,prefix", [("chi_left", [14, 15]), ("chi_mid", [12, 14]), ("chi_right", [11, 12])])
@pytest.mark.parametrize("higher", ["peng", "gang"])
def test_all_chow_shapes_and_exposed_kong_use_real_physical_and_replay_path(chow, prefix, higher):
    async def run():
        state = turn(PLAIN, round_timer=20, step_timer=5, tactical_call=True, record=False)
        prefixes = [prefix, [13] * (3 if higher == "gang" else 2), []]
        pool = Counter({t: 4 for t in TILES}) - Counter(PLAIN + sum(prefixes, []))
        for player, held in zip(state.player_list[1:], prefixes):
            filler = [t for t in pool.elements() if t != 13][:13 - len(held)]
            player.hand_tiles = held + filler
            pool.subtract(filler)
        state.tiles_list = list(pool.elements())
        state.start_game_recording()
        state.start_round_recording()
        act(state, 0, "cut", TileId=13, cutClass=False)
        state.tactical_pre_grace_delay = 0
        clock = Clock(state)
        task = asyncio.create_task(state.resolve_action_window())
        await spin()
        await state.submit_action(1, chow)
        await until(lambda: state._tactical_recheck_active)
        await state.submit_action(2, higher)
        await asyncio.wait_for(task, .5)
        assert state.current_player_index == 2
        assert state.machine.phase == (P.TURN if higher == "gang" else P.DISCARD_ONLY)
        assert physical(state) == Counter({t: 4 for t in TILES})
        ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
        assert sum(tick[0] in ("cl", "cm", "cr", "p", "g") for tick in ticks) == 1
        assert next(tick for tick in ticks if tick[0] == "ca") == ["ca", 1, {"chi_left": "cl", "chi_mid": "cm", "chi_right": "cr"}[chow], 13]
        assert not any("hu" in options for options in state._tactical_action_snapshot.values()) if state._tactical_action_snapshot else True
    asyncio.run(run())


@pytest.mark.parametrize("early_reply", [False, True])
def test_slow_recheck_spectator_does_not_extend_clock_or_invalidate_timely_reply(early_reply):
    async def run():
        state, clock = tactical_state()
        task = asyncio.create_task(state.resolve_action_window())
        await spin()
        captured = []
        primary = SimpleNamespace(websocket=AsyncMock())
        state.game_server = SimpleNamespace(user_id_to_connection={103: primary})
        async def slow_observer(index, packet):
            if index == 2 and (packet.get("ask_other_action_info") or {}).get("is_tactical_recheck"):
                captured.append(deepcopy(packet))
                if early_reply:
                    clock.advance(2)
                    await state.submit_action(2, "peng")
                clock.advance(10)
        state.send_to_realtime_spectators = slow_observer
        await state.submit_action(1, "chi_right")
        await asyncio.wait_for(task, .5)
        assert captured[0]["ask_other_action_info"]["remaining_time"] == 5
        assert state.current_player_index == (2 if early_reply else 1)
        assert state.player_list[2].remaining_time == 20
    asyncio.run(run())


def test_gameinfo_rechecks_effective_flag_and_existing_bank_survives_new_round():
    state, _ = tactical_state()
    assert state.build_game_start_payload(0)["game_info"]["tactical_call"] is True
    state.player_list[1].user_id = 0
    assert state.build_game_start_payload(0)["game_info"]["tactical_call"] is False
    state.player_list[1].user_id = 102
    assert state.sync_tactical_enabled() is False
    state.player_list[1].remaining_time = 3.25
    state.end_draw()
    state.machine.transition(P.READY)
    state.advance_round_after_ready()
    state.initialize_round()
    assert all(p.remaining_time == 20 for p in state.player_list)
    assert state.tactical_call is False


def test_saved_effective_off_is_respected_and_gameinfo_writes_bot_off_back_to_room():
    state, _ = tactical_state()
    room = {"tactical_call": False}
    state.game_server = SimpleNamespace(room_manager=SimpleNamespace(rooms={state.room_id: room}))
    assert state.build_game_start_payload(0)["game_info"]["tactical_call"] is False
    room["tactical_call"] = True
    assert state.sync_tactical_enabled() is False and room["tactical_call"] is False


def test_zero_normal_budget_passes_immediately_without_entering_tactical_phase():
    async def run():
        state, _ = tactical_state(0, 0)
        await asyncio.wait_for(state.resolve_action_window(), .5)
        assert state.machine.phase == P.TURN and state.current_player_index == 1
        assert not state._tactical_recheck_active
        assert all(p.remaining_time == 0 for p in state.player_list)
    asyncio.run(run())


def test_timely_main_replies_before_wait_are_not_dropped_by_tactical_adapter():
    async def run():
        state, _ = tactical_state()
        await state.submit_action(1, "chi_right")
        await state.submit_action(2, "peng")
        await asyncio.wait_for(state.resolve_action_window(), .5)
        assert state.current_player_index == 2 and state.player_list[2].remaining_time == 20
        assert sum(e["action"] in ("chi_right", "peng") for e in state.domain_events) == 1
    asyncio.run(run())


def test_resolving_without_a_window_is_rejected_before_any_mutation():
    async def run():
        state = HangzhouGameState()
        with pytest.raises(ValueError, match="No live"):
            await state.resolve_action_window()
        assert state.machine.phase == P.START and not state.domain_events
    asyncio.run(run())


def test_shared_recheck_hook_default_submission_map_keeps_the_same_time_contract():
    async def run():
        from ..public.tactical_claim import tactical_grace_phase, clear_tactical_round_state
        state, clock = tactical_state()
        task = asyncio.create_task(tactical_grace_phase(state, "chi_right", 1, dict(action_type="chi_right"), 13,
            broadcast_do_action=state._broadcast_tactical_application,
            broadcast_ask_other_action=state._broadcast_tactical_recheck))
        await until(lambda: state._tactical_recheck_active)
        await spin()
        assert prompt(state, 2) == (5, 0)
        clock.advance(3)
        await state.submit_action(2, "peng")
        result = await asyncio.wait_for(task, .5)
        assert result[:2] == ("peng", 2) and state.player_list[2].remaining_time == 20
        clear_tactical_round_state(state)
    asyncio.run(run())


def test_manual_driver_without_recording_still_commits_only_the_winning_meld():
    async def run():
        state, clock = tactical_state()
        state.game_record.clear()
        task = await begin_recheck(state, clock)
        await state.submit_action(2, "peng")
        await asyncio.wait_for(task, .5)
        assert state.current_player_index == 2 and state.player_list[1].combination_tiles == []
        assert state.player_list[2].combination_tiles == ["k13"] and state.game_record == {}
        assert physical(state) == Counter({t: 4 for t in TILES})
    asyncio.run(run())


def test_response_during_application_forwarding_does_not_resume_closed_normal_bank():
    async def run():
        state, clock = tactical_state()
        task = asyncio.create_task(state.resolve_action_window())
        await spin()
        state.game_server = SimpleNamespace(user_id_to_connection={103: SimpleNamespace(websocket=AsyncMock())})
        observed = []
        async def reply_during_forward(index, packet):
            if index == 2 and packet.get("do_action_info", {}).get("is_claim") and not observed:
                observed.append(True)
                assert state._tactical_in_application and not state._tactical_recheck_active
                clock.advance(2)
                await state.submit_action(2, "peng")
                assert state.build_game_start_payload(2)["game_info"]["players_info"][2]["remaining_time"] == 17
        state.send_to_realtime_spectators = reply_during_forward
        clock.advance(8)
        await state.submit_action(1, "chi_right")
        await asyncio.wait_for(task, .5)
        assert observed and state.current_player_index == 2
        assert state.player_list[1].remaining_time == state.player_list[2].remaining_time == 17
        assert not any((p.get("ask_other_action_info") or {}).get("is_tactical_recheck") for p in state.outbound_payloads)
    asyncio.run(run())
