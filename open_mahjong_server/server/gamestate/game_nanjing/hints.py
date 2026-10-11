from ...game_calculation.nanjing.rules import score_hand, waiting_tiles


def build_hints(state, index):
    p = state.player_list[index]
    public = tuple(sorted(state.public_pungs()))
    key = (tuple(sorted(p.hand_tiles)), tuple(p.combination_tiles), tuple(sorted(p.huapai_list)),
           p.pung_claimed, p.passed_win_value, tuple(p.tag_list), public)
    cache = getattr(state, "_nanjing_hint_cache", {})
    if cache.get(index, (None,))[0] == key:
        return cache[index][1]

    def rows(hand, discarded=None):
        waits = sorted(waiting_tiles(hand, p.combination_tiles))
        details = {}
        water = p.passed_win_value
        if discarded is not None:
            declined = score_hand(hand+[discarded],p.combination_tiles,p.huapai_list,
                                  winning_tile=discarded,pung_claimed=p.pung_claimed,public_pungs=public)
            if declined:
                water = max(water or 0,declined.pattern_points)
        for tile in waits:
            q = score_hand(hand+[tile],p.combination_tiles,p.huapai_list,winning_tile=tile,
                           pung_claimed=p.pung_claimed,public_pungs=public,ignore_minimum=True)
            kind = "unmet" if "peida" in p.tag_list else "not_ready" if not q.qualified else (
                "self_only" if water is not None and q.pattern_points <= water else "ron")
            details[tile] = dict(kind=kind,points=q.points if q.qualified else 0,
                                 pattern_points=q.pattern_points,flower_count=len(p.huapai_list))
        return waits, details

    result = dict(source_hand_tiles=list(p.hand_tiles),source_melds=list(p.combination_tiles),
                  waiting_tiles=[],waiting_details={},waiting_by_discard={},waiting_details_by_discard={})
    if len(p.hand_tiles) % 3 == 1:
        result["waiting_tiles"],result["waiting_details"] = rows(p.hand_tiles)
    elif len(p.hand_tiles) % 3 == 2:
        for tile in sorted(state.legal_discard_tiles(index)):
            hand = list(p.hand_tiles)
            hand.remove(tile)
            waits, details = rows(hand,tile)
            result["waiting_by_discard"][tile] = waits
            result["waiting_details_by_discard"][tile] = details
    cache[index] = key,result
    state._nanjing_hint_cache = cache
    return result
