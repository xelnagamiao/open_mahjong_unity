from collections import Counter
from ...game_calculation.shangrao import rules as book

CHOWS = ("chi_left", "chi_mid", "chi_right")


def empty_actions():
    return {i: [] for i in range(4)}


def claim_tiles(action, tile):
    if action in ("peng", "gang"):
        return [tile] * (3 if action == "gang" else 2)
    if action not in CHOWS or tile >= 40:
        return []
    needed = [tile + n for n in {"chi_left": (-2, -1), "chi_mid": (-1, 1), "chi_right": (1, 2)}[action]]
    return needed if tuple(sorted([*needed, tile])) in book.SEQUENCES else []


def legal_cuts(state, index):
    return set(state.player_list[index].hand_tiles)


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    if not state.can_draw_kong() or len(p.hand_tiles) + len(p.combination_tiles) * 3 != 14:
        return set()
    if kind == "angang":
        return {t for t, n in Counter(p.hand_tiles).items() if n == 4}
    if kind == "jiagang" and "peida" not in p.tag_list:
        return {t for t in p.hand_tiles if f"k{t}" in p.combination_tiles}
    return set()


def turn_actions(state, index, after_meld=False):
    p, result = state.player_list[index], []
    if not after_meld and p.hand_tiles and state.can_win(index, "self_draw", p.hand_tiles[-1]):
        result.append("hu_self")
    result.extend(k for k in ("angang", "jiagang") if kong_tiles(state, index, k))
    if legal_cuts(state, index):
        result.append("cut")
    return {**empty_actions(), index: result}


def claim_actions(state, actor, tile):
    result = empty_actions()
    if len(state.tiles_list) <= 1:
        return result
    for index, p in enumerate(state.player_list):
        if index == actor or "peida" in p.tag_list:
            continue
        count = p.hand_tiles.count(tile)
        if count >= 2:
            result[index].append("peng")
        if count == 3 and state.can_draw_kong():
            result[index].append("gang")
        if index == (actor + 1) % 4:
            for action in CHOWS:
                needed = claim_tiles(action, tile)
                if len(needed) == 2 and all(t in p.hand_tiles for t in needed):
                    result[index].append(action)
        if result[index]:
            result[index].append("pass")
    return result


class ActionPolicy:
    def refresh_waiting_tiles(self, state, index, *, exclude_last_tile=False):
        state.player_list[index].waiting_tiles = set()
        return set()
