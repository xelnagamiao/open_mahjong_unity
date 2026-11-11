"""真实时间通过真实广播/路由/队列执行；虚拟时钟不更改配置秒数。"""
import asyncio
from types import SimpleNamespace

import pytest

from . import real_time, boardcast
from .DuplicateGameState import SbrDuplicateGameState, XueliuDuplicateGameState
from .test_duplicate import pair
from .test_action_window import submit
from .duplicate_wall import init_duplicate_board
from ...game_calculation.sichuan.duplicate_rules import make_board
from .wait_action import wait_action
from ..public.game_record_manager import init_game_record, init_game_round
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer
from ..verifier.host import build_db_manager, build_game_server, build_room_data, CaptureSocket


SUBRULES = ("sichuan/doubles_duplicate", "sichuan/xueliu_doubles_duplicate")


def make_state(sub_rule, *, enabled=True, tactical=False, seconds=5):
    messages = []
    db = build_db_manager()
    server = build_game_server(db, messages=messages)
    match, _ = pair(sub_rule, count=1)
    server.gamestate_manager.sichuan_duplicate_matches = {"timing": match}
    room = build_room_data(seed=17, room_id="12345")
    room.update(room_rule="sichuan", sub_rule=sub_rule, team_by_seat=[0, 1, 0, 1],
                duplicate_match_id="timing", duplicate_table="open", real_time=enabled,
                tactical_call=tactical, tactical_grace_seconds=seconds, tactical_pre_grace_delay=0,
                claim_protection=False, step_timer=5, round_timer=20)
    engine = SbrDuplicateGameState if sub_rule == SUBRULES[0] else XueliuDuplicateGameState
    state = engine(server, room, server.calculation_service, db, "timing")
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
    state._reset_round_state()
    init_duplicate_board(state, make_board("clock", 1, layout="xueliu" if sub_rule == SUBRULES[1] else "standard"))
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
        player.hand_tiles = [15, 15, 15, 11, 12, 13, 21, 22, 23, 24, 25, 26, 29]
        player.remaining_time = 20
    state.current_player_index = 0
    state.player_list[0].discard_tiles = [15]
    state.game_status = "waiting_action_after_cut"
    state.action_dict = {0: [], 1: ["hu", "pass"], 2: ["peng", "gang", "hu", "pass"], 3: []}
    state.sichuan_hu_results = {seat: dict(fan=1, fan_list=["基本胡"], hepai_tile=15) for seat in (1, 2)}
    init_game_record(state)
    state._record_rule_profile()
    init_game_round(state)
    server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    return state, messages


async def wait_until(predicate):
    for _ in range(300):
        if predicate():
            return
        await asyncio.sleep(0)
    assert predicate(), "expected state transition was not reached"


async def broadcast(state):
    await state.broadcast_ask_other_action()
    await asyncio.gather(*(drain_viewer(state, seat) for seat in range(4)))


def wake(state):
    seat = next(iter(state.waiting_players_list))
    state.action_queues[seat].put_nowait({"action_type": "invalid", "_action_tick": state.server_action_tick})
    state.action_events[seat].set()


@pytest.fixture
def clock(monkeypatch):
    value = SimpleNamespace(now=100.0)
    monkeypatch.setattr(real_time, "time", SimpleNamespace(monotonic=lambda: value.now))
    return value


async def cleanup(state, task=None):
    if task is not None and not task.done():
        task.cancel()
        await asyncio.gather(task, return_exceptions=True)
    await cancel_auxiliary_tasks(state)


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("action", ["peng", "gang", "hu"])
def test_soft_expiry_keeps_real_routed_claim_and_does_not_charge_bank(clock, sub_rule, action):
    async def run():
        state, messages = make_state(sub_rule)
        task = None
        try:
            await broadcast(state)
            asks = {entry["user_id"]: entry["message"]["ask_other_action_info"] for entry in messages
                    if entry["message"].get("ask_other_action_info")}
            assert asks[state.player_list[1].user_id].get("claim_wait_mode") is None
            assert asks[state.player_list[2].user_id]["claim_seconds_remaining"] == 5
            assert asks[state.player_list[2].user_id]["step_remaining"] == 0
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += 5
            wake(state)
            await asyncio.sleep(0)
            assert not task.done()
            assert real_time.claim_clock_fields(state, 2)["claim_wait_mode"] == "waiting_draw"
            assert state.action_dict[2] == ["peng", "gang", "hu", "pass"]
            clock.now += 3
            await submit(state, 2, action)
            await wait_until(lambda: not state.action_dict[2])
            assert not task.done(), "the next player's normal clock must continue"
            assert state.player_list[2].remaining_time == 20
            clock.now += 1
            await submit(state, 1, "pass")
            await asyncio.wait_for(task, 2)
            assert state.player_list[1].remaining_time == 16
            assert state.player_list[2].remaining_time == 20
            assert state.waiting_players_list == []
            assert not any(state.action_dict.values())
            if action == "hu":
                assert state.game_status == "settle_win"
                assert set(state.sichuan_hu_results) == {2}
            else:
                assert state.player_list[2].combination_tiles == ["k15" if action == "peng" else "g15"]
            await submit(state, 2, action)
            assert state.action_queues[2].empty(), "a late packet cannot act on the old discard"
        finally:
            await cleanup(state, task)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("seconds", [3, 5, 8, 10, 15, 20])
def test_only_short_claims_expire_without_charging_or_waiting_forever(clock, sub_rule, seconds):
    async def run():
        state, _ = make_state(sub_rule, seconds=seconds)
        state.action_dict[1] = []
        state.sichuan_hu_results = {}
        task = None
        try:
            await broadcast(state)
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += seconds - .01
            wake(state)
            await asyncio.sleep(0)
            assert not task.done()
            clock.now += .01
            wake(state)
            await asyncio.wait_for(task, 2)
            assert state.game_status == "deal_card"
            assert state.player_list[2].remaining_time == 20
            assert not any(state.action_dict.values())
            await submit(state, 2, "peng")
            assert state.action_queues[2].empty()
        finally:
            await cleanup(state, task)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
def test_reconnect_restores_partial_short_time_and_soft_wait(clock, sub_rule):
    async def run():
        state, messages = make_state(sub_rule)
        try:
            await broadcast(state)
            tick = state.server_action_tick
            clock.now += 2.25
            for passed, expected_mode, expected_seconds in ((0, "real_time", 2.75), (4, "waiting_draw", 0)):
                clock.now += passed
                await boardcast.reconnected_send_pending_ask(state, state.player_list[2].user_id)
                info = messages[-1]["message"]["ask_other_action_info"]
                assert info["action_tick"] == tick
                assert info["claim_wait_mode"] == expected_mode
                assert info["claim_seconds_remaining"] == expected_seconds
                assert info["action_list"] == ["peng", "gang", "hu", "pass"]
                assert info["step_remaining"] == 0
                assert state.player_list[2].remaining_time == 20
        finally:
            await cleanup(state)
    asyncio.run(run())


def test_retired_and_empty_wall_seats_are_skipped_for_normal_clock(clock):
    async def run():
        state, _ = make_state(SUBRULES[0])
        state.player_list[1].is_hu = True
        state.action_dict[1] = []
        try:
            await broadcast(state)
            assert state._next_active_index(0) == 2
            assert not state._real_time_claim_clocks[2].short
            assert real_time.claim_clock_fields(state, 2) == {"remaining_time": 20, "step_remaining": 5}
        finally:
            await cleanup(state)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
def test_two_hu_responses_include_soft_waiting_winner(clock, sub_rule):
    async def run():
        state, _ = make_state(sub_rule)
        task = None
        try:
            await broadcast(state)
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += 8
            await submit(state, 2, "hu")
            await submit(state, 1, "hu")
            await asyncio.wait_for(task, 2)
            assert set(state.sichuan_hu_results) == {1, 2}
            assert state.game_status == "settle_win"
        finally:
            await cleanup(state, task)
    asyncio.run(run())


@pytest.mark.parametrize("receipt_at,accepted", [(24.9, True), (25.1, False)])
def test_normal_deadline_uses_receipt_not_collector_time(clock, receipt_at, accepted):
    async def run():
        state, _ = make_state(SUBRULES[0])
        state.action_dict[2] = []
        try:
            await broadcast(state)
            clock.now += receipt_at
            await submit(state, 1, "hu")
            clock.now += 5
            await asyncio.wait_for(wait_action(state), 2)
            assert state.game_status == ("settle_win" if accepted else "deal_card")
            assert state.player_list[1].remaining_time == (1 if accepted else 0)
        finally:
            await cleanup(state)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
def test_disabled_and_robbed_kong_do_not_enter_real_time(clock, sub_rule):
    async def run():
        for enabled, status in ((False, "waiting_action_after_cut"), (True, "waiting_action_qianggang")):
            state, messages = make_state(sub_rule, enabled=enabled)
            state.game_status = status
            state.jiagang_tile = 15
            try:
                await broadcast(state)
                assert not state._real_time_claim_clocks
                asks = [entry["message"]["ask_other_action_info"] for entry in messages
                        if entry["message"].get("ask_other_action_info")]
                assert all(info.get("claim_wait_mode") is None for info in asks)
                assert state.tactical_grace_seconds == 5
                assert state.game_record["game_title"]["real_time"] is enabled
            finally:
                await cleanup(state)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("response", ["hu", "timeout"])
def test_tactical_recheck_uses_fresh_hard_deadline_and_reconnect(clock, sub_rule, response):
    async def run():
        state, messages = make_state(sub_rule, tactical=True, seconds=8)
        state.action_dict[2] = ["peng", "gang", "pass"]
        state.sichuan_hu_results.pop(2)
        task = None
        try:
            await broadcast(state)
            opening_tick = state.server_action_tick
            # 普通过牌仍可在后续抢断再问；短时已经过期的碰仍有效。
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += 10
            await submit(state, 2, "peng")
            await submit(state, 1, "pass")
            await wait_until(lambda: state.server_action_tick == opening_tick + 1)
            await asyncio.gather(*(drain_viewer(state, seat) for seat in range(4)))
            await wait_until(lambda: state._real_time_claim_clocks[1].delivered is not None)
            clock.now += 3
            await boardcast.reconnected_send_pending_ask(state, state.player_list[1].user_id)
            info = messages[-1]["message"]["ask_other_action_info"]
            assert info["remaining_time"] == 5 and info["step_remaining"] == 0
            assert info["is_tactical_recheck"] is True
            assert info.get("claim_wait_mode") is None
            assert info["action_list"] == ["hu", "pass"]
            old_bank = state.player_list[1].remaining_time
            if response == "hu":
                await submit(state, 1, "hu", tick=opening_tick)
                assert state.action_queues[1].empty(), "opening-tick replies cannot replace a recheck"
                await submit(state, 1, "hu")
            else:
                clock.now += 5
                # 精确硬截止后不可用旧按钮继续拖住已申请的碰牌。
                await submit(state, 1, "hu")
                assert state.action_queues[1].empty()
                wake(state)
            await asyncio.wait_for(task, 2)
            assert state.player_list[1].remaining_time == old_bank
            assert state.player_list[2].remaining_time == 20
            assert state.game_status == ("settle_win" if response == "hu" else "onlycut_after_action")
            assert not any(state.action_dict.values())
        finally:
            await cleanup(state, task)
    asyncio.run(run())


@pytest.mark.parametrize("action", ["pass", "force_pass"])
def test_explicit_pass_ends_soft_claim_and_force_pass_prevents_recheck(clock, action):
    async def run():
        state, _ = make_state(SUBRULES[0], tactical=True)
        state.action_dict[2].append("force_pass")
        task = None
        try:
            await broadcast(state)
            opening_tick = state.server_action_tick
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += 7
            await submit(state, 2, action)
            await wait_until(lambda: not state.action_dict[2])
            await submit(state, 2, "hu")
            assert state.action_queues[2].empty()
            await submit(state, 1, "hu")
            await asyncio.wait_for(task, 2)
            assert state.server_action_tick == opening_tick
            assert set(state.sichuan_hu_results) == {1}
            assert state.player_list[2].remaining_time == 20
        finally:
            await cleanup(state, task)
    asyncio.run(run())


def test_bloodflow_required_hu_remains_automatic(clock):
    async def run():
        state, _ = make_state(SUBRULES[1])
        state.action_dict = {0: [], 1: ["hu"], 2: [], 3: []}
        state.player_list[1].has_won = state.player_list[1].post_hu_lock = True
        state.sichuan_hu_results = {1: state.sichuan_hu_results[1]}
        try:
            await broadcast(state)
            await asyncio.wait_for(wait_action(state), 2)
            assert state.game_status == "settle_win"
            assert set(state.sichuan_hu_results) == {1}
            assert state.player_list[1].remaining_time == 20
        finally:
            await cleanup(state)
    asyncio.run(run())


def test_each_short_clock_starts_on_its_own_delivery(clock):
    async def run():
        state, _ = make_state(SUBRULES[0])
        state.action_dict[3] = ["hu", "pass"]
        release = asyncio.Event()
        socket = state.game_server.user_id_to_connection[state.player_list[3].user_id].websocket
        send = socket.send_json

        async def delayed(payload):
            await release.wait()
            await send(payload)

        socket.send_json = delayed
        try:
            await state.broadcast_ask_other_action()
            await drain_viewer(state, 2)
            clock.now += 4
            assert real_time.claim_clock_fields(state, 2)["claim_seconds_remaining"] == 1
            assert real_time.claim_clock_fields(state, 3)["claim_seconds_remaining"] == 5
            release.set()
            await drain_viewer(state, 3)
            clock.now += 2
            assert real_time.claim_clock_fields(state, 2)["claim_wait_mode"] == "waiting_draw"
            assert real_time.claim_clock_fields(state, 3)["claim_seconds_remaining"] == 3
        finally:
            release.set()
            await cleanup(state)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
def test_late_peng_immediately_interrupts_next_players_normal_clock(clock, sub_rule):
    async def run():
        state, messages = make_state(sub_rule, tactical=True)
        state.action_dict[2] = ["peng", "pass"]
        state.sichuan_hu_results.pop(2)
        task = None
        try:
            await broadcast(state)
            initial_tick = state.server_action_tick
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += 8
            await submit(state, 2, "peng")
            await wait_until(lambda: state.server_action_tick == initial_tick + 1)
            await drain_viewer(state, 1)
            assert state.player_list[1].remaining_time == 17
            assert real_time.claim_clock_fields(state, 1)["remaining_time"] == 5
            assert real_time.claim_clock_fields(state, 1)["is_tactical_recheck"]
            assert not task.done()
            clock.now += 5
            wake(state)
            await asyncio.wait_for(task, 2)
            assert state.player_list[1].remaining_time == 17
            assert state.game_status == "onlycut_after_action"
        finally:
            await cleanup(state, task)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("first,second", [(1, 2), (2, 1)])
def test_first_hu_gives_other_winners_full_recheck_regardless_of_seat(clock, sub_rule, first, second):
    async def run():
        state, _ = make_state(sub_rule, tactical=True)
        task = None
        try:
            await broadcast(state)
            initial_tick = state.server_action_tick
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += 2
            await submit(state, first, "hu")
            await wait_until(lambda: state.server_action_tick == initial_tick + 1)
            await drain_viewer(state, second)
            assert state.action_dict[second] == ["hu", "pass"]
            assert real_time.claim_clock_fields(state, second)["remaining_time"] == 5
            await submit(state, second, "hu")
            await asyncio.wait_for(task, 2)
            assert state.game_status == "settle_win"
            assert set(state.sichuan_hu_results) == {1, 2}
            assert state.server_action_tick == initial_tick + 1, "equal hu must not restart another deadline"
        finally:
            await cleanup(state, task)
    asyncio.run(run())


@pytest.mark.parametrize("sub_rule", SUBRULES)
def test_reply_during_claim_broadcast_is_not_lost_when_tick_changes(clock, sub_rule):
    async def run():
        state, _ = make_state(sub_rule, tactical=True)
        state.action_dict[2] = ["peng", "pass"]
        state.sichuan_hu_results.pop(2)
        socket = state.game_server.user_id_to_connection[state.player_list[1].user_id].websocket
        original = socket.send_json
        received = []

        async def send(payload):
            await original(payload)
            info = payload.get("do_action_info")
            if info and info.get("is_claim") and info.get("action_list") == ["peng"]:
                received.append(state.server_action_tick)
                await submit(state, 1, "hu", tick=initial_tick)

        socket.send_json = send
        task = None
        try:
            await broadcast(state)
            initial_tick = state.server_action_tick
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += 8
            await submit(state, 2, "peng")
            await asyncio.wait_for(task, 2)
            assert received == [initial_tick]
            assert state.game_status == "settle_win"
            assert set(state.sichuan_hu_results) == {1}
            assert state.player_list[1].remaining_time == 17
            assert state.player_list[2].combination_tiles == []
        finally:
            await cleanup(state, task)
    asyncio.run(run())


def test_player_reconnect_starts_undelivered_clock_once_but_spectator_does_not(clock):
    async def run():
        state, messages = make_state(SUBRULES[0])
        release = asyncio.Event()
        player = state.player_list[2]
        conn = state.game_server.user_id_to_connection[player.user_id]
        old_socket = conn.websocket
        original = old_socket.send_json

        async def blocked(payload):
            await release.wait()
            await original(payload)

        old_socket.send_json = blocked
        try:
            await state.broadcast_ask_other_action()
            await asyncio.sleep(0)
            assert state._real_time_claim_clocks[2].delivered is None
            clock.now += 1
            spectator_id = 999
            state.game_server.user_id_to_connection[spectator_id] = SimpleNamespace(websocket=CaptureSocket(spectator_id, messages))
            await boardcast.reconnected_send_pending_ask_for_viewer(state, spectator_id, 2)
            assert state._real_time_claim_clocks[2].delivered is None
            conn.websocket = CaptureSocket(player.user_id, messages)
            await boardcast.reconnected_send_pending_ask(state, player.user_id)
            assert state._real_time_claim_clocks[2].delivered == 101
            clock.now += 3
            assert real_time.claim_clock_fields(state, 2)["claim_seconds_remaining"] == 2
            release.set()
            await drain_viewer(state, 2)
            await boardcast.reconnected_send_pending_ask(state, player.user_id)
            assert state._real_time_claim_clocks[2].delivered == 101
        finally:
            release.set()
            await cleanup(state)
    asyncio.run(run())


@pytest.mark.parametrize("offline", [False, True])
def test_slow_bot_or_offline_recheck_has_a_hard_deadline(clock, monkeypatch, offline):
    async def run():
        state, _ = make_state(SUBRULES[0], tactical=True)
        state.action_dict[2] = ["peng", "pass"]
        state.sichuan_hu_results.pop(2)
        if offline:
            state.player_list[1].tag_list.append("offline")
        else:
            state.player_list[1].user_id = 2
        release = asyncio.Event()

        async def slow(*args):
            await release.wait()

        monkeypatch.setattr(boardcast, "offline_auto_action" if offline else "smart_bot_action", slow)
        task = None
        try:
            await broadcast(state)
            opening_tick = state.server_action_tick
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(0)
            clock.now += 8
            await submit(state, 2, "peng")
            await wait_until(lambda: state.server_action_tick == opening_tick + 1
                             and state._real_time_claim_clocks[1].delivered is not None)
            clock.now += 5
            wake(state)
            await asyncio.wait_for(task, 2)
            assert state.game_status == "onlycut_after_action"
            assert state.player_list[1].remaining_time == 17
        finally:
            release.set()
            await cleanup(state, task)
    asyncio.run(run())
