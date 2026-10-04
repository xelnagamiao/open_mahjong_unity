"""MIL 红中2024的规则接口。通用癞子结构实现由 jokers 包提供。"""

from copy import deepcopy
from dataclasses import replace
from functools import lru_cache

from .scoring import (CONFIG, FANS, JOKER, NUMBERS, RULE_VERSION, SUB_RULE, TILES,
    Interpretation, bird_hits, kong_payments, meld_tiles, normalize_config,
    reverse_kong_payments, score_interpretation, valid_tiles, win_payments)


@lru_cache(maxsize=1)
def policy():
    # 延迟加载避免注册房间时加载求解器；仍只使用唯一共用核心。
    from ..jokers import JokerPolicy
    return JokerPolicy(joker_tiles=(JOKER,), logical_tiles=NUMBERS,
        physical_tiles=TILES, meld_count=4, closed_cap=4, shapes=("standard", "seven_pairs"))


def is_complete(hand, melds=()):
    if not valid_tiles(hand, melds):
        return False
    from ..jokers import can_win
    return can_win(hand, melds, policy())


def waits(hand, melds=()):
    if not valid_tiles(hand, melds, complete=False):
        return set()
    from ..jokers import structural_waits
    return set(structural_waits(hand, melds, policy()))


@lru_cache(maxsize=4096)
def _score_cached(hand, melds, winning_tile, replacement):
    from ..jokers import iter_winning_shapes
    best, best_key = None, None
    suits = {t//10 for t in hand if t != JOKER}
    suits.update(t//10 for code in melds for t in meld_tiles(code))
    fixed = int(JOKER not in hand) + int(replacement)
    pure_bound = 2 if len(suits) <= 1 else 0
    # 完整番表给出的可证明上界：大对子与一条龙不能同时成立；龙七对替代七对。
    # 先找七对（至多3番），再找基本型（形状番至多1番）。达到上界可安全结束，
    # 没有达到仍遍历所有解释。不能因为“4番已封顶”而少记更高原始番数。
    kinds = (("seven_pairs", 3), ("standard", 1)) if not melds else (("standard", 1),)
    for kind, shape_bound in kinds:
        ceiling = fixed + pure_bound + shape_bound
        if best is not None and best["raw_fan"] >= ceiling:
            continue
        shape_policy = replace(policy(), shapes=(kind,))
        searches = [shape_policy]
        if len(suits) == 1:
            pure_domain = tuple(t for t in NUMBERS if t//10 in suits)
            searches.insert(0, replace(shape_policy, logical_tiles=pure_domain))
        for search_index, search_policy in enumerate(searches):
            # 清一色分区已完整求解时，余下非清一色解释只能达到这个上界。
            if search_index and best is not None and best["raw_fan"] >= fixed + shape_bound:
                break
            for shape in iter_winning_shapes(hand, melds, search_policy, winning_tile=winning_tile):
                interpreted = Interpretation(shape.kind, shape.closed_logical_tiles,
                    tuple(group.logical for group in shape.melds), shape.winning_logical_tile,
                    tuple((use.index, use.physical, use.logical) for use in shape.joker_uses))
                detail = score_interpretation(hand, melds, interpreted, replacement=replacement)
                key = (detail["raw_fan"], tuple(-t for t in detail["logical_hand"]),
                       -shape.winning_logical_tile, tuple(detail["fan_names"]))
                if best_key is None or key > best_key:
                    best, best_key = detail, key
                if best["raw_fan"] == ceiling:
                    break
    return best


def score(hand, melds=(), *, winning_tile=None, replacement=False):
    if not valid_tiles(hand, melds) or type(replacement) is not bool:
        return None
    if winning_tile is None:
        winning_tile = hand[-1]
    if type(winning_tile) is not int or winning_tile not in hand:
        return None
    return deepcopy(_score_cached(tuple(hand), tuple(melds), winning_tile, replacement))
