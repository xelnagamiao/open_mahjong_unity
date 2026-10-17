"""Own-hand inputs for local client calculation. No waits or scores are computed here."""

def local_context(hand, melds, indicator, table, index, blocked=False, sea=False, previous_gold=None):
    from ..public.local_hint_context import hint_context
    return hint_context("wuhan", hand, melds, indicator=indicator, table=table,
                        index=index, blocked=blocked, sea=sea, previous_gold=previous_gold)
