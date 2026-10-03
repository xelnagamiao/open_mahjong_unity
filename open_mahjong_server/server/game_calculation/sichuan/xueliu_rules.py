"""血流成河示例规则配置与番值。

当前配置为项目内可调整的示例，不绑定任何外部平台的规则版本。

这是一个独立的小规则层，供房间配置、状态机和和牌检查共同使用。现有
``sichuan/standard`` 继续使用 SBR（竞技版）番表；不要把这里的番值混到
``sichuan_hepai_check.py``，否则已有血战牌谱会被重新解释。

血流开局分为弃三张与换三张，分别使用三副/四副面子加一对将。
两者使用独立番表，规则快照显式记录开局方式、定缺与计分版本。
"""

from __future__ import annotations

from .xueliu_exchange_rules import FAN_VALUES as XUELIU_EXCHANGE_FAN_VALUES
from dataclasses import dataclass
from typing import Iterable, Mapping, Sequence


XUELIU_SUB_RULE = "sichuan/xueliu"
XUELIU_EXCHANGE_SUB_RULE = "sichuan/xueliu_exchange"
SICHUAN_STANDARD_SUB_RULE = "sichuan/standard"
SUPPORTED_SICHUAN_SUB_RULES = frozenset({SICHUAN_STANDARD_SUB_RULE, XUELIU_SUB_RULE, XUELIU_EXCHANGE_SUB_RULE})

# Public constants keep the UI/room profile and score tests on one source of
# truth. These are blood-flow hand-shape values, not the MIL/SBR standard values.
XUELIU_FAN_VALUES = {
    "基本胡": 1,
    "碰碰胡": 4,
    "清一色": 6,
    "清碰": 12,
    "门清自摸": 1,
    "明杠": 1,
    "暗杠": 2,
}


def xueliu_base_from_fan(fan: int) -> int:
    """Blood-flow values are direct settlement amounts, not SBR exponents."""
    return max(1, int(fan))


@dataclass(frozen=True)
class XueliuRuleProfile:
    """可序列化的血流规则快照。

    ``discard_three`` 特意使用弃置语义，避免将其误实现为换三张。
    """

    sub_rule: str = XUELIU_SUB_RULE
    version: int = 3
    tile_count: int = 108
    choose_missing_suit: bool = False
    discard_three: bool = True
    exchange_three: bool = False
    meld_count: int = 3
    discard_three_same_suit: bool = True
    allow_chi: bool = False
    allow_tian_hu: bool = False
    allow_post_hu_ming_gang: bool = False
    allow_post_hu_angang: bool = True
    allow_post_hu_jiagang: bool = True
    end_when_wall_exhausted: bool = True
    hu_winner_stays_active: bool = True
    flower_pig_multiplier: int = 2

    @property
    def fan_values(self) -> Mapping[str, int]:
        return XUELIU_EXCHANGE_FAN_VALUES if self.exchange_three else XUELIU_FAN_VALUES

    def as_dict(self) -> dict:
        return {
            "sub_rule": self.sub_rule,
            "version": self.version,
            "choose_missing_suit": self.choose_missing_suit,
            "tile_count": self.tile_count,
            "discard_three": self.discard_three,
            "exchange_three": self.exchange_three,
            "meld_count": self.meld_count,
            "discard_three_same_suit": self.discard_three_same_suit,
            "allow_chi": self.allow_chi,
            "allow_tian_hu": self.allow_tian_hu,
            "allow_post_hu_ming_gang": self.allow_post_hu_ming_gang,
            "allow_post_hu_angang": self.allow_post_hu_angang,
            "allow_post_hu_jiagang": self.allow_post_hu_jiagang,
            "end_when_wall_exhausted": self.end_when_wall_exhausted,
            "hu_winner_stays_active": self.hu_winner_stays_active,
            "flower_pig_multiplier": self.flower_pig_multiplier,
            "fan_values": dict(self.fan_values),
            "gang_score_immediate": self.exchange_three,
            "root_multiplier": self.exchange_three,
        }


XUELIU_RULE_PROFILE = XueliuRuleProfile()
XUELIU_EXCHANGE_RULE_PROFILE = XueliuRuleProfile(sub_rule=XUELIU_EXCHANGE_SUB_RULE, discard_three=False, exchange_three=True, meld_count=4, choose_missing_suit=True)


def is_xueliu_sub_rule(sub_rule: str | None) -> bool:
    """Return whether *sub_rule* selects the blood-flow ruleset."""

    return sub_rule in (XUELIU_SUB_RULE, XUELIU_EXCHANGE_SUB_RULE)


def is_valid_discard_three(tiles: Sequence[int]) -> bool:
    """Return whether a opening ``弃三张`` selection is legal."""

    if len(tiles) != 3:
        return False
    try:
        suits = {int(tile) // 10 for tile in tiles}
    except (TypeError, ValueError):
        return False
    return len(suits) == 1 and next(iter(suits), 0) in (1, 2, 3)


def validate_sichuan_sub_rule(sub_rule: str | None) -> str:
    """Validate and return a normalized Sichuan sub-rule name."""

    value = sub_rule or SICHUAN_STANDARD_SUB_RULE
    if value not in SUPPORTED_SICHUAN_SUB_RULES:
        raise ValueError(
            "不支持的四川麻将子规则"
        )
    return value


def _meld_sign(meld: object) -> str:
    if isinstance(meld, str) and meld:
        return meld[0]
    if isinstance(meld, Mapping):
        kind = meld.get("type") or meld.get("kind") or meld.get("meld_type")
        return str(kind or "")[:1]
    return ""


def _meld_count(melds: Iterable[object], sign: str, *, case_sensitive: bool = False) -> int:
    if case_sensitive:
        return sum(1 for meld in melds if _meld_sign(meld) == sign)
    return sum(1 for meld in melds if _meld_sign(meld).lower() == sign.lower())


def _same_suit(tiles: Sequence[int]) -> bool:
    suits = {int(tile) // 10 for tile in tiles if isinstance(tile, int)}
    return bool(suits) and len(suits) == 1


def _meld_tile_values(melds: Iterable[object]) -> list[int]:
    values: list[int] = []
    for meld in melds:
        if isinstance(meld, str):
            digits = meld[1:]
        elif isinstance(meld, Mapping):
            digits = str(meld.get("tile") or meld.get("target_tile") or "")
        else:
            digits = ""
        try:
            if digits:
                values.append(int(digits))
        except ValueError:
            continue
    return values


def calculate_xueliu_fan(
    *,
    pengpeng: bool = False,
    qingyise: bool = False,
    qingpeng: bool | None = None,
    menqing: bool = False,
    zimo: bool = False,
    minggang_count: int = 0,
    angang_count: int = 0,
) -> tuple[int, list[str]]:
    """Calculate the fan values supported by the current blood-flow profile.

    The hand-shape tier is mutually exclusive: 清碰=12 supersedes 清一色=6
    and 碰碰胡=4. A normal winning hand starts at 1. 门清自摸、明杠 and
    暗杠 are additive. Returning the names alongside the value makes the result
    suitable for the existing game-record protocol.
    """

    minggang_count = max(0, int(minggang_count))
    angang_count = max(0, int(angang_count))
    if qingpeng is None:
        qingpeng = bool(pengpeng and qingyise)

    if qingpeng:
        fan, names = 12, ["清碰"]
    elif qingyise:
        fan, names = 6, ["清一色"]
    elif pengpeng:
        fan, names = 4, ["碰碰胡"]
    else:
        fan, names = 1, ["基本胡"]

    if menqing and zimo:
        fan += 1
        names.append("门清自摸")
    if minggang_count:
        fan += minggang_count
        names.extend(["明杠"] * minggang_count)
    if angang_count:
        fan += angang_count * 2
        names.extend(["暗杠"] * angang_count)
    return fan, names


def calculate_xueliu_fan_from_hand(
    hand_tiles: Sequence[int],
    melds: Sequence[object] = (),
    *,
    zimo: bool = False,
) -> tuple[int, list[str]]:
    """Best-effort feature extraction for a completed Sichuan hand.

    The structural checker remains authoritative about whether a hand wins.
    This helper only translates features into values and is intentionally
    conservative when handed an unfamiliar meld encoding.
    """

    melds = tuple(melds)
    all_tiles = list(hand_tiles) + _meld_tile_values(melds)
    pengpeng = bool(melds) and all(_meld_sign(m).lower() in {"k", "g"} for m in melds)
    # A concealed hand with no sequence can be recognized from the hand's
    # multiplicities. The full decomposition is still performed by the caller.
    counts: dict[int, int] = {}
    for tile in all_tiles:
        counts[tile] = counts.get(tile, 0) + 1
    if not melds and counts:
        pengpeng = all(count in (2, 3, 4) for count in counts.values()) and any(
            count >= 3 for count in counts.values()
        )
    qingyise = _same_suit(all_tiles)
    minggang = _meld_count(melds, "g", case_sensitive=True)
    angang = _meld_count(melds, "G", case_sensitive=True)
    menqing = not melds
    return calculate_xueliu_fan(
        pengpeng=pengpeng,
        qingyise=qingyise,
        menqing=menqing,
        zimo=zimo,
        minggang_count=minggang,
        angang_count=angang,
    )


def calculate_xueliu_fan_from_sichuan_result(
    fan_names: Sequence[str],
    melds: Sequence[object] = (),
    *,
    zimo: bool = False,
) -> tuple[int, list[str]]:
    """Translate an existing Sichuan decomposition's fan names to values.

    ``Sichuan_Hepai_Check`` already determines the winning shape. Mapping its
    names here avoids duplicating that solver while keeping standard fan values
    untouched. Situational SBR fans are deliberately omitted from blood-flow profile.
    """

    names = list(fan_names)
    explicit_qingpeng = "清碰" in names
    pengpeng = explicit_qingpeng or any(name in names for name in ("大对子", "对对和", "碰碰胡"))
    qingyise = "清一色" in names or explicit_qingpeng
    qingpeng = explicit_qingpeng or (qingyise and pengpeng)
    return calculate_xueliu_fan(
        pengpeng=pengpeng,
        qingyise=qingyise,
        qingpeng=qingpeng,
        menqing=not melds,
        zimo=zimo,
        minggang_count=_meld_count(melds, "g", case_sensitive=True),
        angang_count=_meld_count(melds, "G", case_sensitive=True),
    )


__all__ = [
    "SICHUAN_STANDARD_SUB_RULE",
    "SUPPORTED_SICHUAN_SUB_RULES",
    "XUELIU_RULE_PROFILE",
    "XUELIU_SUB_RULE",
    "XueliuRuleProfile",
    "XUELIU_FAN_VALUES",
    "xueliu_base_from_fan",
    "calculate_xueliu_fan",
    "calculate_xueliu_fan_from_hand",
    "calculate_xueliu_fan_from_sichuan_result",
    "is_xueliu_sub_rule",
    "is_valid_discard_three",
    "validate_sichuan_sub_rule",
]
