"""Tests for CalcExecutor (InterpreterPoolExecutor), registry dispatch, and L1 cache."""
from __future__ import annotations

import asyncio
from types import SimpleNamespace

from server.game_calculation import calc_executor
from server.game_calculation.async_calculation_service import AsyncCalculationService
from server.runtime.cpu_allocation import compute_pool_allocation


def test_async_calculation_service_tingpai_and_l1_cache():
    async def scenario():
        alloc = compute_pool_allocation(1, environ={})
        await calc_executor.warm_calc_executor(alloc)
        try:
            svc = AsyncCalculationService()
            state = SimpleNamespace(lifecycle_state="running", server_action_tick=1)
            player = SimpleNamespace(waiting_tiles=set())
            hand = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42]

            waits1 = await svc.refresh_player_waiting_tiles(state, player, "guobiao", hand, [])
            assert waits1 == {41, 42}
            assert player.waiting_tiles == {41, 42}

            # Same hand (even unsorted) hits L1 cache immediately without dispatching
            waits2 = await svc.refresh_player_waiting_tiles(state, player, "guobiao", list(reversed(hand)), [])
            assert waits2 == {41, 42}

            # Score check through unified hepai dispatch
            score, fans = await svc.hepai("guobiao", hand + [41], [], ["自摸"], 41)
            assert isinstance(score, int)
            assert isinstance(fans, list)
        finally:
            await calc_executor.shutdown_calc_executor()

    asyncio.run(scenario())


def test_calc_executor_interpreter_backend():
    async def scenario():
        alloc = compute_pool_allocation(8, environ={})
        await calc_executor.warm_calc_executor(alloc)
        assert calc_executor._ACTIVE_BACKEND == "interpreter"
        try:
            svc = AsyncCalculationService()
            waits = await svc.tingpai("riichi", [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45, 46, 46], [])
            assert waits == {45, 46}
            qq_waits = await svc.tingpai("qingque", [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19], [])
            assert qq_waits == set(range(11, 20))
        finally:
            await calc_executor.shutdown_calc_executor()

    asyncio.run(scenario())
