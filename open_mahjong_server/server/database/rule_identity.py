"""Canonical replay identities; legacy Jiandan names remain storage/API aliases only."""


def canonical_rule_identity(rule, sub_rule):
    if rule in ("jiandan", "nanque") or sub_rule in ("jiandan/standard", "nanque/standard"):
        return "zhongyong", "zhongyong/nanque"
    if rule == "guangdong" and not sub_rule:
        # The Guangdong room entry point without a profile historically creates Tuidao.
        return "guangdong", "guangdong/tuidao_mil2024"
    if rule == "shanghai" and not sub_rule:
        # 上海建房入口一直默认敲麻；清混碰有明确的独立子规则。
        return "shanghai", "shanghai/qiaoma"
    return rule, sub_rule


def canonical_game_record(record):
    title = record.get("game_title") or {}
    rule, sub_rule = canonical_rule_identity(title.get("rule"), title.get("sub_rule"))
    if (rule, sub_rule) == (title.get("rule"), title.get("sub_rule")):
        return record
    return {**record, "game_title": {**title, "rule": rule, "sub_rule": sub_rule}}
