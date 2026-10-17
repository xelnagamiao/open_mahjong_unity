"""按牌谱和完整子规则重建番种；未接入番种的规则返回 NULL。"""
from collections import Counter
import re

from .statistics_catalog import BY_SUB_RULE


def parse_fan_label(value, rule):
    """返回番种及达成次数；显示番值不等于达成次数。"""
    if not isinstance(value, str):
        return None, 0
    name = value.strip()
    # 虹雀标签“番名*次数|合计番值”：|N 是番值，*N 才是达成数量。
    if rule == "hongque":
        name = re.sub(r"\|\d+(?:\.\d+)?$", "", name)
    count = 1
    multiple = re.fullmatch(r"(.+?)\s*[*×]\s*(\d+)", name)
    if multiple:
        name, count = multiple[1].strip(), int(multiple[2])
    # 敲麻房间可修改番值，不能因此拆成多个番种；保留正字（门风）等名称。
    name = re.sub(r"[（(]\d+(?:\.\d+)?\s*(?:番|台)[）)]$", "", name).strip()
    return name, count


def _fan_mapping(rule):
    if rule == "qingque":
        from .qingque.store_qingque import FAN_NAME_TO_FIELD
        return FAN_NAME_TO_FIELD
    if rule == "classical":
        from .classical.store_classical import FAN_NAME_TO_FIELD
        return FAN_NAME_TO_FIELD
    if rule == "changsha":
        from ..game_calculation.changsha.changsha_hepai_check import BIG_HU_NAMES, INITIAL_HU_NAMES
        # 鸟牌、倍数、骰子仅是付款信息，不是番种。
        return {name: name for name in BIG_HU_NAMES | set(INITIAL_HU_NAMES.values()) | {"小胡"}}
    if rule == "taiwan":
        from ..game_calculation.taiwan.fan import FAN_DEFINITIONS
        # 连庄拉庄为结算附加台数，不能当作达成 N 次的番种。
        return {definition[0]: definition[0] for definition in FAN_DEFINITIONS.values()}
    return None


def collect_subrule_fans(record, original_index, sub_rule):
    definition = BY_SUB_RULE.get(sub_rule)
    if not definition or not definition["fan_stats"]:
        return None
    if definition["rule"] == "guobiao":
        from .guobiao.record_analyzer import collect_fans_for_player
        return collect_fans_for_player(record, original_index)
    if definition["rule"] == "riichi":
        from .riichi.record_stats import analyze_riichi_record
        return (analyze_riichi_record(record, original_index) or {}).get("fan_stats", {})
    rule = definition["rule"]
    mapping = _fan_mapping(rule)
    if sub_rule == "changsha/exchange_six":
        mapping = {**mapping, **{name: name for name in ("十三将", "无对十三将", "全幺九", "报听", "明牌", "闷杠")}}
    from .guobiao.record_analyzer import _parse_hu_tick
    from .guobiao.round_score_utils import resolve_round_seats
    counts = Counter()
    fields = set(mapping.values()) if mapping is not None else set()

    def add(fans):
        if not isinstance(fans, (list, tuple)) or any("错和" in str(f) for f in fans):
            return
        for fan in fans:
            name, count = parse_fan_label(fan, rule)
            # 新规则直接保存结算器的规范名称，无须借用其他规则的字段。
            field = (name if name in fields else mapping.get(name)) if mapping is not None else name
            if field and count > 0:
                counts[field] += count

    for rd in (record.get("game_round") or {}).values():
        if not isinstance(rd, dict):
            continue
        if sub_rule == "changsha/exchange_six":
            from .changsha.exchange_six_record import statistics_round
            rd = statistics_round(rd)
        seats = resolve_round_seats(rd)
        if original_index not in range(len(seats)):
            continue
        for tick in rd.get("action_ticks") or []:
            hu = _parse_hu_tick(tick) if isinstance(tick, list) else None
            if not hu or hu["winner_seat"] != seats[original_index]:
                continue
            add(hu["yaku"])
        # 起手胡不结束回合，不能伪装成 hu_self，否则会污染基础和牌率。
        if rule == "changsha":
            for event in rd.get("initial_hu_events") or []:
                if isinstance(event, dict) and event.get("seat") == seats[original_index]:
                    add(event.get("fans"))
    return dict(counts)
