"""Full-branch tests for CalcExecutor (InterpreterPoolExecutor), registry dispatch, and L1 cache."""
from __future__ import annotations

import asyncio
from concurrent.futures import BrokenExecutor, Future
from types import SimpleNamespace
from unittest.mock import Mock

import pytest

from server.game_calculation import calc_executor
from server.game_calculation.async_calculation_service import AsyncCalculationService
from server.runtime.cpu_allocation import compute_pool_allocation


def _echo_sum(a: int, b: int, *, scale: int = 1) -> int:
    return (a + b) * scale


def test_async_calculation_service_tingpai_l1_cache_filter_and_race_guard():
    async def scenario():
        alloc = compute_pool_allocation(1, environ={})
        await calc_executor.warm_calc_executor(alloc)
        try:
            svc = AsyncCalculationService(sync_service="mock")
            assert svc.sync_service == "mock"
            state = SimpleNamespace(lifecycle_state="running", server_action_tick=1)
            player = SimpleNamespace(waiting_tiles=set())
            hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42]

            # Cache miss + write
            waits1 = await svc.refresh_player_waiting_tiles(state, player, "guobiao", hand, [])
            assert waits1 == {41, 42}
            assert player.waiting_tiles == {41, 42}

            # Same hand (even unsorted) hits L1 cache immediately without dispatching
            waits2 = await svc.refresh_player_waiting_tiles(state, player, "guobiao", list(reversed(hand)), [])
            assert waits2 is player.waiting_tiles

            # External replacement of player.waiting_tiles invalidates L1 ref check
            player.waiting_tiles = set(player.waiting_tiles)
            waits2b = await svc.refresh_player_waiting_tiles(state, player, "guobiao", hand, [])
            assert waits2b == {41, 42}

            # Non-empty combination_tiles branch + tile_filter branch
            hand_with_meld = [11, 12, 13, 21, 22, 23, 41, 41, 42, 42]
            filtered = await svc.refresh_player_waiting_tiles(
                state, player, "guobiao", hand_with_meld, ["k31"],
                extra={"k": 1}, tile_filter=lambda s: {t for t in s if t == 41}
            )
            assert filtered == {41}
            assert player.waiting_tiles == {41}

            # Race guard: closed lifecycle_state must NOT overwrite player.waiting_tiles
            closed_state = SimpleNamespace(lifecycle_state="closed", server_action_tick=2)
            stale_res = await svc.refresh_player_waiting_tiles(
                closed_state, player, "guobiao", hand, [], extra={"k": 2}
            )
            assert stale_res == {41, 42}
            assert player.waiting_tiles == {41}

            # Race guard: server_action_tick changes during off-loop calc must NOT overwrite
            class AdvancingState:
                lifecycle_state = "running"
                def __init__(self):
                    self._calls = 0
                @property
                def server_action_tick(self):
                    self._calls += 1
                    return self._calls

            adv_state = AdvancingState()
            stale_res2 = await svc.refresh_player_waiting_tiles(
                adv_state, player, "guobiao", hand, [], extra={"k": 3}
            )
            assert stale_res2 == {41, 42}
            assert player.waiting_tiles == {41}

            # tingpai with non-empty combination_tiles and extra != None
            direct_waits = await svc.tingpai("guobiao", hand_with_meld, ["k31"], extra={"unused": True})
            assert direct_waits == {41, 42}

            # hepai with non-empty combination_tiles, extra == None and extra != None
            score, fans = await svc.hepai("guobiao", hand_with_meld + [41], ["k31"], ["自摸"], 41)
            assert isinstance(score, int) and isinstance(fans, list)
            score2, fans2 = await svc.hepai("guobiao", hand_with_meld + [41], ["k31"], ["自摸"], 41, extra={"unused": True})
            assert score2 == score and fans2 == fans

            # call_rule stateless JSON dispatch
            res = await svc.call_rule(
                "server.game_calculation.test_calc_executor", "_echo_sum", 3, 4, scale=2
            )
            assert res == 14

            # run_room_snapshot (creates and reuses _calc_cpu_lock)
            assert await svc.run_room_snapshot(state, _echo_sum, 2, 5) == 7
            assert await svc.run_room_snapshot(state, _echo_sum, 2, 5, scale=3) == 21
        finally:
            await calc_executor.shutdown_calc_executor()
            # Calling shutdown a second time when already None is a no-op
            await calc_executor.shutdown_calc_executor()

    asyncio.run(scenario())


def test_calc_executor_interpreter_backend_kwargs_and_rewarm():
    async def scenario():
        # Lazy current_calc_allocation + _get_executor path
        await calc_executor.shutdown_calc_executor()
        alloc_auto = calc_executor.current_calc_allocation()
        assert alloc_auto.cpu_budget >= 1

        alloc = compute_pool_allocation(4, environ={})
        await calc_executor.warm_calc_executor(alloc)
        assert calc_executor._ACTIVE_BACKEND == "interpreter"
        try:
            # Interpreter path without kwargs and with kwargs (partial branch)
            assert await calc_executor.run_calc_cpu(_echo_sum, 4, 5) == 9
            assert await calc_executor.run_calc_cpu(_echo_sum, 4, 5, scale=10) == 90

            svc = AsyncCalculationService()
            waits = await svc.tingpai("riichi", [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45, 46, 46], [])
            assert waits == {45, 46}
            qq_waits = await svc.tingpai("qingque", [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19], [])
            assert qq_waits == set(range(11, 20))

            # Re-warming with a new allocation while _EXECUTOR is active shuts down old executor first
            alloc_inline = compute_pool_allocation(1, environ={})
            res_alloc = await calc_executor.warm_calc_executor(alloc_inline)
            assert res_alloc.calc_backend == "inline"
            assert calc_executor._EXECUTOR is None

            # Re-warming with allocation=None keeps current allocation
            res_same = await calc_executor.warm_calc_executor(None)
            assert res_same.calc_backend == "inline"
        finally:
            await calc_executor.shutdown_calc_executor()

    asyncio.run(scenario())


def test_calc_executor_lazy_init_when_unwarmed(monkeypatch):
    async def scenario():
        await calc_executor.shutdown_calc_executor()
        monkeypatch.setattr(
            calc_executor, "resolve_server_cpu_allocation", lambda: compute_pool_allocation(1, environ={})
        )
        try:
            assert await calc_executor.run_calc_cpu(_echo_sum, 1, 2) == 3
            # Second call hits already-initialized inline branch
            assert await calc_executor.run_calc_cpu(_echo_sum, 2, 3) == 5
        finally:
            await calc_executor.shutdown_calc_executor()

    asyncio.run(scenario())


class _StartupPool:
    def __init__(self, submit):
        self.submit = Mock(side_effect=submit)
        self.shutdown = Mock()


def _future(*, error=None, value=None):
    result = Future()
    if error is not None:
        result.set_exception(error)
    else:
        result.set_result(value)
    return result


@pytest.mark.parametrize('point', ['construct', 'submit', 'future'])
def test_broken_startup_only_degrades_once_and_keeps_budget(monkeypatch, caplog, point):
    async def scenario():
        await calc_executor.shutdown_calc_executor()
        alloc = compute_pool_allocation(5, environ={})
        broken = BrokenExecutor('test broken startup infrastructure')
        pending = Future()
        pool = _StartupPool([pending, broken] if point == 'submit' else [_future(error=broken), pending])
        build = Mock(side_effect=broken) if point == 'construct' else Mock(return_value=(pool, 'interpreter'))
        check = Mock(wraps=calc_executor._check_calc_worker)
        monkeypatch.setattr(calc_executor, '_build_executor', build)
        monkeypatch.setattr(calc_executor, '_check_calc_worker', check)
        try:
            with caplog.at_level('INFO'):
                assert await calc_executor.warm_calc_executor(alloc) is alloc
            assert calc_executor.current_calc_allocation() is alloc
            assert calc_executor._ACTIVE_BACKEND == 'inline' and calc_executor._EXECUTOR is None
            assert 'Calc pool broke during startup' in caplog.text and 'riichi score=7700' in caplog.text
            for n in range(3):
                assert await calc_executor.run_calc_cpu(_echo_sum, n, 1) == n + 1
            assert build.call_count == check.call_count == 1
            if point != 'construct':
                assert pending.cancelled()
                pool.shutdown.assert_called_once_with(wait=True, cancel_futures=True)
        finally:
            await calc_executor.shutdown_calc_executor()
        assert pool.shutdown.call_count == (0 if point == 'construct' else 1)
    asyncio.run(scenario())


@pytest.mark.parametrize('error', [ValueError('invalid rule'), ModuleNotFoundError('missing module'), RuntimeError('bad result')])
def test_worker_check_error_is_not_a_broken_pool(monkeypatch, caplog, error):
    async def scenario():
        await calc_executor.shutdown_calc_executor()
        pool = _StartupPool([_future(error=error)])
        monkeypatch.setattr(calc_executor, '_build_executor', lambda _: (pool, 'interpreter'))
        try:
            with caplog.at_level('INFO'), pytest.raises(type(error)) as actual:
                await calc_executor.warm_calc_executor(compute_pool_allocation(2, environ={}))
            assert actual.value is error and 'Calc executor is ready' not in caplog.text
            assert 'verifying inline' not in caplog.text
            pool.shutdown.assert_called_once_with(wait=True, cancel_futures=True)
        finally:
            await calc_executor.shutdown_calc_executor()
    asyncio.run(scenario())


def test_cancelled_startup_retires_candidate_without_fallback(monkeypatch, caplog):
    async def scenario():
        await calc_executor.shutdown_calc_executor()
        pending = Future(); pool = _StartupPool([pending]); check = Mock()
        monkeypatch.setattr(calc_executor, '_build_executor', lambda _: (pool, 'interpreter'))
        monkeypatch.setattr(calc_executor, '_check_calc_worker', check)
        try:
            task = asyncio.create_task(calc_executor.warm_calc_executor(compute_pool_allocation(2, environ={})))
            await asyncio.sleep(0)
            assert pool.submit.called
            task.cancel()
            with pytest.raises(asyncio.CancelledError):
                await task
            assert pending.cancelled() and not check.called and 'verifying inline' not in caplog.text
            pool.shutdown.assert_called_once_with(wait=True, cancel_futures=True)
        finally:
            await calc_executor.shutdown_calc_executor()
    asyncio.run(scenario())


@pytest.mark.parametrize('fallback',[False,True])
def test_incorrect_real_selfcheck_aborts_both_inline_paths(monkeypatch,caplog,fallback):
    async def scenario():
        await calc_executor.shutdown_calc_executor()
        if fallback:
            monkeypatch.setattr(calc_executor, '_build_executor', Mock(side_effect=BrokenExecutor('test failure')))
        monkeypatch.setattr(calc_executor, 'tingpai_dispatch', lambda *args: (99,))
        try:
            with caplog.at_level('INFO'), pytest.raises(RuntimeError,match='guobiao waiting-tile startup check failed'):
                await calc_executor.warm_calc_executor(compute_pool_allocation(2 if fallback else 1,environ={}))
            assert 'Calc executor is ready' not in caplog.text
        finally:
            await calc_executor.shutdown_calc_executor()
    asyncio.run(scenario())


def test_no_runtime_recovery_or_replay_of_application_errors(monkeypatch):
    async def scenario():
        await calc_executor.shutdown_calc_executor()
        errors = (ValueError('business error'), BrokenExecutor('after successful startup'))
        pool = _StartupPool([_future(), *(_future(error=e) for e in errors)])
        build = Mock(return_value=(pool,'interpreter'))
        monkeypatch.setattr(calc_executor,'_build_executor',build)
        caller = Mock(return_value=123)
        try:
            await calc_executor.warm_calc_executor(compute_pool_allocation(2,environ={}))
            for error in errors:
                with pytest.raises(type(error)) as actual:
                    await calc_executor.run_calc_cpu(caller)
                assert actual.value is error
            assert build.call_count==1 and calc_executor._EXECUTOR is pool and not caller.called
        finally:await calc_executor.shutdown_calc_executor()
    asyncio.run(scenario())
