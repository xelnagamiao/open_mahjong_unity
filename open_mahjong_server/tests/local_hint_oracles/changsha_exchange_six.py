"""Offline reference calculator; gameplay only transports the input request."""
from server.game_calculation.changsha.exchange_six import evaluate, waiting_tiles, big_weight, base_score


def rows(request, hand):
    c, melds = request['context'], request['melds']
    if c['blocked']:
        return []
    result = []
    for tile in sorted(waiting_tiles(hand, melds, reported_ready=c['reported_ready'])):
        _, fans = evaluate(hand + [tile], melds, reported_ready=c['reported_ready'])
        locked = c['locked_patterns']
        if locked is not None:
            frozen = locked.get(tile, locked.get(str(tile)))
            if frozen is None:
                continue
            fans = list(frozen) or ['小胡']
        for key, fan in (('reported_ready', '报听'), ('open_hand', '明牌')):
            if c[key]:
                fans = [f for f in fans if f != '小胡'] + [fan]
        weight = big_weight(fans)
        result.append(dict(tile=tile, score=base_score(weight), fans=sorted(fans),
                           blocked_small_hu=c['small_hu_forbidden'] and weight == 0))
    return result


def compute(request):
    hand, melds = list(request['hand']), list(request['melds'])
    result = dict(hand=hand, melds=melds, waits=rows(request, hand), discard_waits={})
    if len(hand) % 3 == 2:
        for tile in request['cuts']:
            rest = list(hand)
            rest.remove(tile)
            result['discard_waits'][str(tile)] = rows(request, rest)
    return result
