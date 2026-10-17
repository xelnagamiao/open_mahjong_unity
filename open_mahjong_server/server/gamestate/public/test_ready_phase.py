"""One result acknowledgement must never ready another human seat early."""
import asyncio
import importlib
from contextlib import suppress
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from . import ask_timing, ready_phase
from .test_action_window import ADAPTERS, make_state, reply
from .lifecycle import cancel_auxiliary_tasks
from ..verifier.host import build_db_manager, build_game_server, build_room_data


async def until(predicate, task=None):
    async def poll():
        while not predicate():
            if task is not None and task.done():
                await task
                pytest.fail("ready phase ended while other humans were still pending")
            await asyncio.sleep(0)
    await asyncio.wait_for(poll(), .5)


@pytest.mark.parametrize("adapter", [ADAPTERS[1], ADAPTERS[2], ADAPTERS[5]],
                         ids=["qingque", "changsha", "sichuan"])
def test_late_single_ready_keeps_other_humans_pending(monkeypatch, adapter):
    """Exercise the real round-end callers, not just an isolated wait_action."""
    async def run():
        state, _, wait = make_state(adapter)
        rule, package, name = adapter
        module = importlib.import_module(f"server.gamestate.{package}.{name}")
        now = [1000.0]
        clock = SimpleNamespace(time=lambda: now[0])
        for target in (module, ask_timing, ready_phase):
            monkeypatch.setattr(target, "time", clock, raising=False)
        seen = []

        async def capture(payload):
            if "ready_status_info" in payload:
                seen.append(payload["ready_status_info"]["player_to_ready"])

        for conn in state.game_server.user_id_to_connection.values():
            conn.websocket.send_json = capture

        class NextRound(Exception):
            pass

        if rule == "sichuan":
            monkeypatch.setattr(module, "shuhewei_ready_wait_seconds", lambda *a: 10.0)
            operation = state._ready_phase(False)
        else:
            state.Debug = True
            state.current_round = 1
            state.max_round = 1
            state.broadcast_game_start = AsyncMock()
            state.broadcast_do_action = AsyncMock()
            state.broadcast_ask_hand_action = AsyncMock()
            state.refresh_waiting_tiles = Mock()

            async def finish_hand():
                if state.game_status == "waiting_ready":
                    return await wait(state)
                state.game_status = "END"
                state.hu_class = "hu_self"
                state.result_dict = {"hu_self": (8, [])}

            state.wait_action = finish_hand
            monkeypatch.setattr(module, "init_" + rule + "_tiles", lambda *_: None)
            monkeypatch.setattr(module, "check_action_hand_action", lambda *a, **kw: {})
            monkeypatch.setattr(module, "broadcast_result", AsyncMock())
            monkeypatch.setattr(module, "hu_result_ready_wait_seconds", lambda *a: 10.0)
            monkeypatch.setattr(module, "next_game_round_" + rule + "_switchseat",
                                Mock(side_effect=NextRound))
            if rule == "changsha":
                state._detect_initial_hu_types = Mock()
                state.initial_hu_types = {}
                state._score_changsha_win = Mock(return_value={"actual_hu_score": 24, "fan_display": []})
            operation = getattr(state, "game_loop_" + rule)()

        task = asyncio.create_task(operation)
        try:
            await until(lambda: bool(getattr(state, "_ask_delivered_at", {})), task)
            assert seen[-1] == {i: False for i in range(4)}
            now[0] += 6.0  # Past halfway, but still four seconds before expiry.
            await reply(state, 0, "ready", state.server_action_tick)
            await until(lambda: any(status[0] for status in seen), task)
            await asyncio.sleep(.01)
            assert not task.done(), "one ready prematurely completed the whole table"
            assert seen[-1] == {0: True, 1: False, 2: False, 3: False}
            assert all(state.action_dict[i] == ["ready"] for i in (1, 2, 3))
            # Each further confirmation changes only its own seat.
            for seat in (1, 2, 3):
                await reply(state, seat, "ready", state.server_action_tick)
                await until(lambda: any(status[seat] for status in seen), task)
            if rule == "sichuan":
                await asyncio.wait_for(task, .5)
            else:
                with pytest.raises(NextRound):
                    await asyncio.wait_for(task, .5)
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError, NextRound):
                await task
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


DRIVER_RULES = [
    ("game_zhongyong", "ZhongyongGameState"),
    ("game_zhongyong", "NanqueGameState"),
    ("game_jiandan", "JiandanGameState"),
    ("game_guizhou", "GuizhouGameState"),
    ("game_hangzhou", "HangzhouGameState"),
    ("game_fuzhou", "FuzhouGameState"),
    ("game_nanchang", "NanchangGameState"),
    ("game_nanjing", "NanjingGameState"),
    ("game_malaysia", "MalaysiaGameState"),
    ("game_yixing", "YixingGameState"),
    ("game_wenzhou", "WenzhouGameState"),
    ("game_vietnamese", "VietnameseGameState"),
    ("game_filipino", "FilipinoGameState"),
    ("game_hongkong", "HongKongGameState"),
]


@pytest.mark.parametrize("package,name", DRIVER_RULES, ids=[r[1] for r in DRIVER_RULES])
def test_driver_rule_waits_for_every_human(package, name):
    async def run():
        cls = getattr(importlib.import_module(f"server.gamestate.{package}.{name}"), name)
        state = cls(calculation_service=object())
        state.initialize_round()
        if hasattr(state, "machine"):
            state.machine.phase = type(state.machine.phase)("END")
        else:
            state.game_status = "END"
        state.game_server = build_game_server(build_db_manager(), messages=[])
        for player, uid in zip(state.player_list, state.game_server.user_id_to_connection):
            player.user_id = uid
            player.remaining_time = 20
        task = asyncio.create_task(state.run_round_ready_phase(timeout=10))
        try:
            await until(lambda: state.game_status == "waiting_ready", task)
            await state.submit_action(0, "ready")
            await until(lambda: state.action_queues[0].empty(), task)
            await asyncio.sleep(.01)
            assert not task.done()
            others = range(1, len(state.player_list))
            assert all("ready" in state.action_dict[i] for i in others)
            statuses = state.emit_ready_status_payloads()[0]["ready_status_info"]["player_to_ready"]
            await state.flush_outbound_payloads()
            assert all(not statuses[i] for i in others)
            for seat in others:
                await state.submit_action(seat, "ready")
            await asyncio.wait_for(task, .5)
            assert not any(state.action_dict.values())
            assert all(p.remaining_time == 20 for p in state.player_list)
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError):
                await task
            await state.cleanup_game_state()
    asyncio.run(run())


def test_malaysia_ready_during_first_broadcast_does_not_use_previous_turn_deadline():
    from ..game_malaysia.MalaysiaGameState import MalaysiaGameState

    async def run():
        state = MalaysiaGameState(calculation_service=object())
        state.initialize_round()
        state.machine.phase = type(state.machine.phase)("END")
        state._active_deadlines = {i: 0.0 for i in range(3)}
        state._received_at = {}
        state.game_server = build_game_server(build_db_manager(), messages=[])
        for player, uid in zip(state.player_list, state.game_server.user_id_to_connection):
            player.user_id = uid
        sent = False

        async def send(payload):
            nonlocal sent
            if "ready_status_info" in payload and not sent:
                sent = True
                await state.submit_action(0, "ready")

        state.game_server.user_id_to_connection[state.player_list[0].user_id].websocket.send_json = send
        task = asyncio.create_task(state.run_round_ready_phase(timeout=10))
        try:
            await until(lambda: not state.action_dict.get(0) and sent, task)
            assert not task.done()
            for seat in (1, 2):
                await state.submit_action(seat, "ready")
            await asyncio.wait_for(task, .5)
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError):
                await task
            await state.cleanup_game_state()
    asyncio.run(run())


TAIWAN_RULES = [
    ("game_changchun", "ChangchunGameState"),
    ("game_guangdong", "GuangdongGameState"),
    ("game_hongzhong", "HongzhongGameState"),
    ("game_jiujiang", "JiujiangGameState"),
    ("game_shanxi", "ShanxiGameState"),
    ("game_shanghai", "ShanghaiGameState"),
    ("game_shanghai", "QinghunpengGameState"),
    ("game_shanghai", "LajiheGameState"),
    ("game_shenyang", "ShenyangGameState"),
    ("game_singapore", "SingaporeGameState"),
    ("game_tuidao", "TuidaoGameState"),
    ("game_wuhan", "WuhanGameState"),
]


@pytest.mark.parametrize("package,name", TAIWAN_RULES, ids=[r[1] for r in TAIWAN_RULES])
def test_taiwan_derived_rule_waits_for_every_human(package, name):
    async def run():
        cls = getattr(importlib.import_module(f"server.gamestate.{package}.{name}"), name)
        db = build_db_manager()
        server = build_game_server(db, messages=[])
        room = build_room_data(seed=20261017, room_id="independent-ready", hepai_limit=0)
        room.pop("sub_rule", None)
        room["tips"] = False
        state = cls(server, room, server.calculation_service, db, "independent-ready")
        for seat, player in enumerate(state.player_list):
            player.player_index = player.original_player_index = seat
        task = asyncio.create_task(state.run_hu_result_ready_phase(0))
        try:
            await until(lambda: state.game_status == "waiting_ready", task)
            await reply(state, 0, "ready", state.server_action_tick)
            await until(lambda: state.action_queues[0].empty(), task)
            await asyncio.sleep(.01)
            assert not task.done()
            assert all("ready" in state.action_dict[i] for i in (1, 2, 3))
            for seat in (1, 2, 3):
                await reply(state, seat, "ready", state.server_action_tick)
            await asyncio.wait_for(task, .5)
            assert all(ready_phase.ready_status_by_seat(state).values())
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError):
                await task
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


def test_hongque_waits_for_every_human():
    from ..game_hongque.HongqueGameState import HongqueGameState

    async def run():
        db = build_db_manager()
        server = build_game_server(db, messages=[])
        room = build_room_data(seed=20261017, room_id="independent-ready", hepai_limit=0)
        state = HongqueGameState(server, room)
        state._ready_phase_active = True
        task = asyncio.create_task(state._wait_for_hu_ready(0))
        try:
            await state._handle_ready_action(state.players[0])
            await asyncio.sleep(.01)
            assert not task.done()
            assert state._ready_players == {0}
            for player in state.players[1:]:
                await state._handle_ready_action(player)
            await asyncio.wait_for(task, .5)
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError):
                await task
    asyncio.run(run())


@pytest.mark.parametrize("adapter", ADAPTERS, ids=[a[0] for a in ADAPTERS])
@pytest.mark.parametrize("with_bot", [False, True], ids=["four-humans", "with-bot"])
def test_ready_confirmation_only_affects_its_seat(monkeypatch, adapter, with_bot):
    async def run():
        state, broadcast, _ = make_state(adapter)
        if with_bot:
            state.player_list[3].user_id = 1
        now = [1000.0]
        clock = SimpleNamespace(time=lambda: now[0])
        monkeypatch.setattr(ask_timing, "time", clock)
        monkeypatch.setattr(ready_phase, "time", clock)
        # Taiwan builds monotonic deadlines from the same wall-clock origin.
        waiter_module = importlib.import_module(f"server.gamestate.{adapter[1]}.wait_action")
        if adapter[0] == "taiwan":
            monkeypatch.setattr(waiter_module, "time", SimpleNamespace(
                time=clock.time, monotonic=clock.time))
        task = asyncio.create_task(ready_phase.run_result_ready_phase(
            state, 10, broadcast.broadcast_ready_status))
        try:
            await until(lambda: state.game_status == "waiting_ready", task)
            await asyncio.sleep(0)
            now[0] += 6
            await reply(state, 0, "ready", state.server_action_tick)
            await until(lambda: state.action_queues[0].empty(), task)
            await asyncio.sleep(.01)
            assert not task.done()
            assert state.action_dict[1] == state.action_dict[2] == ["ready"]
            assert state.player_list[1].remaining_time == state.player_list[2].remaining_time == 10
            assert not ready_phase.ready_status_by_seat(state)[1]
            assert not ready_phase.ready_status_by_seat(state)[2]
            assert bool(state.action_dict[3]) is not with_bot
            # A duplicate from seat 0 must not acknowledge seats 1/2 either.
            await reply(state, 0, "ready", state.server_action_tick)
            for seat in range(1, 3 if with_bot else 4):
                await reply(state, seat, "ready", state.server_action_tick)
            await asyncio.wait_for(task, .5)
            assert all(ready_phase.ready_status_by_seat(state).values())
        finally:
            task.cancel()
            with suppress(asyncio.CancelledError):
                await task
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
