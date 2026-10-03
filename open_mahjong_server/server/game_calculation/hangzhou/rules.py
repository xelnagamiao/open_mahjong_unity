"""MIL《杭州麻将（推广）竞赛规则（试行2025版）》第七、八节。

Physical tile IDs remain intact throughout scoring.  In this project 46 is
white (the fixed Hangzhou joker), and 47 is green.  Only the common jokers
package solves tile structures; this module supplies Hangzhou policy and fans.
"""

from collections import Counter
from dataclasses import asdict, dataclass
from functools import lru_cache

RULE_VERSION = "mil-hangzhou-2025-om1"
SUB_RULE = "hangzhou/mil2025"
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
WINDS = (41, 42, 43, 44)
DRAGONS = (45, 46, 47)
HONORS = WINDS + DRAGONS
TILES = NUMBERS + HONORS
JOKER = 46
FAN_CAP = 4
DEAD_WALL_TILES = 20


def normalize_config(raw=None):
    """The published MIL version has no optional house-rule switches."""
    if raw is None:
        raw = {}
    if not isinstance(raw, dict) or set(raw) - {"rule_version"}:
        raise ValueError("杭州 MIL 标准规没有额外馆规选项")
    if raw.get("rule_version", RULE_VERSION) != RULE_VERSION:
        raise ValueError("不支持的杭州规则版本")
    return {"rule_version": RULE_VERSION}


def meld_tiles(code):
    """Decode existing server s/k/g/G meld notation without joker exposure."""
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "skgG":
        raise ValueError("非法面子")
    try:
        tile = int(code[1:])
    except ValueError as exc:
        raise ValueError("面子牌张无效") from exc
    if code[0] == "s":
        if tile not in NUMBERS or not 2 <= tile % 10 <= 8:
            raise ValueError("非法顺子")
        return (tile - 1, tile, tile + 1)
    if tile not in TILES or tile == JOKER:
        raise ValueError("财神不可吃、碰、杠")
    return (tile,) * (3 if code[0] == "k" else 4)


def _valid(hand, melds, complete):
    if not isinstance(hand, (list, tuple)) or not isinstance(melds, (list, tuple)):
        return None
    if len(melds) > 4 or len(hand) + 3 * len(melds) != (14 if complete else 13):
        return None
    if any(type(t) is not int or t not in TILES for t in hand):
        return None
    try:
        groups = tuple(meld_tiles(code) for code in melds)
    except ValueError:
        return None
    all_tiles = tuple(hand) + tuple(t for group in groups for t in group)
    if max(Counter(all_tiles).values(), default=0) > 4:
        return None
    return groups


@dataclass(frozen=True)
class Fan:
    id: str
    name: str
    fan: int


@dataclass(frozen=True)
class WinContext:
    """Authoritative draw facts, never supplied directly by a client action.

    ``cai_piao_count`` counts the player's consecutive joker discards before
    this subsequent draw.  It awards no fan unless the actual pre-draw hand
    also satisfies 爆头.  The state machine clears it on an ordinary discard.
    """

    self_drawn: bool = True
    after_kong: bool = False
    cai_piao_count: int = 0
    pre_draw_tiles: tuple[int, ...] | None = None

    def __post_init__(self):
        if type(self.self_drawn) is not bool or type(self.after_kong) is not bool:
            raise ValueError("摸牌来源须为布尔值")
        if type(self.cai_piao_count) is not int or not 0 <= self.cai_piao_count <= 4:
            raise ValueError("连续财飘次数必须为 0 至 4")
        if self.pre_draw_tiles is not None:
            if not isinstance(self.pre_draw_tiles, (tuple, list)):
                raise ValueError("摸牌前手牌必须为牌张序列")
            object.__setattr__(self, "pre_draw_tiles", tuple(self.pre_draw_tiles))


@dataclass(frozen=True)
class WinScore:
    shape: str
    fans: tuple[Fan, ...]
    # A common-core shape is serialized rather than rewriting physical tiles.
    decomposition: dict | None = None

    @property
    def raw_fan(self):
        return sum(item.fan for item in self.fans)

    @property
    def fan_total(self):
        return min(FAN_CAP, self.raw_fan)

    @property
    def points(self):
        return 2 ** self.fan_total

    @property
    def fan_ids(self):
        return tuple(item.id for item in self.fans)

    @property
    def fan_names(self):
        return [f"{item.name}（{item.fan}番）" for item in self.fans]

    def as_dict(self):
        return {**asdict(self), "rule_version": RULE_VERSION,
                "raw_fan": self.raw_fan, "fan_total": self.fan_total,
                "points": self.points, "fan_cap": FAN_CAP}


@lru_cache(maxsize=8)
def _policy(shapes=("standard", "seven_pairs"), jokers=(JOKER,)):
    from ..jokers import JokerPolicy
    # Unlike Guangdong/Hongzhong, this book imposes no logical four-copy cap.
    # The common core still validates the complete physical hand's supply.
    return JokerPolicy(joker_tiles=jokers, meld_count=4, closed_cap=None, shapes=shapes)


def waiting_tiles(hand, melds=()):
    """Physically obtainable draws that complete either allowed shape."""
    if _valid(hand, melds, False) is None:
        return set()
    from ..jokers import structural_waits
    return set(structural_waits(hand, melds, _policy()))


def can_baotou(pre_draw_tiles, melds=()):
    """Book VIII-9: remove ONE white from the actual hand before the draw."""
    if _valid(pre_draw_tiles, melds, False) is None or JOKER not in pre_draw_tiles:
        return False
    from ..jokers import can_form_melds, can_form_pairs
    remainder = list(pre_draw_tiles)
    remainder.remove(JOKER)
    if can_form_melds(remainder, melds, _policy()):
        return True
    return not melds and can_form_pairs(remainder, 6, _policy())


def _luxury_pairs(hand):
    counts = Counter(hand)
    if any(tile != JOKER and count == 4 for tile, count in counts.items()):
        return True, False
    # Four physical whites count only when they remain two literal white pairs;
    # using them to repair unrelated singleton pairs does not create a quartet.
    white_natural = counts[JOKER] == 4 and all(
        count % 2 == 0 for tile, count in counts.items() if tile != JOKER)
    return white_natural, white_natural


def evaluate_win(hand, melds=(), win_tile=None, context=None):
    """Best legal normal self-draw score with an auditable physical assignment.

    Hangzhou fans depend only on the two shape families and historical facts,
    not on a choice between equivalent sequences/pungs.  Seven pairs is always
    worth at least two more fan, so choosing its first valid decomposition is
    exact scoring equivalence, not an arbitrary cap on a maximum-score search.
    """
    if _valid(hand, melds, True) is None:
        return None
    if context is None:
        context = WinContext()
    if not isinstance(context, WinContext):
        raise ValueError("和牌上下文必须来自权威摸牌记录")
    if not context.self_drawn:
        return None
    if context.after_kong and not any(code[0] in "gG" for code in melds):
        return None
    if win_tile is None:
        win_tile = hand[-1]
    if type(win_tile) is not int or win_tile not in hand:
        return None
    pre_draw = list(hand)
    pre_draw.remove(win_tile)
    if context.pre_draw_tiles is not None:
        if (_valid(context.pre_draw_tiles, melds, False) is None
                or Counter(pre_draw) != Counter(context.pre_draw_tiles)):
            return None
        pre_draw = context.pre_draw_tiles
    from ..jokers import can_form_pairs, can_win, iter_winning_shapes
    pairs = not melds and can_form_pairs(hand, 7, _policy())
    fans = []
    if pairs:
        luxury, whites_natural = _luxury_pairs(hand)
        fans.append(Fan("luxury_seven_pairs" if luxury else "seven_pairs",
                        "豪华七对" if luxury else "七对子", 4 if luxury else 2))
        selected_policy = _policy(("seven_pairs",), () if whites_natural else (JOKER,))
    else:
        selected_policy = _policy(("standard",))
        if not can_win(hand, melds, selected_policy):
            return None
    if can_baotou(pre_draw, melds):
        fans.append(Fan("baotou", "爆头", 1))
        if context.cai_piao_count:
            fans.append(Fan("cai_piao", "财飘" if context.cai_piao_count == 1
                            else f"财飘×{context.cai_piao_count}", context.cai_piao_count))
    if context.after_kong:
        fans.append(Fan("kong_draw", "杠开", 1))
    if JOKER not in hand:
        fans.append(Fan("no_joker", "无财神", 1))
    if not fans:
        fans.append(Fan("plain", "平和", 0))
    shape = next(iter_winning_shapes(hand, melds, selected_policy,
                                    winning_tile=win_tile), None)
    if shape is None:
        # Any disagreement between fast feasibility and witness generation is
        # a failed core contract, never permission to award an unexplained win.
        return None
    decomposition = {**asdict(shape), "physical_tiles": tuple(hand),
                     "logical_tiles": shape.logical_tiles,
                     "closed_logical_tiles": shape.closed_logical_tiles,
                     "winning_logical_tile": shape.winning_logical_tile}
    return WinScore(shape.kind, tuple(fans), decomposition)


def ten_winds_eligible(hand, discard_history, melds=(), *, had_meld_action=False):
    """Exactly the first ten discards, all seven honors, with no meld action.

    The book calls all seven honor kinds 风牌 (p. 1), not just the four winds.
    A claimed discard must remain in ``discard_history`` for this check.
    """
    if type(had_meld_action) is not bool or had_meld_action or melds:
        return False
    if _valid(hand, melds, False) is None:
        return False
    if not isinstance(discard_history, (list, tuple)) or len(discard_history) != 10:
        return False
    if any(type(t) is not int or t not in HONORS for t in discard_history):
        return False
    # With no chi/pung/kong, a physical copy cannot re-enter this player's hand.
    return max(Counter(tuple(hand) + tuple(discard_history)).values()) <= 4


def score_ten_winds(hand, discard_history, melds=(), *, had_meld_action=False):
    """Score the post-discard special win; a normal 14-tile shape is unneeded."""
    if not ten_winds_eligible(hand, discard_history, melds,
                              had_meld_action=had_meld_action):
        return None
    fans = [Fan("ten_winds", "十风", 4 if JOKER in discard_history else 3)]
    if JOKER not in hand:
        fans.append(Fan("no_joker", "无财神", 1))
    return WinScore("ten_winds", tuple(fans))


@dataclass(frozen=True)
class Transfer:
    payer: int
    winner: int
    points: int
    reason: str


@dataclass(frozen=True)
class Settlement:
    changes: tuple[int, int, int, int]
    transfers: tuple[Transfer, ...]
    normal_total: int
    dealer_multiplier: int

    def as_dict(self):
        return asdict(self)


def _settlement(winner, points, dealer, dealer_streak, chi_counts):
    if (type(winner) is not int or winner not in range(4)
            or type(dealer) is not int or dealer not in range(4)):
        raise ValueError("非法和家或庄家")
    if type(points) is not int or points <= 0:
        raise ValueError("基本分必须为正整数")
    if type(dealer_streak) is not int or not 1 <= dealer_streak <= 3:
        raise ValueError("老庄数必须为 1 至 3")
    if (not isinstance(chi_counts, (tuple, list)) or len(chi_counts) != 4
            or any(type(n) is not int or not 0 <= n <= 4 for n in chi_counts)):
        raise ValueError("必须提供四家有效吃牌次数")
    multiplier = 2 ** dealer_streak
    normal = {seat: points * (multiplier if winner == dealer or seat == dealer else 1)
              for seat in range(4) if seat != winner}
    total = sum(normal.values())
    upper, lower = (winner - 1) % 4, (winner + 1) % 4
    transfers = []
    if chi_counts[winner] >= 3:
        transfers.append(Transfer(upper, winner, total, "三吃承包"))
    if chi_counts[lower] >= 3:
        transfers.append(Transfer(lower, winner, 2 * total, "三吃反承包"))
    if not transfers:
        transfers = [Transfer(payer, winner, amount, "自摸")
                     for payer, amount in normal.items()]
    changes = [0] * 4
    for transfer in transfers:
        changes[transfer.payer] -= transfer.points
        changes[winner] += transfer.points
    return Settlement(tuple(changes), tuple(transfers), total, multiplier)


def settle_win(score, winner, dealer, dealer_streak, chi_counts=(0, 0, 0, 0)):
    if not isinstance(score, WinScore):
        raise ValueError("缺少合法和牌计分")
    return _settlement(winner, score.points, dealer, dealer_streak, chi_counts)


def payments(winner, points, *, dealer, dealer_streak, chi_counts=(0, 0, 0, 0)):
    return list(_settlement(winner, points, dealer, dealer_streak, chi_counts).changes)


def next_dealer(dealer, dealer_streak, winner=None):
    """Draw/dealer win advances 1→2→3; a different winner starts at one."""
    if type(dealer) is not int or dealer not in range(4):
        raise ValueError("非法庄家")
    if type(dealer_streak) is not int or not 1 <= dealer_streak <= 3:
        raise ValueError("老庄数必须为 1 至 3")
    if winner is not None and (type(winner) is not int or winner not in range(4)):
        raise ValueError("非法和家")
    if winner is None or winner == dealer:
        return dealer, min(3, dealer_streak + 1)
    return winner, 1
