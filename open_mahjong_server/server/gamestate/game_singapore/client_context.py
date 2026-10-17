"""Own-hand inputs for local client calculation. No waits or scores are computed here."""

def local_context(hand, melds, flowers, seat, round_wind):
    from ..public.local_hint_context import hint_context
    return hint_context("singapore", hand, melds, flowers=list(flowers), seat=seat, round_wind=round_wind)
