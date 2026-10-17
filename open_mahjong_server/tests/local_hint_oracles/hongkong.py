"""Offline reference for client parity tests; not a server hint endpoint."""
from functools import lru_cache
from server.game_calculation.hongkong import HandContext, score_hand, structural_waits

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
