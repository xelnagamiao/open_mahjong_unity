"""Private structural waits with authoritative baseline scoring for the UI.

These are previews, not permission to win: source-event bonuses and liability
are evaluated again by the action policy when an actual tile is available.
"""
from functools import lru_cache

from ...game_calculation.hongkong import HandContext, score_hand, structural_waits
from .action_check import legal_discards


@lru_cache(maxsize=4096)
def _wait_hints(hand,melds,flowers,rules,seat,round_wind,ready,streak):
    result = []
    for tile in sorted(structural_waits(hand,melds,rules)):
        common = dict(hand=hand+(tile,),melds=melds,flowers=flowers,winning_tile=tile,
                      seat_wind=41+seat,round_wind=round_wind,ready=ready,dealer=seat==0,
                      dealer_streak=streak)
        ron = score_hand(HandContext(**common),rules)
        zimo = score_hand(HandContext(**common,self_draw=True),rules)
        result.append(dict(tile=tile,ron=ron.is_win and not rules.self_draw_only,self_draw=zimo.is_win,
                           ron_fan=ron.fan,self_draw_fan=zimo.fan))
    return result


def private_hints(state,index):
    p = state.player_list[index]
    if not state.tips or p.is_bot or p.is_hu or any(t>50 for t in p.hand_tiles):
        return {}
    complete = len(p.hand_tiles)==state.rules.structure.concealed_tile_count(len(p.combination_tiles),complete=True)
    cuts = sorted(legal_discards(state,index)) if complete else [None]
    result = {}
    for cut in cuts:
        hand = list(p.hand_tiles)
        if cut is not None:
            hand.remove(cut)
        hand.sort()
        waits = _wait_hints(tuple(hand),tuple(p.combination_tiles),tuple(p.huapai_list),
                           state.rules,index,41+(state.current_round-1)//4%4,
                           p.ready_kind if p.declared_ready else '',state.dealer_streak)
        if waits:
            result[','.join(map(str,hand))] = waits
    return result
