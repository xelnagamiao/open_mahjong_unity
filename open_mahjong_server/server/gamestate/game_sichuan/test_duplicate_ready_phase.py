"""Local ready may skip a board timeline, never the paired-board comparison."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from . import DuplicateGameState as duplicate_module
from .test_action_window import submit
from .test_duplicate_active_huazhu import make_state
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer


async def prepare_table(table="closed", synchronous=True):
    state, messages, match = make_state(table)
    state.duplicate_waiting_for = None
    match.data["sync_mode"] = synchronous
    state.game_server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    with patch("asyncio.sleep", new_callable=AsyncMock):
        await state._settle_liuju()
    state._record_team_round()
    return state, messages, match


async def wait_until(predicate):
    for _ in range(200):
        if predicate():
            return
        await asyncio.sleep(.001)
    assert predicate(), "expected state transition was not reached"


async def ready_everyone(state):
    await wait_until(lambda: state.game_status == "waiting_ready")
    for seat in range(4):
        await submit(state, seat, "ready", tick=0)
    await asyncio.gather(*(drain_viewer(state, seat) for seat in range(4)))


@pytest.mark.parametrize("table", ["open", "closed"])
def test_late_table_ready_cannot_skip_its_own_comparison(monkeypatch, table):
    async def run():
        state, messages, match = await prepare_table(table)
        other = "closed" if table == "open" else "open"
        match.board_ready[1] = {other}
        release = asyncio.Event()
        sleeps = []

        async def comparison_sleep(seconds):
            sleeps.append(seconds)
            await release.wait()

        monkeypatch.setattr(duplicate_module, "asyncio", SimpleNamespace(sleep=comparison_sleep))
        task = asyncio.create_task(state._ready_phase(liuju=True))
        try:
            await ready_everyone(state)
            await asyncio.sleep(.02)
            assert not task.done(), "all four local readies skipped the paired-board comparison"
            assert state.duplicate_waiting_for == "comparison"
            assert sleeps == [pytest.approx(8, abs=.05)]
            snapshots = [entry["message"]["duplicate_match_info"] for entry in messages
                         if entry["message"].get("duplicate_match_info")]
            assert snapshots[-1]["waiting_for"] == "comparison"
            assert 7900 <= snapshots[-1]["comparison_remaining_ms"] <= 8000
            assert match.board_ready[1] == {other}
            # A duplicate / delayed ready cannot confirm or restart this phase.
            for seat in range(4):
                await submit(state, seat, "ready", tick=0)
            assert all(queue.empty() for queue in state.action_queues.values())
            assert state.duplicate_waiting_for == "comparison" and not task.done()
            from .boardcast import _base_game_info
            state._duplicate_comparison_deadline -= 2
            reconnect = _base_game_info(state)["duplicate_match"]
            assert reconnect["waiting_for"] == "comparison"
            assert 5900 <= reconnect["comparison_remaining_ms"] <= 6000
            assert _base_game_info(state)["duplicate_match"]["comparison_remaining_ms"] <= reconnect["comparison_remaining_ms"]
            release.set()
            await asyncio.wait_for(task, .5)
            assert match.board_ready[1] == {"open", "closed"}
        finally:
            if not task.done():
                task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)

    asyncio.run(run())


@pytest.mark.parametrize("table", ["open", "closed"])
def test_final_board_has_local_ready_then_its_own_comparison(monkeypatch, table):
    async def run():
        state, messages, match = await prepare_table(table)
        other = "closed" if table == "open" else "open"
        release = asyncio.Event()
        sleeps = []

        async def comparison_sleep(seconds):
            sleeps.append(seconds)
            await release.wait()

        monkeypatch.setattr(duplicate_module, "asyncio", SimpleNamespace(sleep=comparison_sleep))
        first = asyncio.create_task(match.submit(other, 1, [0, 0], [0, 0], {}))
        task = asyncio.create_task(state._after_round_settled())
        try:
            await wait_until(lambda: state.game_status == "waiting_ready")
            assert match.completed and not task.done() and sleeps == []
            summaries = [entry["message"] for entry in messages
                         if entry["message"].get("show_result_info", {}).get("liuju_step") == "board_summary"]
            assert summaries[-1]["show_result_info"]["next_status"] == "round_end_by_ready"
            ready = [entry["message"] for entry in messages if entry["message"].get("ready_status_info")]
            assert ready[-1]["settlement_sync"]["phase"] == "waiting_ready"
            await ready_everyone(state)
            await wait_until(lambda: bool(sleeps))
            assert state.duplicate_waiting_for == "comparison" and not task.done()
            assert sleeps == [pytest.approx(8, abs=.05)]
            release.set()
            await asyncio.wait_for(task, .5)
            await first
            assert match.board_ready == {}, "completed matches must not enter the next-board barrier"
            assert state._duplicate_comparison_deadline is None
        finally:
            for pending in (task, first):
                if not pending.done():
                    pending.cancel()
            await asyncio.gather(task, first, return_exceptions=True)
            await cancel_auxiliary_tasks(state)

    asyncio.run(run())


def test_both_tables_finish_their_comparison_before_next_board(monkeypatch):
    async def run():
        opened, _, match = await prepare_table("open")
        closed, _, _ = await prepare_table("closed")
        closed.duplicate_match = match
        release = {table: asyncio.Event() for table in ("open", "closed")}

        async def comparison_sleep(seconds):
            assert seconds == pytest.approx(8, abs=.05)
            await release[asyncio.current_task().get_name()].wait()

        monkeypatch.setattr(duplicate_module, "asyncio", SimpleNamespace(sleep=comparison_sleep))
        tasks = []
        try:
            tasks.append(asyncio.create_task(opened._ready_phase(liuju=True), name="open"))
            await ready_everyone(opened)
            await wait_until(lambda: opened.duplicate_waiting_for == "comparison")
            release["open"].set()
            await wait_until(lambda: opened.duplicate_waiting_for == "ready")
            assert not tasks[0].done()
            tasks.append(asyncio.create_task(closed._ready_phase(liuju=True), name="closed"))
            await ready_everyone(closed)
            await wait_until(lambda: closed.duplicate_waiting_for == "comparison")
            assert all(not task.done() for task in tasks)
            assert match.board_ready[1] == {"open"}
            release["closed"].set()
            await asyncio.wait_for(asyncio.gather(*tasks), .5)
            assert match.board_ready[1] == {"open", "closed"}
        finally:
            for task in tasks:
                if not task.done():
                    task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            await cancel_auxiliary_tasks(opened)
            await cancel_auxiliary_tasks(closed)

    asyncio.run(run())


def test_interruption_during_comparison_does_not_advance(monkeypatch):
    async def run():
        state, _, match = await prepare_table()
        release = asyncio.Event()

        async def comparison_sleep(seconds):
            await release.wait()

        monkeypatch.setattr(duplicate_module, "asyncio", SimpleNamespace(sleep=comparison_sleep))
        task = asyncio.create_task(state._ready_phase(liuju=True))
        try:
            await ready_everyone(state)
            await wait_until(lambda: state.duplicate_waiting_for == "comparison")
            match.interrupt("test_interruption")
            release.set()
            with pytest.raises(RuntimeError, match="已中断"):
                await asyncio.wait_for(task, .5)
            assert match.board_ready == {} and state._duplicate_comparison_deadline is None
        finally:
            if not task.done():
                task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)

    asyncio.run(run())


def test_asynchronous_ready_does_not_add_a_comparison(monkeypatch):
    async def run():
        state, messages, _ = await prepare_table(synchronous=False)
        sleep = AsyncMock()
        monkeypatch.setattr(duplicate_module, "asyncio", SimpleNamespace(sleep=sleep))
        task = asyncio.create_task(state._ready_phase(liuju=True))
        try:
            await ready_everyone(state)
            await asyncio.wait_for(task, .5)
            sleep.assert_not_awaited()
            assert not any(entry["message"].get("duplicate_match_info") for entry in messages)
        finally:
            if not task.done():
                task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)

    asyncio.run(run())


@pytest.mark.parametrize("humans", [0, 4])
def test_bots_and_ready_timeout_still_receive_comparison(monkeypatch, humans):
    async def run():
        from . import board_settlement
        state, messages, match = await prepare_table()
        match.board_ready[1] = {"open"}
        for seat, player in enumerate(state.player_list):
            if seat >= humans:
                player.user_id = seat + 1
        monkeypatch.setattr(board_settlement, "ready_seconds", lambda _: .025)
        release = asyncio.Event()

        async def comparison_sleep(seconds):
            assert seconds == 8
            await release.wait()

        monkeypatch.setattr(duplicate_module, "asyncio", SimpleNamespace(sleep=comparison_sleep))
        task = asyncio.create_task(state._ready_phase(liuju=True))
        try:
            await wait_until(lambda: getattr(state, "duplicate_waiting_for", None) == "comparison")
            assert not task.done() and not any(state.action_dict.values())
            if humans:
                ready = [entry["message"]["ready_status_info"]["player_to_ready"] for entry in messages
                         if entry["message"].get("ready_status_info")]
                assert all(ready[-1].values())
                assert all(player.remaining_time == 0 for player in state.player_list)
            release.set()
            await asyncio.wait_for(task, .5)
        finally:
            if not task.done():
                task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)

    asyncio.run(run())


def test_delayed_broadcast_cannot_consume_comparison_duration(monkeypatch):
    async def run():
        from . import boardcast
        state, _, match = await prepare_table()
        match.board_ready[1] = {"open"}
        broadcast_release, display_release = asyncio.Event(), asyncio.Event()
        clock = [100.0]
        sleeps = []
        monkeypatch.setattr(duplicate_module, "time", SimpleNamespace(monotonic=lambda: clock[0]))

        async def slow_broadcast(current):
            if current.duplicate_waiting_for == "comparison":
                assert current._duplicate_comparison_deadline is None
                assert boardcast.duplicate_match_info(current)["comparison_remaining_ms"] == 8000
                await broadcast_release.wait()

        async def comparison_sleep(seconds):
            sleeps.append(seconds)
            await display_release.wait()

        monkeypatch.setattr(boardcast, "broadcast_duplicate_status", slow_broadcast)
        monkeypatch.setattr(duplicate_module, "asyncio", SimpleNamespace(sleep=comparison_sleep))
        task = asyncio.create_task(state._ready_phase(liuju=True))
        try:
            await ready_everyone(state)
            await wait_until(lambda: state.duplicate_waiting_for == "comparison")
            clock[0] += 12
            assert not task.done() and sleeps == []
            assert boardcast.duplicate_match_info(state)["comparison_remaining_ms"] == 8000
            broadcast_release.set()
            await wait_until(lambda: bool(sleeps))
            assert sleeps == [8] and state._duplicate_comparison_deadline == 120
            display_release.set()
            await asyncio.wait_for(task, .5)
            assert state._duplicate_comparison_initial_ms is None
        finally:
            if not task.done():
                task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)

    asyncio.run(run())
