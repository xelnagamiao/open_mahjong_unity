"""按实体手牌快照生成提示。只读本家手牌/副露，不读取牌墙或他家牌。"""
from functools import lru_cache

from ...game_calculation.hongzhong import rules as book


@lru_cache(maxsize=4096)
def waiting(hand, melds):
    return tuple(sorted(book.waits(hand, melds)))


@lru_cache(maxsize=4096)
def ordinary_wait_details(hand, melds):
    """Basic-shape scores only: no guessed replacement, birds or other timing."""
    result = []
    for tile in waiting(hand, melds):
        detail = book.score(list(hand) + [tile], melds, winning_tile=tile, replacement=False)
        if detail is not None:
            result.append((tile, detail["fan"], detail["base_score"]))
    return tuple(result)


def _details(hand, melds):
    return {tile: {"fan": fan, "base_score": points}
            for tile, fan, points in ordinary_wait_details(tuple(sorted(hand)), melds)}


@lru_cache(maxsize=1024)
def compute_hand(hand, melds, winning_tile, replacement):
    payload = {"source_hand_tiles": list(hand), "source_melds": list(melds),
               "waiting_tiles": [], "waiting_by_discard": {}, "hint_version": 2,
               "waiting_details": {}, "waiting_details_by_discard": {}}
    total = len(hand) + 3*len(melds)
    if total == 13:
        payload["waiting_tiles"] = list(waiting(tuple(sorted(hand)), melds))
        payload["waiting_details"] = _details(hand, melds)
    elif total == 14:
        for tile in sorted(set(hand)):
            remaining = list(hand); remaining.remove(tile)
            payload["waiting_by_discard"][tile] = list(waiting(tuple(sorted(remaining)), melds))
            payload["waiting_details_by_discard"][tile] = _details(remaining, melds)
    detail = book.score(hand, melds, winning_tile=winning_tile, replacement=replacement) if total == 14 else None
    return payload, detail


def match_hint(payload, hand, melds):
    """精确匹配；也允许从刚才的摸牌快照读取这次弃牌后的听口。"""
    if not payload or sorted(payload["source_melds"]) != sorted(melds):
        return None
    original = payload["source_hand_tiles"]
    if sorted(original) == sorted(hand):
        return payload
    if len(original) == len(hand) + 1:
        for tile, waits in payload["waiting_by_discard"].items():
            remaining = list(original); remaining.remove(tile)
            if sorted(remaining) == sorted(hand):
                return {"source_hand_tiles": list(hand), "source_melds": list(melds),
                        "waiting_tiles": waits, "waiting_by_discard": {},
                        "hint_version": payload.get("hint_version", 1),
                        "waiting_details": payload.get("waiting_details_by_discard", {}).get(tile, {}),
                        "waiting_details_by_discard": {}}
    return None
