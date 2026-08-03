"""Offload bot CPU work without letting one room block the asyncio main loop."""
from __future__ import annotations

import asyncio
import os
import time
from concurrent.futures import ProcessPoolExecutor
from functools import partial
from typing import Any, Callable, TypeVar


T = TypeVar("T")


def _worker_count() -> int:
    configured = os.getenv("BOT_CPU_WORKERS")
    if configured:
        try:
            return max(1, int(configured))
        except ValueError:
            pass
    return max(1, min(4, (os.cpu_count() or 2) - 1))


_EXECUTOR = None


def _init_worker(cache_mib: float) -> None:
    # Imported lazily inside the child so each process receives only its share
    # of the total configured cache budget.
    from .guobiao_shanten import configure_shanten_cache_budget

    configure_shanten_cache_budget(cache_mib)


def _warm_worker() -> int:
    time.sleep(0.01)
    return os.getpid()


def _executor() -> ProcessPoolExecutor:
    global _EXECUTOR
    if _EXECUTOR is None:
        workers = _worker_count()
        try:
            total_cache_mib = max(0.0, float(os.getenv("GUOBIAO_AI_CACHE_MB", "200")))
        except ValueError:
            total_cache_mib = 200.0
        _EXECUTOR = ProcessPoolExecutor(
            max_workers=workers,
            initializer=_init_worker,
            initargs=(total_cache_mib / workers,),
        )
    return _EXECUTOR


def _room_lock(game_state: Any) -> asyncio.Lock:
    lock = getattr(game_state, "_bot_cpu_lock", None)
    if lock is None:
        lock = asyncio.Lock()
        setattr(game_state, "_bot_cpu_lock", lock)
    return lock


async def run_room_bot_cpu(game_state: Any, func: Callable[..., T], /, *args, **kwargs) -> T:
    """Run CPU work off-loop and serialize decisions belonging to one room."""
    loop = asyncio.get_running_loop()
    call = partial(func, *args, **kwargs)
    async with _room_lock(game_state):
        return await loop.run_in_executor(_executor(), call)


def bot_action_is_current(game_state: Any, player_index: int, expected_tick: Any) -> bool:
    """Reject a calculation result if the room advanced while it ran off-loop."""
    return (
        getattr(game_state, "server_action_tick", expected_tick) == expected_tick
        and player_index in getattr(game_state, "waiting_players_list", ())
    )


async def warm_bot_executor() -> None:
    """Start worker processes during server startup, before the first room action."""
    loop = asyncio.get_running_loop()
    executor = _executor()
    await asyncio.gather(
        *(loop.run_in_executor(executor, _warm_worker) for _ in range(_worker_count()))
    )


async def shutdown_bot_executor() -> None:
    global _EXECUTOR
    executor = _EXECUTOR
    _EXECUTOR = None
    if executor is not None:
        await asyncio.to_thread(executor.shutdown, True, cancel_futures=True)
