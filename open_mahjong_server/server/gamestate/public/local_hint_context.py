"""Own-hand inputs for local hint calculation; never performs tile search."""
from copy import deepcopy


def hint_context(rule, hand, melds, *, cuts=None, **context):
    request = dict(rule=rule, hand=list(hand), melds=list(melds), context=deepcopy(context))
    if cuts is not None:
        request["cuts"] = sorted(set(cuts))
    return dict(source_hand_tiles=list(hand), source_melds=list(melds),
                local_request=request)
