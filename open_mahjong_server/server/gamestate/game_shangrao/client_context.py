from ..public.local_hint_context import hint_context


def private_context(state, index, *, for_record=False):
    if not for_record and not (state.tips or state.count_tips):
        return None
    p = state.player_list[index]
    return hint_context("shangrao", p.hand_tiles, p.combination_tiles,
        indicator=state.indicator, config=state.detailed_config,
        flying_bao=p.discard_origin_tiles.count(state.bao),
        flying_seven_stars=p.flying_seven_stars,
        blocked="peida" in p.tag_list)
