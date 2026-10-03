"""MIL 温州对共用癞子核心的政策适配，不包含第二套拆牌算法。"""

from collections import Counter
from functools import lru_cache

from ..jokers import ExternalMeld, JokerPolicy, can_win, iter_winning_shapes
from ..jokers import structural_waits as core_waits
from .rules import TILES, WHITE, natural_tile


@lru_cache(maxsize=256)
def _policy(caishen, natural=False, shapes=("standard", "eight_pairs")):
    return JokerPolicy(
        joker_tiles=() if natural else (caishen,),
        natural_map=() if caishen == WHITE else ((WHITE, caishen),),
        physical_limits=((caishen, 3),),
        meld_count=5,
        closed_cap=None,
        shapes=shapes,
    )


def _external(valid):
    return tuple(ExternalMeld(m.kind, m.physical, m.logical, m.concealed) for m in valid.melds)


@lru_cache(maxsize=32768)
def _flags(hand, external, caishen):
    standard = can_win(hand, external, _policy(caishen, shapes=("standard",)))
    eight = not external and can_win(hand, (), _policy(caishen, shapes=("eight_pairs",)))
    if caishen not in hand:
        return standard, eight, standard, eight
    natural_standard = standard and can_win(hand, external, _policy(caishen, True, ("standard",)))
    natural_eight = eight and can_win(hand, (), _policy(caishen, True, ("eight_pairs",)))
    return standard, eight, natural_standard, natural_eight


def shape_flags(valid):
    flags = _flags(tuple(sorted(valid.hand)), _external(valid), valid.caishen)
    return dict(zip(("standard", "eight_pairs", "natural_standard", "natural_eight_pairs"), flags))


def ordinary_complete(valid):
    return can_win(valid.hand, _external(valid), _policy(valid.caishen))


def structural_waits(valid):
    physical = Counter(valid.physical)
    ghosts = valid.hand.count(valid.caishen)
    if ghosts >= 3:
        return frozenset(t for t in TILES if physical[t] < (3 if t == valid.caishen else 4))
    waits = set(core_waits(valid.hand, _external(valid), _policy(valid.caishen)))
    if ghosts == 2:
        waits.add(valid.caishen)
    return frozenset(waits)


@lru_cache(maxsize=4096)
def _witness(hand, external, policy, winning_tile):
    """缓存不可变见证；实体顺序和和牌张也在键中，不能错复用牌谱下标。"""
    resolved = next(iter_winning_shapes(hand, external, policy, winning_tile), None)
    if resolved is None:
        raise RuntimeError("共用求解器的成和判定缺少对应拆分见证")
    return resolved


def winning_witness(valid, score, winning_tile):
    """最高倍率由类别证明确定，只取该类别的首个合法见证。

    这不是截断候选取最高分：温州同一类别中，组牌顺序、胜张位置与其它
    财神分配均不影响 1/2/4 倍计分。归位类别使用关闭万能替代的同一核心。
    """
    flags = shape_flags(valid)
    shape = score["shape"]
    if shape == "three_caishen":
        if not (flags["standard"] or flags["eight_pairs"]):
            return {
                "kind": "three_caishen",
                "qualifying_indices": [i for i, t in enumerate(valid.hand) if t == valid.caishen],
                "winning_index": max(i for i, t in enumerate(valid.hand) if t == winning_tile),
                "groups": [],
                "assignments": [],
            }
        shape = "standard" if flags["standard"] else "eight_pairs"
    natural = flags["natural_standard" if shape == "standard" else "natural_eight_pairs"]
    policy = _policy(valid.caishen, natural, (shape,))
    resolved = _witness(valid.hand, _external(valid), policy, winning_tile)
    return {
        "kind": resolved.kind,
        "winning_index": resolved.winning_index,
        "winning_group": resolved.winning_group,
        "winning_logical_tile": resolved.winning_logical_tile,
        "groups": [
            {"kind": g.kind, "logical": list(g.logical), "physical": list(g.physical),
             "indices": list(g.indices), "external": g.external, "concealed": g.concealed}
            for g in resolved.groups
        ],
        "assignments": [
            {"index": i, "physical": physical, "natural": natural_tile(physical, valid.caishen),
             "logical": resolved.assignment[i], "is_caishen": physical == valid.caishen,
             "substituted": physical == valid.caishen and resolved.assignment[i] != physical}
            for i, physical in enumerate(valid.hand)
        ],
    }


def clear_caches():
    _policy.cache_clear()
    _flags.cache_clear()
    _witness.cache_clear()


def cache_info():
    return {"policies": _policy.cache_info()._asdict(), "flags": _flags.cache_info()._asdict(),
            "witnesses": _witness.cache_info()._asdict()}
