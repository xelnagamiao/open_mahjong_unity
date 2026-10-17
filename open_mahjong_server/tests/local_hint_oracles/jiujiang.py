"""Offline reference for client parity tests; not a server hint endpoint."""
from functools import lru_cache
from server.game_calculation.jiujiang.patterns import structural_waits
from server.game_calculation.jiujiang.scoring import score

@lru_cache(maxsize=8192)
def wait_details(hand, melds, flowers, declared, heavenly=False):
    result = []
    for tile in structural_waits(tuple(sorted(hand)), melds):
        full = hand + (tile,)
        ron = score(full, melds, flowers, winning_tile=tile, declared_ready=declared, heaven_earth=heavenly)
        tsumo = None if ron else score(full, melds, flowers, winning_tile=tile, self_draw=True,
                                      declared_ready=declared, heaven_earth=heavenly)
        result.append((tile, ron["base_score"] if ron else tsumo["base_score"] if tsumo else 0,
                       "dianhe" if ron else "zimo" if tsumo else "wuyi"))
    return tuple(result)

@lru_cache(maxsize=1024)
def compute_hand(hand, melds, flowers, declared, first_discard_done):
    payload = {"source_hand_tiles":list(hand), "source_melds":list(melds),
        "waiting_tiles":[], "waiting_details":{}, "waiting_by_discard":{}, "waiting_details_by_discard":{}}
    def details(tiles, heavenly=False):
        rows = wait_details(tuple(sorted(tiles)), melds, flowers, declared, heavenly)
        return [tile for tile,_,_ in rows], {tile:{"points":points,"kind":kind} for tile,points,kind in rows}
    count = len(hand) + len(melds)*3
    if count == 13:
        payload["waiting_tiles"], payload["waiting_details"] = details(hand, not first_discard_done)
    elif count == 14:
        for tile in sorted(set(hand)):
            remaining = list(hand)
            remaining.remove(tile)
            waits, rows = details(remaining)
            payload["waiting_by_discard"][tile] = waits
            payload["waiting_details_by_discard"][tile] = rows
    return payload
