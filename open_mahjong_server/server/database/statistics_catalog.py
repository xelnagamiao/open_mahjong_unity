"""统计配表：一个入口只对应一个完整子规则，未知规则不得并入主规则。"""
import json
import re
from pathlib import Path

CATALOG = json.loads(Path(__file__).with_name("statistics_rules.json").read_text(encoding="utf-8"))
RULES = CATALOG["rules"]
BY_KEY = {row["key"]: row for row in RULES}
BY_SUB_RULE = {row["sub_rule"]: row for row in RULES}
DEFAULTS = {row["rule"]: row["sub_rule"] for row in reversed(RULES)}
# 旧的无 profile 广东入口实际创建推倒和。
DEFAULTS["guangdong"] = "guangdong/tuidao_mil2024"


def resolve_statistics_identity(rule, sub_rule=None, title=None, match_type=None):
    title = title or {}
    rule = rule or title.get("rule")
    sub_rule = sub_rule or title.get("sub_rule")
    if rule in ("jiandan", "nanque") or sub_rule in ("jiandan/standard", "nanque/standard"):
        return "zhongyong", "zhongyong/nanque"
    if sub_rule == "hongkong/new13":
        detail = title.get("detailed_config") or {}
        sub_rule = "hongkong/new13_" + ("lianhuise" if detail.get("new13_version") == "lianhuise" else "gametower")
    if not sub_rule or sub_rule == rule:
        mode = match_type or title.get("match_type") or ""
        if rule in ("guobiao", "riichi") and re.search(r"/3(?:_|$)|_sanma", mode):
            sub_rule = rule + "/sanma"
        else:
            sub_rule = DEFAULTS.get(rule)
    return rule, sub_rule


def selection(key):
    row = BY_KEY.get(key) or BY_SUB_RULE.get(key)
    if not row or not row["basic_stats"]:
        raise ValueError("此规则没有基础统计")
    return row


def resolve_record_statistics_identity(record, rule=None, sub_rule=None, match_type=None):
    """原始牌谱优先于旧参与者索引；索引可能已被错误合并到标准规则。"""
    title = record.get("game_title") or {}
    source_rule = title.get("rule")
    source_sub = title.get("sub_rule")
    mode = title.get("match_type") or match_type
    if source_sub:
        definition = BY_SUB_RULE.get(source_sub)
        source_rule = definition["rule"] if definition else source_rule or source_sub.split("/")[0]
        return resolve_statistics_identity(source_rule, source_sub, title, mode)
    # 早期牌谱未写子规则时才兼容旧索引，且不得借用其他主规则的索引。
    fallback_rule, fallback_sub = resolve_statistics_identity(rule, sub_rule, title, mode)
    definition = BY_SUB_RULE.get(fallback_sub)
    if definition:
        fallback_rule = definition["rule"]
    if source_rule:
        source_rule, source_default = resolve_statistics_identity(source_rule, None, title, mode)
        if source_rule != fallback_rule or (title.get("match_type") and source_default == source_rule + "/sanma"):
            return source_rule, source_default
    return fallback_rule, fallback_sub
