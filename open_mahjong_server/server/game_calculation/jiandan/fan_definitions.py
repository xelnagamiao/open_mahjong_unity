from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class FanDefinition:
    fan_id: str
    name: str
    value: int
    row: str
    repeatable: bool = False


FANS: dict[str, FanDefinition] = {}


def _fan(fan_id: str, name: str, value: int, row: str, repeatable: bool = False) -> None:
    FANS[fan_id] = FanDefinition(fan_id, name, value, row, repeatable)


_fan("two_wind_triplets", "二风刻", 1, "winds")
_fan("small_three_winds", "小三风", 3, "winds")
_fan("big_three_winds", "大三风", 8, "winds")
_fan("small_four_winds", "小四喜", 20, "winds")
_fan("big_four_winds", "大四喜", 20, "winds")

_fan("red_dragon", "中", 1, "dragons")
_fan("green_dragon", "发", 1, "dragons")
_fan("white_dragon", "白", 1, "dragons")
_fan("two_dragon_triplets", "二元刻", 3, "dragons")
_fan("small_three_dragons", "小三元", 8, "dragons")
_fan("big_three_dragons", "大三元", 20, "dragons")

_fan("all_simples", "断幺九", 1, "terminals_honors")
_fan("full_straight", "一气通贯", 3, "terminals_honors")
_fan("mixed_outside_hand", "混全带幺", 3, "terminals_honors")
_fan("mixed_terminals", "混幺九", 8, "terminals_honors")
_fan("pure_outside_hand", "纯全带幺", 8, "terminals_honors")
_fan("pure_terminals", "清幺九", 20, "terminals_honors")
_fan("thirteen_orphans", "十三幺九", 20, "terminals_honors")

_fan("half_flush", "混一色", 3, "color")
_fan("full_flush", "清一色", 8, "color")
_fan("all_honors", "字一色", 20, "color")
_fan("nine_gates", "九莲宝灯", 20, "color")

_fan("two_suit_triplets", "二色同刻", 1, "three_suit_number", repeatable=True)
_fan("small_three_suit_triplets", "小三色同刻", 3, "three_suit_number")
_fan("mixed_triple_chow", "三色同顺", 3, "three_suit_number")
_fan("three_suit_triplets", "三色同刻", 8, "three_suit_number")

_fan("closed_hand", "门前清", 1, "closed_opening")
_fan("heavenly_win", "天和", 20, "closed_opening")
_fan("earthly_win", "地和", 20, "closed_opening")

_fan("pinfu", "平和", 1, "shape")
_fan("all_triplets", "对对和", 3, "shape")
_fan("seven_pairs", "七对子", 3, "shape")

_fan("haitei", "海底捞月", 1, "last_tile")
_fan("houtei", "河底捞鱼", 1, "last_tile")

_fan("rinshan", "杠上开花", 1, "kong_win")
_fan("chankan", "抢杠", 1, "kong_win")

_fan("two_concealed_triplets", "二暗刻", 1, "concealed_triplets")
_fan("three_concealed_triplets", "三暗刻", 8, "concealed_triplets")
_fan("four_concealed_triplets", "四暗刻", 20, "concealed_triplets")

_fan("two_consecutive_triplets", "二连刻", 1, "consecutive_triplets", repeatable=True)
_fan("three_consecutive_triplets", "三连刻", 8, "consecutive_triplets")
_fan("four_consecutive_triplets", "四连刻", 20, "consecutive_triplets")

_fan("pure_double_chow", "一般高", 1, "identical_sequences")
_fan("twice_pure_double_chow", "二般高", 8, "identical_sequences")
_fan("pure_triple_chow", "一色三同顺", 8, "identical_sequences")
_fan("pure_quadruple_chow", "一色四同顺", 20, "identical_sequences")

_fan("one_kong", "一杠", 1, "kong_count")
_fan("two_kongs", "二杠", 3, "kong_count")
_fan("three_kongs", "三杠", 8, "kong_count")
_fan("four_kongs", "四杠", 20, "kong_count")


def fan_name(fan_id: str) -> str:
    return FANS[fan_id].name
