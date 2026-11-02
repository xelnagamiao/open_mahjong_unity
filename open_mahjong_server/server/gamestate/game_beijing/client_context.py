from ..public.local_hint_context import hint_context
from .actions import legal_cuts


def private_context(state, index, *, for_record=False):
    if not for_record and not (state.tips or state.count_tips):
        return None
    p = state.player_list[index]
    return hint_context("beijing", p.hand_tiles, p.combination_tiles, cuts=legal_cuts(state, index),
        indicator=state.indicator, hun=state.hun, config=state.detailed_config,
        discarded_hun=p.discarded_hun, tian_ting=p.tian_ting, after_kong=p.after_kong,
        opening=p.draw_kind == "opening", first_draw=state.natural_draw_count[index] <= 1 and not p.discard_origin_tiles,
        remaining=len(state.tiles_list), seat=index, repeat=state.repeat,
        has_draw_slot=p.has_draw_slot, drawn_tile=p.hand_tiles[-1] if p.has_draw_slot else None,
        blocked="peida" in p.tag_list)
