"""Offload bot CPU work without letting one room block the asyncio main loop."""
from __future__ import annotations

import asyncio
import logging
import os
import time
from concurrent.futures import Executor, ProcessPoolExecutor, ThreadPoolExecutor
from concurrent.futures.process import BrokenProcessPool
from functools import partial
from typing import Any, Callable, TypeVar


T = TypeVar("T")
logger = logging.getLogger(__name__)


def _worker_count() -> int:
    configured = os.getenv("BOT_CPU_WORKERS")
    if configured:
        try:
            return max(1, int(configured))
        except ValueError:
            pass
    return max(1, min(4, (os.cpu_count() or 2) - 1))


_EXECUTOR: Executor | None = None
_USE_THREAD_FALLBACK = False


def _init_worker(cache_mib: float) -> None:
    # Imported lazily inside the child so each process receives only its share
    # of the total configured cache budget.
    from .guobiao_shanten import configure_shanten_cache_budget

    configure_shanten_cache_budget(cache_mib)


def _warm_worker() -> int:
    time.sleep(0.01)
    return os.getpid()


def _process_executor() -> ProcessPoolExecutor:
    workers = _worker_count()
    try:
        total_cache_mib = max(0.0, float(os.getenv("GUOBIAO_AI_CACHE_MB", "200")))
    except ValueError:
        total_cache_mib = 200.0
    return ProcessPoolExecutor(
        max_workers=workers,
        initializer=_init_worker,
        initargs=(total_cache_mib / workers,),
    )


def _executor() -> Executor:
    global _EXECUTOR
    if _EXECUTOR is None:
        force_threads = os.getenv("BOT_CPU_EXECUTOR", "").strip().lower() == "thread"
        if _USE_THREAD_FALLBACK or force_threads:
            _EXECUTOR = ThreadPoolExecutor(
                max_workers=_worker_count(),
                thread_name_prefix="mahjong-bot",
            )
        else:
            _EXECUTOR = _process_executor()
    return _EXECUTOR


async def _discard_executor(executor: Executor) -> None:
    """Detach and stop an executor only if it is still the active instance."""
    global _EXECUTOR
    if _EXECUTOR is executor:
        _EXECUTOR = None
    await asyncio.to_thread(executor.shutdown, wait=False, cancel_futures=True)


def _enable_thread_fallback() -> None:
    global _USE_THREAD_FALLBACK
    _USE_THREAD_FALLBACK = True


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
        # Bot decisions are pure calculations, so retrying them after an abrupt
        # worker exit cannot duplicate a game-state mutation.
        for attempt in range(2):
            executor = _executor()
            try:
                return await loop.run_in_executor(executor, call)
            except BrokenProcessPool:
                logger.exception("Bot process pool exited unexpectedly; rebuilding it")
                await _discard_executor(executor)
                if attempt == 0:
                    continue
        _enable_thread_fallback()
        logger.error("Bot process pool failed twice; using the thread fallback")
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
    for attempt in range(2):
        executor = _executor()
        try:
            await asyncio.gather(
                *(loop.run_in_executor(executor, _warm_worker) for _ in range(_worker_count()))
            )
            logger.info("Bot executor is ready (%s workers)", _worker_count())
            return
        except (BrokenProcessPool, OSError):
            logger.exception(
                "Bot process pool warm-up failed (attempt %s/2)", attempt + 1
            )
            await _discard_executor(executor)

    # A bot accelerator must not make the whole game server unavailable. Threads
    # are slower for CPU-heavy work but preserve correct gameplay and async I/O.
    _enable_thread_fallback()
    executor = _executor()
    await loop.run_in_executor(executor, _warm_worker)
    logger.error("Bot executor started in thread fallback mode")


async def shutdown_bot_executor() -> None:
    global _EXECUTOR, _USE_THREAD_FALLBACK
    executor = _EXECUTOR
    _EXECUTOR = None
    _USE_THREAD_FALLBACK = False
    if executor is not None:
        await asyncio.to_thread(executor.shutdown, wait=True, cancel_futures=True)
