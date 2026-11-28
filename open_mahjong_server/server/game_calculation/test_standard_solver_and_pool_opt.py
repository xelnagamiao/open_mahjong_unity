"""Regression & unit tests for standard_solver, L2 tingpai cache, and batched discard_waits."""
import asyncio
from collections import Counter

from .async_calculation_service import AsyncCalculationService
from .registry import discard_waits_dispatch
from .standard_solver import (
    cache_info,
    can_standard_win,
    clear_caches,
    discard_waiting_tiles_map,
    standard_combination_codes,
    standard_decompositions,
    standard_waiting_tiles,
)


def test_standard_solver_nine_gates_and_flush():
    nine_gates = [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19]
    assert standard_waiting_tiles(nine_gates) == {11, 12, 13, 14, 15, 16, 17, 18, 19}
    assert can_standard_win(nine_gates + [15])
    assert not can_standard_win(nine_gates + [25])


def test_standard_solver_sixteen_tile_five_melds():
    # 16-tile ready hand (5 melds + 1 pair = 17 complete)
    hand16 = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 32, 33, 41, 41, 41, 45]
    assert standard_waiting_tiles(hand16) == {45}
    assert can_standard_win(hand16 + [45])


def test_standard_solver_fifth_copy_toggle():
    four_honors = [46, 46, 46, 46]
    assert standard_waiting_tiles(four_honors, allow_fifth_copy=False) == set()
    assert standard_waiting_tiles(four_honors, allow_fifth_copy=True) == {46}


def test_standard_solver_pair_tiles_constraint():
    # Changsha 258 pair constraint
    jiang = frozenset(s * 10 + r for s in (1, 2, 3) for r in (2, 5, 8))
    hand_14_non_jiang = [11, 11, 12, 13, 14, 21, 22, 23, 31, 32, 33, 37, 38, 39]
    assert can_standard_win(hand_14_non_jiang, allow_honors=False)
    assert not can_standard_win(hand_14_non_jiang, allow_honors=False, pair_tiles=jiang)
    hand_14_jiang = [12, 12, 12, 13, 14, 21, 22, 23, 31, 32, 33, 37, 38, 39]
    assert can_standard_win(hand_14_jiang, allow_honors=False, pair_tiles=jiang)


def test_standard_decompositions_unique_canonical():
    # Hand with multiple overlapping sequences/triplets: 111 222 333 + 444 + 55
    hand = [11, 11, 11, 12, 12, 12, 13, 13, 13, 21, 21, 21, 35, 35]
    decomps = standard_decompositions(hand)
    assert len(decomps) == len(set(decomps))
    codes = standard_combination_codes(hand, [])
    # Either three triplets (K11, K12, K13) or three sequences (S12, S12, S12)
    assert len(codes) == 2


def test_discard_waiting_tiles_map_matches_single_waits():
    hand14 = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42, 47]
    batch = discard_waiting_tiles_map(hand14)
    assert batch == {47: {41, 42}}
    # Cross-check against per-tile standard_waiting_tiles
    for t in set(hand14):
        sub = list(hand14)
        sub.remove(t)
        expected = standard_waiting_tiles(sub)
        assert batch.get(t, set()) == expected


def test_async_service_l2_cache_and_batch_discard_waits():
    async def _run():
        svc = AsyncCalculationService(l2_maxsize=2)
        h1 = [11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42]
        h2 = [11, 12, 13, 21, 22, 23, 31, 32, 33, 43, 43, 44, 44]
        h3 = [11, 12, 13, 21, 22, 23, 31, 32, 33, 45, 45, 46, 46]

        w1 = await svc.tingpai("guobiao", h1, [])
        assert w1 == {41, 42}
        assert svc.l2_cache_info()["misses"] == 1
        assert svc.l2_cache_info()["hits"] == 0

        # Different tile order hits L2 cache
        w1_again = await svc.tingpai("guobiao", list(reversed(h1)), [])
        assert w1_again == {41, 42}
        assert svc.l2_cache_info()["hits"] == 1

        # LRU eviction enforces maxsize=2
        await svc.tingpai("guobiao", h2, [])
        await svc.tingpai("guobiao", h3, [])
        assert svc.l2_cache_info()["currsize"] == 2

        # Batch discard_waits
        batch = await svc.discard_waits("guobiao", h1 + [47], [])
        assert batch == {47: {41, 42}}

    asyncio.run(_run())


def test_discard_waits_dispatch_direct():
    import json
    raw = discard_waits_dispatch(
        "guobiao",
        (11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 42, 42, 47),
        (),
    )
    assert json.loads(raw) == {"47": [41, 42]}


def test_cache_info_and_clear():
    clear_caches()
    info = cache_info()
    assert all(v["currsize"] == 0 for v in info.values())
