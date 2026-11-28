"""Unified async calculation facade with L1 hand-dirty caching and CalcExecutor dispatch.

All mahjong rules use this single interface for tingpai / hepai checks so that:
  1. Unchanged hands return cached waiting tiles in 0ms (skipping pool dispatch).
  2. Cache misses run off the main asyncio event loop on CalcExecutor
     (Python 3.14 InterpreterPoolExecutor or spawn ProcessPoolExecutor).
  3. Only immutable primitives / tuples / JSON strings cross the GIL boundary.
"""
from __future__ import annotations

from collections import OrderedDict
import json
from typing import Any, Callable, Dict, Iterable, List, Optional, Set, Tuple

from .calc_executor import run_calc_cpu, run_room_calc_cpu
from .registry import discard_waits_dispatch, hepai_dispatch, invoke_callable_json, tingpai_dispatch

DEFAULT_L2_TINGPAI_MAXSIZE = 16384


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

    def __init__(self, sync_service: Any = None, *, l2_maxsize: int = DEFAULT_L2_TINGPAI_MAXSIZE):
        self.sync_service = sync_service
        self._l2_maxsize = max(0, int(l2_maxsize))
        self._l2_tingpai: OrderedDict[
            Tuple[str, Tuple[int, ...], Tuple[str, ...], str], Tuple[int, ...]
        ] = OrderedDict()
        self._l2_hits = 0
        self._l2_misses = 0

    def _l2_get(self, sig: Tuple[str, Tuple[int, ...], Tuple[str, ...], str]) -> Optional[Tuple[int, ...]]:
        if self._l2_maxsize <= 0:
            return None
        cached = self._l2_tingpai.get(sig)
        if cached is not None:
            self._l2_tingpai.move_to_end(sig)
            self._l2_hits += 1
            return cached
        self._l2_misses += 1
        return None

    def _l2_put(self, sig: Tuple[str, Tuple[int, ...], Tuple[str, ...], str], waits: Tuple[int, ...]) -> None:
        if self._l2_maxsize <= 0:
            return
        self._l2_tingpai[sig] = waits
        self._l2_tingpai.move_to_end(sig)
        if len(self._l2_tingpai) > self._l2_maxsize:
            self._l2_tingpai.popitem(last=False)

    def l2_cache_info(self) -> Dict[str, int]:
        return {
            "hits": self._l2_hits,
            "misses": self._l2_misses,
            "maxsize": self._l2_maxsize,
            "currsize": len(self._l2_tingpai),
        }

    def clear_l2_cache(self) -> None:
        self._l2_tingpai.clear()
        self._l2_hits = 0
        self._l2_misses = 0

    async def tingpai(
        self,
        rule_key: str,
        hand_tiles: Iterable[int],
        combination_tiles: Iterable[str],
        extra: Any = None,
    ) -> Set[int]:
        """Compute waiting tiles off-loop via main-process L2 cache + unified CalcExecutor."""
        extra_json = json.dumps(extra, ensure_ascii=False, sort_keys=True) if extra is not None else ""
        sig = _hand_signature(rule_key, hand_tiles, combination_tiles, extra_json)
        cached = self._l2_get(sig)
        if cached is not None:
            return set(cached)
        waits_tuple = await run_calc_cpu(tingpai_dispatch, rule_key, sig[1], sig[2], extra_json)
        self._l2_put(sig, waits_tuple)
        return set(waits_tuple)

    async def discard_waits(
        self,
        rule_key: str,
        hand_tiles: Iterable[int],
        combination_tiles: Iterable[str],
        extra: Any = None,
    ) -> Dict[int, Set[int]]:
        """Compute ``{discard_tile: waiting_tiles}`` in one batched CalcExecutor call."""
        hand_tuple = tuple(int(t) for t in hand_tiles)
        combo_tuple = tuple(str(c) for c in combination_tiles)
        extra_json = json.dumps(extra, ensure_ascii=False, sort_keys=True) if extra is not None else ""
        raw = await run_calc_cpu(discard_waits_dispatch, rule_key, hand_tuple, combo_tuple, extra_json)
        parsed = json.loads(raw)
        return {int(k): set(v) for k, v in parsed.items()}

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
        """Refresh player.waiting_tiles with L1 hand-dirty + L2 global caching and race guard."""
        extra_json = json.dumps(extra, ensure_ascii=False, sort_keys=True) if extra is not None else ""
        sig = _hand_signature(rule_key, hand_tiles, combination_tiles, extra_json)
        if (
            getattr(player, "_waiting_cache_sig", None) == sig
            and getattr(player, "waiting_tiles", None) is getattr(player, "_waiting_cache_ref", None)
        ):
            return player.waiting_tiles

        expected_tick = getattr(game_state, "server_action_tick", None)
        waits_tuple = self._l2_get(sig)
        if waits_tuple is None:
            waits_tuple = await run_calc_cpu(tingpai_dispatch, rule_key, sig[1], sig[2], extra_json)
            self._l2_put(sig, waits_tuple)
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
