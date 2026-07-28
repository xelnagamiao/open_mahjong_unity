"""台湾麻将的馆规配置与计分数据模型。"""

from dataclasses import dataclass, field
from typing import Dict, FrozenSet, List, Optional, Tuple

from .fan import FAN_DEFINITIONS, SCORING_PRESET_TABLES, preset_fan_tai


NUMBER_TILES: Tuple[int, ...] = tuple(
    tile
    for suit in (1, 2, 3)
    for tile in range(suit * 10 + 1, suit * 10 + 10)
)
WIND_TILES: Tuple[int, ...] = (41, 42, 43, 44)
DRAGON_TILES: Tuple[int, ...] = (45, 46, 47)
STRUCTURE_TILES: Tuple[int, ...] = NUMBER_TILES + WIND_TILES + DRAGON_TILES
FLOWER_TILES: Tuple[int, ...] = (51, 52, 53, 54, 55, 56, 57, 58)

SEAT_FLOWERS: Dict[int, Tuple[int, int]] = {
    41: (51, 55),
    42: (52, 56),
    43: (53, 57),
    44: (54, 58),
}
FLOWER_SETS: Tuple[FrozenSet[int], ...] = (
    frozenset((51, 52, 53, 54)),
    frozenset((55, 56, 57, 58)),
)

LIABILITY_FAN_CONFIG_FIELDS: Dict[str, str] = {
    "five_kongs": "five_kongs_liability_enabled",
    "four_kongs": "four_kongs_liability_enabled",
    "big_four_winds": "big_four_winds_liability_enabled",
    "little_four_winds": "little_four_winds_liability_enabled",
    "big_three_dragons": "big_three_dragons_liability_enabled",
    "little_three_dragons": "little_three_dragons_liability_enabled",
    "all_honors": "all_honors_liability_enabled",
    "full_flush": "full_flush_liability_enabled",
    "half_flush": "half_flush_liability_enabled",
    "all_pungs": "all_pungs_liability_enabled",
}


@dataclass(frozen=True)
class TaiwanRules:
    """
    台湾麻将的馆规配置。
    """

    base_points: int = 5
    points_per_tai: int = 1
    minimum_tai: int = 0
    tai_cap: Optional[int] = None
    dead_wall_count: int = 16

    draw_continues_dealer: bool = True
    draw_increments_streak: bool = True
    dealer_streak_limit: Optional[int] = None
    negative_score_ends_match: bool = False
    dead_wall_mode: str = "fixed_tail_16"

    multi_win_mode: str = "double_head_bump_triple_all"
    chow_discard_restriction_mode: str = "strict"
    pung_same_tile_discard_forbidden: bool = True
    allow_kong_from_upper_discard: bool = False
    missed_win_blocks_self_draw: bool = True
    missed_win_released_by_kong: bool = True
    missed_win_blocks_claims: bool = True
    direct_kong_replacement_win_allowed: bool = False
    allow_rob_added_kong: bool = True
    four_winds_abort: bool = False
    four_kongs_abort: bool = False

    eight_flowers_mode: str = "optional_standalone"
    seven_flowers_steal_eighth_enabled: bool = True
    initial_flower_bonus_enabled: bool = False
    all_flower_tiles_enabled: bool = False
    flower_kong_excludes_seat_flower: bool = False
    no_flowers_enabled: bool = False

    ready_qualification_mode: str = "standard_with_dealer_heavenly_ready"
    public_ready_enabled: bool = False
    declared_ready_win_policy: str = "allow_pass"
    qualified_ready_win_policy: str = "follow_declared_ready_policy"
    declared_ready_auto_added_kong: bool = False

    scoring_preset: str = "sml"
    fan_tai_overrides: Dict[str, int] = field(default_factory=dict)

    all_chows_definition: str = "relaxed"
    little_four_winds_add_wind_pungs: bool = False
    all_honors_add_all_pungs: bool = True
    prefer_triplet_decomposition_on_discard_win: bool = False
    earthly_ready_excludes_concealed_and_declared_ready: bool = False

    human_win_definition: str = "before_first_draw"
    earthly_win_allows_open_calls: bool = False

    opening_flower_replacement_order: str = "player_complete"
    claim_wall_reserve: bool = False
    same_turn_claim_forbidden: bool = False

    eight_and_a_half_pairs_enabled: bool = False
    four_kongs_enabled: bool = False
    five_kongs_enabled: bool = False
    half_begging_enabled: bool = False
    last_tile_claim_enabled: bool = False
    all_wind_pungs_enabled: bool = False
    no_flowers_or_honors_enabled: bool = False
    melded_kong_enabled: bool = False
    concealed_kong_enabled: bool = False
    liability_ron_split_enabled: bool = False
    full_flush_liability_enabled: bool = False
    big_three_dragons_liability_enabled: bool = False
    big_four_winds_liability_enabled: bool = False
    all_pungs_liability_enabled: bool = False
    half_flush_liability_enabled: bool = False
    little_three_dragons_liability_enabled: bool = False
    little_four_winds_liability_enabled: bool = False
    all_honors_liability_enabled: bool = False
    five_kongs_liability_enabled: bool = False
    four_kongs_liability_enabled: bool = False

    @property
    def required_claim_wall_reserve(self) -> int:
        return 4 if self.claim_wall_reserve else 0

    def liability_enabled_for_fan(self, fan_id: str) -> bool:
        field_name = LIABILITY_FAN_CONFIG_FIELDS.get(fan_id)
        return bool(field_name and getattr(self, field_name))

    @property
    def liability_fan_ids(self) -> Tuple[str, ...]:
        return tuple(
            fan_id
            for fan_id in LIABILITY_FAN_CONFIG_FIELDS
            if self.liability_enabled_for_fan(fan_id)
        )

    @classmethod
    def from_dict(cls, raw: Optional[dict]) -> "TaiwanRules":
        if not raw:
            return cls()
        if not isinstance(raw, dict):
            raise ValueError("台湾麻将馆规必须是对象")
        allowed = cls.__dataclass_fields__.keys()
        unknown = sorted(set(raw) - set(allowed))
        if unknown:
            raise ValueError(f"未知台湾麻将馆规字段: {', '.join(unknown)}")
        values = {key: raw[key] for key in allowed if key in raw}
        overrides = values.get("fan_tai_overrides")
        if overrides is not None:
            if not isinstance(overrides, dict):
                raise ValueError("自定义台表必须是对象")
            values["fan_tai_overrides"] = dict(overrides)
        rules = cls(**values)
        rules.validate()
        normalized_overrides = {
            fan_id: tai
            for fan_id, tai in rules.fan_tai_overrides.items()
            if tai != preset_fan_tai(rules.scoring_preset, fan_id)
        }
        if normalized_overrides == rules.fan_tai_overrides:
            return rules
        return cls(
            **{
                **{
                    key: getattr(rules, key)
                    for key in allowed
                    if key != "fan_tai_overrides"
                },
                "fan_tai_overrides": normalized_overrides,
            }
        )

    def validate(self) -> None:
        numeric_fields = (
            "base_points",
            "points_per_tai",
            "minimum_tai",
            "dead_wall_count",
        )
        if any(type(getattr(self, name)) is not int for name in numeric_fields):
            raise ValueError("台湾麻将数值馆规必须是整数")
        if any(getattr(self, name) < 0 for name in numeric_fields):
            raise ValueError("台湾麻将数值馆规不能为负数")
        if self.base_points != 5 or self.points_per_tai != 1:
            raise ValueError("当前台湾麻将底分固定为 5、每台分值固定为 1")
        if self.minimum_tai not in (0, 1, 2, 3):
            raise ValueError("最低起胡只支持 0、1、2、3 台")
        if self.tai_cap is not None and type(self.tai_cap) is not int:
            raise ValueError("手牌台封顶必须是整数或空值")
        if self.tai_cap not in (None, 16, 24):
            raise ValueError("手牌台封顶只支持不封顶、16 或 24 台")
        if self.dead_wall_count != 16:
            raise ValueError("台湾麻将固定尾牌数量必须为 16 张")
        if self.dealer_streak_limit is not None and type(self.dealer_streak_limit) is not int:
            raise ValueError("连庄上限必须是整数或空值")
        if self.dealer_streak_limit not in (None, 9, 10):
            raise ValueError("连庄上限只支持不限、9 或 10")
        if self.dead_wall_mode not in ("fixed_tail_16", "kong_expands_tail", "fixed_replacement_wall_16"):
            raise ValueError("不支持的尾牌模型")
        if self.multi_win_mode not in ("double_head_bump_triple_all", "multiple_winners", "head_bump"):
            raise ValueError("不支持的多响模式")
        if self.chow_discard_restriction_mode not in ("strict", "same_tile", "none"):
            raise ValueError("不支持的食替模式")
        if self.eight_flowers_mode not in (
            "optional_standalone",
            "forced_standalone",
            "additive",
            "compound",
        ):
            raise ValueError("不支持的八仙过海模式")
        if self.declared_ready_win_policy not in ("allow_pass", "force_win"):
            raise ValueError("不支持的报听后拒胡处理模式")
        if self.qualified_ready_win_policy not in (
            "follow_declared_ready_policy",
            "lose_earthly_on_pass",
            "force_win",
        ):
            raise ValueError("不支持的天地听拒胡处理模式")
        if self.ready_qualification_mode not in (
            "disabled",
            "standard_with_dealer_heavenly_ready",
            "standard_without_dealer_heavenly_ready",
            "first_eight_table_discards",
            "each_player_first_discard",
        ):
            raise ValueError("不支持的天地听资格模式")
        if self.scoring_preset not in SCORING_PRESET_TABLES:
            raise ValueError("不支持的台湾麻将台表预设")
        if not isinstance(self.fan_tai_overrides, dict):
            raise ValueError("自定义台表必须是对象")
        for fan_id, tai in self.fan_tai_overrides.items():
            if not isinstance(fan_id, str) or fan_id not in FAN_DEFINITIONS:
                raise ValueError(f"未知台湾麻将台种: {fan_id}")
            if type(tai) is not int or not 1 <= tai <= 64:
                raise ValueError("自定义台值必须是 1 至 64 的整数")
        if self.all_chows_definition not in (
            "relaxed",
            "strict",
        ):
            raise ValueError("不支持的平胡定义")
        if self.human_win_definition not in (
            "before_first_draw",
            "discarder_first_discard",
            "disabled",
        ):
            raise ValueError("不支持的人胡定义")
        if self.opening_flower_replacement_order not in (
            "player_complete",
            "round_robin",
        ):
            raise ValueError("不支持的开局补花顺序")
        boolean_fields = (
            "draw_continues_dealer",
            "draw_increments_streak",
            "negative_score_ends_match",
            "pung_same_tile_discard_forbidden",
            "allow_kong_from_upper_discard",
            "missed_win_blocks_self_draw",
            "missed_win_released_by_kong",
            "missed_win_blocks_claims",
            "direct_kong_replacement_win_allowed",
            "allow_rob_added_kong",
            "four_winds_abort",
            "four_kongs_abort",
            "seven_flowers_steal_eighth_enabled",
            "initial_flower_bonus_enabled",
            "all_flower_tiles_enabled",
            "flower_kong_excludes_seat_flower",
            "no_flowers_enabled",
            "public_ready_enabled",
            "declared_ready_auto_added_kong",
            "eight_and_a_half_pairs_enabled",
            "four_kongs_enabled",
            "five_kongs_enabled",
            "half_begging_enabled",
            "last_tile_claim_enabled",
            "all_wind_pungs_enabled",
            "no_flowers_or_honors_enabled",
            "melded_kong_enabled",
            "concealed_kong_enabled",
            "liability_ron_split_enabled",
            "full_flush_liability_enabled",
            "big_three_dragons_liability_enabled",
            "big_four_winds_liability_enabled",
            "all_pungs_liability_enabled",
            "half_flush_liability_enabled",
            "little_three_dragons_liability_enabled",
            "little_four_winds_liability_enabled",
            "all_honors_liability_enabled",
            "four_kongs_liability_enabled",
            "five_kongs_liability_enabled",
            "little_four_winds_add_wind_pungs",
            "all_honors_add_all_pungs",
            "prefer_triplet_decomposition_on_discard_win",
            "earthly_win_allows_open_calls",
            "earthly_ready_excludes_concealed_and_declared_ready",
            "claim_wall_reserve",
            "same_turn_claim_forbidden",
        )
        if any(type(getattr(self, name)) is not bool for name in boolean_fields):
            raise ValueError("台湾麻将开关馆规必须是布尔值")


@dataclass(frozen=True)
class Meld:
    """采用统一组合码表示的一组面子。"""

    kind: str
    tile: int
    concealed: bool
    code: str
    external: bool = False

    @property
    def tiles(self) -> Tuple[int, ...]:
        if self.kind == "sequence":
            return (self.tile - 1, self.tile, self.tile + 1)
        count = 4 if self.kind == "kong" else 3
        return (self.tile,) * count

    @property
    def minimum_tile(self) -> int:
        return self.tile - 1 if self.kind == "sequence" else self.tile


@dataclass(frozen=True)
class Decomposition:
    pair: int
    melds: Tuple[Meld, ...]
    winning_component: Tuple[str, int] = ("", -1)
    special: Optional[str] = None

    def stable_key(self) -> tuple:
        kind_order = {"sequence": 0, "triplet": 1, "kong": 2}
        meld_key = tuple(
            sorted(
                (
                    meld.minimum_tile,
                    kind_order[meld.kind],
                    meld.tile,
                    0 if meld.concealed else 1,
                    meld.code,
                )
                for meld in self.melds
            )
        )
        return (self.pair, meld_key, self.winning_component, self.special or "")

    def display_codes(self) -> List[str]:
        codes = [meld.code for meld in self.melds]
        return [f"p{self.pair}"] + sorted(codes)


@dataclass(frozen=True)
class Fan:
    fan_id: str
    name: str
    tai: int
    count: int = 1

    @property
    def total(self) -> int:
        return self.tai * self.count

    @property
    def display_name(self) -> str:
        return self.name if self.count == 1 else f"{self.name}*{self.count}"


@dataclass
class HandContext:
    hand_tiles: List[int]
    meld_codes: List[str] = field(default_factory=list)
    winning_tile: Optional[int] = None
    win_source: str = "self_draw"
    pre_win_tiles: Optional[List[int]] = None
    flowers: List[int] = field(default_factory=list)
    seat_wind: int = 41
    round_wind: int = 41

    out_with_replacement_tile: bool = False
    last_tile: bool = False
    last_tile_claim: bool = False
    heavenly_ready: bool = False
    earthly_ready: bool = False
    declared_ready: bool = False
    heavenly_win: bool = False
    earthly_win: bool = False
    human_win: bool = False

    eight_flowers_and_seasons: bool = False
    seven_flowers_steal_eighth: bool = False
    eight_flowers_declined: bool = False
    rules: TaiwanRules = field(default_factory=TaiwanRules)

    @classmethod
    def from_dict(cls, raw: dict) -> "HandContext":
        values = dict(raw or {})
        values["rules"] = TaiwanRules.from_dict(values.get("rules"))
        return cls(**values)


@dataclass(frozen=True)
class ScoreResult:
    is_win: bool
    tai: int = 0
    capped_tai: int = 0
    fans: Tuple[Fan, ...] = ()
    decomposition: Optional[Decomposition] = None
    waits: FrozenSet[int] = frozenset()
    reason: str = ""
    below_minimum: bool = False

    @property
    def fan_names(self) -> List[str]:
        return [fan.display_name for fan in self.fans]

    @property
    def fan_ids(self) -> List[str]:
        return [fan.fan_id for fan in self.fans]

    def as_dict(self) -> dict:
        return {
            "is_win": self.is_win,
            "tai": self.tai,
            "capped_tai": self.capped_tai,
            "fan_ids": self.fan_ids,
            "fan_names": self.fan_names,
            "fan_detail": [
                {
                    "id": fan.fan_id,
                    "name": fan.name,
                    "tai": fan.tai,
                    "count": fan.count,
                    "total": fan.total,
                }
                for fan in self.fans
            ],
            "decomposition": (
                [f"special:{self.decomposition.special}"]
                if self.decomposition and self.decomposition.special
                else self.decomposition.display_codes()
                if self.decomposition
                else []
            ),
            "winning_component": (
                list(self.decomposition.winning_component)
                if self.decomposition
                else None
            ),
            "special": self.decomposition.special if self.decomposition else None,
            "waits": sorted(self.waits),
            "reason": self.reason,
            "below_minimum": self.below_minimum,
        }


@dataclass(frozen=True)
class Payment:
    payer: int
    winner: int
    hand_tai: int
    dealer_tai: int
    amount: int


@dataclass(frozen=True)
class Settlement:
    score_changes: Dict[int, int]
    payments: Tuple[Payment, ...]
