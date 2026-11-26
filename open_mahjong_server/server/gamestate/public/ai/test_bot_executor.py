"""Full-branch tests for BotExecutor (process pool, retry, thread fallback, warm/shutdown)."""
from __future__ import annotations

import asyncio
from concurrent.futures import ThreadPoolExecutor
from concurrent.futures.process import BrokenProcessPool
from types import SimpleNamespace

from server.gamestate.public.ai import bot_executor
from server.gamestate.public.ai.shanten_cache_config import configured_shanten_cache_budgets
from server.runtime.cpu_allocation import compute_pool_allocation


def _double(x: int, *, bonus: int = 0) -> int:
    return x * 2 + bonus


def test_worker_count_and_helpers(monkeypatch):
    asyncio.run(bot_executor.shutdown_bot_executor())
    try:
        monkeypatch.setenv("BOT_CPU_WORKERS", "5")
        assert bot_executor._worker_count() == 5

        monkeypatch.setenv("BOT_CPU_WORKERS", "invalid")
        bot_executor._ALLOCATION = compute_pool_allocation(8, environ={})
        assert bot_executor._worker_count() == 4

        monkeypatch.delenv("BOT_CPU_WORKERS", raising=False)
        bot_executor._ALLOCATION = None
        assert bot_executor._worker_count() >= 1

        # _init_worker and _warm_worker
        bot_executor._init_worker(configured_shanten_cache_budgets())
        assert isinstance(bot_executor._warm_worker(), int)

        # bot_action_is_current branches
        state = SimpleNamespace(server_action_tick=7, waiting_players_list={1, 2})
        assert bot_executor.bot_action_is_current(state, 1, 7) is True
        assert bot_executor.bot_action_is_current(state, 1, 6) is False
        assert bot_executor.bot_action_is_current(state, 3, 7) is False
    finally:
        asyncio.run(bot_executor.shutdown_bot_executor())


def test_thread_mode_and_room_lock(monkeypatch):
    async def scenario():
        await bot_executor.shutdown_bot_executor()
        monkeypatch.setenv("BOT_CPU_EXECUTOR", "thread")
        try:
            state = SimpleNamespace()
            r1 = await bot_executor.run_room_bot_cpu(state, _double, 10)
            r2 = await bot_executor.run_room_bot_cpu(state, _double, 10, bonus=3)
            assert r1 == 20 and r2 == 23
            assert isinstance(bot_executor._EXECUTOR, ThreadPoolExecutor)
            # Reusing existing lock on same state
            lock1 = bot_executor._room_lock(state)
            lock2 = bot_executor._room_lock(state)
            assert lock1 is lock2
            await bot_executor.warm_bot_executor(compute_pool_allocation(2, environ={}))
        finally:
            await bot_executor.shutdown_bot_executor()
            # Idempotent second shutdown when _EXECUTOR is None
            await bot_executor.shutdown_bot_executor()

    asyncio.run(scenario())


def test_run_room_bot_cpu_retry_and_thread_fallback(monkeypatch):
    class FailingPool:
        def __init__(self, fail_times: int):
            self.fail_times = fail_times
            self.calls = 0
            self.shutdown_called = False

        def submit(self, fn, /, *args, **kwargs):
            self.calls += 1
            fut = asyncio.Future()
            raise BrokenProcessPool("simulated crash")

        def shutdown(self, wait=True, cancel_futures=False):
            self.shutdown_called = True

    class BrokenExecutor:
        def __init__(self):
            self.shutdown_called = False

        def submit(self, fn, /, *args, **kwargs):
            from concurrent.futures import Future
            f = Future()
            f.set_exception(BrokenProcessPool("simulated worker death"))
            return f

        def shutdown(self, wait=True, cancel_futures=False):
            self.shutdown_called = True

    async def scenario():
        # Case 1: fail once on attempt 0, succeed on attempt 1 after rebuild
        await bot_executor.shutdown_bot_executor()
        monkeypatch.delenv("BOT_CPU_EXECUTOR", raising=False)
        pools = [BrokenExecutor(), ThreadPoolExecutor(max_workers=1)]
        idx = {"i": 0}

        def make_pool():
            p = pools[idx["i"]]
            idx["i"] += 1
            return p

        monkeypatch.setattr(bot_executor, "_process_executor", make_pool)
        try:
            state = SimpleNamespace()
            res = await bot_executor.run_room_bot_cpu(state, _double, 6)
            assert res == 12
            assert pools[0].shutdown_called is True
            assert bot_executor._USE_THREAD_FALLBACK is False
        finally:
            await bot_executor.shutdown_bot_executor()

        # Case 2: fail twice -> switches to thread fallback and succeeds
        broken1, broken2 = BrokenExecutor(), BrokenExecutor()
        broken_iter = iter([broken1, broken2])
        monkeypatch.setattr(bot_executor, "_process_executor", lambda: next(broken_iter))
        try:
            state = SimpleNamespace()
            res = await bot_executor.run_room_bot_cpu(state, _double, 9, bonus=1)
            assert res == 19
            assert broken1.shutdown_called and broken2.shutdown_called
            assert bot_executor._USE_THREAD_FALLBACK is True
            assert isinstance(bot_executor._EXECUTOR, ThreadPoolExecutor)
        finally:
            await bot_executor.shutdown_bot_executor()

        # Case 3: _discard_executor when executor is no longer the active global instance
        dummy_a = BrokenExecutor()
        dummy_b = BrokenExecutor()
        bot_executor._EXECUTOR = dummy_b
        await bot_executor._discard_executor(dummy_a)
        assert bot_executor._EXECUTOR is dummy_b
        assert dummy_a.shutdown_called is True
        await bot_executor.shutdown_bot_executor()

    asyncio.run(scenario())


def test_warm_bot_executor_process_retry_and_fallback(monkeypatch):
    class BrokenWarmExecutor:
        def __init__(self, exc_cls):
            self.exc_cls = exc_cls
            self.shutdown_called = False

        def submit(self, fn, /, *args, **kwargs):
            from concurrent.futures import Future
            f = Future()
            f.set_exception(self.exc_cls("warm failed"))
            return f

        def shutdown(self, wait=True, cancel_futures=False):
            self.shutdown_called = True

    async def scenario():
        await bot_executor.shutdown_bot_executor()
        monkeypatch.delenv("BOT_CPU_EXECUTOR", raising=False)
        monkeypatch.setenv("BOT_CPU_WORKERS", "1")
        # 1) Real spawn ProcessPoolExecutor warm + execution
        try:
            await bot_executor.warm_bot_executor(compute_pool_allocation(2, environ={}))
            assert bot_executor._USE_THREAD_FALLBACK is False
            state = SimpleNamespace()
            assert await bot_executor.run_room_bot_cpu(state, _double, 7) == 14
        finally:
            await bot_executor.shutdown_bot_executor()

        # 2) Both warm attempts raise BrokenProcessPool / OSError -> thread fallback
        b1 = BrokenWarmExecutor(BrokenProcessPool)
        b2 = BrokenWarmExecutor(OSError)
        it = iter([b1, b2])
        monkeypatch.setattr(bot_executor, "_process_executor", lambda: next(it))
        try:
            await bot_executor.warm_bot_executor(None)
            assert b1.shutdown_called and b2.shutdown_called
            assert bot_executor._USE_THREAD_FALLBACK is True
            assert isinstance(bot_executor._EXECUTOR, ThreadPoolExecutor)
        finally:
            await bot_executor.shutdown_bot_executor()

    asyncio.run(scenario())
