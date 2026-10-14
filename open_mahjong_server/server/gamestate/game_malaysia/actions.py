"""Offers encode the exact physical fly use, never a client-supplied outcome."""
from collections import Counter
from ...game_calculation.malaysia import rules as R


def empty():
    return {i: [] for i in range(3)}


def cuts(p):
    return set(p.hand_tiles) & set(R.TILES) if not any(t in R.BONUS for t in p.hand_tiles) else set()


def kongs(s, i, kind):
    p = s.player_list[i]
    if not p.has_draw_slot or not s.tiles_list or any(t in R.BONUS for t in p.hand_tiles):
        return []
    if kind == "angang":
        return sorted(t for t, n in Counter(p.hand_tiles).items() if n == 4 and t in R.TILES)
    return sorted({m.logical[0] for m in p.melds if m.kind == "triplet" and R.FLY not in m.physical and m.logical[0] in p.hand_tiles})


def claims(p, tile, chow, wall):
    if tile not in R.TILES or not wall:
        return {}
    choices, c = {}, Counter(p.hand_tiles)
    for fly in range(3):
        if c[tile] >= 2 - fly and c[R.FLY] >= fly:
            choices[f"malaysia_peng:{fly}"] = R.Meld("triplet", (tile,) + (tile,) * (2 - fly) + (R.FLY,) * fly, (tile,) * 3)
    if c[tile] >= 3:
        choices["gang"] = R.Meld("kong", (tile,) * 4, (tile,) * 4)
    if chow and tile in R.DOTS:
        for start in range(max(21, tile - 2), min(tile, 27) + 1):
            other = tuple(t for t in range(start, start + 3) if t != tile)
            for wild in (0,) + other:
                physical = (tile,) + tuple(R.FLY if t == wild else t for t in other)
                needed = Counter(physical[1:])
                if all(c[t] >= n for t, n in needed.items()):
                    choices[f"malaysia_chi:{start + 1}:{wild}"] = R.Meld("sequence", physical, (tile,) + other)
    return choices


def turn(s, i, discard_only=False):
    p, result = s.player_list[i], []
    if not discard_only and s.score_win(i, "self_draw", p.last_drawn_tile) is not None:
        result.append("hu_self")
    if not discard_only:
        result.extend(a for a in ("angang", "jiagang") if kongs(s, i, a))
        if p.has_draw_slot and p.last_drawn_tile in R.TILES:
            result.extend(f"malaysia_redeem:{n}" for n, m in enumerate(p.melds)
                if m.kind == "triplet" and R.FLY in m.physical and m.logical[0] == p.last_drawn_tile)
    if R.FLY in p.hand_tiles and s.tiles_list:
        result.append("malaysia_fly")
    if cuts(p):
        result.append("cut")
    return {**empty(), i: result}


def responses(s, actor, tile, *, rob=False):
    result = empty()
    for i, p in enumerate(s.player_list):
        if i == actor:
            continue
        if s.score_win(i, "rob_kong" if rob else "discard", tile) is not None:
            result[i].append("hu")
        if not rob:
            result[i].extend(claims(p, tile, i == (actor + 1) % 3, s.tiles_list))
        if result[i]:
            result[i].append("pass")
    return result
