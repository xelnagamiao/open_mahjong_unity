"""广东 MIL2023 的线上步时 + 局时合同；时钟可控，动作/广播使用真实路径。"""

import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_state import make_state, hand, HAND
from ..game_taiwan import boardcast
from ..game_taiwan.wait_action import _collect_responses, _build_ask_deadlines
from ..public.ai.get_action import get_action
from ..public.ask_timing import begin_ask_round, note_ask_delivered


class Clock:
    def __init__(self):
        self.now = 100.0

    def time(self):
        return self.now

    def monotonic(self):
        return self.now

    def advance(self, seconds):
        self.now += seconds


@pytest.fixture
def clock():
    value = Clock()
    with patch("time.time", value.time), patch("time.monotonic", value.monotonic):
        yield value


class Socket:
    def __init__(self, callback=None):
        self.messages = []
        self.callback = callback

    async def send_json(self, payload):
        self.messages.append(deepcopy(payload))
        if self.callback:
            await self.callback(payload)


def state_for_claim(**options):
    state = make_state(**options)
    state.game_status = "waiting_action_after_cut"
    state.current_player_index = 0
    state.player_list[0].discard_tiles = [11]
    state.action_dict = {0: [], 1: ["peng", "pass"], 2: [], 3: []}
    state.game_server.players = {}
    for player in state.player_list:
        connection = SimpleNamespace(user_id=player.user_id, websocket=Socket())
        state.game_server.user_id_to_connection[player.user_id] = connection
        state.game_server.players[str(player.user_id)] = connection
    state.prepare_private_fields = AsyncMock()
    return state


async def collect(state):
    hook = getattr(state, "collect_action_responses", None)
    return await hook() if hook else await _collect_responses(state)


def deliver(state, indexes):
    state.prepare_action_window()
    begin_ask_round(state)
    for index in indexes:
        note_ask_delivered(state, index)


def submit(state, index, action="pass"):
    state.action_queues[index].put_nowait({"action_type": action})
    state.action_events[index].set()


def test_prompt_reply_during_broadcast_is_not_rejected(clock):
    async def run():
        state = state_for_claim()
        state.prepare_action_window()

        async def answer(payload):
            if payload["type"].endswith("ask_other_action"):
                await get_action(state, "102", "pass", False, None, None, None,
                                 action_tick=payload["ask_other_action_info"]["action_tick"])

        state.game_server.user_id_to_connection[102].websocket.callback = answer
        await boardcast.broadcast_ask_other_action(state)
        assert not state.action_queues[1].empty()
        responses, _ = await collect(state)
        assert responses[1]["action_type"] == "pass"
        assert state.player_list[1].remaining_time == 20

    asyncio.run(run())


def test_early_queued_response_survives_late_collection(clock):
    async def run():
        state = state_for_claim()
        deliver(state, (1,))
        clock.advance(1)
        submit(state, 1)
        clock.advance(29)  # Other outbound work delayed collector, not the user's answer.
        responses, _ = await collect(state)
        assert responses[1]["action_type"] == "pass"
        assert state.player_list[1].remaining_time == 20

    asyncio.run(run())


def test_answered_seat_does_not_get_a_reconnect_prompt_while_other_seat_waits(clock):
    async def run():
        state = state_for_claim()
        state.action_dict[2] = ["peng", "pass"]
        deliver(state, (1, 2))
        calls = 0
        captured = []

        async def controlled_wait(tasks, timeout, return_when):
            nonlocal calls
            calls += 1
            if calls == 1:
                clock.advance(8)
                submit(state, 1)
            else:
                await boardcast.reconnected_send_pending_ask_for_viewer(state, 102, 1)
                captured.extend(state.game_server.user_id_to_connection[102].websocket.messages)
                submit(state, 2)
            await asyncio.sleep(0)
            return {task for task in tasks if task.done()}, {task for task in tasks if not task.done()}

        with patch("asyncio.wait", controlled_wait):
            responses, _ = await collect(state)
        assert set(responses) == {1, 2}
        assert state.player_list[1].remaining_time == 17
        assert not captured

    asyncio.run(run())


@pytest.mark.parametrize("bank,step,elapsed,expected_bank", [
    (20, 5, 3, 20), (20, 5, 8, 17), (31, 8, 7, 31), (31, 8, 11, 28),
    (0, 8, 7, 0), (20, 0, 7, 13), (0, 0, 0, 0),
])
@pytest.mark.parametrize("status", ["waiting_action_after_cut", "waiting_action_qianggang", "waiting_hand_action"])
def test_configured_window_and_receipt_charge(clock, bank, step, elapsed, expected_bank, status):
    async def run():
        state = state_for_claim(round_timer=bank, step_timer=step)
        state.game_status = status
        state.current_player_index = 1 if status == "waiting_hand_action" else 0
        state.action_dict = {i: ["cut"] if i == 1 and status == "waiting_hand_action"
                             else ["pass"] if i == 1 else [] for i in range(4)}
        deliver(state, (1,))
        deadline = state._action_windows[1].deadline
        assert deadline == 100 + bank + step
        assert state.action_clock(state.player_list[1]) == (bank, step)
        clock.advance(elapsed)
        if bank + step:
            submit(state, 1, state.action_dict[1][0])
        responses, _ = await collect(state)
        assert bool(responses) == bool(bank + step)
        assert state.player_list[1].remaining_time == expected_bank
        assert state._action_windows[1].deadline == deadline
        assert state.waiting_players_list == []

    asyncio.run(run())


@pytest.mark.parametrize("bank,step", [(20, 5), (7, 8), (0, 8), (0, 0)])
def test_expired_claim_passes_and_clears_only_its_bank(clock, bank, step):
    async def run():
        state = state_for_claim(round_timer=bank, step_timer=step)
        deliver(state, (1,))
        clock.advance(bank + step)
        submit(state, 1, "peng")  # At the deadline is late, not a valid replacement.
        await state.wait_action()
        assert state.game_status == "deal_card"
        assert state.player_list[1].remaining_time == 0
        assert state.player_list[0].remaining_time == bank
        assert state.action_queues[1].empty()

    asyncio.run(run())


def test_fractional_bank_is_retained_across_decisions(clock):
    async def run():
        state = state_for_claim()
        for expected in (19.1, 18.2):
            deliver(state, (1,))
            clock.advance(5.9)
            submit(state, 1)
            responses, _ = await collect(state)
            assert responses[1]["action_type"] == "pass"
            assert state.player_list[1]._guangdong_exact_bank == pytest.approx(expected)
        deliver(state, (1,))
        window = state._action_windows[1]
        assert window.deadline - clock.now == pytest.approx(23.2)
        # One integer rounding allowance for the total, not one for each component.
        clock.advance(0.7)
        assert sum(state.action_clock(state.player_list[1])) == 23

    asyncio.run(run())


@pytest.mark.parametrize("bank,step,elapsed,expected", [
    (20, 5, 3, (20, 2)), (20, 5, 8, (17, 0)),
    (0, 8, 5, (0, 3)), (9, 3, 4, (8, 0)),
])
def test_actual_reconnect_packet_retains_deadline_without_debit(clock, bank, step, elapsed, expected):
    async def run():
        state = state_for_claim(round_timer=bank, step_timer=step)
        deliver(state, (1,))
        origin, deadline = state._action_windows[1].origin, state._action_windows[1].deadline
        clock.advance(elapsed)
        for _ in range(3):
            await boardcast.reconnected_send_pending_ask_for_viewer(state, 102, 1)
            packet = state.game_server.user_id_to_connection[102].websocket.messages[-1]["ask_other_action_info"]
            assert (packet["remaining_time"], packet["step_remaining"]) == expected
            note_ask_delivered(state, 1)  # Duplicate delivery does not restart the clock.
            assert state._action_windows[1].origin == origin
            assert state._action_windows[1].deadline == deadline
            assert state.player_list[1].remaining_time == bank
        submit(state, 1)
        await collect(state)
        assert state.player_list[1].remaining_time == expected[0]
        assert not state.pending_ask_actions(1)

    asyncio.run(run())


def test_hand_reconnect_uses_remaining_step_and_expired_window_has_no_prompt(clock):
    async def run():
        state = state_for_claim(round_timer=0, step_timer=8)
        state.game_status = "waiting_hand_action"
        state.current_player_index = 1
        state.action_dict = {i: ["cut"] if i == 1 else [] for i in range(4)}
        deliver(state, (1,))
        clock.advance(5)
        await boardcast.reconnected_send_pending_ask_for_viewer(state, 102, 1)
        messages = state.game_server.user_id_to_connection[102].websocket.messages
        info = messages[-1]["ask_hand_action_info"]
        assert (info["remaining_time"], info["step_remaining"]) == (0, 3)
        clock.advance(3)
        await boardcast.reconnected_send_pending_ask_for_viewer(state, 102, 1)
        assert len(messages) == 1

    asyncio.run(run())


def test_wall_clock_adjustment_does_not_change_monotonic_deadline(clock):
    async def run():
        state = state_for_claim()
        deliver(state, (1,))
        clock.advance(8)
        with patch("time.time", return_value=99999999):
            await boardcast.reconnected_send_pending_ask_for_viewer(state, 102, 1)
            packet = state.game_server.user_id_to_connection[102].websocket.messages[-1]["ask_other_action_info"]
            assert (packet["remaining_time"], packet["step_remaining"]) == (17, 0)
            submit(state, 1)
            await collect(state)
        assert state.player_list[1].remaining_time == 17

    asyncio.run(run())


def test_actual_pung_opens_fresh_cut_window_with_remaining_bank(clock):
    async def run():
        state = state_for_claim()
        state.action_dict = {i: ["peng", "pass"] if i == 2 else [] for i in range(4)}
        hand(state, 0, []).discard_tiles = [11]
        player = hand(state, 2, [11, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41])
        deliver(state, (2,))
        clock.advance(8)
        submit(state, 2, "peng")
        await state.wait_action()
        assert state.current_player_index == 2
        assert state.game_status == "onlycut_after_action"
        assert player.remaining_time == 17
        state.game_status = "waiting_hand_action"
        state.action_dict = state.after_claim_actions()
        state.server_action_tick += 1
        deliver(state, (2,))
        assert state.action_clock(player) == (17, 5)
        assert state._action_windows[2].deadline - clock.now == 22
        assert state.action_dict[2] == ["cut"]
        clock.advance(2)
        submit(state, 2, "cut")
        await collect(state)
        assert player.remaining_time == 17

    asyncio.run(run())


def test_next_hand_restores_configured_bank_and_discards_old_window(clock):
    state = state_for_claim(round_timer=31, step_timer=8)
    deliver(state, (1,))
    clock.advance(14)
    submit(state, 1)
    asyncio.run(collect(state))
    assert state.player_list[1].remaining_time == 25
    state._reset_hand_runtime()
    assert state._action_windows == {}
    assert all(player.remaining_time == 31 for player in state.player_list)
    deliver(state, (1,))
    assert state.action_clock(state.player_list[1]) == (31, 8)


def test_response_receipt_is_server_owned_and_first_answer_is_retained(clock):
    async def run():
        state = state_for_claim()
        deliver(state, (1,))
        clock.advance(8)
        state.action_queues[1].put_nowait({"action_type": "pass", "_guangdong_received_at": -999})
        clock.advance(1)
        submit(state, 1, "peng")
        responses, _ = await collect(state)
        assert responses[1] == {"action_type": "pass"}
        assert state.player_list[1].remaining_time == 17
        assert not state.pending_ask_actions(1)

    asyncio.run(run())


def test_first_delivery_is_not_taxed_for_earlier_outbound_delay(clock):
    async def run():
        state = state_for_claim()
        state.prepare_action_window()
        begin_ask_round(state)
        clock.advance(30)
        submit(state, 1)  # Fast reply while send_json is finishing.
        note_ask_delivered(state, 1)
        responses, _ = await collect(state)
        assert responses[1]["action_type"] == "pass"
        assert state.player_list[1].remaining_time == 20

    asyncio.run(run())


def test_reconnect_during_first_send_delay_does_not_expire_unsent_window(clock):
    async def run():
        state = state_for_claim()
        state.prepare_action_window()
        begin_ask_round(state)
        clock.advance(30)  # Another socket is still sending, this seat has not been sent yet.
        assert state.is_action_pending(1)
        await boardcast.reconnected_send_pending_ask_for_viewer(state, 102, 1)
        messages = state.game_server.user_id_to_connection[102].websocket.messages
        assert messages[-1]["ask_other_action_info"]["remaining_time"] == 20
        assert messages[-1]["ask_other_action_info"]["step_remaining"] == 5
        assert state._action_windows[1].delivered is None  # Supplement is not a new window.
        note_ask_delivered(state, 1)
        assert state._action_windows[1].deadline == 155
        clock.advance(8)
        submit(state, 1)
        await collect(state)
        assert state.player_list[1].remaining_time == 17

    asyncio.run(run())


def test_unsent_seat_fallback_starts_after_broadcast_returns(clock):
    async def run():
        state = state_for_claim()
        state.prepare_action_window()
        begin_ask_round(state)
        clock.advance(30)

        async def answer(tasks, timeout, return_when):
            assert state._action_windows[1].deadline == 155
            clock.advance(4)
            submit(state, 1)
            await asyncio.sleep(0)
            return {task for task in tasks if task.done()}, {task for task in tasks if not task.done()}

        with patch("asyncio.wait", answer):
            responses, _ = await collect(state)
        assert responses[1]["action_type"] == "pass"
        assert state.player_list[1].remaining_time == 20

    asyncio.run(run())


@pytest.mark.parametrize("draw_slot", [False, True])
def test_hand_timeout_uses_real_default_discard_and_no_flower_replacement(clock, draw_slot):
    async def run():
        state = state_for_claim(round_timer=0, step_timer=8)
        state.game_status = "waiting_hand_action"
        state.current_player_index = 0
        player = hand(state, 0, HAND + [55])
        player.has_draw_slot = draw_slot
        player.last_drawn_tile = 55 if draw_slot else None
        state.action_dict = {i: ["cut"] if i == 0 else [] for i in range(4)}
        state._prepare_other_scores = AsyncMock()
        deliver(state, (0,))
        clock.advance(8)
        await state.wait_action()
        assert player.discard_tiles[-1] == 55  # Draw slot or largest initial hand tile.
        assert player.remaining_time == 0
        assert player.discarded_ghosts == 1
        assert not player.huapai_list
        assert not state.flower_tiles and not state.ready_candidate_cuts(0)

    asyncio.run(run())


@pytest.mark.parametrize("action", ["angang", "jiagang", "hu_self"])
def test_own_kong_and_win_dispatch_use_whole_room_budget(clock, action):
    async def run():
        state = state_for_claim()
        state.game_status = "waiting_hand_action"
        state.current_player_index = 1
        state.action_dict = {i: [action] if i == 1 else [] for i in range(4)}
        state.has_normal_self_draw = lambda _: False
        state.execute_angang = AsyncMock()
        state.execute_jiagang = AsyncMock()
        state.accept_self_draw = lambda index: setattr(state, "timing_win_accepted", index)
        deliver(state, (1,))
        clock.advance(8)
        state.action_queues[1].put_nowait({"action_type": action, "target_tile": 11})
        await state.wait_action()
        assert state.player_list[1].remaining_time == 17
        if action == "hu_self":
            assert state.timing_win_accepted == 1
        else:
            getattr(state, "execute_" + action).assert_awaited_once_with(1, 11)

    asyncio.run(run())


def test_stale_window_reply_neither_ends_nor_debits_current_window(clock):
    async def run():
        state = state_for_claim()
        state.server_action_tick = 20
        deliver(state, (1,))
        clock.advance(6)
        state.action_queues[1].put_nowait({"action_type": "peng", "_action_tick": 19})
        assert state.is_action_pending(1)

        async def answer(tasks, timeout, return_when):
            assert state.player_list[1].remaining_time == 20
            clock.advance(2)
            submit(state, 1)
            await asyncio.sleep(0)
            return {task for task in tasks if task.done()}, {task for task in tasks if not task.done()}

        with patch("asyncio.wait", answer):
            responses, _ = await collect(state)
        assert responses[1]["action_type"] == "pass"
        assert state.player_list[1].remaining_time == 17

    asyncio.run(run())


def test_real_broadcast_packets_and_each_seat_delivery_have_matching_budgets(clock):
    async def run():
        state = state_for_claim(round_timer=9, step_timer=3)
        state.action_dict[2] = ["pass"]

        async def slow_spectator(_index, _response):
            clock.advance(4)

        state.send_to_realtime_spectators = slow_spectator
        state.prepare_action_window()
        await boardcast.broadcast_ask_other_action(state)
        assert state._action_windows[1].deadline == 112
        assert state._action_windows[2].deadline == 116
        for index in (1, 2):
            message = state.game_server.user_id_to_connection[state.player_list[index].user_id].websocket.messages[-1]
            info = message["ask_other_action_info"]
            assert (info["remaining_time"], info["step_remaining"]) == (9, 3)
        submit(state, 1)
        submit(state, 2)
        await collect(state)
        assert (state.player_list[1].remaining_time, state.player_list[2].remaining_time) == (4, 8)

    asyncio.run(run())


def test_realtime_spectator_resnapshot_uses_same_clock_without_mutating_owner(clock):
    async def run():
        state = state_for_claim()
        viewer = SimpleNamespace(user_id=201, websocket=Socket())
        state.game_server.user_id_to_connection[201] = viewer
        deliver(state, (1,))
        clock.advance(8)
        origin, deadline = state._action_windows[1].origin, state._action_windows[1].deadline
        await boardcast.send_realtime_spectator_snapshot(state, 201, 1)
        packet = viewer.websocket.messages[-1]["ask_other_action_info"]
        assert (packet["remaining_time"], packet["step_remaining"]) == (17, 0)
        assert state._action_windows[1].origin == origin
        assert state._action_windows[1].deadline == deadline
        assert state.player_list[1].remaining_time == 20
        submit(state, 1)
        await collect(state)
        await boardcast.send_realtime_spectator_snapshot(state, 201, 1)
        assert viewer.websocket.messages[-1]["type"].endswith("game_start")
        assert state.player_list[1].remaining_time == 17

    asyncio.run(run())


@pytest.mark.parametrize("config,expected", [
    ({}, (20, 5)),
    ({"roundTimerValue": 40, "stepTimerValue": 10}, (40, 10)),
    ({"roundTimerValue": 0, "stepTimerValue": 8}, (0, 8)),
    ({"roundTimerValue": 0, "stepTimerValue": 0}, (0, 0)),
])
def test_room_request_save_and_initial_state_preserve_timers(clock, config, expected):
    from ...room.test_guangdong_room import manager
    from ...room.guangdong_room import handle_create_guangdong_room
    from ...game_calculation.guangdong.config import SUB_RULE
    from .GuangdongGameState import GuangdongGameState

    async def run():
        rooms = manager()
        websocket = Socket()
        await handle_create_guangdong_room(rooms.game_server, "conn",
            {"roomname": "广东计时", "sub_rule": SUB_RULE, **config}, websocket)
        assert websocket.messages[-1]["success"]
        room = rooms.rooms["gd-unit-room"]
        assert (room["round_timer"], room["step_timer"]) == expected
        room["player_list"] = [101, 102, 103, 104]
        state = GuangdongGameState(rooms.game_server, room, None, None, "gd-timing-room-test")
        assert (state.round_time, state.step_time) == expected
        assert all(player.remaining_time == expected[0] for player in state.player_list)

    asyncio.run(run())


@pytest.mark.parametrize("option", ["claim_protection"])
def test_mil_rejects_each_unsupported_claim_option(option):
    from ...room.guangdong_room import GuangdongMilRoomValidator

    config = dict(room_name="广东边界", round_timer=20, step_timer=8)
    allowed = GuangdongMilRoomValidator(**config)
    assert allowed.tactical_call is True and allowed.claim_protection is False
    with pytest.raises(ValueError, match="不支持此选项"):
        GuangdongMilRoomValidator(**config, **{option: True})
    state = make_state(round_timer=20, step_timer=8)
    assert state.tactical_call is False and state.claim_protection is False


def test_normal_zero_bank_claim_keeps_custom_step_and_no_recheck_flag(clock):
    async def run():
        state = state_for_claim(round_timer=0, step_timer=8)
        state.prepare_action_window()
        await boardcast.broadcast_ask_other_action(state)
        packet = state.game_server.user_id_to_connection[102].websocket.messages[-1]
        info = packet["ask_other_action_info"]
        assert (info["remaining_time"], info["step_remaining"]) == (0, 8)
        assert not info.get("is_tactical_recheck")
        clock.advance(6)  # Later than either a three- or five-second fixed cap.
        submit(state, 1)
        responses, _ = await collect(state)
        assert responses[1]["action_type"] == "pass"
        assert state.player_list[1].remaining_time == 0

    asyncio.run(run())


@pytest.mark.parametrize("status", ["waiting_action_after_cut", "waiting_action_qianggang", "waiting_hand_action"])
def test_exact_wire_clock_matches_fractional_bank_and_reconnect_deadline(clock, status):
    async def run():
        state = state_for_claim()
        state.game_status = status
        state.jiagang_tile = 11
        if status == "waiting_hand_action":
            state.current_player_index = 1
            state.action_dict[1] = ["cut"]
        player = state.player_list[1]
        player._guangdong_exact_bank, player.remaining_time = 18.2, 19
        state.prepare_action_window()
        broadcaster = boardcast.broadcast_ask_hand_action if status == "waiting_hand_action" else boardcast.broadcast_ask_other_action
        await broadcaster(state)
        name = "ask_hand_action_info" if status == "waiting_hand_action" else "ask_other_action_info"
        messages = state.game_server.user_id_to_connection[102].websocket.messages
        initial = messages[-1][name]
        assert (initial["remaining_time_ms"], initial["step_remaining_ms"]) == (18200, 5000)
        deadline = state._action_windows[1].deadline
        assert clock.now + (initial["remaining_time_ms"] + initial["step_remaining_ms"]) / 1000 == pytest.approx(deadline)
        for elapsed in (.35, 6.9):
            clock.advance(elapsed)
            await boardcast.reconnected_send_pending_ask_for_viewer(state, 102, 1)
            info = messages[-1][name]
            wire_deadline = clock.now + (info["remaining_time_ms"] + info["step_remaining_ms"]) / 1000
            assert wire_deadline == pytest.approx(deadline, abs=.001)
            assert player._guangdong_exact_bank == 18.2  # Snapshot does not debit the owner.
        assert info["step_remaining_ms"] == 0 and info["remaining_time_ms"] == 15950

    asyncio.run(run())


@pytest.mark.parametrize("override", [None, 1.25])
def test_common_tactical_grace_is_independent_and_preserves_bank(clock, override):
    from ..public.tactical_claim import TACTICAL_GRACE_SECONDS, tactical_grace_phase
    import math

    async def run():
        players = [SimpleNamespace(remaining_time=20) for _ in range(4)]
        state = SimpleNamespace(
            player_list=players, step_time=12, tactical_call=True, server_action_tick=8,
            action_priority={"pass": 0, "force_pass": 0, "chi_left": 1, "peng": 2},
            _tactical_action_snapshot={0: ["chi_left"], 1: ["peng"], 2: [], 3: []},
            _tactical_passed_players=set(), _tactical_force_passed_players=set(),
            _tactical_committed_players=set(),
            action_dict={0: ["chi_left"], 1: ["peng"], 2: [], 3: []},
            action_events={i: asyncio.Event() for i in range(4)},
            action_queues={i: asyncio.Queue() for i in range(4)},
        )
        if override is not None:
            state.tactical_grace_seconds = override
        duration = TACTICAL_GRACE_SECONDS if override is None else override
        asks = []

        async def ask(_state, **fields):
            asks.append(fields)

        async def unexpected_do(*args, **kwargs):
            raise AssertionError("Already announced application must not be announced again")

        async def expire_grace(tasks, return_when):
            clock.advance(duration)
            return {tasks[-1]}, set(tasks[:-1])

        with patch("asyncio.wait", side_effect=expire_grace):
            result = await tactical_grace_phase(
                state, "chi_left", 0, {"action_type": "chi_left"}, 33,
                broadcast_do_action=unexpected_do, broadcast_ask_other_action=ask,
                initial_claim_broadcasted=True,
            )
        assert asks == [{"remaining_time_override": math.ceil(duration), "is_tactical_recheck": True}]
        assert clock.now == 100 + duration  # Independent of room step12 and ordinary bank20.
        assert [player.remaining_time for player in players] == [20] * 4
        assert result[:2] == ("chi_left", 0)
        assert TACTICAL_GRACE_SECONDS == 5.0

    asyncio.run(run())
