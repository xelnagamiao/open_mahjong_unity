"""Own-hand inputs for local client calculation. No waits or scores are computed here."""

def local_context(hand, melds, flowers, declared, first_discard_done):
    from ..public.local_hint_context import hint_context
    return hint_context("jiujiang", hand, melds, flowers=list(flowers), declared=declared,
                        first_discard_done=first_discard_done)
