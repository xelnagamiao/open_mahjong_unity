"""Own-hand inputs for local client calculation. No waits or scores are computed here."""
from dataclasses import dataclass
from .actions import legal_cuts
from .state_machine import Phase as P

@dataclass(frozen=True)
class HintRequest:
    """Own hand, public cai history and the already validated discard policy."""
    hand: tuple
    melds: tuple
    cuts: tuple
    rule_version: str
    cai_piao_count: int = 0

def current_request(state, index, *, for_record=False):
    p = state.player_list[index]
    if p.is_hu or state.machine.phase in (P.END, P.READY, P.FINISHED):
        return None
    if not for_record and (not (state.tips or state.count_tips) or p.is_bot):
        return None
    return HintRequest(tuple(p.hand_tiles), tuple(p.combination_tiles),
                       tuple(sorted(legal_cuts(state, index))), state.rule_version, p.cai_piao_count)

def local_context(request):
    from ..public.local_hint_context import hint_context
    result = hint_context("hangzhou", request.hand, request.melds,
                          cuts=request.cuts, cai_piao_count=request.cai_piao_count)
    result["source_cai_piao_count"] = request.cai_piao_count
    return result

def private_context(state, index):
    request = current_request(state, index)
    return {} if request is None else local_context(request)

def _snapshot_request(state, payload):
    """Freeze the packet's own-hand view, including intermediate cut/kong frames."""
    index = payload.get("player_index")
    info = payload.get("game_info") or {}
    public = info.get("hangzhou_info")
    if index not in range(4) or not public or not (state.tips or state.count_tips) or state.player_list[index].is_bot:
        return None
    if public["phase"] in (P.END.value, P.READY.value, P.FINISHED.value):
        return None
    hand = tuple(info["self_hand_tiles"])
    melds = tuple(info["players_info"][index]["combination_tiles"])
    cuts = ((hand[-1],) if info["self_has_draw_slot"] else ()) if public["forced_draw_discard"][index] else tuple(sorted(set(hand)))
    return HintRequest(hand, melds, cuts, public["rule_version"], public["cai_piao_counts"][index])

async def attach_contexts(state, *, payloads=(), indices=()):
    """Attach immutable inputs; the client computes waits and future-draw fans."""
    for payload in payloads:
        request = _snapshot_request(state, payload)
        if request is None:
            continue
        for section in ("game_info", "ask_hand_action_info", "do_action_info"):
            public = (payload.get(section) or {}).get("hangzhou_info")
            if public is not None:
                public.update(local_context(request))
    for index in range(4):
        request = current_request(state, index, for_record=True)
        if request is not None:
            state.record_hints(index, local_context(request))
    return True
