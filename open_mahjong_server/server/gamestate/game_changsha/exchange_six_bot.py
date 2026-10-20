"""Efficiency decisions over private-hand/public-table snapshots for 换六张."""
from collections import Counter

from ...game_calculation.changsha.exchange_six import waiting_tiles, VALID_TILES, TERMINALS, JIANG_TILES
from ...game_calculation.changsha.changsha_hepai_check import _expand_meld
from ..public.ai.smart_bot_logic import calc_shanten, tile_to_34


def _distance(hand, melds):
    value = calc_shanten(hand, len(melds))
    counts = Counter(hand)
    if not melds:
        value = min(value, 6-sum(n//2 for n in counts.values()))
        kept = len(set(hand) & TERMINALS)
        jiang = {t:n for t,n in counts.items() if t in JIANG_TILES}
        kept += min(8, len(jiang)+int(any(n >= 2 for n in jiang.values())))
        value = min(value, 13-kept)
    for group in (JIANG_TILES, TERMINALS):
        if all(t in group for code in melds for t in _expand_meld(code)):
            value = min(value, 13-3*len(melds)-sum(n for t,n in counts.items() if t in group))
    return value


def _score(hand, melds, visible, reported_ready):
    hand = list(hand)
    distance = _distance(hand, melds)
    # Expensive full scoring is only needed at structural tenpai. The cheap
    # distance also includes the exchange rule's nonstandard winning shapes.
    waits = waiting_tiles(hand, melds, reported_ready=reported_ready) if distance <= 0 else set()
    if waits:
        held = Counter(hand)
        return (0, sum(max(0, 4-held[t]-visible[tile_to_34(t)]) for t in waits))
    held = Counter(hand)
    outs = sum(max(0, 4-held[t]-visible[tile_to_34(t)]) for t in VALID_TILES
               if held[t] < 4 and _distance(hand+[t], melds) < distance)
    # Ordinary structural tenpai may still lack the required 2/5/8 pair.
    return (min(-1, -distance), outs)


def _best_cut(hand, melds, visible, reported_ready, forced_cut=None):
    candidates = [forced_cut] if forced_cut is not None else sorted(set(hand))
    remaining = {}
    for tile in candidates:
        rest = list(hand)
        rest.remove(tile)
        remaining[tile] = rest
    closest = min(_distance(rest, melds) for rest in remaining.values())
    best = None
    for tile in candidates:
        rest = remaining[tile]
        if _distance(rest, melds) > max(1, closest):
            continue
        score = _score(rest, melds, visible, reported_ready)
        candidate = (score, tile == hand[-1], tile)
        if best is None or candidate > best:
            best = candidate
    return best[2], best[0]


def choose_action(phase, hand, melds, visible, actions, targets, reported_ready=False, forced_cut=None):
    for action in actions:
        if action.startswith("hu_"):
            return dict(action_type=action)
    for action in ("changsha_four_joy", "sea_bottom", "changsha_report_ready"):
        if action in actions:
            choices = targets.get(action, ())
            tile = None
            if choices:
                tile = max(choices, key=lambda t: _best_cut(hand, melds, visible, True, t)[1]) if action == "changsha_report_ready" else choices[0]
            return dict(action_type=action, target_tile=tile)
    if phase not in ("hand", "claims"):
        return dict(action_type="pass")
    if phase == "claims" and not targets:
        return dict(action_type="pass")
    if phase == "hand":
        tile, baseline = _best_cut(hand, melds, visible, reported_ready, forced_cut)
        decision = dict(action_type="cut", TileId=tile)
    else:
        baseline = _score(hand, melds, visible, reported_ready)
        decision = dict(action_type="pass")
    for action, tiles in targets.items():
        for tile in tiles:
            rest, codes = list(hand), list(melds)
            if action in ("angang", "jiagang", "gang", "buzhang"):
                if phase == "hand" and f"k{tile}" in codes:
                    needed = [tile]
                    codes[codes.index(f"k{tile}")] = f"g{tile}"
                else:
                    needed = [tile] * (4 if phase == "hand" else 3)
                    codes.append(f"{'G' if phase == 'hand' else 'g'}{tile}")
            elif action == "peng":
                needed = [tile, tile]
                codes.append(f"k{tile}")
            elif action.startswith("chi_"):
                offsets = {"chi_left": (-2, -1), "chi_mid": (-1, 1), "chi_right": (1, 2)}[action]
                needed = [tile+d for d in offsets]
                codes.append(f"s{sorted([tile, *needed])[1]}")
            else:
                continue
            for held in needed:
                rest.remove(held)
            score = (_best_cut(rest, codes, visible, reported_ready)[1]
                     if action == "peng" or action.startswith("chi_")
                     else _score(rest, codes, visible, reported_ready))
            if score > baseline:
                baseline = score
                decision = dict(action_type=action, target_tile=tile)
    return decision
