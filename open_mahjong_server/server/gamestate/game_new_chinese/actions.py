from collections import Counter

from ...game_calculation.new_chinese import rules
from ..game_beijing.actions import CHOWS, claim_tiles, empty_actions


def wait_context(state, index):
    if not state.hepai_limit:
        return {}
    p = state.player_list[index]
    return dict(hepai_limit=state.hepai_limit, exchange_three=state.detailed_config["exchange_three"],
                visible=state.visible_counts(), chain=max(p.chain, p.longest_chain))


def ready_cuts(hand, codes, *, visible=None, **context):
    result = {}
    for tile in set(hand):
        rest = list(hand)
        rest.remove(tile)
        after_cut = visible.copy() if visible is not None else None
        if after_cut is not None:
            after_cut[tile] += 1
        found = rules.waits(rest, codes, visible=after_cut, **context)
        if found:
            result[tile] = sorted(found)
    return result


def legal_cuts(state, index):
    p = state.player_list[index]
    if p.declared:
        return {p.hand_tiles[-1]} if p.has_draw_slot else set()
    if state.must_declare == index:
        return set(ready_cuts(p.hand_tiles, p.combination_tiles, **wait_context(state, index)))
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


def keeps_declared_waits(state, index, remaining, codes):
    p = state.player_list[index]
    if not p.declared:
        return True
    before = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
    context = wait_context(state, index)
    before_waits = rules.waits(before, p.combination_tiles, **context)
    if context:
        context["chain"] = max(p.chain + 1, p.longest_chain)
    after_waits = rules.waits(remaining, codes, **context)
    return bool(after_waits) and after_waits <= before_waits


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
        if keeps_declared_waits(state, index, rest, codes):
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
                context = wait_context(state, index)
                if context:
                    context["visible"].update(claim_tiles(chow, tile))
                if ready_cuts(*after, **context):
                    offered.append(chow + "_ting")
            if not rob and player.hand_tiles.count(tile) >= 2:
                offered.append("peng")
        if not rob and state.supplements:
            after = after_meld(player, "gang", tile)
            if after and keeps_declared_waits(state, index, *after):
                offered.append("gang")
        if offered:
            offered.append("pass")
    return result
