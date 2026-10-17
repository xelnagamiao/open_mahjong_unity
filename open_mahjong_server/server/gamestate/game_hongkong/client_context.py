"""Own-hand inputs for local client calculation. No waits or scores are computed here."""
from .action_check import legal_discards

def private_context(state,index):
    p = state.player_list[index]
    if not (state.tips or state.count_tips) or p.is_bot or p.is_hu or any(t>50 for t in p.hand_tiles):
        return {}
    from ..public.local_hint_context import hint_context
    request = hint_context("hongkong", p.hand_tiles, p.combination_tiles,
        cuts=legal_discards(state,index), profile=state.rules.sub_rule,
        flowers_enabled=state.rules.flowers, new13_version=state.rules.new13_version,
        self_draw_only=state.rules.self_draw_only, flowers=p.huapai_list,
        seat=index, round_wind=41+(state.current_round-1)//4%4,
        ready=p.ready_kind if p.declared_ready else '', dealer_streak=state.dealer_streak)["local_request"]
    return {"__local__": [{"local_request": request}]}
