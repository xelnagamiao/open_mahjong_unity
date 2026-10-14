"""Units of historical win counters; payment amounts are never inferred as fan.

The legacy column names are kept for protocol compatibility. Their units are
declared per subrule in statistics_rules.json and shared with both clients.
"""
import re

from .statistics_catalog import BY_SUB_RULE, DEFAULTS

SBR_FAN_VALUES = {
    "平和": 0, "杠": 1, "根": 1, "大对子": 1, "金钩钓": 1,
    "清一色": 2, "七对": 2, "杠上花": 1, "杠上炮": 1, "抢杠": 1, "海底": 1,
}


def statistic_units(title):
    rule = title.get("rule")
    definition = BY_SUB_RULE.get(title.get("sub_rule") or DEFAULTS.get(rule), {})
    return definition.get("win_stat_unit", "fan"), definition.get("deal_in_stat_unit", "fan")


def sichuan_win_fan(title, round_data, tick, tick_index):
    """Prefer the recorded uncapped fan; SBR legacy records retain exact fan names.

    Blood-flow's hu_score already is the rule's linear fan value, including its
    historical profile. Do not reinterpret it with today's blood-battle table.
    """
    saved = (round_data.get("win_fan_by_tick") or {}).get(str(tick_index))
    if saved is not None:
        if type(saved) is not int or saved < 0:
            raise ValueError("Invalid recorded Sichuan fan")
        return saved
    if str(title.get("sub_rule", "")).startswith("sichuan/xueliu"):
        return int(tick[2])
    names = tick[3]
    if not isinstance(names, list) or not names:
        raise ValueError("Sichuan record has no fan evidence")
    total = 0
    for raw in names:
        match = re.fullmatch(r"(.+?)(?:\s*[*×]\s*(\d+))?", str(raw).strip())
        name = match[1].strip() if match else ""
        if name not in SBR_FAN_VALUES:
            raise ValueError(f"Unknown historical Sichuan fan: {raw}")
        total += SBR_FAN_VALUES[name] * (int(match[2]) if match[2] else 1)
    return total
