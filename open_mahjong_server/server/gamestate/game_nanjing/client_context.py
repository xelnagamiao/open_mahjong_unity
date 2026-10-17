"""Own-hand inputs for local client calculation. No waits or scores are computed here."""

def local_context(state, index):
    from ..public.local_hint_context import hint_context
    p = state.player_list[index]
    return hint_context("nanjing", p.hand_tiles, p.combination_tiles,
        cuts=state.legal_discard_tiles(index), flowers=p.huapai_list,
        pung_claimed=p.pung_claimed, passed_win_value=p.passed_win_value,
        peida="peida" in p.tag_list, public_pungs=sorted(state.public_pungs()))
