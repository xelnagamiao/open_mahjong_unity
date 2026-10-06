"""Room timing contract regressions, using server receipts and a controlled clock."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from . import action_timing as timing
from .test_shanghai_state import make_state, lock
from ...game_calculation.shanghai.test_qiaoma import PLAIN
from ..game_taiwan.boardcast import (
    broadcast_ask_hand_action, broadcast_ask_other_action,
    reconnected_send_pending_ask_for_viewer, send_realtime_spectator_snapshot,
)
from ..game_taiwan.wait_action import wait_action, _build_ask_deadlines
from ..public import ask_timing
from ..public.ai.get_action import get_action
from ...room.room_manager import RoomManager
from ...room.room_router import handle_create_Shanghai_room
from .ShanghaiGameState import ShanghaiGameState


class Clock:
    def __init__(self):
        self.now = 100.0
        self.wall_offset = 0.0

    def time(self):
        return self.now + self.wall_offset

    def monotonic(self):
        return self.now + 1000.0

    def advance(self, seconds):
        self.now += seconds


@pytest.fixture
def clock(monkeypatch):
    value = Clock()
    monkeypatch.setattr(timing, "time", value)
    monkeypatch.setattr(ask_timing, "time", value)
    return value


def game(clock, bank=20, step=5, status="waiting_action_after_cut", actions=None):
    state = make_state(round_timer=bank, step_timer=step)
    state.game_status = status
    state.action_dict = actions or {1: ["peng", "pass"]}
    state.player_list[0].discard_tiles = [11]
    state.prepare_action_window()
    state.server_action_tick = 7
    state._ask_broadcast_time = clock.time()
    state._ask_delivered_at = {i: clock.time() for i in state.action_dict}
    return state


def reply(state, index, action="pass", **data):
    state.action_queues[index].put_nowait({"action_type": action, "_action_tick": state.server_action_tick, **data})
    state.action_events[index].set()


async def auto_wait(clock, tasks, timeout, **_):
    clock.advance(timeout)
    await asyncio.sleep(0)
    return {task for task in tasks if task.done()}, {task for task in tasks if not task.done()}


@pytest.mark.parametrize("bank,step,elapsed,expected", [
    (20, 5, 2, 20), (20, 5, 8, 17), (13, 8, 11, 10),
    (0, 7, 6, 0), (9, 0, 3, 6),
])
@pytest.mark.parametrize("status,action", [
    ("waiting_action_after_cut", "peng"), ("waiting_action_qianggang", "hu_first"),
    ("waiting_hand_action", "riichi_cut"), ("waiting_buhua_round", "buhua"),
])
def test_all_actual_prompts_charge_step_then_room_bank(clock, bank, step, elapsed, expected, status, action):
    async def run():
        state = game(clock, bank, step, status, {1: [action, "pass"]})
        state.player_list[1].hand_tiles = PLAIN[:-1] + [29]
        clock.advance(elapsed)
        reply(state, 1, action, TileId=29)
        responses, allowed = await state.collect_action_responses()
        assert responses[1]["action_type"] == action
        assert allowed[1] == [action, "pass"]
        assert state.player_list[1].remaining_time == expected
        assert timing.bank(state.player_list[1]) == expected
        assert state.action_dict[1] == [] and state.waiting_players_list == []
        assert state._qiaoma_deadlines[1] == pytest.approx(1100 + bank + step)
    asyncio.run(run())


@pytest.mark.parametrize("bank,step", [(20, 5), (13, 8), (0, 7), (9, 0), (0, 0)])
def test_timeout_uses_entire_configured_budget_then_passes(clock, monkeypatch, bank, step):
    async def run():
        state = game(clock, bank, step)
        state.resolve_discard_responses = AsyncMock()
        monkeypatch.setattr(timing.asyncio, "wait", lambda tasks, **kw: auto_wait(clock, tasks, **kw))
        await wait_action(state)
        assert clock.time() == pytest.approx(100 + bank + step)
        assert state.player_list[1].remaining_time == 0
        state.resolve_discard_responses.assert_awaited_once_with({}, {1: ["peng", "pass"]})
    asyncio.run(run())


def test_fractional_overtime_is_not_forgiven_on_each_new_step(clock):
    async def run():
        state = game(clock)
        for _ in range(2):
            clock.advance(5.9)
            reply(state, 1)
            await state.collect_action_responses()
            state.action_dict = {1: ["peng", "pass"]}
            state.prepare_action_window()
            state.server_action_tick += 1
            state._ask_broadcast_time = clock.time()
            state._ask_delivered_at = {1: clock.time()}
        assert timing.bank(state.player_list[1]) == pytest.approx(18.2)
        assert state.player_list[1].remaining_time == 19  # ceil for the integer wire protocol
        assert state.claim_clock(state.player_list[1]) == (19, 5)
    asyncio.run(run())


@pytest.mark.parametrize("response_at,expected_bank", [(2, 20), (8, 17)])
def test_timely_queued_receipt_survives_late_collection_without_extra_charge(clock, response_at, expected_bank):
    async def run():
        state = game(clock)
        clock.advance(response_at)
        reply(state, 1, "peng")
        clock.advance(30)
        responses, _ = await state.collect_action_responses()
        assert responses[1]["action_type"] == "peng"
        assert state.player_list[1].remaining_time == expected_bank
    asyncio.run(run())


def test_late_receipt_and_stale_tick_cannot_revive_expired_window(clock, monkeypatch):
    async def run():
        state = game(clock, bank=0, step=5)
        reply(state, 1, "peng", _action_tick=state.server_action_tick - 1)
        clock.advance(5)
        reply(state, 1, "peng")
        responses, _ = await state.collect_action_responses()
        assert responses == {}
        assert state.player_list[1].remaining_time == 0
    asyncio.run(run())


@pytest.mark.parametrize("status,elapsed,expected", [
    ("waiting_action_after_cut", 3, (20, 2)),
    ("waiting_action_qianggang", 8, (17, 0)),
    ("waiting_hand_action", 8, (17, 0)),
    ("waiting_buhua_round", 4, (20, 1)),
])
def test_reconnect_packets_preserve_origin_and_do_not_debit(clock, status, elapsed, expected):
    async def run():
        state = game(clock, status=status)
        state.current_player_index = 1 if status in ("waiting_hand_action", "waiting_buhua_round") else 0
        state.jiagang_tile = 11
        socket = AsyncMock()
        state.game_server.user_id_to_connection[102] = SimpleNamespace(websocket=socket)
        original = dict(state._ask_delivered_at)
        clock.advance(elapsed)
        for _ in range(3):
            await reconnected_send_pending_ask_for_viewer(state, 102, 1)
            info = socket.send_json.await_args.args[0]
            info = info.get("ask_hand_action_info") or info["ask_other_action_info"]
            assert (info["remaining_time"], info["step_remaining"]) == expected
            assert info["action_tick"] == 7
            assert state._ask_delivered_at == original
            assert state.player_list[1].remaining_time == 20
        reply(state, 1)
        await state.collect_action_responses()
        assert state.player_list[1].remaining_time == expected[0]
    asyncio.run(run())


def test_reconnect_does_not_reask_or_charge_resolved_seat_while_other_seat_thinks(clock, monkeypatch):
    async def run():
        state = game(clock, actions={1: ["peng", "pass"], 2: ["peng", "pass"]})
        clock.advance(8)
        reply(state, 1)
        socket = AsyncMock()
        state.game_server.user_id_to_connection[102] = SimpleNamespace(websocket=socket)
        async def waiting(tasks, **_):
            assert state.player_list[1].remaining_time == 17
            assert state.waiting_players_list == [2]
            clock.advance(1)
            await reconnected_send_pending_ask_for_viewer(state, 102, 1)
            socket.send_json.assert_not_awaited()
            assert state.claim_clock(state.player_list[1], reconnecting=True) == (17, 0)
            reply(state, 2)
            await asyncio.sleep(0)
            return set(), set(tasks)
        monkeypatch.setattr(timing.asyncio, "wait", waiting)
        responses, _ = await state.collect_action_responses()
        assert set(responses) == {1, 2}
        assert state.player_list[1].remaining_time == 17
        assert state.player_list[2].remaining_time == 16
    asyncio.run(run())


def test_real_broadcast_delivery_and_human_enqueue_do_not_wait_for_other_sockets(clock):
    async def run():
        state = game(clock)
        state.game_server.players = {"client": SimpleNamespace(user_id=102)}
        delivered = []
        class Socket:
            def __init__(self, index):
                self.index = index
            async def send_json(self, payload):
                delivered.append((self.index, payload))
                if self.index == 1:
                    # Human replies before broadcast has completed every spectator.
                    clock.advance(2)
                    await get_action(state, "client", "peng", False, None, None, None,
                                     action_tick=state.server_action_tick)
        for index, player in enumerate(state.player_list):
            state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=Socket(index))
        async def slow_spectator(*_):
            clock.advance(30)
        state.send_to_realtime_spectators = slow_spectator
        await broadcast_ask_other_action(state)
        responses, _ = await state.collect_action_responses()
        assert responses[1]["action_type"] == "peng"
        assert state.player_list[1].remaining_time == 20
        info = delivered[0][1]["ask_other_action_info"]
        assert (info["remaining_time"], info["step_remaining"]) == (20, 5)
    asyncio.run(run())


@pytest.mark.parametrize("kind", ["chi_right", "peng"])
def test_claim_then_discard_gets_new_step_and_retains_only_remaining_bank(clock, kind):
    async def run():
        state = game(clock, actions={1: [kind, "pass"]})
        state.player_list[1].hand_tiles = [11, 11, 12, 13, 22, 23, 24, 25, 26, 27, 31, 32, 44]
        for player in state.player_list:
            player.opening_flowers_done = True
        clock.advance(8)
        reply(state, 1, kind)
        await wait_action(state)
        assert state.game_status == "onlycut_after_action"
        assert state.current_player_index == 1
        state.action_dict = state.after_claim_actions()
        state.game_status = "waiting_hand_action"
        state.prepare_action_window()
        socket = AsyncMock()
        state.game_server.user_id_to_connection[102] = SimpleNamespace(websocket=socket)
        await broadcast_ask_hand_action(state)
        info = socket.send_json.await_args.args[0]["ask_hand_action_info"]
        assert (info["remaining_time"], info["step_remaining"]) == (17, 5)
        clock.advance(4)
        reply(state, 1, "cut", TileId=44)
        await state.collect_action_responses()
        assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_timeout_locked_hand_discards_draw_and_next_round_restores_full_bank(clock, monkeypatch):
    async def run():
        state = game(clock, bank=20, step=5, status="waiting_hand_action", actions={0: ["cut"]})
        player = state.player_list[0]
        lock(player, PLAIN[:-1])
        player.hand_tiles.append(29)
        player.last_drawn_tile = 29
        player.has_draw_slot = True
        for p in state.player_list:
            p.opening_flowers_done = True
        monkeypatch.setattr(timing.asyncio, "wait", lambda tasks, **kw: auto_wait(clock, tasks, **kw))
        await wait_action(state)
        assert player.discard_tiles[-1] == 29
        assert player.hand_tiles == PLAIN[:-1]
        assert player.remaining_time == 0
        state._reset_hand_runtime()
        assert all(p.remaining_time == 20 and timing.bank(p) == 20 for p in state.player_list)
        assert state._qiaoma_deadlines == {}
    asyncio.run(run())


def test_flower_timeout_keeps_required_replacement_semantics(clock, monkeypatch):
    async def run():
        state = game(clock, bank=0, step=2, status="waiting_hand_action", actions={0: ["buhua"]})
        player = state.player_list[0]
        lock(player, PLAIN[:-1])
        player.hand_tiles.append(51)
        player.last_drawn_tile = 51
        player.has_draw_slot = True
        state.tiles_list = [11, 12, 13, 29, 46]
        monkeypatch.setattr(timing.asyncio, "wait", lambda tasks, **kw: auto_wait(clock, tasks, **kw))
        await wait_action(state)
        assert player.huapai_list == [51] and player.last_drawn_tile == 46
        state.prepare_action_window()
        socket = AsyncMock()
        state.game_server.user_id_to_connection[101] = SimpleNamespace(websocket=socket)
        await broadcast_ask_hand_action(state)
        info = socket.send_json.await_args.args[0]["ask_hand_action_info"]
        assert info["action_list"] == ["buhua"]
        assert (info["remaining_time"], info["step_remaining"]) == (0, 2)
    asyncio.run(run())


def test_forced_ready_win_is_preserved_on_timeout(clock, monkeypatch):
    async def run():
        state = game(clock, bank=0, step=0, status="waiting_hand_action", actions={0: ["hu_self"]})
        player = state.player_list[0]
        lock(player, PLAIN)
        accepted = []
        state.accept_self_draw = lambda index: accepted.append(index)
        await wait_action(state)
        assert accepted == [0]
    asyncio.run(run())


@pytest.mark.parametrize("locked", [False, True])
def test_invalid_ready_or_locked_discard_keeps_original_window(clock, monkeypatch, locked):
    async def run():
        state = game(clock, status="waiting_hand_action", actions={0: ["cut", "riichi_cut"]})
        player = state.player_list[0]
        player.hand_tiles = PLAIN[:-1] + [29]
        player.last_drawn_tile = 29
        player.has_draw_slot = True
        player.ready_locked = locked
        clock.advance(8)
        # 11 cannot declare this hand ready, nor can a locked hand discard it.
        reply(state, 0, "cut" if locked else "riichi_cut", TileId=11)
        async def waiting(tasks, **_):
            assert state.action_dict[0] == ["cut", "riichi_cut"]
            assert state.player_list[0].remaining_time == 20
            assert state.claim_clock(player, reconnecting=True) == (17, 0)
            clock.advance(1)
            reply(state, 0, "cut", TileId=29)
            await asyncio.sleep(0)
            return set(), set(tasks)
        monkeypatch.setattr(timing.asyncio, "wait", waiting)
        responses, _ = await state.collect_action_responses()
        assert responses[0]["TileId"] == 29
        assert player.remaining_time == 16
        assert state._qiaoma_deadlines[0] == 1125
    asyncio.run(run())


def test_spectator_pending_ask_projects_host_clock_without_reset(clock):
    async def run():
        state = game(clock)
        socket = AsyncMock()
        state.game_server.user_id_to_connection[501] = SimpleNamespace(websocket=socket)
        state.game_status = "waiting_hand_action"
        state.current_player_index = 1
        state.action_dict = {1: ["cut"]}
        state.server_action_tick = 8
        clock.advance(8)
        await send_realtime_spectator_snapshot(state, 501, 1)
        info = socket.send_json.await_args.args[0]["ask_hand_action_info"]
        assert (info["remaining_time"], info["step_remaining"]) == (17, 0)
        assert state._ask_delivered_at[1] == 100
        assert state.player_list[1].remaining_time == 20
    asyncio.run(run())


@pytest.mark.parametrize("bank,step", [(20, 5), (13, 8), (0, 5), (9, 0), (0, 0)])
def test_creation_request_room_storage_state_and_round_reset_preserve_custom_zero(bank, step):
    async def run():
        server = SimpleNamespace(
            players={"client": SimpleNamespace(user_id=101, username="host", current_room_id=None)},
            db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
            gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False),
            user_id_to_connection={}, match_manager=None,
        )
        manager = RoomManager(server)
        manager._broadcast_room_info = AsyncMock()
        server.create_Shanghai_room = manager.create_Shanghai_room
        socket = AsyncMock()
        await handle_create_Shanghai_room(server, "client", {
            "type": "room/create_Shanghai_room", "roomname": "上海计时", "gameround": 1,
            "password": "", "roundTimerValue": bank, "stepTimerValue": step,
            "tips": True, "sub_rule": "shanghai/qiaoma",
        }, socket)
        response = socket.send_json.await_args.args[0]
        assert response["success"]
        config = next(iter(manager.rooms.values()))
        assert (config["round_timer"], config["step_timer"]) == (bank, step)
        assert (response["room_info"]["round_timer"], response["room_info"]["step_timer"]) == (bank, step)
        config["player_list"] = [101, 102, 103, 104]
        state = ShanghaiGameState(server, config, None, None, "room-timing")
        assert (state.round_time, state.step_time) == (bank, step)
        assert all(player.remaining_time == bank for player in state.player_list)
        for player in state.player_list:
            timing.set_bank(player, 0)
        state._reset_hand_runtime()
        assert all(player.remaining_time == bank and timing.bank(player) == bank for player in state.player_list)
    asyncio.run(run())


def test_delivered_window_and_debit_use_monotonic_time_when_wall_clock_changes(clock):
    async def run():
        state = game(clock)
        state.on_action_window_delivered(1)
        clock.advance(8)
        clock.wall_offset = 3600
        assert state.claim_clock(state.player_list[1], reconnecting=True) == (17, 0)
        reply(state, 1)
        responses, _ = await state.collect_action_responses()
        assert responses[1]["action_type"] == "pass"
        assert state.player_list[1].remaining_time == 17
        assert state._qiaoma_deadlines[1] == 1125
    asyncio.run(run())


def test_initial_delivery_delay_does_not_consume_provisional_reconnect_budget(clock):
    async def run():
        state = game(clock)
        state._ask_delivered_at = {}
        clock.advance(4)
        assert state.claim_clock(state.player_list[1], reconnecting=True) == (20, 1)
        clock.advance(10)
        ask_timing.note_ask_delivered(state, 1)
        assert state.claim_clock(state.player_list[1], reconnecting=True) == (20, 5)
        clock.advance(2)
        ask_timing.note_ask_delivered(state, 1)  # repeated notification must not reset the origin
        reply(state, 1)
        responses, _ = await state.collect_action_responses()
        assert responses[1]["action_type"] == "pass"
        assert state.player_list[1].remaining_time == 20
        assert state._qiaoma_deadlines[1] == 1139
    asyncio.run(run())


@pytest.mark.parametrize("status", ["waiting_hand_action", "waiting_action_after_cut", "waiting_action_qianggang"])
def test_fractional_protocol_budget_matches_authoritative_deadline_without_ceil_grace(clock, status):
    async def run():
        actions = ["cut"] if status == "waiting_hand_action" else ["peng", "pass"]
        state = game(clock, status=status, actions={1: actions})
        state.current_player_index = 1 if status == "waiting_hand_action" else 0
        state.jiagang_tile = 11
        state.player_list[1].hand_tiles = PLAIN[:-1] + [29]
        timing.set_bank(state.player_list[1], 19.1)
        for player in state.player_list:
            state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=AsyncMock())
        state.prepare_action_window()
        if status == "waiting_hand_action":
            await broadcast_ask_hand_action(state)
        else:
            await broadcast_ask_other_action(state)
        socket = state.game_server.user_id_to_connection[102].websocket
        key = "ask_hand_action_info" if status == "waiting_hand_action" else "ask_other_action_info"
        initial = socket.send_json.await_args.args[0][key]
        assert initial["remaining_time_ms"] == 19100 and initial["step_remaining_ms"] == 5000
        clock.advance(4.5)
        await reconnected_send_pending_ask_for_viewer(state, 102, 1)
        info = socket.send_json.await_args.args[0][key]
        assert (info["remaining_time"], info["step_remaining"]) == (20, 1)
        assert (info["remaining_time_ms"], info["step_remaining_ms"]) == (19100, 500)
        reply(state, 1, "cut" if status == "waiting_hand_action" else "pass", TileId=29)
        await state.collect_action_responses()
        wire_remaining = (info["remaining_time_ms"] + info["step_remaining_ms"]) / 1000
        assert wire_remaining == pytest.approx(state._qiaoma_deadlines[1] - clock.monotonic(), abs=0.001)
        assert timing.bank(state.player_list[1]) == pytest.approx(19.1)
    asyncio.run(run())


@pytest.mark.parametrize("action", ["angang", "jiagang"])
def test_valid_kong_target_is_accepted_and_invalid_target_keeps_same_clock(clock, monkeypatch, action):
    async def run():
        state = game(clock, status="waiting_hand_action", actions={0: [action, "cut"]})
        player = state.player_list[0]
        player.hand_tiles = [11, 11, 11, 11, 12, 13, 21, 23, 25, 27, 31, 33, 35, 41]
        player.combination_tiles = [] if action == "angang" else ["k11"]
        clock.advance(8)
        reply(state, 0, action, target_tile=41)
        async def waiting(tasks, **_):
            assert state.action_dict[0] == [action, "cut"]
            clock.advance(1)
            reply(state, 0, action, target_tile=11)
            await asyncio.sleep(0)
            return set(), set(tasks)
        monkeypatch.setattr(timing.asyncio, "wait", waiting)
        responses, _ = await state.collect_action_responses()
        assert responses[0]["action_type"] == action and responses[0]["target_tile"] == 11
        assert player.remaining_time == 16 and state._qiaoma_deadlines[0] == 1125
    asyncio.run(run())


@pytest.mark.parametrize("hand_ask", [False, True])
def test_connected_spectators_receive_current_budget_without_mutating_player_packet(clock, hand_ask):
    async def run():
        state = game(clock, status="waiting_hand_action" if hand_ask else "waiting_action_after_cut",
                     actions={1: ["cut"] if hand_ask else ["peng", "pass"]})
        state.current_player_index = 1 if hand_ask else 0
        state.player_list[1].hand_tiles = PLAIN[:-1] + [29]
        state.realtime_spectators = [SimpleNamespace(user_id=i, host_user_id=102) for i in (501, 502)]
        for player in state.player_list:
            state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=AsyncMock())
        async def slow_first(_):
            clock.advance(8)
        first, second = AsyncMock(), AsyncMock()
        first.send_json.side_effect = slow_first
        state.game_server.user_id_to_connection.update({
            501: SimpleNamespace(websocket=first), 502: SimpleNamespace(websocket=second),
        })
        await (broadcast_ask_hand_action(state) if hand_ask else broadcast_ask_other_action(state))
        key = "ask_hand_action_info" if hand_ask else "ask_other_action_info"
        primary = state.game_server.user_id_to_connection[102].websocket.send_json.await_args.args[0][key]
        early, later = first.send_json.await_args.args[0][key], second.send_json.await_args.args[0][key]
        assert (primary["remaining_time_ms"], primary["step_remaining_ms"]) == (20000, 5000)
        assert (early["remaining_time_ms"], early["step_remaining_ms"]) == (20000, 5000)
        assert (later["remaining_time_ms"], later["step_remaining_ms"]) == (17000, 0)
        assert state.player_list[1].remaining_time == 20
        reply(state, 1, "cut" if hand_ask else "pass", TileId=29)
        await state.collect_action_responses()
        assert state.player_list[1].remaining_time == 17
    asyncio.run(run())


def test_resolved_spectator_forward_does_not_reopen_a_finished_ask(clock):
    async def run():
        state = game(clock)
        clock.advance(8)
        reply(state, 1)
        await state.collect_action_responses()
        packet = {"ask_other_action_info": {"action_tick": 7, "action_list": ["peng", "pass"]}}
        state._refresh_spectator_clock(1, packet)
        info = packet["ask_other_action_info"]
        assert info["action_list"] == []
        assert (info["remaining_time_ms"], info["step_remaining_ms"]) == (0, 0)
        assert timing.bank(state.player_list[1]) == 17
    asyncio.run(run())


def test_undelivered_fallback_window_ignores_wall_jump_and_reconnect_does_not_restart(clock):
    async def run():
        state = game(clock)
        ask_timing.begin_ask_round(state)
        clock.advance(8)
        clock.wall_offset = 3600
        assert state.claim_clock(state.player_list[1], reconnecting=True) == (17, 0)
        assert state._ask_delivered_at == {}
        reply(state, 1)
        await state.collect_action_responses()
        assert timing.bank(state.player_list[1]) == 17
        assert state._qiaoma_deadlines[1] == 1125
    asyncio.run(run())


@pytest.mark.parametrize("bank,step", [(20, 5), (0, 8)])
def test_shanghai_tactical_and_protection_are_fixed_off_and_normal_claim_keeps_room_budget(clock, bank, step):
    async def run():
        state = make_state(round_timer=bank, step_timer=step, tactical_call=True, claim_protection=True)
        assert state.tactical_call is False and state.claim_protection is False
        state.game_status = "waiting_action_after_cut"
        state.player_list[0].discard_tiles = [11]
        state.action_dict = {1: ["peng", "pass"]}
        state.prepare_action_window()
        socket = AsyncMock()
        state.game_server.user_id_to_connection[102] = SimpleNamespace(websocket=socket)
        await broadcast_ask_other_action(state)
        info = socket.send_json.await_args.args[0]["ask_other_action_info"]
        assert (info["remaining_time_ms"], info["step_remaining_ms"]) == (bank * 1000, step * 1000)
        assert not info.get("is_tactical_recheck", False)
        clock.advance(6)
        reply(state, 1)
        await state.collect_action_responses()
        assert timing.bank(state.player_list[1]) == max(0, bank - max(0, 6 - step))
    asyncio.run(run())
