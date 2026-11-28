"""高性能罗伯特加座门闩（纯函数，供 room_manager 与单测共用）。"""
from __future__ import annotations

from typing import Any, Dict, Optional

GUOBIAO_HEURISTIC_ALLOWED_SUB_RULES = frozenset(("guobiao/standard", "guobiao/blood_battle", "guobiao/four_jokers", "guobiao/huahun", "guobiao/sixteen"))
HONGQUE_HEURISTIC_ALLOWED_SUB_RULES = frozenset(("", "hongque/v1.6"))
GUOBIAO_HEURISTIC_UNSUPPORTED_MSG = "高性能罗伯特暂未支持该国标变种规则"
HONGQUE_W_HEURISTIC_UNSUPPORTED_MSG = "高性能罗伯特暂未支持虹雀W变种规则"
GUOBIAO_HEURISTIC_NON_GUOBIAO_MSG = "高性能罗伯特仅可加入国标、虹雀、青雀、川麻或立直房间"


def guobiao_heuristic_bot_reject_reason(room_data: Dict[str, Any]) -> Optional[str]:
    """若不可加座返回错误文案，否则 None。

    允许标准虹雀（不含虹雀W）、青雀、川麻（含单打/血流/2v2/复式）、立直麻将，
    或国标标准、血战到底等受支持变种（国标缺省子规则视为 standard）。
    """
    room_rule = room_data.get("room_rule")
    if room_rule in ("qingque", "sichuan", "riichi"):
        return None
    if room_rule == "hongque":
        sub_rule = room_data.get("sub_rule") or ""
        if sub_rule not in HONGQUE_HEURISTIC_ALLOWED_SUB_RULES:
            return HONGQUE_W_HEURISTIC_UNSUPPORTED_MSG
        return None
    if room_rule != "guobiao":
        return GUOBIAO_HEURISTIC_NON_GUOBIAO_MSG
    sub_rule = room_data.get("sub_rule") or "guobiao/standard"
    if sub_rule not in GUOBIAO_HEURISTIC_ALLOWED_SUB_RULES:
        return GUOBIAO_HEURISTIC_UNSUPPORTED_MSG
    return None
