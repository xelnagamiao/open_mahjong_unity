from collections import Counter

from ...game_calculation.new_chinese import rules
from ..game_beijing.actions import CHOWS, claim_tiles, empty_actions


def ready_cuts(hand, codes):
    result = {}
    for tile in set(hand):
        rest = list(hand)
        rest.remove(tile)
        found = rules.waits(rest, codes)
        if found:
            result[tile] = sorted(found)
    return result


def legal_cuts(state, index):
    p = state.player_list[index]
    if p.declared or state.must_declare == index:
        return set(ready_cuts(p.hand_tiles, p.combination_tiles))
    return set(p.hand_tiles)


def after_meld(p, action, tile):
    needed = claim_tiles(action, tile)
    if not needed or not Counter(needed) <= Counter(p.hand_tiles):
        return None
    rest = list(p.hand_tiles)
    for t in needed:
        rest.remove(t)
    code = ("g" if action == "gang" else "k") + str(tile)
    if action in CHOWS:
        code = "s" + str(sorted([*needed, tile])[1])
    return rest, [*p.combination_tiles, code]


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    if not state.supplements or state.must_declare == index:
        return set()
    result = set()
    for tile, n in Counter(p.hand_tiles).items():
        codes = list(p.combination_tiles)
        rest = list(p.hand_tiles)
        if kind == "angang" and n == 4:
            codes.append("G" + str(tile))
            rest = [t for t in rest if t != tile]
        elif kind == "jiagang" and "k" + str(tile) in codes:
            codes[codes.index("k" + str(tile))] = "g" + str(tile)
            rest.remove(tile)
        else:
            continue
        if not p.declared or rules.waits(rest, codes):
            result.add(tile)
    return result


def claim_actions(state, actor, tile, rob=False):
    result = empty_actions()
    if tile == state.bao:
        return result
    for index, player in enumerate(state.player_list):
        if index == actor or "peida" in player.tag_list:
            continue
        offered = result[index]
        if state.quote(index, tile, "rob" if rob else "discard", actor):
            offered.append("hu")
        if not player.declared:
            for chow in CHOWS:
                after = after_meld(player, chow, tile)
                if not after:
                    continue
                if not rob and index == (actor + 1) % 4:
                    offered.append(chow)
                if ready_cuts(*after):
                    offered.append(chow + "_ting")
            if not rob and player.hand_tiles.count(tile) >= 2:
                offered.append("peng")
        if not rob and state.supplements:
            after = after_meld(player, "gang", tile)
            if after and (not player.declared or rules.waits(*after)):
                offered.append("gang")
        if offered:
            offered.append("pass")
    return result
