"""对齐 Unity RuleManifest 的牌谱回放旗标。

按 game_title.rule / sub_rule 解析，对应各族 *RuleBootstrap 的登记值。
新增规则时只改本表，不要在 goto_action / meld_codec 里再写 startswith。
"""
from __future__ import annotations

from dataclasses import dataclass, replace
from typing import Any, Mapping, Optional


def _title_str(game_title: Optional[Mapping[str, Any]], key: str, default: str = "") -> str:
    if not game_title:
        return default
    value = game_title.get(key, default)
    return default if value is None else str(value)


def rule_id_from_title(game_title: Optional[Mapping[str, Any]] = None, rule: Optional[str] = None) -> str:
    if rule:
        text = str(rule).strip().lower()
        if "/" in text:
            return text.split("/", 1)[0]
        return text
    raw = _title_str(game_title, "rule").lower()
    if raw:
        return raw.split("/", 1)[0]
    sub = _title_str(game_title, "sub_rule").lower()
    if "/" in sub:
        return sub.split("/", 1)[0]
    return sub


@dataclass(frozen=True)
class RecordRuleFlags:
    rule_id: str = ""
    kong_replacement_from_front: bool = False
    replacement_from_tail_end: bool = False
    jiagang_extends_last_meld: bool = False
    face_down_ankan: bool = False
    peek_ankan: bool = False
    record_hu_tile_tick_index: int = 5
    infers_dingque_from_discards: bool = False
    hu_tick_follows_shuhewei: bool = False
    record_tracks_riichi_field: bool = False


_BY_RULE: dict[str, RecordRuleFlags] = {
    "sichuan": RecordRuleFlags(
        rule_id="sichuan",
        kong_replacement_from_front=True,
        peek_ankan=True,
        infers_dingque_from_discards=True,
    ),
    "hongque": RecordRuleFlags(
        rule_id="hongque",
        jiagang_extends_last_meld=True,
    ),
    "changsha": RecordRuleFlags(
        rule_id="changsha",
        face_down_ankan=True,
    ),
    "riichi": RecordRuleFlags(
        rule_id="riichi",
        peek_ankan=True,
        record_tracks_riichi_field=True,
    ),
    "classical": RecordRuleFlags(
        rule_id="classical",
        hu_tick_follows_shuhewei=True,
        record_hu_tile_tick_index=7,
    ),
    "taiwan": RecordRuleFlags(rule_id="taiwan", replacement_from_tail_end=True),
    "guobiao": RecordRuleFlags(rule_id="guobiao"),
    "qingque": RecordRuleFlags(rule_id="qingque"),
    "zhongyong": RecordRuleFlags(rule_id="zhongyong", replacement_from_tail_end=True),
}


def resolve_record_flags(
    game_title: Optional[Mapping[str, Any]] = None,
    rule: Optional[str] = None,
) -> RecordRuleFlags:
    rid = rule_id_from_title(game_title, rule)
    title_rule = _title_str(game_title, "rule").strip().lower().split("/", 1)[0]
    title_sub = _title_str(game_title, "sub_rule").strip().lower().split("/", 1)[0]
    if "jiandan" in {rid, title_rule, title_sub}:
        raise ValueError("旧版南雀牌谱已停止支持，请使用中庸麻将下的新南雀牌谱。")
    if rid in _BY_RULE:
        if rid == "sichuan" and _title_str(game_title, "sub_rule") in {"sichuan/xueliu", "sichuan/xueliu_exchange"}:
            return replace(_BY_RULE[rid], infers_dingque_from_discards=False)
        return _BY_RULE[rid]
    return RecordRuleFlags(rule_id=rid)
