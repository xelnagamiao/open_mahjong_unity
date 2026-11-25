"""Unified async tingpai & L1 hand-dirty cache helpers for all GameState classes.

Every rule family uses this single pattern so that:
  1. Unchanged hands hit L1 cache in 0ms without dispatching to CalcExecutor.
  2. Cache misses execute off-loop via AsyncCalculationService -> CalcExecutor.
  3. Synchronous unit-test entry points share the exact same L1 signature key.
"""
from __future__ import annotations

import json
from typing import Any, Callable, Iterable, Optional, Set, Tuple

from ...game_calculation.async_calculation_service import AsyncCalculationService


def waiting_cache_signature(
    rule_key: str,
    hand_tiles: Iterable[int],
    combination_tiles: Iterable[str],
    extra: Any = None,
) -> Tuple[str, Tuple[int, ...], Tuple[str, ...], str]:
    extra_json = (
        json.dumps(extra, ensure_ascii=False, sort_keys=True)
        if extra is not None
        else ""
    )
    return (
        rule_key,
        tuple(sorted(int(t) for t in hand_tiles)),
        tuple(str(c) for c in combination_tiles),
        extra_json,
    )


def get_async_calc_service(state: Any) -> AsyncCalculationService:
    game_server = getattr(state, "game_server", None)
    svc = getattr(game_server, "async_calculation_service", None)
    if svc is None:
        svc = getattr(state, "_async_calculation_service", None)
        if svc is None:
            svc = AsyncCalculationService(getattr(state, "calculation_service", None))
            setattr(state, "_async_calculation_service", svc)
    return svc


async def async_refresh_waiting_tiles(
    state: Any,
    player_index: int,
    rule_key: str,
    hand_tiles: Iterable[int],
    combination_tiles: Iterable[str],
    *,
    extra: Any = None,
    tile_filter: Optional[Callable[[Set[int]], Set[int]]] = None,
) -> Set[int]:
    """Refresh player.waiting_tiles off-loop via AsyncCalculationService + L1 cache."""
    player = state.player_list[player_index]
    svc = get_async_calc_service(state)
    return await svc.refresh_player_waiting_tiles(
        state,
        player,
        rule_key,
        hand_tiles,
        combination_tiles,
        extra=extra,
        tile_filter=tile_filter,
    )


def sync_refresh_waiting_tiles_cached(
    player: Any,
    rule_key: str,
    hand_tiles: Iterable[int],
    combination_tiles: Iterable[str],
    compute_fn: Callable[[], Set[int]],
    *,
    extra: Any = None,
) -> Set[int]:
    """L1 hand-dirty cache wrapper shared with async_refresh_waiting_tiles.

    When an async caller has already refreshed the player via CalcExecutor, any
    subsequent synchronous check_action_after_cut hits this cache in 0ms.
    """
    sig = waiting_cache_signature(rule_key, hand_tiles, combination_tiles, extra)
    if (
        getattr(player, "_waiting_cache_sig", None) == sig
        and getattr(player, "waiting_tiles", None) is getattr(player, "_waiting_cache_ref", None)
    ):
        return player.waiting_tiles
    waits = set(compute_fn())
    player.waiting_tiles = waits
    player._waiting_cache_sig = sig
    player._waiting_cache_ref = waits
    return waits
