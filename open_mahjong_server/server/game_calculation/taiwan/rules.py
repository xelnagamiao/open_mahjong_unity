"""台湾麻将的馆规配置与计分数据模型。"""

from dataclasses import dataclass, field
from typing import Dict, FrozenSet, List, Optional, Tuple


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
    dead_wall_mode: str = "fixed_16"

    multi_win_mode: str = "two_head_three_all"
    strict_kuikae: str = "strict"
    peng_kuikae_forbidden: bool = True
    allow_kong_from_upper_discard: bool = False
    water_blocks_self_draw: bool = True
    water_release_by_kong: bool = True
    water_blocks_claims: bool = True
    kong_discard_self_draw: bool = False
    allow_rob_added_kong: bool = True
    four_winds_abort: bool = False
    four_kongs_abort: bool = False

    eight_immortals_mode: str = "optional_separate"
    seven_robs_one: bool = True
    heavenly_earthly_flower_tai: int = 0
    flower_kong_tai: int = 1
    flower_scoring: str = "seat"
    no_flower_tai: int = 0

    heavenly_earthly_ready_enabled: bool = True
    declared_ready_win_policy: str = "allow_pass"

    public_ready_tai: int = 0

    scoring_preset: str = "sml"

    eight_pairs_half: bool = False
    half_exposed_tai: int = 0
    river_bottom_tai: int = 0
    all_winds_tai: int = 0
    no_honor_no_flower_tai: int = 0
    open_kong_tai: int = 0
    concealed_kong_tai: int = 0
    dangerous_discard_liability: bool = False

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
        preset = values.get("scoring_preset", "sml")
        if "flower_kong_tai" not in raw and preset in (
            "star31",
            "shenlaiye",
        ):
            values["flower_kong_tai"] = 2
        rules = cls(**values)
        rules.validate()
        return rules

    def validate(self) -> None:
        numeric_fields = (
            "base_points",
            "points_per_tai",
            "minimum_tai",
            "dead_wall_count",
            "heavenly_earthly_flower_tai",
            "flower_kong_tai",
            "no_flower_tai",
            "public_ready_tai",
            "half_exposed_tai",
            "river_bottom_tai",
            "all_winds_tai",
            "no_honor_no_flower_tai",
            "open_kong_tai",
            "concealed_kong_tai",
        )
        if any(type(getattr(self, name)) is not int for name in numeric_fields):
            raise ValueError("台湾麻将数值馆规必须是整数")
        if any(getattr(self, name) < 0 for name in numeric_fields):
            raise ValueError("台湾麻将台数与分值不能为负数")
        if self.base_points < 0 or self.points_per_tai < 0:
            raise ValueError("底分与每台分值不能为负数")
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
        if self.dead_wall_mode not in ("fixed_16", "kong_add_one", "replacement_wall_16"):
            raise ValueError("不支持的尾牌模型")
        if self.multi_win_mode not in ("two_head_three_all", "multi", "head_bump"):
            raise ValueError("不支持的多响模式")
        if self.strict_kuikae not in ("strict", "same_tile", "none"):
            raise ValueError("不支持的食替模式")
        if self.flower_scoring not in ("seat", "any"):
            raise ValueError("花牌计台只支持座位正花或任意花")
        if self.heavenly_earthly_flower_tai not in (0, 4):
            raise ValueError("天地和时点花胡加台只支持关闭或 4 台")
        if self.eight_immortals_mode not in (
            "optional_separate",
            "forced_separate",
            "add_to_normal",
            "compound",
        ):
            raise ValueError("不支持的八仙过海模式")
        if self.declared_ready_win_policy not in ("allow_pass", "force_win"):
            raise ValueError("不支持的报听后拒胡处理模式")
        if self.scoring_preset not in (
            "sml",
            "star31",
            "shenlaiye",
            "cml",
        ):
            raise ValueError("不支持的台湾麻将台表预设")

        boolean_fields = (
            "draw_continues_dealer",
            "draw_increments_streak",
            "negative_score_ends_match",
            "peng_kuikae_forbidden",
            "allow_kong_from_upper_discard",
            "water_blocks_self_draw",
            "water_release_by_kong",
            "water_blocks_claims",
            "kong_discard_self_draw",
            "allow_rob_added_kong",
            "four_winds_abort",
            "four_kongs_abort",
            "seven_robs_one",
            "heavenly_earthly_ready_enabled",
            "eight_pairs_half",
            "dangerous_discard_liability",
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

    after_kong: bool = False
    last_tile: bool = False
    river_bottom: bool = False
    heavenly_ready: bool = False
    earthly_ready: bool = False
    declared_ready: bool = False
    heavenly_win: bool = False
    earthly_win: bool = False
    human_win: bool = False

    eight_immortals: bool = False
    seven_robs_one: bool = False
    eight_immortals_declined: bool = False
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
