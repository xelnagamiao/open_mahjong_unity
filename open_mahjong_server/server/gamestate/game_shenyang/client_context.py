"""Own-hand inputs for local client calculation. No waits or scores are computed here."""

def local_context(hand, codes, index, closed, ready, long_hair, passed_fan, borrowed, final_four):
    from ..public.local_hint_context import hint_context
    payload = hint_context("shenyang", hand, codes,
        cuts=[hand[-1]] if ready and hand else sorted(set(hand)),
        index=index, closed=list(closed), ready=ready, long_hair=long_hair,
        passed_fan=passed_fan, borrowed=list(borrowed), final_four=final_four)
    return payload

def request(state, index):
    p = state.player_list[index]
    return (tuple(p.hand_tiles), tuple(p.combination_tiles), index,
            tuple(state.closed(i) for i in range(4)), p.declared_ready, state.long_hair,
            p.passed_fan, state.borrowed(index), state.final_four)
