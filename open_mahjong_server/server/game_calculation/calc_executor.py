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
from concurrent.futures import BrokenExecutor, InterpreterPoolExecutor
from functools import partial
import json
import logging
from typing import Any, Callable, Optional, TypeVar

from ..runtime.cpu_allocation import CpuAllocation, resolve_server_cpu_allocation
from .registry import hepai_dispatch, tingpai_dispatch, warm_calc_worker

T = TypeVar("T")
logger = logging.getLogger(__name__)

_ALLOCATION: Optional[CpuAllocation] = None
_EXECUTOR: Optional[InterpreterPoolExecutor] = None
_ACTIVE_BACKEND: str = "inline"
_CHECK_RECEIPT = "startup self-check passed: guobiao/riichi waits=41,42; guobiao fan=25; riichi score=7700"


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


def _check_calc_worker() -> None:
    """Small, deterministic hands exercise the same dispatchers used for real games."""
    warm_calc_worker()
    hand = (11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42)
    for rule in ('guobiao', 'riichi'):
        if tingpai_dispatch(rule, hand, ()) != (41, 42):
            raise RuntimeError(f'{rule} waiting-tile startup check failed')
    exposed = (11, 12, 13, 14, 14, 15, 16, 17, 17, 17, 11)
    fan, _ = json.loads(hepai_dispatch('guobiao', exposed, ('k19',), ('点和', '场风南', '自风西'), 11))
    pinfu = (12, 13, 14, 22, 23, 24, 33, 34, 35, 36, 37, 38, 28, 28)
    ctx = '{"context":{"player_wind":1,"round_wind":0,"is_riichi":true,"dora_indicators":[37]}}'
    score = json.loads(hepai_dispatch('riichi', pinfu, (), (), 38, ctx))
    if fan != 25 or score['score'] != 7700:
        raise RuntimeError('winning-hand startup check failed')


async def warm_calc_executor(allocation: Optional[CpuAllocation] = None) -> CpuAllocation:
    """Only a broken startup pool may degrade; incorrect calculations must stop startup."""
    global _ALLOCATION, _EXECUTOR, _ACTIVE_BACKEND
    alloc = allocation or current_calc_allocation()
    if _EXECUTOR is not None:
        await shutdown_calc_executor()
    _ALLOCATION = alloc
    candidate = None
    pending = []
    try:
        candidate, backend = _build_executor(alloc)
        if candidate is not None:
            loop = asyncio.get_running_loop()
            for _ in range(alloc.calc_workers):
                pending.append(loop.run_in_executor(candidate, _check_calc_worker))
            await asyncio.gather(*pending)
            _EXECUTOR, _ACTIVE_BACKEND = candidate, backend
            candidate = None  # ownership transferred only after successful checks
            logger.info("Calc executor is ready (%s workers, backend=interpreter); %s", alloc.calc_workers, _CHECK_RECEIPT)
            return alloc
    except BrokenExecutor:
        logger.warning("Calc pool broke during startup; verifying inline backend", exc_info=True)
    finally:
        if candidate is not None:
            for future in pending:
                future.cancel()
            await asyncio.gather(*pending, return_exceptions=True)
            await asyncio.to_thread(candidate.shutdown, wait=True, cancel_futures=True)

    _EXECUTOR, _ACTIVE_BACKEND = None, "inline"
    _check_calc_worker()
    logger.info("Calc executor is ready (inline mode, cpu_budget=%s); %s", alloc.cpu_budget, _CHECK_RECEIPT)
    return alloc


async def shutdown_calc_executor() -> None:
    global _ALLOCATION, _EXECUTOR, _ACTIVE_BACKEND
    executor = _EXECUTOR
    _EXECUTOR = None
    _ALLOCATION = None
    _ACTIVE_BACKEND = "inline"
    if executor is not None:
        await asyncio.to_thread(executor.shutdown, wait=True, cancel_futures=True)
