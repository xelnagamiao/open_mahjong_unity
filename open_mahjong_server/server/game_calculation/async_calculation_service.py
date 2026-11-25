"""Unified async calculation facade with L1 hand-dirty caching and CalcExecutor dispatch.

All mahjong rules use this single interface for tingpai / hepai checks so that:
  1. Unchanged hands return cached waiting tiles in 0ms (skipping pool dispatch).
  2. Cache misses run off the main asyncio event loop on CalcExecutor
     (Python 3.14 InterpreterPoolExecutor or spawn ProcessPoolExecutor).
  3. Only immutable primitives / tuples / JSON strings cross the GIL boundary.
"""
from __future__ import annotations

import json
from typing import Any, Callable, Dict, Iterable, List, Optional, Set, Tuple

from .calc_executor import run_calc_cpu, run_room_calc_cpu
from .registry import hepai_dispatch, invoke_callable_json, tingpai_dispatch


def _hand_signature(
    rule_key: str,
    hand_tiles: Iterable[int],
    combination_tiles: Iterable[str],
    extra_json: str = "",
) -> Tuple[str, Tuple[int, ...], Tuple[str, ...], str]:
    return (
        rule_key,
        tuple(sorted(int(t) for t in hand_tiles)),
        tuple(str(c) for c in combination_tiles),
        extra_json,
    )


class AsyncCalculationService:
    """Unified coroutine facade for all rule tingpai / hepai calculations."""

    def __init__(self, sync_service: Any = None):
        self.sync_service = sync_service

    async def tingpai(
        self,
        rule_key: str,
        hand_tiles: Iterable[int],
        combination_tiles: Iterable[str],
        extra: Any = None,
    ) -> Set[int]:
        """Compute waiting tiles off-loop via the unified CalcExecutor."""
        hand_tuple = tuple(int(t) for t in hand_tiles)
        combo_tuple = tuple(str(c) for c in combination_tiles)
        extra_json = json.dumps(extra, ensure_ascii=False, sort_keys=True) if extra is not None else ""
        waits_tuple = await run_calc_cpu(tingpai_dispatch, rule_key, hand_tuple, combo_tuple, extra_json)
        return set(waits_tuple)

    async def refresh_player_waiting_tiles(
        self,
        game_state: Any,
        player: Any,
        rule_key: str,
        hand_tiles: Iterable[int],
        combination_tiles: Iterable[str],
        *,
        extra: Any = None,
        tile_filter: Optional[Callable[[Set[int]], Set[int]]] = None,
    ) -> Set[int]:
        """Refresh player.waiting_tiles with L1 hand-dirty caching and race guard."""
        extra_json = json.dumps(extra, ensure_ascii=False, sort_keys=True) if extra is not None else ""
        sig = _hand_signature(rule_key, hand_tiles, combination_tiles, extra_json)
        if (
            getattr(player, "_waiting_cache_sig", None) == sig
            and getattr(player, "waiting_tiles", None) is getattr(player, "_waiting_cache_ref", None)
        ):
            return player.waiting_tiles

        expected_tick = getattr(game_state, "server_action_tick", None)
        hand_tuple = tuple(int(t) for t in hand_tiles)
        combo_tuple = tuple(str(c) for c in combination_tiles)
        waits_tuple = await run_calc_cpu(tingpai_dispatch, rule_key, hand_tuple, combo_tuple, extra_json)
        waiting = set(waits_tuple)
        if tile_filter is not None:
            waiting = set(tile_filter(waiting))

        # Race guard: do not write stale waits onto a closed/advanced game.
        if (
            getattr(game_state, "lifecycle_state", "running") == "running"
            and getattr(game_state, "server_action_tick", expected_tick) == expected_tick
        ):
            player.waiting_tiles = waiting
            player._waiting_cache_sig = sig
            player._waiting_cache_ref = waiting
        return waiting

    async def hepai(
        self,
        rule_key: str,
        hand_tiles: Iterable[int],
        combination_tiles: Iterable[str],
        way_to_hepai: Iterable[str],
        get_tile: int,
        extra: Any = None,
    ) -> Any:
        """Compute hepai / fan scoring off-loop via the unified CalcExecutor."""
        hand_tuple = tuple(int(t) for t in hand_tiles)
        combo_tuple = tuple(str(c) for c in combination_tiles)
        way_tuple = tuple(str(w) for w in way_to_hepai)
        extra_json = json.dumps(extra, ensure_ascii=False, sort_keys=True) if extra is not None else ""
        raw = await run_calc_cpu(
            hepai_dispatch, rule_key, hand_tuple, combo_tuple, way_tuple, int(get_tile), extra_json
        )
        return json.loads(raw)

    async def call_rule(
        self,
        mod_path: str,
        func_name: str,
        *args: Any,
        **kwargs: Any,
    ) -> Any:
        """Invoke any stateless rule calculation function on CalcExecutor via JSON."""
        payload_json = json.dumps({"args": list(args), "kwargs": kwargs}, ensure_ascii=False)
        raw = await run_calc_cpu(invoke_callable_json, mod_path, func_name, payload_json)
        return json.loads(raw)

    async def run_room_snapshot(self, game_state: Any, func: Callable[..., Any], /, *args, **kwargs) -> Any:
        """Run a room-serialized calculation snapshot on CalcExecutor (not BotExecutor)."""
        return await run_room_calc_cpu(game_state, func, *args, **kwargs)
