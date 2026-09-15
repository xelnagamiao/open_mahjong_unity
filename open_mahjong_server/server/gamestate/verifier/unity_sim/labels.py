"""对齐 Unity GameCanvas_ActionButton / ActionDisplay 的文案（国标）。"""
from __future__ import annotations

from typing import Dict, Iterable, List

from ..protocol import filter_client_actions

WINDS = {0: "东", 1: "南", 2: "西", 3: "北"}

BUTTON_TEXT: Dict[str, str] = {
    "peng": "碰",
    "gang": "杠",
    "hu_self": "自摸",
    "hu_flower": "花胡",
    "initial_hu": "起手胡",
    "sea_bottom": "要海底",
    "hu": "和",
    "hu_first": "和",
    "hu_second": "和",
    "hu_third": "和",
    "buhua": "补花",
    "angang": "暗杠",
    "buzhang": "补张",
    "jiagang": "加杠",
    "chi_left": "吃",
    "chi_mid": "吃",
    "chi_right": "吃",
    "jiuzhongjiupai": "九老峰回",
    "riichi_cut": "立直",
    "pass": "取消",
    "force_pass": "放弃",
    "ready": "准备",
}

DISPLAY_TEXT: Dict[str, str] = {
    "chi_left": "吃",
    "chi_mid": "吃",
    "chi_right": "吃",
    "peng": "碰",
    "angang": "暗杠",
    "buzhang": "补张",
    "jiagang": "加杠",
    "gang": "杠",
    "initial_hu": "起手胡",
    "hu_flower": "花胡",
    "hu": "和",
    "hu_self": "自摸",
    "hu_first": "和",
    "hu_second": "和",
    "hu_third": "和",
    "buhua": "补花",
    "riichi": "立直",
}

GROUP_KEYS = {
    "chi_left": "chi",
    "chi_mid": "chi",
    "chi_right": "chi",
    "angang": "angang",
    "buzhang": "buzhang",
    "jiagang": "jiagang",
}


def display_text(action: str) -> str:
    return DISPLAY_TEXT.get(action, "")


def build_action_buttons(action_list: Iterable[str], *, kind: str) -> List[dict]:
    """对齐 SetActionButton：cut 不生成按钮；吃/暗杠/加杠/补张合并。"""
    filtered = filter_client_actions(list(action_list or []), kind=kind)
    is_sea_bottom = "sea_bottom" in filtered
    buttons: List[dict] = []
    groups: Dict[str, dict] = {}
    for action in filtered:
        if action in ("cut", "hongque_supplement"):
            continue
        group = GROUP_KEYS.get(action)
        if group:
            if group not in groups:
                text = "吃" if group == "chi" else BUTTON_TEXT.get(action, action)
                button = {"text": text, "actions": [action]}
                groups[group] = button
                buttons.append(button)
            else:
                groups[group]["actions"].append(action)
            continue
        text = BUTTON_TEXT.get(action, action)
        if action == "pass" and is_sea_bottom:
            text = "不要"
        buttons.append({"text": text, "actions": [action]})
    return buttons
