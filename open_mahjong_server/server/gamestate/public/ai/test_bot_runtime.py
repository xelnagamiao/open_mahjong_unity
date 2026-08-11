from __future__ import annotations

import asyncio
import time
from concurrent.futures import ThreadPoolExecutor
from concurrent.futures.process import BrokenProcessPool
from types import SimpleNamespace

from . import bot_executor
from .bot_executor import bot_action_is_current, run_room_bot_cpu
from .bounded_lru_cache import MemoryBoundedLRUCache
from .guobiao_shanten import shanten_cache_stats


def _busy_cpu(seconds: float) -> int:
    deadline = time.perf_counter() + seconds
    value = 0
    while time.perf_counter() < deadline:
        value = (value * 33 + 17) & 0xFFFF
    return value


def test_memory_bounded_lru_evicts_oldest_and_tracks_stats():
    cache = MemoryBoundedLRUCache("test", budget_bytes=30, entry_charge_bytes=10)
    cache[1] = "a"
    cache[2] = "b"
    cache[3] = "c"
    assert cache.get(1) == "a"  # make 1 most-recently used
    cache[4] = "d"
    assert 2 not in cache
    assert list(cache) == [3, 1, 4]
    snap = cache.snapshot()
    assert snap.entries == 3
    assert snap.estimated_bytes <= snap.budget_bytes
    assert snap.hits == 1
    assert snap.evictions == 1


def test_default_guobiao_cache_budget_reserves_decision_headroom():
    stats = shanten_cache_stats()
    persistent = sum(item["budget_bytes"] for item in stats.values())
    assert persistent == 160 * 1024 * 1024


def test_stale_bot_result_is_rejected():
    room = SimpleNamespace(server_action_tick=8, waiting_players_list=[1])
    assert bot_action_is_current(room, 1, 8)
    room.server_action_tick = 9
    assert not bot_action_is_current(room, 1, 8)


def test_warmup_falls_back_when_process_workers_cannot_start(monkeypatch):
    class BrokenExecutor:
        def submit(self, *_args, **_kwargs):
            raise BrokenProcessPool("simulated worker startup failure")

        def shutdown(self, **_kwargs):
            return None

    async def scenario():
        monkeypatch.setattr(bot_executor, "_EXECUTOR", None)
        monkeypatch.setattr(bot_executor, "_USE_THREAD_FALLBACK", False)
        monkeypatch.setattr(bot_executor, "_process_executor", BrokenExecutor)
        try:
            await bot_executor.warm_bot_executor()
            assert isinstance(bot_executor._EXECUTOR, ThreadPoolExecutor)
        finally:
            await bot_executor.shutdown_bot_executor()

    asyncio.run(scenario())


def test_room_cpu_work_is_serial_and_event_loop_keeps_running():
    async def scenario():
        room = SimpleNamespace()
        heartbeats = 0

        async def heartbeat():
            nonlocal heartbeats
            deadline = time.perf_counter() + 0.07
            while time.perf_counter() < deadline:
                heartbeats += 1
                await asyncio.sleep(0.005)

        started = time.perf_counter()
        await asyncio.gather(
            run_room_bot_cpu(room, _busy_cpu, 0.04),
            run_room_bot_cpu(room, _busy_cpu, 0.04),
            heartbeat(),
        )
        return heartbeats, time.perf_counter() - started

    heartbeats, elapsed = asyncio.run(scenario())
    assert heartbeats >= 3
    assert elapsed >= 0.07


def test_parallel_rooms_do_not_starve_main_event_loop():
    async def scenario():
        rooms = [SimpleNamespace() for _ in range(8)]
        stamps = []

        async def heartbeat():
            deadline = time.perf_counter() + 0.25
            while time.perf_counter() < deadline:
                stamps.append(time.perf_counter())
                await asyncio.sleep(0.005)

        await asyncio.gather(
            heartbeat(),
            *(run_room_bot_cpu(room, _busy_cpu, 0.15) for room in rooms),
        )
        return [later - earlier for earlier, later in zip(stamps, stamps[1:])]

    gaps = asyncio.run(scenario())
    assert len(gaps) >= 10
    assert max(gaps) < 0.08
