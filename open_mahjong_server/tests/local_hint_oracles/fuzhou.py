"""Offline reference for client parity tests; not a server hint endpoint."""
from server.game_calculation.fuzhou import rules as book

def build_hints(state,index):
    p = state.player_list[index]
    key = (tuple(p.hand_tiles),tuple(p.combination_tiles),tuple(p.huapai_list),
           state.detailed_config["base_score"],state.dealer_streak,state.jin,p.discarded_gold,
           p.golden_middle,p.youjin,tuple(sorted(p.locked_waits)),state.youjin_owner,state.in_last_four)
    cache = getattr(state, "_reference_hint_cache", {})
    if cache.get(index,(None,))[0]==key:
        return cache[index][1]

    def rows(hand,discarded=None):
        waits = sorted(book.waiting_tiles(hand,p.combination_tiles,jin=state.jin)) if state.jin else []
        details = {}
        for t in waits:
            limited = bool(p.locked_waits) and t not in p.locked_waits
            self_only = p.discarded_gold or discarded==state.jin or p.golden_middle or state.youjin_owner is not None or t==state.jin or state.in_last_four
            # A waiting/discard preview quotes an ordinary future win. Use the
            # settlement calculator, but do not carry a previous kong draw or
            # assume a future heavenly win, rob-kong, or indicator claim.
            quote = None if limited else book.score_hand([*hand,t],p.combination_tiles,p.huapai_list,
                jin=state.jin,base_score=state.detailed_config["base_score"],include_witness=False,
                context=book.WinContext(source="self_draw" if self_only else "discard",winning_tile=t,
                    dealer_streak=state.dealer_streak,golden_middle=p.golden_middle,youjin=p.youjin))
            details[t] = dict(kind="unmet" if quote is None else "self_only" if self_only else "ron",
                              points=quote["points"] if quote is not None else 0)
        return waits,details

    result = dict(source_hand_tiles=list(p.hand_tiles),source_melds=list(p.combination_tiles),
        waiting_tiles=[],waiting_details={},waiting_by_discard={},waiting_details_by_discard={})
    if len(p.hand_tiles)%3==1:
        result["waiting_tiles"],result["waiting_details"] = rows(p.hand_tiles)
    elif len(p.hand_tiles)%3==2:
        for tile in sorted(state.legal_discard_tiles(index)):
            hand = list(p.hand_tiles)
            hand.remove(tile)
            result["waiting_by_discard"][tile],result["waiting_details_by_discard"][tile] = rows(hand,tile)
    cache[index] = key,result
    return result
