"""Guizhou room clocks: exact bank accounting and delivery/receipt boundaries."""

import asyncio
from copy import deepcopy
from types import SimpleNamespace

import pytest

from . import clock as clock_module
from .actions import claim_actions
from .state_machine import Phase as P
from .test_flow import PLAIN, PUNGS, configured_turn, state


class ManualTime:
    def __init__(self):
        self.value = 100.0

    def monotonic(self):
        return self.value

    def advance(self, seconds):
        self.value += seconds


@pytest.fixture
def timer(monkeypatch):
    timer = ManualTime()
    # Do not patch the process-wide time module / asyncio's real event clock.
    monkeypatch.setattr(clock_module, "time", SimpleNamespace(monotonic=timer.monotonic))
    return timer


def turn(bank=20, step=5, hand=PLAIN):
    s = configured_turn(hand)
    s.round_time, s.step_time = bank, step
    for p in s.player_list:
        p.remaining_time = bank
    s.outbound_payloads.clear()
    s.outbound_send_cursor = 0
    s.open_action_window(s.begin_turn(0))
    return s


def claim(bank=20, step=5, count=2):
    s = turn(bank, step)
    p = s.player_list[1]
    p.hand_tiles = [31] * count + [11, 12, 14, 16, 18, 21, 24, 27, 33, 35, 39][:13-count]
    p.has_draw_slot = False
    s.player_list[0].discard_tiles.append(31)
    s.player_list[0].discard_origin_tiles.append(31)
    s.player_list[0].discard_riichi_flags.append(False)
    s.discard_log.append((0, 31))
    s.open_action_window(s._window(P.RESPONSE, 0, 31, claim_actions(s, 0, 31)))
    assert s.waiting_players_list == [1]
    return s


def begin(s, index=0):
    c = s.live_pending_window["_clocks"][index]
    c.start(s.step_time, s.player_list[index].remaining_time)
    return c


async def cut(s, index=0):
    p = s.player_list[index]
    await s.submit_action(index, "cut", TileId=p.hand_tiles[-1],
                          cutIndex=len(p.hand_tiles)-1, cutClass=p.has_draw_slot)


def test_fractional_overtime_is_not_refunded_on_each_operation(timer):
    async def run():
        s = turn(step=0)
        begin(s)
        timer.advance(.4)
        await cut(s)
        await s.wait_action()
        assert s.player_list[0].remaining_time == pytest.approx(19.6)
    asyncio.run(run())


def test_response_receipt_freezes_bank_before_delayed_waiter(timer):
    async def run():
        s = turn()
        begin(s)
        timer.advance(7.25)
        await cut(s)
        timer.advance(11)  # Sending other views/observers must not charge this player.
        await s.wait_action()
        assert s.player_list[0].remaining_time == pytest.approx(17.75)
    asyncio.run(run())


@pytest.mark.parametrize("elapsed,expected", [(4, 20), (24.9, .1)])
def test_valid_response_is_charged_at_ingress_before_server_validation_cost(timer, elapsed, expected):
    async def run():
        s = turn()
        begin(s)
        timer.advance(elapsed)
        validate = s.validate_response
        def slow_validate(*args):
            timer.advance(10)
            return validate(*args)
        s.validate_response = slow_validate
        await cut(s)
        await s.wait_action()
        assert s.player_list[0].remaining_time == pytest.approx(expected)
        assert s.live_pending_window["_clocks"][0].used() == pytest.approx(elapsed)
    asyncio.run(run())


def test_response_after_expiry_is_rejected_and_timeout_passes(timer):
    async def run():
        s = claim(bank=0, step=9)
        begin(s, 1)
        timer.advance(9.01)
        with pytest.raises(ValueError, match="超时"):
            await s.submit_action(1, "peng")
        response = await s.wait_action()
        assert response[1]["action_type"] == "pass"
        assert s.player_list[1].remaining_time == 0
    asyncio.run(run())


class Socket:
    def __init__(self, timer, delay=0):
        self.timer, self.delay = timer, delay
        self.sent = []

    async def send_json(self, payload):
        self.timer.advance(self.delay)
        await asyncio.sleep(0)
        self.sent.append((self.timer.value, deepcopy(payload)))


def connect(s, timer, delays=(0, 0, 0, 0)):
    sockets = [Socket(timer, delay) for delay in delays]
    s.game_server = SimpleNamespace(user_id_to_connection={
        p.user_id: SimpleNamespace(websocket=sockets[i]) for i, p in enumerate(s.player_list)
    })
    return sockets


def test_clock_starts_at_own_ask_delivery_before_other_views_and_observers(timer):
    async def run():
        s = turn()
        sockets = connect(s, timer, (2, 3, 3, 3))

        async def slow_observer(viewer, payload):
            timer.advance(4)
        s.send_to_realtime_spectators = slow_observer
        await s.flush_outbound_payloads()
        c = s.live_pending_window["_clocks"][0]
        own_sent_at, ask = sockets[0].sent[-1]
        assert c.started == own_sent_at
        assert ask["ask_hand_action_info"]["remaining_time"] == 20
        assert ask["ask_hand_action_info"]["step_remaining"] == 5
        assert c.remaining() == pytest.approx(max(0, 25-(timer.value-own_sent_at)))
    asyncio.run(run())


def test_simultaneous_initial_declarations_have_independent_delivery_deadlines(timer):
    async def run():
        s = state()
        s.round_time, s.step_time = 20, 5
        s.initialize_round()
        for i in (1, 2, 3):
            s.player_list[i].hand_tiles = PLAIN[:-1]
        s.open_action_window(s.opening_window())
        sockets = connect(s, timer, (0, 1, 2, 3))
        await s.flush_outbound_payloads()
        clocks = s.live_pending_window["_clocks"]
        assert [clocks[i].started for i in (1, 2, 3)] == [101, 103, 106]
        assert [clocks[i].remaining() for i in (1, 2, 3)] == [20, 22, 25]
        assert not sockets[0].sent
    asyncio.run(run())


def test_response_during_own_send_is_not_restarted_by_the_delivery_callback(timer):
    async def run():
        s = turn()
        sockets = connect(s, timer)
        async def immediate_response(payload):
            await cut(s)
            timer.advance(30)  # Simulated write completion after the response.
        sockets[0].send_json = immediate_response
        await s.flush_outbound_payloads()
        c = s.live_pending_window["_clocks"][0]
        assert c.started is None and c.used() == 0
        response = await s.wait_action()
        assert response[0]["action_type"] == "cut"
        assert s.player_list[0].remaining_time == 20
        assert c.started is None and c.used() == 0
    asyncio.run(run())


def test_concealed_kong_automatic_stage_does_not_add_a_player_response_clock(timer):
    async def run():
        hand = [11]*4 + [12,13,14,21,22,23,31,32,33,39]
        s = turn(hand=hand)
        previous = begin(s)
        timer.advance(6.4)
        await s.submit_action(0, "angang", target_tile=11)
        s.apply_action_results(s.live_pending_window, await s.wait_action())
        assert s.machine.phase == P.KONG and not s.waiting_players_list
        assert not s.live_pending_window["_clocks"]
        assert s.player_list[0].remaining_time == pytest.approx(18.6)
        s.apply_action_results(s.live_pending_window, await s.wait_action(timeout=0))
        assert s.machine.phase == P.TURN and s.player_list[0].after_kong
        c = s.live_pending_window["_clocks"][0]
        assert c is not previous and c.remaining() == pytest.approx(23.6)
    asyncio.run(run())


def test_reconnect_reprojects_after_game_start_send_without_restarting_clock(timer):
    async def run():
        s = turn()
        c = begin(s)
        original_start = c.started
        sockets = connect(s, timer, (4, 0, 0, 0))
        # Keep this probe about the snapshot's queue delay, not send transit.
        original_send = sockets[0].send_json

        async def send(payload):
            sockets[0].delay = 4 if payload["type"].endswith("game_start") else 0
            await original_send(payload)
        sockets[0].send_json = send
        await s.player_reconnect(s.player_list[0].user_id)
        ask = sockets[0].sent[-1][1]["ask_hand_action_info"]
        assert ask["step_remaining"] == 1
        assert ask["remaining_time"] == 20
        assert c.started == original_start
        assert c.remaining() == pytest.approx(21)
        assert s.player_list[0].remaining_time == 20
    asyncio.run(run())


def test_nonacting_dealer_does_not_receive_self_ask_at_initial_ready(timer):
    s = state()
    s.round_time, s.step_time = 20, 5
    s.initialize_round()
    for i in (1, 2, 3):
        s.player_list[i].hand_tiles = PLAIN[:-1]
    s.outbound_payloads.clear()
    s.open_action_window(s.opening_window())
    assert set(s.live_pending_window["_clocks"]) == {1, 2, 3}
    assert all(p["player_index"] != 0 for p in s.outbound_payloads)


@pytest.mark.parametrize("bank,step,elapsed,expected", [
    (20, 5, 3.9, 20), (20, 5, 8, 17),
    (12, 9, 10.1, 10.9), (0, 8, 7.9, 0), (6, 0, .65, 5.35),
])
def test_claim_uses_room_budget_instead_of_a_fixed_report_cap(timer, bank, step, elapsed, expected):
    async def run():
        s = claim(bank, step)
        c = begin(s, 1)
        timer.advance(elapsed)
        await s.submit_action(1, "pass")
        timer.advance(40)
        responses = await s.wait_action(timeout=3)
        assert responses[1]["action_type"] == "pass"
        assert s.player_list[1].remaining_time == pytest.approx(expected)
        assert c.used() == pytest.approx(elapsed)
    asyncio.run(run())


@pytest.mark.parametrize("bank,step", [(20, 5), (7, 11), (0, 9), (3, 0)])
def test_full_claim_budget_expires_to_pass_and_not_at_an_external_timeout(timer, bank, step):
    async def run():
        s = claim(bank, step)
        c = begin(s, 1)
        task = asyncio.create_task(s.wait_action(timeout=0))
        await asyncio.sleep(0)
        timer.advance(bank+step-.2)
        s.action_events[1].set()
        for _ in range(3):
            await asyncio.sleep(0)
        assert not task.done()
        assert c.remaining() == pytest.approx(.2)
        timer.advance(.201)
        s.action_events[1].set()
        responses = await asyncio.wait_for(task, 1)
        assert responses[1]["action_type"] == "pass"
        assert s.player_list[1].remaining_time == 0
    asyncio.run(run())


def test_fractional_overtime_accumulates_across_new_windows(timer):
    async def run():
        s = turn()
        for n in range(3):
            begin(s)
            timer.advance(5.4)
            await cut(s)
            await s.wait_action()
            if n < 2:
                s.open_action_window(s.begin_turn(0))
        assert s.player_list[0].remaining_time == pytest.approx(18.8)
    asyncio.run(run())


def test_ready_selection_cancel_share_one_budget_with_exact_bank_and_paused_delivery(timer):
    async def run():
        s = turn()
        s.player_list[0].discard_count = 0
        s.open_action_window(s.begin_turn(0))
        c = begin(s)
        timer.advance(3.2)
        await s.submit_action(0, "guizhou_ready")
        s.apply_action_results(s.live_pending_window, await s.wait_action())
        assert s.live_pending_window["_clocks"][0] is c
        assert c.display() == (20, 2)
        timer.advance(100)  # Selection presentation is not another thinking step.
        assert c.remaining() == pytest.approx(21.8)
        begin(s)
        timer.advance(2.9)
        await s.submit_action(0, "guizhou_ready_cancel")
        s.apply_action_results(s.live_pending_window, await s.wait_action())
        assert s.player_list[0].remaining_time == pytest.approx(18.9)
        assert s.live_pending_window["_clocks"][0] is c
        assert c.display() == (19, 0)
        begin(s)
        timer.advance(.3)
        await cut(s)
        await s.wait_action()
        assert s.player_list[0].remaining_time == pytest.approx(18.6)
    asyncio.run(run())


def test_pung_opens_fresh_discard_step_with_only_the_residual_bank(timer):
    async def run():
        s = claim()
        previous = begin(s, 1)
        timer.advance(6.4)
        await s.submit_action(1, "peng")
        s.apply_action_results(s.live_pending_window, await s.wait_action())
        assert s.machine.phase == P.DISCARD_ONLY
        assert s.player_list[1].remaining_time == pytest.approx(18.6)
        current = s.live_pending_window["_clocks"][1]
        assert current is not previous
        assert current.remaining() == pytest.approx(23.6)
        assert current.display() == (19, 5)
        assert s.action_dict[1] == ["cut"]
        begin(s, 1)
        timer.advance(4.8)
        await cut(s, 1)
        await s.wait_action()
        assert s.player_list[1].remaining_time == pytest.approx(18.6)
    asyncio.run(run())


def test_direct_kong_replacement_draw_has_a_new_step_not_the_claim_deadline(timer):
    async def run():
        s = claim(count=3)
        previous = begin(s, 1)
        timer.advance(7.2)
        await s.submit_action(1, "gang")
        s.apply_action_results(s.live_pending_window, await s.wait_action())
        assert s.machine.phase == P.TURN and s.player_list[1].after_kong
        assert s.player_list[1].has_draw_slot
        current = s.live_pending_window["_clocks"][1]
        assert current is not previous and current.display() == (18, 5)
        assert current.remaining() == pytest.approx(22.8)
    asyncio.run(run())


def test_expired_ready_selection_cannot_reopen_an_exhausted_clock(timer):
    async def run():
        s = turn(bank=0)
        s.player_list[0].discard_count = 0
        s.open_action_window(s.begin_turn(0))
        begin(s)
        timer.advance(4.9)
        await s.submit_action(0, "guizhou_ready")
        s.apply_action_results(s.live_pending_window, await s.wait_action())
        begin(s)
        timer.advance(.2)
        with pytest.raises(ValueError, match="超时"):
            await s.submit_action(0, "guizhou_ready_cancel")
        response = await s.wait_action()
        assert response[0]["action_type"] == "cut"
        assert response[0]["TileId"] in s.legal_discard_tiles(0)
        assert response[0]["is_timeout_action"]
    asyncio.run(run())


@pytest.mark.parametrize("declared", [False, True])
def test_turn_timeout_uses_legal_discard_and_clears_bank(timer, declared):
    async def run():
        s = turn(bank=2, step=9)
        if declared:
            s.player_list[0].ready_kind = "soft_ready"
            s.open_action_window(s.begin_turn(0))
        begin(s)
        timer.advance(11.01)
        response = (await s.wait_action())[0]
        assert response["action_type"] == "cut"
        assert response["TileId"] in s.legal_discard_tiles(0)
        assert response["cutIndex"] == 13 and response["cutClass"] is True
        assert response["is_timeout_action"]
        assert s.player_list[0].remaining_time == 0
    asyncio.run(run())


def test_rob_kong_mandatory_win_keeps_its_timeout_semantics_and_room_budget(timer):
    from .test_contracts import prepare_added_kong
    async def run():
        s = prepare_added_kong()
        s.step_time, s.round_time = 9, 4
        for p in s.player_list:
            p.remaining_time = 4
        s.apply_action_results(s.live_pending_window, {0: {"action_type": "jiagang", "target_tile": 29}})
        assert s.action_dict[1] == ["hu"]
        c = begin(s, 1)
        assert c.remaining() == 13
        timer.advance(3.5)
        assert c.display() == (4, 6)
        timer.advance(9.6)
        responses = await s.wait_action(timeout=3)
        assert responses[1]["action_type"] == "hu"
        assert s.player_list[1].remaining_time == 0
        s.apply_action_results(s.live_pending_window, responses)
        assert s.machine.phase == P.END
    asyncio.run(run())


def test_optional_ron_expiry_passes_instead_of_forcing_win(timer):
    async def run():
        s = turn(bank=1, step=7)
        p = s.player_list[1]
        p.hand_tiles, p.has_draw_slot = PUNGS[:-1], False
        s.open_action_window(s._window(P.RESPONSE, 0, 29, claim_actions(s, 0, 29)))
        assert s.action_dict[1] == ["hu", "pass"]
        begin(s, 1)
        timer.advance(8.1)
        responses = await s.wait_action()
        assert responses[1]["action_type"] == "pass"
        assert p.remaining_time == 0
    asyncio.run(run())


def test_multiple_claimants_are_debited_at_their_own_receipts_once(timer):
    async def run():
        s = turn()
        s.open_action_window(s._window(P.RESPONSE, 0, 31, {0: [], 1: ["pass"], 2: ["pass"], 3: []}))
        c1, c2 = begin(s, 1), begin(s, 2)
        timer.advance(6.25)
        await s.submit_action(1, "pass")
        timer.advance(4.5)
        await s.submit_action(2, "pass")
        await s.wait_action()
        assert s.player_list[1].remaining_time == pytest.approx(18.75)
        assert s.player_list[2].remaining_time == pytest.approx(14.25)
        assert s.player_list[0].remaining_time == s.player_list[3].remaining_time == 20
        assert c1.used() == pytest.approx(6.25) and c2.used() == pytest.approx(10.75)
    asyncio.run(run())


def test_completed_response_is_not_reasked_or_debited_by_reconnect_and_spectator(timer):
    async def run():
        s = claim()
        c = begin(s, 1)
        timer.advance(6.5)
        await s.submit_action(1, "pass")
        sockets = connect(s, timer)
        observer = Socket(timer)
        s.game_server.user_id_to_connection[9001] = SimpleNamespace(websocket=observer)
        for _ in range(2):
            await s.player_reconnect(s.player_list[1].user_id)
            await s.send_realtime_spectator_snapshot(9001, 1)
            timer.advance(8)
        assert all(not p[1].get("ask_other_action_info") for p in sockets[1].sent+observer.sent)
        assert s.player_list[1].remaining_time == pytest.approx(18.5)
        assert c.used() == pytest.approx(6.5)
        with pytest.raises(ValueError, match="重复"):
            await s.submit_action(1, "pass")
        await s.wait_action()
        assert s.player_list[1].remaining_time == pytest.approx(18.5)
        assert s.build_pending_action_payload(1) is None
    asyncio.run(run())


def test_active_snapshots_and_integer_wire_match_one_exact_deadline(timer):
    async def run():
        s = claim(bank=9.6, step=8)
        c = begin(s, 1)
        start = c.started
        timer.advance(10.3)
        expected = (8, 0)
        for viewer in range(4):
            payload = s.build_game_start_payload(viewer)
            assert payload["game_info"]["players_info"][1]["remaining_time"] == expected[0]
            assert type(payload["game_info"]["players_info"][1]["remaining_time"]) is int
        precise = s.build_pending_action_payload(1)["game_info"]["guizhou_info"]["action_clock"]
        assert precise == pytest.approx(dict(action_tick=s.server_action_tick, remaining_time=7.3, step_remaining=0))
        for _ in range(3):
            ask = s.build_pending_action_payload(1)["ask_other_action_info"]
            assert (ask["remaining_time"], ask["step_remaining"]) == expected
            restored = s.restore_payloads(1)
            assert len(restored) == 2 and restored[1]["ask_other_action_info"] == ask
            assert c.remaining() == pytest.approx(7.3)
            assert c.started == start and s.player_list[1].remaining_time == 9.6
        sockets = connect(s, timer)
        observer = Socket(timer)
        s.game_server.user_id_to_connection[9001] = SimpleNamespace(websocket=observer)
        await s.player_reconnect(s.player_list[1].user_id)
        await s.send_realtime_spectator_snapshot(9001, 1)
        assert sockets[1].sent[-1][1]["ask_other_action_info"] == observer.sent[-1][1]["ask_other_action_info"]
        assert c.started == start and c.remaining() == pytest.approx(7.3)
    asyncio.run(run())


def test_reconnect_in_viewer_fifo_refreshes_after_queue_delay(timer):
    from ..public.outbound_pipe import schedule_viewer_send
    async def run():
        s = turn()
        c = begin(s)
        sockets = connect(s, timer)
        stale = s.build_pending_action_payload(0)
        async def previous():
            timer.advance(7.4)
        schedule_viewer_send(s, 0, previous)
        await s.send_payload_to_player(0, stale)
        ask = sockets[0].sent[-1][1]["ask_hand_action_info"]
        assert (ask["remaining_time"], ask["step_remaining"]) == (18, 0)
        assert c.started == 100 and c.remaining() == pytest.approx(17.6)
    asyncio.run(run())


def test_stale_ask_cannot_start_the_next_window_clock(timer):
    async def run():
        s = turn()
        stale = deepcopy(s.outbound_payloads[0])
        s.open_action_window(s.begin_turn(0))
        c = s.live_pending_window["_clocks"][0]
        sockets = connect(s, timer)
        await s._deliver_claim_payload(0, stale)
        assert not sockets[0].sent and c.started is None and c.remaining() == 25
    asyncio.run(run())


def test_collector_reentry_keeps_the_original_window_start(timer):
    async def run():
        s = claim(bank=2, step=9)
        c = begin(s, 1)
        first = asyncio.create_task(s.wait_action())
        await asyncio.sleep(0)
        timer.advance(10.7)
        first.cancel()
        await asyncio.gather(first, return_exceptions=True)
        second = asyncio.create_task(s.wait_action())
        await asyncio.sleep(0)
        assert c.started == 100 and c.remaining() == pytest.approx(.3)
        timer.advance(.31)
        s.action_events[1].set()
        response = await asyncio.wait_for(second, 1)
        assert response[1]["action_type"] == "pass" and s.player_list[1].remaining_time == 0
    asyncio.run(run())


def test_failed_owner_delivery_uses_one_full_budget_fallback_not_enqueue_time(timer, caplog):
    async def run():
        s = turn()
        sockets = connect(s, timer)
        async def failed(payload):
            timer.advance(7)
            raise ConnectionError("expected failed Guizhou test delivery")
        sockets[0].send_json = failed
        await s.flush_outbound_payloads()
        c = s.live_pending_window["_clocks"][0]
        assert c.started is None and c.remaining() == 25
        task = asyncio.create_task(s.wait_action())
        await asyncio.sleep(0)
        assert c.started == 107 and c.remaining() == 25
        timer.advance(5.5)
        await cut(s)
        await task
        assert s.player_list[0].remaining_time == pytest.approx(19.5)
        assert any("outbound send failed viewer=0" in r.message for r in caplog.records)
    asyncio.run(run())


def test_offline_snapshot_fallback_does_not_start_or_reset_the_clock(timer):
    async def run():
        s = turn()
        payload = s.build_pending_action_payload(0)
        before = len(s.outbound_payloads)
        assert await s.send_payload_to_player(0, payload, record_fallback=False) is False
        assert len(s.outbound_payloads) == before
        assert await s.send_payload_to_player(0, payload) is False
        c = s.live_pending_window["_clocks"][0]
        assert c.started is None and c.remaining() == 25
        await s.flush_outbound_payloads()
        start = c.started
        timer.advance(6.4)
        await s.send_payload_to_player(0, s.build_pending_action_payload(0))
        await s.flush_outbound_payloads()
        assert c.started == start and c.remaining() == pytest.approx(18.6)
        with pytest.raises(ValueError, match="非法座位"):
            await s.send_payload_to_player(-1, {})
    asyncio.run(run())


@pytest.mark.parametrize("phase", [P.END, P.READY])
def test_ended_reconnect_and_spectator_restore_never_spend_or_restart_game_bank(timer, phase):
    async def run():
        s = turn()
        s.player_list[0].remaining_time = 7.6
        s.end_draw()
        if phase == P.READY:
            s.machine.transition(P.READY)
        sockets = connect(s, timer, (2, 0, 0, 0))
        observer = Socket(timer, 3)
        s.game_server.user_id_to_connection[9001] = SimpleNamespace(websocket=observer)
        await s.player_reconnect(s.player_list[0].user_id)
        await s.send_realtime_spectator_snapshot(9001, 0)
        expected = ["game_start", "show_result"] + (["ready_status"] if phase == P.READY else [])
        assert [p[1]["type"].rsplit("/",1)[-1] for p in sockets[0].sent] == expected
        assert [p[1]["type"].rsplit("/",1)[-1] for p in observer.sent] == expected
        assert s.player_list[0].remaining_time == 7.6
        assert not any(p[1].get("ask_hand_action_info") for p in sockets[0].sent+observer.sent)
        assert s.live_pending_window["_clocks"][0].started is None
    asyncio.run(run())


def test_explicit_result_ready_and_ready_timeout_do_not_spend_game_bank(timer):
    async def run():
        s = turn()
        s.player_list[0].remaining_time = 3.25
        s.end_draw()
        task = asyncio.create_task(s.run_round_ready_phase(.04))
        for _ in range(100):
            await asyncio.sleep(0)
            if s.machine.phase == P.READY and s.waiting_players_list:
                break
        await s.submit_action(0, "ready")
        await asyncio.wait_for(task, 1)
        assert [p.remaining_time for p in s.player_list] == [3.25, 20, 20, 20]
        assert s.live_pending_window is None and not s.waiting_players_list
    asyncio.run(run())


@pytest.mark.parametrize("bank", [20, 7, 0])
def test_next_hand_restores_configured_bank_after_dealer_rotation(bank):
    s = turn(bank, step=9)
    for i, p in enumerate(s.player_list):
        p.remaining_time = i * .3
    s.end_draw()
    s.machine.transition(P.READY)
    s.next_dealer = 2
    s.advance_round_after_ready()
    s.initialize_round()
    assert [p.remaining_time for p in s.player_list] == [bank] * 4
    assert s.live_pending_window is None
    assert s.step_time == 9 and s.round_time == bank


def test_initial_ready_uses_room_budget_and_dealer_is_not_charged(timer):
    async def run():
        s = state()
        s.round_time, s.step_time = 12, 9
        s.initialize_round()
        for i in (1, 2, 3):
            s.player_list[i].hand_tiles = PLAIN[:-1]
        s.open_action_window(s.opening_window())
        for i in (1, 2, 3):
            begin(s, i)
        timer.advance(10.25)
        for i in (1, 2, 3):
            await s.submit_action(i, "baoting_initial" if i == 1 else "pass")
        s.apply_action_results(s.live_pending_window, await s.wait_action(timeout=3))
        assert s.machine.phase == P.TURN and s.player_list[1].ready_kind == "hard_ready"
        assert [p.remaining_time for p in s.player_list] == pytest.approx([12, 10.75, 10.75, 10.75])
        assert s.live_pending_window["_clocks"][0].display() == (12, 9)
    asyncio.run(run())


def test_stale_request_repair_uses_delivery_clock_and_viewer_fifo(timer):
    from .get_action import handle_action
    from ..public.outbound_pipe import schedule_viewer_send
    async def run():
        s = turn()
        ws = Socket(timer, 2)
        server = SimpleNamespace(gamestate_manager=SimpleNamespace(get_game_state_by_gamestate_id=lambda _: s),
                                 players={"own": SimpleNamespace(user_id=101)})
        async def previous():
            timer.advance(6)
        schedule_viewer_send(s, 0, previous)
        await handle_action(server, "own", dict(gamestate_id=s.gamestate_id, action_tick=-1), ws)
        c = s.live_pending_window["_clocks"][0]
        assert c.started == timer.value == 108
        ask = ws.sent[-1][1]["ask_hand_action_info"]
        assert (ask["remaining_time"], ask["step_remaining"]) == (20, 5)
        assert c.remaining() == 25
    asyncio.run(run())


@pytest.mark.parametrize("options,expected", [
    ({}, (20, 5)), ({"roundTimerValue": 7, "stepTimerValue": 11}, (7, 11)),
    ({"roundTimerValue": 0, "stepTimerValue": 8}, (0, 8)),
])
def test_creation_saves_timers_and_game_initialization_uses_them(options, expected):
    from .GuizhouGameState import GuizhouGameState
    from .test_contracts import fake_room_manager
    from ...room.guizhou_room import create_guizhou_room
    async def run():
        manager, _ = fake_room_manager()
        response = await create_guizhou_room(manager, "c", room_name="timing", **options)
        assert response.success
        room = manager.rooms[123456]
        assert (room["round_timer"], room["step_timer"]) == expected
        room["player_list"] = [101, 102, 103, 104]
        s = GuizhouGameState(room_data=room, calculation_service=object())
        s.initialize_round()
        s.open_action_window(s.begin_turn(0))
        assert (s.round_time, s.step_time) == expected
        assert [p.remaining_time for p in s.player_list] == [expected[0]] * 4
        ask = s.build_pending_action_payload(0)["ask_hand_action_info"]
        assert (ask["remaining_time"], ask["step_remaining"]) == expected
    asyncio.run(run())


@pytest.mark.parametrize("fields,expected", [
    ({}, (20, 5)), ({"roundTimerValue": 0, "stepTimerValue": 11}, (0, 11)),
])
def test_creation_request_handler_preserves_defaults_and_explicit_zero(fields, expected):
    from unittest.mock import AsyncMock
    from ...room.guizhou_room import handle_create_guizhou_room
    async def run():
        create = AsyncMock(return_value=SimpleNamespace(model_dump=lambda **_: {"success": True}))
        server = SimpleNamespace(room_manager=SimpleNamespace(create_Guizhou_room=create))
        await handle_create_guizhou_room(server, "c", {"roomname": "timing", **fields},
                                        SimpleNamespace(send_json=AsyncMock()))
        assert (create.call_args.kwargs["roundTimerValue"], create.call_args.kwargs["stepTimerValue"]) == expected
    asyncio.run(run())
