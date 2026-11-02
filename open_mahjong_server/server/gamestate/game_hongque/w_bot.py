from collections import Counter
from functools import lru_cache

from .w_rules import FACES, available_groups, _subtract, is_winning_hand


@lru_cache(maxsize=8192)
def _unmatched(hand, pair_used=False):
    if not hand:
        return 0
    best = 1 + _unmatched(hand[1:], pair_used)
    for group in available_groups(hand):
        if hand[0] not in group or (len(group) == 2 and pair_used):
            continue
        best = min(best, _unmatched(_subtract(hand, group), pair_used or len(group) == 2))
    all_pairs = sum(count % 2 for count in Counter(hand).values())
    return min(best, all_pairs)


def value(hand, melds=(), visible=(), include_colourless=True):
    hand = tuple(sorted(hand))
    visible = Counter(visible)
    owned = Counter(hand + tuple(tile for meld in melds for tile in meld.get("tiles", ())))
    waits = sum(max(0, 2 - visible[tile]) for tile in FACES
                if (include_colourless or tile[0] != "H") and owned[tile] < 2
                and is_winning_hand((*hand, tile), melds))
    partial = sum(1 for i, a in enumerate(hand) for b in hand[i + 1:]
                  if a == b or (a[2] == b[2] and "H" not in (a[0], b[0]))
                  or (a[0] == b[0] and abs(int(a[2]) - int(b[2])) <= 4))
    return (1 if waits else 0, waits, -_unmatched(hand), partial)


def choose_discard(hand, melds=(), visible=(), drawn_tile=None, include_colourless=True):
    choices = []
    for tile in sorted(set(hand)):
        rest = list(hand)
        rest.remove(tile)
        choices.append((value(rest, melds, visible, include_colourless), tile == drawn_tile, tile))
    return max(choices)[2] if choices else None


def choose_turn_plan(hand, melds, visible, kong, *, drawn_tile=None, include_colourless=True, **kwargs):
    if is_winning_hand(hand, melds):
        return {"action": "win"}
    if any(item.get("kind") == "kong_win" for item in kong):
        return {"action": "win"}
    return {"action": "discard", "tile": choose_discard(hand, melds, visible, drawn_tile, include_colourless)}


def choose_claim_plan(hand, melds, candidates, visible, *, include_colourless=True):
    win = next((candidate for candidate in candidates if candidate.get("kind") == "win"), None)
    if win:
        return {"action": "claim", "candidate_id": win["id"]}
    baseline = value(hand, melds, visible, include_colourless)
    best = None
    for candidate in candidates:
        rest = list(_subtract(hand, candidate["hand_tiles"]))
        opened = [*melds, {"tiles": candidate["tiles"]}]
        discard = choose_discard(rest, opened, visible, include_colourless=include_colourless)
        if discard is None:
            continue
        rest.remove(discard)
        rank = value(rest, opened, visible, include_colourless)
        if rank > baseline and (best is None or rank > best[0]):
            best = rank, candidate
    return {"action": "claim", "candidate_id": best[1]["id"]} if best else {"action": "pass"}
