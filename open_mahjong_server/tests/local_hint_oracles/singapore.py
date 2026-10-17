"""Offline reference for client parity tests; not a server hint endpoint."""
from server.game_calculation.singapore import rules as book

def compute_hints(hand, melds, flowers, seat, round_wind):
    result = {"source_hand_tiles": list(hand), "source_melds": list(melds),
              "waiting_tiles": [], "waiting_details": {}, "waiting_by_discard": {}, "waiting_details_by_discard": {}}
    def waits(tiles):
        candidates = sorted(book.structural_waits(tuple(sorted(tiles)), melds))
        details = {}
        for tile in candidates:
            ron = book.score(tiles + (tile,), melds, flowers, winning_tile=tile, seat=seat, round_wind=round_wind)
            tsumo = book.score(tiles + (tile,), melds, flowers, winning_tile=tile, self_draw=True, seat=seat, round_wind=round_wind)
            value = ron or tsumo
            details[tile] = {"points": value["tai"] if value else 0, "kind": "dianhe" if ron else "zimo" if tsumo else "wuyi"}
        return candidates, details
    if len(hand) + len(melds)*3 == 13:
        result["waiting_tiles"], result["waiting_details"] = waits(tuple(hand))
    elif len(hand) + len(melds)*3 == 14 and not any(t in book.BONUS_TILES for t in hand):
        for tile in sorted(set(hand)):
            remaining = list(hand); remaining.remove(tile)
            candidates, details = waits(tuple(remaining))
            if candidates:
                result["waiting_by_discard"][tile] = candidates
                result["waiting_details_by_discard"][tile] = details
    return result
