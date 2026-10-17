"""Own-hand inputs for local client calculation. No waits or scores are computed here."""
from ..public.local_hint_context import hint_context

def local_context(state, index):
    p = state.player_list[index]
    return hint_context("fuzhou", p.hand_tiles, p.combination_tiles,
        cuts=state.legal_discard_tiles(index), jin=state.jin, flowers=list(p.huapai_list),
        base_score=state.detailed_config["base_score"], dealer_streak=state.dealer_streak,
        discarded_gold=p.discarded_gold, golden_middle=p.golden_middle, youjin=p.youjin,
        locked_waits=sorted(p.locked_waits), youjin_active=state.youjin_owner is not None,
        last_four=state.in_last_four, blocked="peida" in p.tag_list)
