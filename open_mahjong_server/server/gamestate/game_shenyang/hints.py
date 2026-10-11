from functools import lru_cache

from ...game_calculation.shenyang import rules as book


@lru_cache(maxsize=512)
def compute(hand, codes, index, closed, ready, long_hair, passed_fan, borrowed, final_four):
    def rows(standing, discarded=None):
        result = {}
        threshold = passed_fan
        if discarded is not None:
            passed = book.score(list(standing)+[discarded], codes, winning_tile=discarded,
                                self_draw=False, declared_ready=ready, long_hair=long_hair, borrowed=borrowed)
            if passed:
                threshold = max(threshold, min(book.payments(index, passed, discarder=p, closed=closed)[1][0]["fan"]
                                               for p in range(4) if p != index))
        for tile in sorted(book.waits(standing, codes, qualify=False)):
            full = list(standing) + [tile]
            draw = book.score(full, codes, winning_tile=tile, self_draw=True,
                              declared_ready=ready, long_hair=long_hair, borrowed=borrowed, last_four=final_four)
            ron = book.score(full, codes, winning_tile=tile, self_draw=False,
                             declared_ready=ready, long_hair=long_hair, borrowed=borrowed)
            if draw is None:
                result[tile] = dict(status="unmet", label="未满足")
                continue
            draw_changes, _ = book.payments(index, draw, closed=closed)
            legal_ron = []
            if ron and not final_four:
                for payer in range(4):
                    if payer == index:
                        continue
                    _, payment = book.payments(index, ron, discarder=payer, closed=closed)
                    if payment[0]["fan"] > threshold:
                        legal_ron.append(payment[0]["score"])
            if not legal_ron:
                result[tile] = dict(status="self_draw", label="仅自摸", self_draw_points=draw_changes[index])
            else:
                low, high = min(legal_ron), max(legal_ron)
                amount = str(low) if low == high else f"{low}–{high}"
                result[tile] = dict(status="win", label=f"点和{amount}分\n自摸{draw_changes[index]}分",
                                    ron_min=low, ron_max=high, self_draw_points=draw_changes[index])
        return result
    waiting, by_cut = {}, {}
    if len(hand) + len(codes) * 3 == 13:
        waiting = rows(hand)
    elif len(hand) + len(codes) * 3 == 14:
        for tile in (hand[-1],) if ready else sorted(set(hand)):
            standing = list(hand)
            standing.remove(tile)
            by_cut[tile] = rows(tuple(standing), tile)
    return dict(source_hand_tiles=list(hand), source_melds=list(codes), waiting=waiting, by_discard=by_cut)


def request(state, index):
    p = state.player_list[index]
    return (tuple(p.hand_tiles), tuple(p.combination_tiles), index,
            tuple(state.closed(i) for i in range(4)), p.declared_ready, state.long_hair,
            p.passed_fan, state.borrowed(index), state.final_four)
