"""Dedicated Python 3.14 InterpreterPoolExecutor for real-time tingpai & hepai calculation.

Physically isolated from BotExecutor so heavy AI searches never queue ahead of
live human table actions.

Modes (driven by server.runtime.cpu_allocation):
  - Multi-core (budget >= 2): InterpreterPoolExecutor (per-interpreter GIL,
    in-process memory transfer, zero OS pipe overhead)
  - Single-core (budget == 1): inline mode (0 extra subinterpreters)
"""
from __future__ import annotations

import asyncio
from concurrent.futures import InterpreterPoolExecutor
from functools import partial
import logging
from typing import Any, Callable, Optional, TypeVar

from ..runtime.cpu_allocation import CpuAllocation, resolve_server_cpu_allocation
from .registry import warm_calc_worker

T = TypeVar("T")
logger = logging.getLogger(__name__)

_ALLOCATION: Optional[CpuAllocation] = None
_EXECUTOR: Optional[InterpreterPoolExecutor] = None
_ACTIVE_BACKEND: str = "inline"


def current_calc_allocation() -> CpuAllocation:
    global _ALLOCATION
    if _ALLOCATION is None:
        _ALLOCATION = resolve_server_cpu_allocation()
    return _ALLOCATION


def _build_executor(allocation: CpuAllocation) -> tuple[Optional[InterpreterPoolExecutor], str]:
    if allocation.calc_backend == "inline" or allocation.calc_workers <= 0:
        return None, "inline"
    return InterpreterPoolExecutor(max_workers=allocation.calc_workers), "interpreter"


def _get_executor() -> tuple[Optional[InterpreterPoolExecutor], str]:
    global _EXECUTOR, _ACTIVE_BACKEND
    if _EXECUTOR is None and (_ACTIVE_BACKEND != "inline" or _ALLOCATION is None):
        _EXECUTOR, _ACTIVE_BACKEND = _build_executor(current_calc_allocation())
    return _EXECUTOR, _ACTIVE_BACKEND


def _room_calc_lock(game_state: Any) -> asyncio.Lock:
    lock = getattr(game_state, "_calc_cpu_lock", None)
    if lock is None:
        lock = asyncio.Lock()
        setattr(game_state, "_calc_cpu_lock", lock)
    return lock


async def run_calc_cpu(func: Callable[..., T], /, *args, **kwargs) -> T:
    """Run a pure calculation function on the real-time CalcExecutor pool."""
    executor, backend = _get_executor()
    if executor is None or backend == "inline":
        return func(*args, **kwargs)

    loop = asyncio.get_running_loop()
    if kwargs:
        return await loop.run_in_executor(executor, partial(func, *args, **kwargs))
    return await loop.run_in_executor(executor, func, *args)


async def run_room_calc_cpu(game_state: Any, func: Callable[..., T], /, *args, **kwargs) -> T:
    """Serialize off-loop real-time calculations belonging to one game room."""
    async with _room_calc_lock(game_state):
        return await run_calc_cpu(func, *args, **kwargs)


async def warm_calc_executor(allocation: Optional[CpuAllocation] = None) -> CpuAllocation:
    """Initialize and warm the real-time InterpreterPoolExecutor during server startup."""
    global _ALLOCATION, _EXECUTOR, _ACTIVE_BACKEND
    if allocation is not None:
        if _EXECUTOR is not None:
            await shutdown_calc_executor()
        _ALLOCATION = allocation
    alloc = current_calc_allocation()
    _EXECUTOR, _ACTIVE_BACKEND = _build_executor(alloc)
    if _EXECUTOR is None or _ACTIVE_BACKEND == "inline":
        warm_calc_worker()
        logger.info("Calc executor is ready (inline mode, cpu_budget=%s)", alloc.cpu_budget)
        return alloc

    loop = asyncio.get_running_loop()
    await asyncio.gather(
        *(loop.run_in_executor(_EXECUTOR, warm_calc_worker) for _ in range(alloc.calc_workers))
    )
    logger.info("Calc executor is ready (%s workers, backend=interpreter)", alloc.calc_workers)
    return alloc


async def shutdown_calc_executor() -> None:
    global _ALLOCATION, _EXECUTOR, _ACTIVE_BACKEND
    executor = _EXECUTOR
    _EXECUTOR = None
    _ALLOCATION = None
    _ACTIVE_BACKEND = "inline"
    if executor is not None:
        await asyncio.to_thread(executor.shutdown, wait=True, cancel_futures=True)
