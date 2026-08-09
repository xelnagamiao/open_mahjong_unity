"""高性能罗伯特加座门闩（纯函数，供 room_manager 与单测共用）。"""
from __future__ import annotations

from typing import Any, Dict, Optional

GUOBIAO_HEURISTIC_ALLOWED_SUB_RULE = "guobiao/standard"
GUOBIAO_HEURISTIC_UNSUPPORTED_MSG = "高性能罗伯特暂未支持该国标变种规则"
GUOBIAO_HEURISTIC_NON_GUOBIAO_MSG = "高性能罗伯特仅可加入国标标准或虹雀房间"


def guobiao_heuristic_bot_reject_reason(room_data: Dict[str, Any]) -> Optional[str]:
    """若不可加座返回错误文案，否则 None。

    允许虹雀，或 room_rule==guobiao 且 sub_rule==guobiao/standard
    （国标缺省子规则视为 standard）。
    """
    room_rule = room_data.get("room_rule")
    if room_rule == "hongque":
        return None
    if room_rule != "guobiao":
        return GUOBIAO_HEURISTIC_NON_GUOBIAO_MSG
    sub_rule = room_data.get("sub_rule") or GUOBIAO_HEURISTIC_ALLOWED_SUB_RULE
    if sub_rule != GUOBIAO_HEURISTIC_ALLOWED_SUB_RULE:
        return GUOBIAO_HEURISTIC_UNSUPPORTED_MSG
    return None
