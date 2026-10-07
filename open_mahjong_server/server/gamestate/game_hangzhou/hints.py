"""Bounded event-driven structural hints; no hidden wall/opponent data."""

import asyncio
from dataclasses import dataclass
from functools import lru_cache

from ...game_calculation.hangzhou.rules import waiting_tiles
from .actions import legal_cuts
from .state_machine import Phase as P


@dataclass(frozen=True)
class HintRequest:
    """Only physical own-hand data and the already validated discard policy."""
    hand: tuple
    melds: tuple
    cuts: tuple
    rule_version: str


@lru_cache(maxsize=1024)
def _waits(hand, melds):
    return tuple(sorted(waiting_tiles(hand, melds)))


def current_request(state, index, *, for_record=False):
    p = state.player_list[index]
    if p.is_hu or state.machine.phase in (P.END, P.READY, P.FINISHED):
        return None
    if not for_record and (not (state.tips or state.count_tips) or p.is_bot):
        return None
    return HintRequest(tuple(p.hand_tiles), tuple(p.combination_tiles),
                       tuple(sorted(legal_cuts(state, index))), state.rule_version)


@lru_cache(maxsize=256)
def compute(request):
    """Worker-safe immutable output. Never receives a GameState or Player."""
    hand, melds = request.hand, request.melds
    waits, by_cut = (), ()
    if len(hand) + 3 * len(melds) == 13:
        waits = _waits(tuple(sorted(hand)), melds)
    elif len(hand) + 3 * len(melds) == 14:
        rows = []
        for tile in request.cuts:
            remaining = list(hand)
            remaining.remove(tile)
            rows.append((tile, _waits(tuple(sorted(remaining)), melds)))
        by_cut = tuple(rows)
    return waits, by_cut


def compute_batch(requests):
    return {request: compute(request) for request in requests}


def to_wire(request, values):
    waits, by_cut = values
    return dict(source_hand_tiles=list(request.hand), source_melds=list(request.melds),
                waiting_tiles=list(waits), waiting_by_discard={tile: list(row) for tile, row in by_cut})


def private_hints(state, index):
    request = current_request(state, index)
    if request is None:
        return {}
    if getattr(state, "_async_hints_enabled", False):
        values = state._prepared_hints.get(request)
        return {} if values is None else to_wire(request, values)
    return to_wire(request, compute(request))


def _snapshot_request(state, payload):
    """Freeze the packet's own-hand view, including intermediate cut/kong frames."""
    index = payload.get("player_index")
    info = payload.get("game_info") or {}
    public = info.get("hangzhou_info")
    if index not in range(4) or not public or not (state.tips or state.count_tips) or state.player_list[index].is_bot:
        return None
    if public["phase"] in (P.END.value, P.READY.value, P.FINISHED.value):
        return None
    hand = tuple(info["self_hand_tiles"])
    melds = tuple(info["players_info"][index]["combination_tiles"])
    cuts = ((hand[-1],) if info["self_has_draw_slot"] else ()) if public["forced_draw_discard"][index] else tuple(sorted(set(hand)))
    return HintRequest(hand, melds, cuts, public["rule_version"])


async def prepare(state, *, payloads=(), indices=()):
    """Calculate a batch off-loop, then publish only on the captured action tick."""
    snapshots = [(payload, _snapshot_request(state, payload)) for payload in payloads]
    requested = {request for _, request in snapshots if request is not None}
    requested.update(request for index in indices if (request := current_request(state, index)) is not None)
    # Replay hints remain available with live tips disabled and for bot seats.
    # Keep their immutable requests separate from private wire eligibility.
    recorded = {index: request for index in range(4)
                if (request := current_request(state, index, for_record=True)) is not None}
    requested.update(recorded.values())
    if not requested:
        return True
    tick = state.round_index, state.server_action_tick
    values = {request: state._prepared_hints[request] for request in requested if request in state._prepared_hints}
    missing = tuple(request for request in requested if request not in values)
    if missing:
        values.update(await asyncio.to_thread(compute_batch, missing))
    if tick != (state.round_index, state.server_action_tick):
        return False
    state._prepared_hints.update(values)
    for payload, request in snapshots:
        if request is None:
            continue
        hints = to_wire(request, values[request])
        for section in ("game_info", "ask_hand_action_info", "do_action_info"):
            public = (payload.get(section) or {}).get("hangzhou_info")
            if public is not None:
                public.update(hints)
    # Record only the current physical state, never a stale intermediate frame.
    for index, request in recorded.items():
        state.record_hints(index, to_wire(request, values[request]))
    # Retain the current batch only: no unbounded per-room hand cache.
    state._prepared_hints = values
    return True
