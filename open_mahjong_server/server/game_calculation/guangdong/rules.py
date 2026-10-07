"""MIL 广东政策与共用癞子核心的薄适配层。"""

from collections import Counter
from copy import deepcopy
from dataclasses import replace
from functools import lru_cache

from ..jokers import JokerPolicy, can_win, iter_winning_shapes, structural_waits as core_waits
from .config import TILES, PHYSICAL_TILES, GHOSTS
from .scoring import Context, Interpretation, score_interpretation

POLICY = JokerPolicy(joker_tiles=GHOSTS, physical_tiles=PHYSICAL_TILES,
                     closed_cap=4, shapes=("standard", "seven_pairs", "thirteen_orphans"))


def valid_inventory(hand, melds, nominal):
    """四鬼的独立形状也必须满足真实库存；外露面子不计闭手逻辑上限。"""
    if not isinstance(hand, (list, tuple)) or not isinstance(melds, (list, tuple)):
        return False
    if len(melds) > 4 or len(hand) + 3 * len(melds) != nominal:
        return False
    if any(type(t) is not int or t not in PHYSICAL_TILES for t in hand):
        return False
    physical = list(hand)
    for code in melds:
        if not isinstance(code, str) or len(code) != 3 or code[0] not in "kgG" or not code[1:].isdigit():
            return False
        tile = int(code[1:])
        if tile not in TILES:
            return False
        physical.extend([tile] * (3 if code[0] == "k" else 4))
    return all(count <= (1 if tile in GHOSTS else 4) for tile, count in Counter(physical).items())


def is_complete(hand, melds=()):
    if not valid_inventory(hand, melds, 14):
        return False
    return sum(t in GHOSTS for t in hand) == 4 or can_win(hand, melds, POLICY)


@lru_cache(maxsize=8192)
def _structural_waits(hand, melds):
    if not valid_inventory(hand, melds, 13):
        return frozenset()
    waits = set(core_waits(hand, melds, POLICY))
    if sum(t in GHOSTS for t in hand) >= 3:
        for tile in PHYSICAL_TILES:
            complete = hand + (tile,)
            if sum(t in GHOSTS for t in complete) == 4 and valid_inventory(complete, melds, 14):
                waits.add(tile)
    return frozenset(waits)


def structural_waits(hand, melds=()):
    if not valid_inventory(hand, melds, 13):
        return frozenset()
    return _structural_waits(tuple(sorted(hand)), tuple(melds))


def _view(witness, physical_hand):
    return Interpretation(kind=witness.kind, logical_hand=witness.closed_logical_tiles,
        groups=tuple(group.logical for group in witness.melds if not group.external),
        pair=witness.pair or None,
        substitutions=tuple((physical_hand[use.index], use.logical) for use in witness.joker_uses),
        winning_logical=witness.winning_logical_tile or None)


@lru_cache(maxsize=256)
def _interpretations(hand, melds, winning_tile):
    # 点和/自摸、海底、过水门槛等上下文共用相同结构见证。
    # 缓存冻结的规则视图，避免每种上下文重新穷举；小上限约束每个工作进程的内存。
    views = []
    seen = set()
    # 19番只依赖逻辑面子、将、闭手多重集合及和牌逻辑张，不依赖鬼的
    # 非和牌实体身份或和牌张所在组。公共核心完整保留这些特征，
    # 每个等价类只返回一条真实实体见证，未截断任何计分可能性。
    for witness in iter_winning_shapes(hand, melds, POLICY, winning_tile=winning_tile,
                                      equivalence="logical_structure"):
        view = _view(witness, hand)
        feature = (view.kind, view.logical_hand, view.groups, view.pair, view.winning_logical)
        if feature in seen:
            continue
        seen.add(feature)
        views.append(view)
    return tuple(views)


@lru_cache(maxsize=16384)
def _score(hand, melds, winning_tile, context):
    if (not valid_inventory(hand, melds, 14) or winning_tile not in hand
            or not context.self_draw and winning_tile in GHOSTS
            or sum(t in GHOSTS for t in hand) + context.discarded_ghosts > 4):
        return None
    best = None
    if sum(t in GHOSTS for t in hand) == 4:
        best = score_interpretation(hand, melds, Interpretation("four_ghosts", ()), context)
    for view in _interpretations(hand, melds, winning_tile):
        candidate = score_interpretation(hand, melds, view, context)
        if best is None or candidate["base_score"] > best["base_score"]:
            best = candidate
    return best if best and best["is_win"] else None


def score(hand, melds=(), *, winning_tile=None, context=None):
    if not valid_inventory(hand, melds, 14):
        return None
    context = context or Context()
    winning_tile = hand[-1] if winning_tile is None else winning_tile
    return deepcopy(_score(tuple(sorted(hand)), tuple(melds), winning_tile, context))


@lru_cache(maxsize=8192)
def _waiting_scores(hand, melds, context, passed_base_score):
    result = []
    for tile in sorted(_structural_waits(hand, melds)):
        complete = tuple(sorted(hand + (tile,)))
        # 提示只描述基本牌型与起和门槛；过水和偶然番由实际动作判断。
        ron_context = Context(discarded_ghosts=context.discarded_ghosts,
                              require_minimum_score=context.require_minimum_score)
        self_context = replace(ron_context, self_draw=True)
        ron = _score(complete, melds, tile, ron_context)
        self_draw = _score(complete, melds, tile, self_context)
        best = max((item for item in (ron, self_draw) if item),
                   key=lambda item: item["base_score"], default=None)
        # 不达标的结构听牌也保留，客户端统一显示“未起和”。
        result.append({"tile": tile, "fan": best["fan"] if best else 0,
            "score": best["base_score"] if best else 0,
            "ron": bool(ron), "self_draw": bool(self_draw),
            "ron_fan": ron["fan"] if ron else 0,
            "self_draw_fan": self_draw["fan"] if self_draw else 0,
            "ron_score": ron["base_score"] if ron else 0,
            "self_draw_score": self_draw["base_score"] if self_draw else 0})
    return tuple(result)


def waiting_scores(hand, melds=(), *, context=None, passed_base_score=-1):
    if not valid_inventory(hand, melds, 13):
        return []
    return deepcopy(list(_waiting_scores(tuple(sorted(hand)), tuple(melds), context or Context(), passed_base_score)))


def clear_caches():
    for function in (_structural_waits, _interpretations, _score, _waiting_scores):
        function.cache_clear()


def evaluate_score_batch(requests):
    """进程工作池入口，所有输入/输出均为不可变政策或普通协议值。"""
    return [_score(tuple(hand), tuple(melds), tile, context) for hand, melds, tile, context in requests]
