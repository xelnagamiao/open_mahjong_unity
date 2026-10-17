"""Own-hand inputs for local client calculation. No waits or scores are computed here."""

def private_context(state,index,*,for_record=False):
    if not for_record and not (state.tips or state.count_tips):
        return {}
    from ..public.local_hint_context import hint_context
    p = state.player_list[index]
    request = hint_context("nanchang", p.hand_tiles, p.combination_tiles,
        indicator=state.indicator, index=index, discarded=p.discard_origin_tiles,
        missed_fan=p.missed_fan, declared_jingdiao=p.declared_jingdiao,
        peida="peida" in p.tag_list)["local_request"]
    return {"__local__": [{"local_request": request}]}
