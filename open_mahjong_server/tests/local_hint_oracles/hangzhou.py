"""Offline reference for client parity tests; not a server hint endpoint."""
from dataclasses import dataclass
from functools import lru_cache
from server.game_calculation.hangzhou.rules import JOKER, WinContext, evaluate_win, waiting_tiles

@dataclass(frozen=True)
class HintRequest:
    """Own hand, public cai history and the already validated discard policy."""
    hand: tuple
    melds: tuple
    cuts: tuple
    rule_version: str
    cai_piao_count: int = 0

@lru_cache(maxsize=1024)
def _waits(hand, melds):
    return tuple(sorted(waiting_tiles(hand, melds)))

@lru_cache(maxsize=1024)
def _wait_details(hand, melds, cai_piao_count):
    # Quote the next ordinary draw. A previous kong draw's bonus expires on
    # discard; do not assume the player will make another kong in the future.
    context = WinContext(cai_piao_count=cai_piao_count, pre_draw_tiles=hand)
    details = []
    for tile in _waits(hand, melds):
        score = evaluate_win(hand + (tile,), melds, tile, context)
        if score is not None:
            details.append((tile, score.fan_total, score.points))
    return tuple(details)

@lru_cache(maxsize=256)
def compute(request):
    """Worker-safe immutable output. Never receives a GameState or Player."""
    hand, melds = request.hand, request.melds
    details, by_cut = (), ()
    if len(hand) + 3 * len(melds) == 13:
        details = _wait_details(tuple(sorted(hand)), melds, request.cai_piao_count)
    elif len(hand) + 3 * len(melds) == 14:
        rows = []
        for tile in request.cuts:
            remaining = list(hand)
            remaining.remove(tile)
            # A white cut extends the consecutive cai history; every other
            # hypothetical cut clears it, exactly as HandFlow._turn does.
            cai_count = request.cai_piao_count + 1 if tile == JOKER else 0
            rows.append((tile, _wait_details(tuple(sorted(remaining)), melds, cai_count)))
        by_cut = tuple(rows)
    return details, by_cut

def compute_batch(requests):
    return {request: compute(request) for request in requests}

def to_wire(request, values):
    details, by_cut = values
    def scores(rows):
        return {tile: dict(fan=fan, base_score=points) for tile, fan, points in rows}
    return dict(source_hand_tiles=list(request.hand), source_melds=list(request.melds),
                source_cai_piao_count=request.cai_piao_count,
                waiting_tiles=[tile for tile, _, _ in details],
                waiting_by_discard={cut: [tile for tile, _, _ in row] for cut, row in by_cut},
                waiting_details=scores(details),
                waiting_details_by_discard={cut: scores(row) for cut, row in by_cut})

from server.gamestate.game_hangzhou.client_context import current_request, local_context, private_context as private_hints, attach_contexts as prepare
