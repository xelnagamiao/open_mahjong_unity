from collections import Counter
from ...game_calculation.nanchang import rules as book

CHOWS = ("chi_left", "chi_mid", "chi_right")


def empty_actions():
    return {i:[] for i in range(4)}


def claim_tiles(action, tile):
    if action in ("peng", "gang"):
        return [tile] * (3 if action == "gang" else 2)
    if tile < 40:
        return [tile+n for n in {"chi_left":(-2,-1), "chi_mid":(-1,1), "chi_right":(1,2)}[action]]
    sequences = book.SEQUENCES_BY_TILE[tile]
    position = CHOWS.index(action) if len(sequences) > 1 else 0
    if position >= len(sequences) or len(sequences) == 1 and action != "chi_mid":
        return []
    return [t for t in sequences[position] if t != tile]


def legal_cuts(state, index):
    p = state.player_list[index]
    if p.declared_jingdiao:
        return set()
    tiles = set(p.hand_tiles)
    if "peida" in p.tag_list:
        return tiles-set(book.jing_tiles(state.indicator)) or tiles
    return tiles


def kong_tiles(state, index, kind):
    p = state.player_list[index]
    if not p.has_draw_slot or not state.tiles_list:
        return set()
    choices = ({t for t,n in Counter(p.hand_tiles).items() if n == 4} if kind == "angang"
               else {t for t in p.hand_tiles if f"k{t}" in p.combination_tiles})
    if "peida" in p.tag_list and kind != "angang":
        return set()
    if p.declared_jingdiao:
        choices &= {p.hand_tiles[-1]}
        choices = {t for t in choices if state.jingdiao_after_kong(index, t, kind)}
    return choices


def turn_actions(state, index, discard_only=False):
    p, result = state.player_list[index], []
    if not discard_only:
        if state.can_win(index, "self_draw", p.hand_tiles[-1]):
            result.append("hu_self")
        result.extend(k for k in ("angang", "jiagang") if kong_tiles(state,index,k))
    if legal_cuts(state,index):
        result.append("cut")
    return {**empty_actions(),index:result}


def claim_actions(state, actor, tile):
    result = empty_actions()
    for index,p in enumerate(state.player_list):
        if index == actor:
            continue
        if state.can_win(index,"discard",tile,payer=actor):
            result[index].append("hu")
        if "peida" not in p.tag_list and state.tiles_list:
            count = p.hand_tiles.count(tile)
            if count >= 2 and not p.declared_jingdiao:
                result[index].append("peng")
            if count == 3 and (not p.declared_jingdiao or state.jingdiao_after_kong(index,tile,"gang")):
                result[index].append("gang")
            if index == (actor+1)%4 and not p.declared_jingdiao:
                for action in CHOWS:
                    needed = claim_tiles(action,tile)
                    if len(needed) == 2 and tuple(sorted(needed+[tile])) in book.SEQUENCES and all(t in p.hand_tiles for t in needed):
                        result[index].append(action)
                if book.is_jingdiao(p.hand_tiles,p.combination_tiles,indicator=state.indicator):
                    result[index].append("nc_jingdiao")
        if result[index]:
            result[index].append("pass")
    return result


class ActionPolicy:
    def refresh_waiting_tiles(self,state,index,*,exclude_last_tile=False):
        p = state.player_list[index]
        hand = p.hand_tiles[:-1] if exclude_last_tile and p.has_draw_slot else p.hand_tiles
        p.waiting_tiles = book.waiting_tiles(hand,p.combination_tiles,indicator=state.indicator)
        return p.waiting_tiles
