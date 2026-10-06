"""Protocol/action regressions with a controlled monotonic clock; no long sleeps."""

import asyncio
from collections import Counter
from copy import deepcopy
import json
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_flow import PLAIN, act, physical, turn
from .state_machine import Phase as P
from ...game_calculation.hangzhou.rules import TILES


class Clock:
    def __init__(self, state):
        self.now = 1000.0
        state._timing_now = lambda: self.now

    def advance(self, seconds):
        self.now += seconds

    def wake(self, state):
        for index in state.waiting_players_list:
            state.action_events[index].set()


async def spin():
    for _ in range(8):
        await asyncio.sleep(0)


def claim_state(bank=20, step=5, *, multiple=False, kong=False, responder_bank=None):
    tile = 13 if multiple else 47
    actor_hand = list(PLAIN) if multiple else [47] + PLAIN[1:]
    state = turn(actor_hand, round_timer=bank, step_timer=step, record=False)
    prefixes = [[11, 12], [13, 13], []] if multiple else [[47] * (3 if kong else 2), [], []]
    pool = Counter({t: 4 for t in TILES}) - Counter(actor_hand + sum(prefixes, []))
    # Preserve all physical tile counts, and keep the selected claim options
    # distinct: next-seat chow + other-seat pung, or one honor pung.
    for player, prefix in zip(state.player_list[1:], prefixes):
        fillers = [t for t in pool.elements() if t != tile][:13 - len(prefix)]
        player.hand_tiles = prefix + fillers
        pool.subtract(fillers)
    state.tiles_list = list(pool.elements())
    if responder_bank is not None:
        state.player_list[1].remaining_time = responder_bank
    state.start_game_recording()
    state.start_round_recording()
    act(state, 0, "cut", TileId=tile, cutClass=False)
    assert physical(state) == Counter({t: 4 for t in TILES})
    assert state.waiting_players_list == ([1, 2] if multiple else [1])
    clock = Clock(state)
    return state, clock


async def start_wait(state):
    task = asyncio.create_task(state.wait_action())
    await spin()
    return task


async def answer_all(state, action="pass", player=1, **data):
    for index in list(state.waiting_players_list):
        await state.submit_action(index, action if index == player else "pass", **(data if index == player else {}))


def prompt(state, index):
    payload = state.build_pending_action_payload(index)
    info = payload.get("ask_hand_action_info") or payload["ask_other_action_info"]
    return info["remaining_time"], info["step_remaining"]


@pytest.mark.parametrize("bank,step", [(20, 5), (40, 8), (3, 10), (0, 5), (20, 0), (0, 0)])
def test_first_hand_and_claim_use_the_exact_room_budget(bank, step):
    state = turn(round_timer=bank, step_timer=step)
    assert prompt(state, 0) == (bank, step)
    state, _ = claim_state(bank, step)
    assert prompt(state, 1) == (bank, step)


@pytest.mark.parametrize("claim", [False, True])
@pytest.mark.parametrize("elapsed,expected_bank,expected_step", [(2, 20, 3), (5, 20, 0), (8, 17, 0)])
def test_step_then_bank_for_hand_and_claim(claim, elapsed, expected_bank, expected_step):
    async def run():
        state, clock = claim_state() if claim else (turn(round_timer=20, step_timer=5), None)
        clock = clock or Clock(state)
        index = 1 if claim else 0
        waiter = await start_wait(state)
        clock.advance(elapsed)
        assert prompt(state, index) == (expected_bank, expected_step)
        assert state._action_deadlines[index] == 1025
        if claim:
            await answer_all(state)
        else:
            await state.submit_action(0, "cut", TileId=42, cutClass=True)
        await asyncio.wait_for(waiter, 0.5)
        assert state.player_list[index].remaining_time == expected_bank
    asyncio.run(run())


def test_repeated_fractional_overtime_is_charged_and_wire_rounding_never_adds_two_seconds():
    state = turn(round_timer=20, step_timer=5)
    state._consume_time_bank(0, 5.9)
    state._consume_time_bank(0, 5.9)
    assert state.player_list[0].remaining_time == pytest.approx(18.2)
    state.open_action_window(state.begin_turn(0))
    clock = Clock(state)
    state._start_action_clock(0)
    clock.advance(0.8)
    # 18.2 bank + 4.2 step => 22.4 total, advertised ceil total is 23.
    bank, step = prompt(state, 0)
    assert bank + step == 23 and step == 5
    assert all(type(p["remaining_time"]) is int for p in state.build_game_start_payload(0)["game_info"]["players_info"])


@pytest.mark.parametrize("bank,step", [(20, 5), (4, 8), (0, 5), (20, 0), (0, 0)])
def test_claim_timeout_uses_full_budget_passes_and_exhausts_only_waiting_banks(bank, step):
    async def run():
        state, clock = claim_state(bank, step)
        waiting = list(state.waiting_players_list)
        waiter = await start_wait(state)
        if bank + step:
            clock.advance(bank + step - 0.25)
            clock.wake(state)
            await spin()
            assert not waiter.done(), "No fixed 3/5-second premature pass"
            assert sum(prompt(state, 1)) == 1
            clock.advance(0.25)
            clock.wake(state)
        responses = await asyncio.wait_for(waiter, 0.5)
        assert all(row["action_type"] == "pass" for row in responses.values())
        assert all(state.player_list[index].remaining_time == 0 for index in waiting)
        assert state.player_list[0].remaining_time == bank
    asyncio.run(run())


def test_hand_timeout_chooses_legal_forced_draw_discard_and_rejects_late_action():
    async def run():
        state = turn(round_timer=20, step_timer=5)
        state.cai_discard_locks = {1}
        state.open_action_window(state.begin_turn(0))
        clock = Clock(state)
        waiter = await start_wait(state)
        clock.advance(25)
        with pytest.raises(ValueError, match="时间"):
            await state.submit_action(0, "cut", TileId=42, cutClass=True)
        clock.wake(state)
        response = (await asyncio.wait_for(waiter, 0.5))[0]
        assert response["TileId"] == 42 and response["cutClass"] and response["is_timeout_action"]
        assert state.player_list[0].remaining_time == 0
        state.apply_action_results(state.live_pending_window, {0: response})
        assert state.player_list[0].discard_tiles[-1] == 42
    asyncio.run(run())


@pytest.mark.parametrize("action", ["peng", "chi_left"])
def test_claim_then_discard_gets_one_fresh_step_and_preserves_spent_bank(action):
    async def run():
        state, clock = claim_state(multiple=action.startswith("chi"))
        waiter = await start_wait(state)
        clock.advance(8)
        await answer_all(state, action)
        state.apply_action_results(state.live_pending_window, await waiter)
        assert state.machine.phase == P.DISCARD_ONLY
        assert prompt(state, 1) == (17, 5)
        waiter = await start_wait(state)
        clock.advance(4)
        tile = next(iter(state.legal_discard_tiles(1)))
        await state.submit_action(1, "cut", TileId=tile, cutClass=False)
        await waiter
        assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


@pytest.mark.parametrize("kind", ["angang", "jiagang", "gang"])
def test_kong_replacement_gets_a_new_step_not_a_new_bank(kind):
    async def run():
        if kind == "gang":
            state, clock = claim_state(kong=True)
            index = 1
        else:
            state = turn([11] * (4 if kind == "angang" else 1) + PLAIN[4:],
                         () if kind == "angang" else ("k11",), round_timer=20, step_timer=5)
            clock, index = Clock(state), 0
        waiter = await start_wait(state)
        clock.advance(8)
        await state.submit_action(index, kind, **({} if kind == "gang" else {"target_tile": 11}))
        state.apply_action_results(state.live_pending_window, await waiter)
        assert state.machine.phase == P.TURN and state.player_list[index].after_kong
        assert prompt(state, index) == (17, 5)
        waiter = await start_wait(state)
        clock.advance(3)
        tile = state.player_list[index].hand_tiles[-1]
        await state.submit_action(index, "cut", TileId=tile, cutClass=True)
        await waiter
        assert state.player_list[index].remaining_time == 17
    asyncio.run(run())


@pytest.mark.parametrize("action", ["pass", "hu_self"])
def test_ten_winds_is_an_actual_hu_pass_window_with_room_budget(action):
    async def run():
        state = turn(PLAIN[:-1] + [43], round_timer=20, step_timer=5)
        state.player_list[0].discard_origin_tiles = [41,42,42,43,43,44,44,45,47]
        act(state, 0, "cut", TileId=43, cutClass=True)
        assert state.machine.phase == P.TEN_WINDS
        clock = Clock(state)
        assert prompt(state, 0) == (20, 5)
        waiter = await start_wait(state)
        clock.advance(8)
        await state.submit_action(0, action)
        state.apply_action_results(state.live_pending_window, await waiter)
        assert state.player_list[0].remaining_time == 17
        assert state.machine.phase == (P.RESPONSE if action == "pass" else P.END)
    asyncio.run(run())


def test_reconnect_duplicate_and_spectator_snapshots_do_not_reset_or_deduct_twice():
    async def run():
        state, clock = claim_state()
        waiter = await start_wait(state)
        deadline = state._action_deadlines[1]
        clock.advance(8)
        state.send_payload_to_player = AsyncMock()
        for _ in range(2):
            await state.player_reconnect(state.player_list[1].user_id)
            for payload in state.restore_payloads(1)[1:]:
                assert payload["ask_other_action_info"]["remaining_time"] == 17
                assert payload["ask_other_action_info"]["step_remaining"] == 0
                state._start_action_clock(1, payload)
            assert prompt(state, 1) == (17, 0)
            assert state.player_list[1].remaining_time == 20
            assert state._action_deadlines[1] == deadline
        await answer_all(state)
        await waiter
        assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_each_responder_keeps_its_own_bank_and_deadline():
    async def run():
        state, clock = claim_state(multiple=True, responder_bank=4)
        waiting = list(state.waiting_players_list)
        waiter = await start_wait(state)
        assert state._action_deadlines == {1: 1009, 2: 1025}
        clock.advance(9)
        clock.wake(state)
        await spin()
        if 1 in state.waiting_players_list:
            pytest.fail("The exhausted responder was not passed")
        assert state.build_pending_action_payload(1) is None
        for index in list(state.waiting_players_list):
            await state.submit_action(index, "pass")
        responses = await asyncio.wait_for(waiter, 0.5)
        assert responses[1]["action_type"] == "pass" and state.player_list[1].remaining_time == 0
        assert all(state.player_list[index].remaining_time == 16 for index in waiting if index != 1)
    asyncio.run(run())


def test_original_delivery_and_arrival_survive_a_slow_other_viewer_and_late_wait_entry():
    async def run():
        state = turn(round_timer=20, step_timer=5)
        clock = Clock(state)
        sent = []
        class Socket:
            async def send_json(self, payload):
                clock.advance(1)
                sent.append(json.loads(json.dumps(payload)))
        state.game_server = SimpleNamespace(user_id_to_connection={p.user_id: SimpleNamespace(websocket=Socket()) for p in state.player_list})
        async def spectator(index, payload):
            if index == 0 and payload.get("ask_hand_action_info"):
                clock.advance(8)
                await state.submit_action(0, "cut", TileId=42, cutClass=True)
                clock.advance(10)
        state.send_to_realtime_spectators = spectator
        await state._send_claim_protection_payload(0, state.build_pending_action_payload(0))
        deadline = state._action_deadlines[0]
        assert deadline == 1026
        assert state.build_pending_action_payload(0) is None
        assert len(state.restore_payloads(0)) == 1
        assert state.build_game_start_payload(0)["game_info"]["players_info"][0]["remaining_time"] == 17
        # Re-delivering an already built packet must neither start a fresh
        # step nor charge for the time spent waiting on another viewer.
        duplicate = deepcopy(sent[0])
        await state.send_payload_to_player(0, duplicate)
        assert state._action_deadlines[0] == deadline
        assert (duplicate["ask_hand_action_info"]["remaining_time"], duplicate["ask_hand_action_info"]["step_remaining"]) == (17, 0)
        assert sent[0]["ask_hand_action_info"]["remaining_time"] == 20
        assert sent[0]["ask_hand_action_info"]["step_remaining"] == 5
        response = await state.wait_action(timeout=3)  # A caller hint must not cap the room budget.
        assert response[0]["TileId"] == 42 and state.player_list[0].remaining_time == 17
    asyncio.run(run())


def test_delayed_first_send_and_spectator_delivery_do_not_steal_time():
    async def run():
        state, clock = claim_state()
        class Socket:
            async def send_json(self, payload):
                clock.advance(7)
        spectator = AsyncMock()
        state.send_to_realtime_spectators = spectator
        observer = SimpleNamespace(websocket=AsyncMock())
        state.game_server = SimpleNamespace(user_id_to_connection={
            state.player_list[1].user_id: SimpleNamespace(websocket=Socket()), 999: observer})
        clock.advance(11)
        assert prompt(state, 1) == (20, 5), "Unsent asks have not started spending"
        await state._send_claim_protection_payload(1, state.build_pending_action_payload(1))
        assert state._action_deadlines[1] == 1043
        clock.advance(8)
        await state.send_realtime_spectator_snapshot(999, 1)
        assert observer.websocket.send_json.await_count == 2
        observer_prompt = observer.websocket.send_json.await_args_list[-1].args[0]["ask_other_action_info"]
        assert (observer_prompt["remaining_time"], observer_prompt["step_remaining"]) == (17, 0)
        assert prompt(state, 1) == (17, 0) and state._action_deadlines[1] == 1043
    asyncio.run(run())


def test_next_round_restores_configured_bank_and_ready_does_not_spend_it():
    async def run():
        state = turn(round_timer=40, step_timer=8)
        clock = Clock(state)
        waiter = await start_wait(state)
        clock.advance(11)
        await state.submit_action(0, "hu_self")
        state.apply_action_results(state.live_pending_window, await waiter)
        assert state.player_list[0].remaining_time == 37
        await state.run_round_ready_phase(timeout=0)
        assert state.player_list[0].remaining_time == 37
        state.advance_round_after_ready()
        state.initialize_round()
        state.open_action_window(state.opening_window())
        assert all(p.remaining_time == 40 for p in state.player_list)
        assert prompt(state, 0) == (40, 8)
    asyncio.run(run())


@pytest.mark.parametrize("bank,step,elapsed,expected", [(40, 8, 6, 40), (40, 8, 10.5, 37.5),
                                                      (0, 5, 2, 0), (20, 0, 3, 17)])
def test_custom_step_and_bank_control_actual_charges(bank, step, elapsed, expected):
    async def run():
        state, clock = claim_state(bank, step)
        waiter = await start_wait(state)
        clock.advance(elapsed)
        await answer_all(state)
        await waiter
        assert state.player_list[1].remaining_time == expected
    asyncio.run(run())


def test_two_real_decisions_charge_fractional_overtime_exactly_once_each():
    async def run():
        state, clock = claim_state()
        for action in ("peng", "cut"):
            waiter = await start_wait(state)
            clock.advance(5.9)
            data = {"TileId": next(iter(state.legal_discard_tiles(1))), "cutClass": False} if action == "cut" else {}
            await state.submit_action(1, action, **data)
            state.apply_action_results(state.live_pending_window, await waiter)
        assert state.player_list[1].remaining_time == pytest.approx(18.2)
        assert physical(state) == Counter({t: 4 for t in TILES})
    asyncio.run(run())


def test_reconnect_of_already_answered_responder_only_restores_table():
    async def run():
        state, clock = claim_state(multiple=True)
        waiter = await start_wait(state)
        clock.advance(8)
        await state.submit_action(1, "pass")
        await spin()
        assert state.waiting_players_list == [2]
        assert state.player_list[1].remaining_time == 17
        clock.advance(3)
        assert state.build_pending_action_payload(1) is None
        assert len(state.restore_payloads(1)) == 1
        await state.submit_action(2, "pass")
        await waiter
        assert state.player_list[1].remaining_time == 17 and state.player_list[2].remaining_time == 14
    asyncio.run(run())


@pytest.mark.parametrize("timers", [None, (40, 8), (0, 5), (20, 0), (0, 0)])
def test_request_room_persistence_and_initialization_preserve_time_contract(timers):
    from .test_contracts import fake_room_manager
    from .HangzhouGameState import HangzhouGameState
    from ...room.hangzhou_room import create_hangzhou_room, handle_create_hangzhou_room
    from ...response import Response
    async def run():
        manager, _ = fake_room_manager()
        async def create(connection, **kwargs):
            return await create_hangzhou_room(manager, connection, **kwargs)
        server = SimpleNamespace(room_manager=SimpleNamespace(create_Hangzhou_room=create))
        socket = SimpleNamespace(send_json=AsyncMock())
        message = {"roomname": "timing contract"}
        expected = timers or (20, 5)
        if timers is not None:
            message.update(roundTimerValue=timers[0], stepTimerValue=timers[1])
        await handle_create_hangzhou_room(server, "c", message, socket)
        response = socket.send_json.await_args.args[0]
        room = manager.rooms[123456]
        assert response["success"] and (room["round_timer"], room["step_timer"]) == expected
        assert (response["room_info"]["round_timer"], response["room_info"]["step_timer"]) == expected
        state = HangzhouGameState(room_data={**room, "player_list": [101, 102, 103, 104]})
        state.initialize_round()
        state.open_action_window(state.opening_window())
        assert all(player.remaining_time == expected[0] for player in state.player_list)
        packet = Response.model_validate(state.build_pending_action_payload(0)).model_dump(exclude_none=True)
        assert (packet["ask_hand_action_info"]["remaining_time"], packet["ask_hand_action_info"]["step_remaining"]) == expected
    asyncio.run(run())


def test_each_live_spectator_gets_current_clock_without_changing_primary_packet():
    async def run():
        state = turn(round_timer=20, step_timer=5)
        clock = Clock(state)
        first, second = [], []
        class ObserverSocket:
            def __init__(self, sent, delay):
                self.sent, self.delay = sent, delay
            async def send_json(self, payload):
                self.sent.append(deepcopy(payload))
                clock.advance(self.delay)
        primary = SimpleNamespace(websocket=AsyncMock())
        state.game_server = SimpleNamespace(user_id_to_connection={101: primary,
            998: SimpleNamespace(websocket=ObserverSocket(first, 8)),
            999: SimpleNamespace(websocket=ObserverSocket(second, 0))})
        state.realtime_spectators = [SimpleNamespace(user_id=i, host_user_id=101) for i in (998, 999)]
        payload = state.build_pending_action_payload(0)
        await state._send_claim_protection_payload(0, payload)
        assert (first[0]["ask_hand_action_info"]["remaining_time"], first[0]["ask_hand_action_info"]["step_remaining"]) == (20, 5)
        assert (second[0]["ask_hand_action_info"]["remaining_time"], second[0]["ask_hand_action_info"]["step_remaining"]) == (17, 0)
        assert (payload["ask_hand_action_info"]["remaining_time"], payload["ask_hand_action_info"]["step_remaining"]) == (20, 5)
        assert state._action_deadlines[0] == 1025
    asyncio.run(run())


def test_sequential_primary_delivery_keeps_distinct_deadlines_and_next_turn_step():
    async def run():
        state, clock = claim_state(multiple=True, responder_bank=4)
        state.game_server = SimpleNamespace(user_id_to_connection={
            state.player_list[i].user_id: SimpleNamespace(websocket=AsyncMock()) for i in (1, 2)})
        async def slow_forward(index, _):
            if index == 1:
                clock.advance(8)
        state.send_to_realtime_spectators = slow_forward
        for index in (1, 2):
            await state._send_claim_protection_payload(index, state.build_pending_action_payload(index))
        waiter = await start_wait(state)
        assert state._action_deadlines == {1: 1009, 2: 1033}
        clock.advance(1.5)
        clock.wake(state)
        await spin()
        assert state.waiting_players_list == [2] and prompt(state, 2) == (20, 4)
        await state.submit_action(2, "pass")
        state.apply_action_results(state.live_pending_window, await waiter)
        assert state.player_list[1].remaining_time == 0 and state.player_list[2].remaining_time == 20
        assert state.current_player_index == 1 and prompt(state, 1) == (0, 5)
        assert physical(state) == Counter({t: 4 for t in TILES})
    asyncio.run(run())


def test_invalid_and_duplicate_replies_never_replace_the_first_valid_arrival():
    async def run():
        state, clock = claim_state()
        waiter = await start_wait(state)
        clock.advance(8)
        with pytest.raises(ValueError):
            await state.submit_action(1, "hu")
        assert state._action_clocks[1].received_at is None
        await state.submit_action(1, "pass")
        clock.advance(4)
        with pytest.raises(ValueError):
            await state.submit_action(1, "pass")
        await waiter
        assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_authenticated_stale_tick_resend_does_not_restart_the_clock():
    from .get_action import handle_action
    async def run():
        state, clock = claim_state()
        waiter = await start_wait(state)
        clock.advance(8)
        websocket = SimpleNamespace(send_json=AsyncMock())
        server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: state),
            players={"own": SimpleNamespace(user_id=state.player_list[1].user_id)})
        message = dict(type="gamestate/hangzhou/send_action", gamestate_id=state.gamestate_id,
                       action="pass", action_tick=state.server_action_tick - 1)
        await handle_action(server, "own", message, websocket)
        info = websocket.send_json.await_args.args[0]["ask_other_action_info"]
        assert (info["remaining_time"], info["step_remaining"]) == (17, 0)
        assert state._action_deadlines[1] == 1025
        await handle_action(server, "own", dict(message, action_tick=state.server_action_tick), websocket)
        await waiter
        assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_fractional_bank_is_not_capped_by_the_inherited_integer_timeout_hint():
    async def run():
        state = turn(round_timer=20, step_timer=5)
        state._consume_time_bank(0, 5.9)
        state._consume_time_bank(0, 5.9)
        state.open_action_window(state.begin_turn(0))
        clock = Clock(state)
        hint = state.estimated_action_window_timeout()
        assert hint == 23  # Inherited estimate rounds the real 23.2 budget down.
        waiter = asyncio.create_task(state.wait_action(timeout=hint))
        await spin()
        clock.advance(23.1)
        clock.wake(state)
        await spin()
        assert not waiter.done() and prompt(state, 0) == (1, 0)
        await state.submit_action(0, "cut", TileId=42, cutClass=True)
        await waiter
        assert state.player_list[0].remaining_time == pytest.approx(0.1)
    asyncio.run(run())


def test_match_pause_precedes_first_ask_and_reconnect_cannot_start_a_paused_clock(monkeypatch):
    from . import runtime
    from .HangzhouGameState import HangzhouGameState
    async def run():
        state = HangzhouGameState(room_data={**HangzhouGameState._default_room_data(), "round_timer": 20, "step_timer": 5})
        clock = Clock(state)
        state.vote_manager = SimpleNamespace(phase="pause_pending")
        sockets = {player.user_id: SimpleNamespace(websocket=AsyncMock()) for player in state.player_list}
        state.game_server = SimpleNamespace(user_id_to_connection=sockets)
        checkpoints = []
        async def checkpoint(current):
            checkpoints.append(clock.now)
            current.vote_manager.phase = "paused"
            assert current._action_clocks[0].started_at is None, "A new decision must not start before the pause checkpoint"
            await current.player_reconnect(current.player_list[0].user_id)
            assert current.build_pending_action_payload(0) is None
            assert current._action_clocks[0].started_at is None
            with pytest.raises(ValueError, match="暂停"):
                await current.submit_action(0, "cut", TileId=current.player_list[0].hand_tiles[-1], cutClass=True)
            clock.advance(100)
            current.vote_manager.phase = "idle"
        monkeypatch.setattr(runtime, "vote_checkpoint", checkpoint)
        async def resolve(timeout=None):
            assert state._action_deadlines[0] == 1125
            waiter = await start_wait(state)
            clock.advance(2)
            await state.submit_action(0, "cut", TileId=state.player_list[0].hand_tiles[-1], cutClass=True)
            state.apply_action_results(state.live_pending_window, await waiter)
            assert state.player_list[0].remaining_time == 20
            state.open_action_window(state.end_draw())
            state.match_finishing = True
        state.resolve_action_window = resolve
        state.complete_game_lifecycle = AsyncMock()
        await state.run_game_loop()
        assert checkpoints == [1000] and state.player_list[0].remaining_time == 20
        assert state.machine.phase == P.FINISHED
    asyncio.run(run())
