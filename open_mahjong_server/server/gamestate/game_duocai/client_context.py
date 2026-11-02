from dataclasses import asdict

from ..public.local_hint_context import hint_context
from ...game_calculation.duocai import tiles as t
from .actions import legal_cuts


def private_context(state, index, *, for_record=False):
    if not for_record and not (state.tips or state.count_tips):
        return None
    p = state.player_list[index]
    visible = [v for other in state.player_list for v in other.discard_tiles]
    visible += [v for other in state.player_list for m in other.melds
                if not m.concealed or state.variant == 'riichi' or other is p for v in m.physical]
    visible += state.wall.indicators()
    return hint_context('duocai', p.hand_tiles, p.combination_tiles, cuts=legal_cuts(state, index),
        variant=state.variant, physical_melds=[asdict(m) for m in p.melds], visible_tiles=visible,
        seat_wind=41 + index, round_wind=41 + min(3, (state.current_round - 1) // 4),
        dealer=0, player_index=index, honba=state.honba, riichi_sticks=state.riichi_sticks,
        indicators=state.wall.indicators(), riichi=p.riichi, ippatsu=p.ippatsu,
        discards=list(p.discard_origin_tiles), temporary_furiten=p.temporary_furiten,
        riichi_furiten=p.riichi_furiten, has_draw_slot=p.has_draw_slot,
        after_kong=p.after_kong, remaining=len(state.wall.live), rule_version=state.rule_version)
