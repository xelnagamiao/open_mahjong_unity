"""Offline reference for client parity tests; not a server hint endpoint."""
from server.game_calculation.nanchang import rules as book

def hand_key(hand):
    return ",".join(str(t) for t in sorted(hand))

def for_hand(state,index,hand,discarded):
    p = state.player_list[index]
    if not hasattr(state, "_reference_wait_cache"): state._reference_wait_cache = {}
    key = (index,tuple(sorted(hand)),tuple(p.combination_tiles),tuple(discarded),state.indicator,
           p.missed_fan,p.declared_jingdiao,p.after_kong,"peida" in p.tag_list,
           len(state.discard_log),len(state.domain_events))
    if key in state._reference_wait_cache:
        return state._reference_wait_cache[key]
    result = []
    for tile in sorted(book.waiting_tiles(hand,p.combination_tiles,indicator=state.indicator)):
        statuses,points,upper = {},{},{}
        for payer in range(4):
            if payer == index:
                continue
            status,quote = state.win_status(index,"discard",tile,payer=payer,hand=hand,discarded=discarded,private=True,preview=True)
            statuses[payer] = status
            points[payer] = quote["points"] if status == "allowed" else 0
            upper[payer] = points[payer]
            if status == "allowed" and quote["german"]:
                possible = book.score_hand(list(hand)+[tile],p.combination_tiles,indicator=state.indicator,
                    winning_tile=tile,self_draw=False,discarded=discarded,other_has_jing=False,
                    declared_jingdiao=p.declared_jingdiao,winner=index,dealer=0,payer=payer)
                upper[payer] = possible["points"]
        status,zimo = state.win_status(index,"self_draw",tile,hand=list(hand)+[tile],discarded=discarded,private=True,preview=True)
        candidates = set(statuses.values())
        label = "allowed" if "allowed" in candidates else "below_minimum" if status == "below_minimum" else "self_draw_only" if status == "allowed" else "unmet"
        result.append(dict(tile=tile,status=label,melds=list(p.combination_tiles),self_draw=status == "allowed",self_draw_points=zimo["points"] if status == "allowed" else 0,
                           ron_status=statuses,ron_points=points,points=max(points.values(),default=0),
                           points_min=min((v for v in points.values() if v>0),default=0),points_max=max(upper.values(),default=0)))
    state._reference_wait_cache[key] = result
    if len(state._reference_wait_cache) > 128:
        state._reference_wait_cache.pop(next(iter(state._reference_wait_cache)))
    return result
