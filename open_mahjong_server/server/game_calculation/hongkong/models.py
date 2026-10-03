"""Immutable inputs for the Hong Kong profiles, including legacy room IDs.

Tile and meld encodings are the existing server protocol (s = middle of chow,
k = pung, g = open kong, G = concealed kong). Rulebook decisions are versioned
separately from room presentation and may not be supplied by a game action.
"""

from dataclasses import dataclass, field
from typing import Literal

from ..hand_structure import THIRTEEN_TILE_MAHJONG, SIXTEEN_TILE_MAHJONG

NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
WINDS = (41, 42, 43, 44)
DRAGONS = (45, 46, 47)
HONORS = WINDS + DRAGONS
TILES = NUMBERS + HONORS
FLOWERS = tuple(range(51, 59))
ORPHANS = frozenset((11, 19, 21, 29, 31, 39) + HONORS)
PROFILES = ("hongkong/qingzhang", "hongkong/new13", "hongkong/new16")
GAMETOWER = "hongkong/new13_gametower"
LIANHUISE = "hongkong/new13_lianhuise"
QINGZHANG_REMIX = "hongkong/qingzhang_lianhuise"
SUPPORTED_PROFILES = PROFILES + (GAMETOWER, LIANHUISE, QINGZHANG_REMIX)
VERSIONS = {
    PROFILES[0]: "hkma-classical-2025-om1",
    PROFILES[1]: "gd-new13-gametower-20260930-om1",
    PROFILES[2]: "hkma-modern16-20250728-detail-om1",
    GAMETOWER: "gd-new13-gametower-20260930-om1",
    QINGZHANG_REMIX: "lianhuise-qingzhang-remix-20261002-om2",
}


@dataclass(frozen=True)
class HongKongRules:
    sub_rule: str = PROFILES[0]
    flowers: bool | None = None
    new13_full_shoot: bool = True
    new13_version: str = "gametower"
    self_draw_only: bool = False
    win_claim: str = "default"
    dealer_mode: str = "default"
    liability_twelve: bool = True
    liability_dragons: bool = True
    liability_kong: bool = True
    liability_limit: bool = True

    def __post_init__(self):
        if self.new13_version not in ("gametower", "lianhuise"):
            raise ValueError("新章十三张版本须为 Wiki 或恋绘色")
        if self.sub_rule in (GAMETOWER, LIANHUISE):
            object.__setattr__(self, "new13_version", "lianhuise" if self.sub_rule == LIANHUISE else "gametower")
        if self.flowers is None:
            object.__setattr__(self, "flowers", self.is_sixteen or self.is_lianhuise or self.is_remix)
        if any(type(getattr(self, key)) is not bool for key in
               ("flowers", "new13_full_shoot", "self_draw_only", "liability_twelve", "liability_dragons", "liability_kong", "liability_limit")):
            raise ValueError("Hong Kong switches must be JSON booleans")
        if self.sub_rule not in SUPPORTED_PROFILES:
            raise ValueError(f"Unknown Hong Kong profile: {self.sub_rule}")
        if self.is_sixteen or self.is_remix:
            object.__setattr__(self, "flowers", True)
        if self.win_claim not in ("default", "head_bump", "multiple"):
            raise ValueError("无效的多人和牌设置")
        if self.dealer_mode not in ("default", "rotate", "win_or_draw"):
            raise ValueError("无效的连庄设置")
        if self.is_gametower and self.flowers:
            raise ValueError("The selected Guangdong thirteen-tile source uses 136 tiles")

    @property
    def version(self):
        if self.is_lianhuise:
            return "lianhuise-new13-20260930-om1"
        return VERSIONS[self.sub_rule]

    @property
    def is_lianhuise(self):
        return self.sub_rule == LIANHUISE or (self.sub_rule == PROFILES[1] and self.new13_version == "lianhuise")

    @property
    def is_gametower(self):
        return self.sub_rule == GAMETOWER or (self.sub_rule == PROFILES[1] and self.new13_version == "gametower")

    @property
    def is_remix(self):
        return self.sub_rule == QINGZHANG_REMIX

    @property
    def starting_score(self):
        return 1200 if self.is_remix else 0

    @property
    def can_rob_concealed_kong(self):
        return self.open_concealed_kong or self.is_remix

    @property
    def open_concealed_kong(self):
        # Remix also exposes the tile identity, with the two outer backs shown.
        return self.is_old or self.is_lianhuise or self.is_remix

    @property
    def has_ready(self):
        return self.is_sixteen or self.is_gametower

    @property
    def allow_multiple_winners(self):
        return self.win_claim == "multiple" or (self.win_claim == "default" and self.is_sixteen)

    @property
    def repeat_dealer(self):
        return self.dealer_mode == "win_or_draw" or (self.dealer_mode == "default" and (self.is_sixteen or self.is_lianhuise or self.is_remix))

    @property
    def use_twelve_liability(self):
        return not self.is_sixteen and self.liability_twelve

    @property
    def use_dragon_liability(self):
        return (self.is_old or self.is_lianhuise) and self.liability_dragons

    @property
    def use_kong_liability(self):
        return self.is_lianhuise and self.liability_kong

    @property
    def is_old(self):
        return self.sub_rule == PROFILES[0]

    @property
    def is_sixteen(self):
        return self.sub_rule == PROFILES[2]

    @property
    def structure(self):
        return SIXTEEN_TILE_MAHJONG if self.is_sixteen else THIRTEEN_TILE_MAHJONG

    @property
    def minimum_fan(self):
        return 1 if self.is_remix else 3 if self.is_old or self.is_lianhuise else 0

    def room_config(self):
        from dataclasses import asdict
        return {key: value for key, value in asdict(self).items() if key != "sub_rule"}

    @classmethod
    def from_room(cls, room):
        config = room.get("detailed_config")
        if config is None:
            config = {}
        if not isinstance(config, dict):
            raise ValueError("香港麻将详细设置必须为对象")
        if set(config) - (set(cls.__dataclass_fields__) - {"sub_rule"}):
            raise ValueError("香港麻将详细设置包含本规则版本不支持的项目")
        if "flowers" in config and type(config["flowers"]) is not bool:
            raise ValueError("Hong Kong switches must be JSON booleans")
        return cls(sub_rule=room.get("sub_rule", PROFILES[0]), **config)


@dataclass(frozen=True)
class Meld:
    kind: Literal["sequence", "triplet", "kong"]
    tile: int
    external: bool = False
    concealed: bool = True

    @property
    def tiles(self):
        if self.kind == "sequence":
            return (self.tile - 1, self.tile, self.tile + 1)
        return (self.tile,) * (4 if self.kind == "kong" else 3)


@dataclass(frozen=True)
class Shape:
    kind: str
    pair: int = 0
    melds: tuple[Meld, ...] = ()
    # -1 = pair; otherwise the meld that contains the winning tile.
    winning_component: int = -1

    def concealed_pungs(self, self_draw):
        return tuple(i for i, m in enumerate(self.melds)
                     if m.kind != "sequence" and m.concealed
                     and (self_draw or i != self.winning_component))


@dataclass(frozen=True)
class HandContext:
    hand: tuple[int, ...]
    melds: tuple[str, ...] = ()
    winning_tile: int = 0
    self_draw: bool = False
    seat_wind: int = 41
    round_wind: int = 41
    flowers: tuple[int, ...] = ()
    rob_kong: bool = False
    last_tile: bool = False
    replacement: str = ""  # "kong" or "flower"; flowers reset old-style kong chains.
    consecutive_kongs: int = 0
    tail_draws: int = 0
    heavenly: bool = False
    earthly: bool = False
    humanly: bool = False
    ready: str = ""  # ordinary / closed / heaven / earth / human
    immediate: bool = False
    river_count: int = 99
    multi_ron: int = 1
    dealer: bool = False
    dealer_streak: int = 0
    flower_win: str = ""  # seven / eight / seven_steal

    def __post_init__(self):
        object.__setattr__(self, "hand", tuple(self.hand))
        object.__setattr__(self, "melds", tuple(self.melds))
        object.__setattr__(self, "flowers", tuple(self.flowers))


@dataclass(frozen=True)
class Fan:
    id: str
    name: str
    value: int
    # Nonzero masks identify specific groups used by a local pattern.
    groups: int = 0
    unit: str = ""


@dataclass(frozen=True)
class ScoreResult:
    is_win: bool = False
    fans: tuple[Fan, ...] = ()
    raw_fan: int = 0
    fan: int = 0
    shape: str = ""
    rule_version: str = ""

    @property
    def fan_ids(self):
        return tuple(f.id for f in self.fans)

    @property
    def fan_names(self):
        return tuple(f.name for f in self.fans)
