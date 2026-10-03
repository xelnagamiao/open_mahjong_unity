"""MIL 贵州麻将（推广）2023，第八、九节。

Physical wins count all four copies; the book's end-of-hand readiness test only
excludes a fifth copy already in the *concealed* hand (page 13, examples 1–4).
Keep those two questions separate so a dead wait can qualify for 查叫 without
ever permitting an impossible physical win.
"""

from collections import Counter
from dataclasses import dataclass
from functools import lru_cache

from ..jiandan.decompose import Meld, find_standard_decompositions

RULE_VERSION = "mil-guizhou-2023-om1"
SUB_RULE = "guizhou/standard"
TILES = tuple(suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10))
NORMAL_CHICKENS = (31, 28)


def normalize_config(value=None):
    if value is None:
        return {"rule_version": RULE_VERSION}
    if not isinstance(value, dict) or set(value) - {"rule_version"}:
        raise ValueError("贵州 MIL 标准规没有额外馆规选项")
    if value.get("rule_version", RULE_VERSION) != RULE_VERSION:
        raise ValueError("不支持的贵州规则版本")
    return {"rule_version": RULE_VERSION}


@dataclass(frozen=True)
class Fan:
    id: str
    name: str
    points: int


@dataclass(frozen=True)
class Score:
    points: int
    fans: tuple[Fan, ...]
    shape: str

    @property
    def raw_points(self):
        return sum(f.points for f in self.fans)

    @property
    def fan_ids(self):
        return tuple(f.id for f in self.fans)

    @property
    def requires_passport(self):
        return self.fan_ids == ("plain",)


def meld_tiles(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "kgG":
        raise ValueError("贵州麻将只允许碰、杠面子")
    try:
        tile = int(code[1:])
    except ValueError as exc:
        raise ValueError("面子牌张无效") from exc
    if tile not in TILES:
        raise ValueError("贵州麻将不使用字牌或花牌")
    return (tile,) * (3 if code[0] == "k" else 4)


def _validate(hand, melds, complete, theoretical=False):
    if any(type(tile) is not int or tile not in TILES for tile in hand):
        return None
    if len(melds) > 4 or len(hand) != (14 if complete else 13) - 3 * len(melds):
        return None
    try:
        exposed = tuple(meld_tiles(code) for code in melds)
    except (TypeError, ValueError):
        return None
    counts = Counter(hand)
    if max(counts.values(), default=0) > 4:
        return None
    if not theoretical:
        counts.update(tile for group in exposed for tile in group)
        if max(counts.values(), default=0) > 4:
            return None
    return exposed


@lru_cache(maxsize=16384)
def _score(hand, melds, ready, heavenly, kong_draw, hot_discard, rob_kong, theoretical):
    exposed = _validate(hand, melds, True, theoretical)
    if exposed is None:
        return None
    counts = Counter(hand)
    declared = tuple(Meld("triplet" if c[0] == "k" else "kong", group,
                          concealed=c[0] == "G", declared=True)
                     for c, group in zip(melds, exposed))
    standard = find_standard_decompositions(hand, declared, allow_quad_split=True)
    pairs = not melds and len(hand) == 14 and all(n % 2 == 0 for n in counts.values())
    if not standard and not pairs:
        return None
    common = []
    all_tiles = hand + tuple(t for group in exposed for t in group)
    if len({t // 10 for t in all_tiles}) == 1:
        common.append(Fan("pure", "清一色", 13))
    if ready:
        common.append(Fan(ready, "原报（硬报）" if ready == "hard_ready" else "软报",
                          26 if ready == "hard_ready" else 13))
    if heavenly:
        common.append(Fan("heavenly", "天和", 26))
    candidates = []
    if standard:
        fans = list(common)
        # Four declared pungs/kongs necessarily include 大对子: do not add 8.
        if len(melds) == 4:
            fans.append(Fan("single", "单吊", 13))
        elif any(all(m.kind in ("triplet", "kong") for m in d.melds) for d in standard):
            fans.append(Fan("all_pungs", "大对子", 8))
        candidates.append((fans, "standard"))
    if pairs:
        dragon = 4 in counts.values()
        candidates.append((common + [Fan("dragon_pairs" if dragon else "seven_pairs",
                                         "龙七对" if dragon else "七对", 26 if dragon else 13)],
                           "seven_pairs"))
    fans, shape = max(candidates, key=lambda item: sum(f.points for f in item[0]))
    if not fans:
        fans.append(Fan("plain", "平和", 3))
    if kong_draw:
        fans.append(Fan("kong_draw", "杠开", 1))
    if hot_discard:
        fans.append(Fan("hot_discard", "热炮", 1))
    if rob_kong:
        fans.append(Fan("rob_kong", "抢杠", 0))
    return Score(min(39, sum(f.points for f in fans)), tuple(fans), shape)


def score_hand(hand, melds=(), *, ready="", heavenly=False, kong_draw=False,
               hot_discard=False, rob_kong=False, theoretical=False):
    if ready not in ("", "soft_ready", "hard_ready"):
        raise ValueError("无效的开局报听类型")
    if not isinstance(hand, (list, tuple)) or not isinstance(melds, (list, tuple)):
        return None
    if any(type(t) is not int for t in hand) or any(not isinstance(m, str) for m in melds):
        return None
    return _score(tuple(sorted(hand)), tuple(melds), ready, bool(heavenly),
                  bool(kong_draw), bool(hot_discard), bool(rob_kong), bool(theoretical))


@lru_cache(maxsize=16384)
def _waits(hand, melds, theoretical):
    if _validate(hand, melds, False) is None:
        return frozenset()
    counts = Counter(hand)
    if not theoretical:
        counts.update(t for code in melds for t in meld_tiles(code))
    return frozenset(t for t in TILES if counts[t] < 4 and
                     score_hand(hand + (t,), melds, theoretical=theoretical) is not None)


def waiting_tiles(hand, melds=(), *, theoretical=True):
    if not isinstance(hand, (list, tuple)) or not isinstance(melds, (list, tuple)):
        return set()
    if any(type(t) is not int for t in hand) or any(not isinstance(m, str) for m in melds):
        return set()
    return set(_waits(tuple(sorted(hand)), tuple(melds), bool(theoretical)))


def maximum_ready_score(hand, melds=(), *, ready=""):
    """查叫 excludes situational kong/hot/rob bonuses, including phantom waits."""
    return max((score_hand(tuple(hand) + (t,), melds, ready=ready, theoretical=True).points
                for t in waiting_tiles(hand, melds)), default=0)
