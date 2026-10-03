"""MIL 广东 2023 番种与计分；输入为共享癞子核心已证明合法的拆分。

本模块不求解牌型。实体手牌与逻辑拆分分别传入，绝不把鬼替换写回牌局。
"""

from collections import Counter
from dataclasses import dataclass
from typing import Tuple

from .config import DRAGONS, GHOSTS, HONORS, ORPHANS, WINDS


FANS = {
    "self_draw": ("自摸", 1), "concealed": ("门清", 1),
    "all_pungs": ("碰碰和", 4), "half_flush": ("混一色", 4),
    "rob_kong": ("抢杠", 4), "last_tile": ("海底", 4),
    "kong_flower": ("杠上花", 5), "seven_pairs": ("七对", 6),
    "full_flush": ("清一色", 8), "terminals_honors": ("幺九", 12),
    "little_three_dragons": ("小三元", 12), "little_four_winds": ("小四喜", 12),
    "heavenly": ("天和", 12), "earthly": ("地和", 12),
    "four_ghosts": ("四鬼和", 12), "nine_gates": ("九莲宝灯", 16),
    "big_three_dragons": ("大三元", 16), "big_four_winds": ("大四喜", 16),
    "thirteen_orphans": ("十三幺", 16),
}


@dataclass(frozen=True)
class Interpretation:
    """规则适配层的计分视图；groups 不包括副露，logical_hand 仅闭手。"""
    kind: str
    logical_hand: Tuple[int, ...]
    groups: Tuple[Tuple[int, ...], ...] = ()
    pair: int | None = None
    substitutions: Tuple[Tuple[int, int], ...] = ()
    winning_logical: int | None = None


@dataclass(frozen=True)
class Context:
    self_draw: bool = False
    rob_kong: bool = False
    last_tile: bool = False
    kong_flower: bool = False
    heavenly: bool = False
    earthly: bool = False
    discarded_ghosts: int = 0
    require_minimum_score: bool = True


def coefficient(physical_hand, discarded_ghosts):
    """用户确认的平台补则：无鬼×2 与出鬼每张×2 连续相乘。"""
    if type(discarded_ghosts) is not int or not 0 <= discarded_ghosts <= 4:
        raise ValueError("出鬼次数必须为 0 至 4")
    return (2 if not any(t in GHOSTS for t in physical_hand) else 1) * (1 << discarded_ghosts)


def _meld_tiles(melds):
    # 入口适配器已验证只允许碰、明杠、暗杠，不接受吃。
    return tuple(tuple([int(code[1:])] * (4 if code[0] in "gG" else 3)) for code in melds)


def _accidental(context):
    return {key for key in ("self_draw", "rob_kong", "last_tile", "kong_flower", "heavenly", "earthly")
            if getattr(context, key)}


def _nine_gates(shape, melds):
    if melds or shape.winning_logical is None or len(shape.logical_hand) != 14:
        return False
    counts = Counter(shape.logical_hand)
    if counts[shape.winning_logical] <= 0:
        return False
    counts[shape.winning_logical] -= 1
    return any(counts == Counter([suit * 10 + r for r in (1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9)])
               for suit in (1, 2, 3))


def score_interpretation(physical_hand, melds, shape: Interpretation, context: Context):
    """仅对一条合法见证计分；外层遍历所有合法见证取高，不截断。"""
    found = _accidental(context)
    if shape.kind == "four_ghosts":
        if sum(t in GHOSTS for t in physical_hand) != 4:
            raise ValueError("四鬼见证必须含四张实体鬼")
        found.discard("self_draw")
        found.add("four_ghosts")
    else:
        concealed = all(code.startswith("G") for code in melds)
        if concealed:
            found.add("concealed")
        all_tiles = shape.logical_hand + tuple(t for group in _meld_tiles(melds) for t in group)
        suits = {t // 10 for t in all_tiles if t < 40}
        honors = any(t in HONORS for t in all_tiles)
        if len(suits) == 1:
            found.add("half_flush" if honors else "full_flush")
        if shape.kind == "seven_pairs":
            found.add("seven_pairs")
        elif shape.kind == "thirteen_orphans":
            found.add("thirteen_orphans")
        elif shape.kind == "standard":
            groups = shape.groups + _meld_tiles(melds)
            triplets = {group[0] for group in groups if len(set(group)) == 1}
            if all(len(set(group)) == 1 for group in groups):
                found.add("all_pungs")
                if all(t in ORPHANS for t in all_tiles):
                    found.add("terminals_honors")
            dragons, winds = triplets & DRAGONS, triplets & WINDS
            if len(dragons) == 3:
                found.add("big_three_dragons")
            elif len(dragons) == 2 and shape.pair in DRAGONS - dragons:
                found.add("little_three_dragons")
            if len(winds) == 4:
                found.add("big_four_winds")
            elif len(winds) == 3 and shape.pair in WINDS - winds:
                found.add("little_four_winds")
            if _nine_gates(shape, melds):
                found.add("nine_gates")
        else:
            raise ValueError("未知广东计分形状")

    # 八-2-7：A 必然包含 B 时不重复计 B。互不蕴含的番可叠加。
    exclusions = {
        "seven_pairs": {"concealed"}, "thirteen_orphans": {"concealed"},
        "nine_gates": {"concealed", "full_flush"},
        "terminals_honors": {"all_pungs"}, "big_four_winds": {"all_pungs"},
        "heavenly": {"self_draw", "concealed"}, "earthly": {"self_draw", "concealed"},
        "kong_flower": {"self_draw"},
    }
    remove = set().union(*(exclusions.get(key, set()) for key in found))
    ids = tuple(key for key in FANS if key in found - remove)
    fan = sum(FANS[key][1] for key in ids)
    multiplier = coefficient(physical_hand, context.discarded_ghosts)
    base = fan * multiplier
    return {
        "is_win": fan >= 2 and (base >= 4 or not context.require_minimum_score),
        "fan": fan, "base_score": base, "coefficient": multiplier,
        "fan_ids": list(ids),
        "fan_names": [f"GD|{key}|{FANS[key][1]}|{FANS[key][0]}" for key in ids],
        "shape": shape.kind,
        "logical_hand": list(shape.logical_hand),
        "substitutions": [{"physical": physical, "logical": logical} for physical, logical in shape.substitutions],
        "discarded_ghosts": context.discarded_ghosts,
    }
