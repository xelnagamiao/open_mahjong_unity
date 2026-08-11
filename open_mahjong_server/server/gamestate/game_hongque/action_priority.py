"""虹雀多人鸣牌的权威动作名与优先级。"""
from __future__ import annotations


POSITION_SUFFIX = {1: "first", 2: "second", 3: "third"}

# 同类动作按距出牌者的顺时针座位排序；不同类动作严格保持
# 和 > 虹 > 碰 > 吃。荣和虽然带座位名，但 multi_ron 模式下同级收集。
HONGQUE_ACTION_PRIORITY = {
    "hu_first": 12,
    "hu_second": 12,
    "hu_third": 12,
    "hong_first": 11,
    "hong_second": 10,
    "hong_third": 9,
    "peng_first": 8,
    "peng_second": 7,
    "peng_third": 6,
    "chi_first": 5,
    "chi_second": 4,
    "chi_third": 3,
    "pass": 0,
    "discard": 0,
    "kong": 0,
    "supplement": 0,
    "ready": 0,
}


def relative_position_suffix(claimant_index: int | None,
                             discarder_index: int | None) -> str:
    if claimant_index is None or discarder_index is None:
        return "third"
    distance = (claimant_index - discarder_index) % 4
    return POSITION_SUFFIX.get(distance, "third")


def claim_action_type(kind: str, claimant_index: int | None,
                      discarder_index: int | None) -> str:
    prefix = {
        "rainbow": "hong",
        "triplet": "peng",
        "sequence": "chi",
        "win": "hu",
    }[kind]
    return f"{prefix}_{relative_position_suffix(claimant_index, discarder_index)}"

