"""Physical Fuzhou tiles and five-meld scoring; no transport or mutable state.

The golden indicator is removed from play. Its remaining three physical copies
are wild only inside a winning closed hand. All honours are replacement flowers.
"""
from collections import Counter
from dataclasses import dataclass
from functools import lru_cache

from ..jokers import JokerPolicy, can_win, structural_waits, iter_winning_shapes, can_form_melds

SUB_RULE = "fuzhou/standard"
VERSION = "fuzhou-user-20261014-v1"
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
HONORS = tuple(range(41, 48))
FLOWERS = HONORS + tuple(range(51, 59))
PHYSICAL = tuple(t for t in NUMBERS + HONORS for _ in range(4)) + tuple(range(51, 59))


def normalize_config(raw=None):
    if raw is None:
        raw = {}
    if not isinstance(raw, dict) or set(raw) - {"rule_version", "base_score"}:
        raise ValueError("未知福州馆规设置")
    if raw.get("rule_version", VERSION) != VERSION:
        raise ValueError("不支持的福州规则版本")
    base = raw.get("base_score", 10)
    if type(base) is not int or base not in (10, 15, 20):
        raise ValueError("福州底分只能为10、15或20")
    return dict(rule_version=VERSION, base_score=base)


@lru_cache(maxsize=27)
def policy(jin):
    if type(jin) is not int or jin not in NUMBERS:
        raise ValueError("金指示牌必须是序数牌")
    return JokerPolicy(joker_tiles=(jin,), logical_tiles=NUMBERS,
                       physical_tiles=NUMBERS, physical_limits=((jin, 3),), meld_count=5)


def meld_tiles(meld):
    if not isinstance(meld, str) or len(meld) != 3 or meld[0] not in "skgG":
        raise ValueError("非法面子")
    try:
        tile = int(meld[1:])
    except ValueError:
        raise ValueError("非法面子牌张") from None
    if tile not in NUMBERS:
        raise ValueError("福州花牌不能组成面子")
    if meld[0] == "s":
        if tile % 10 not in range(2, 9):
            raise ValueError("非法顺子")
        return (tile - 1, tile, tile + 1)
    return (tile,) * (3 if meld[0] == "k" else 4)


def valid_tiles(hand, melds=(), flowers=(), *, jin=None, allow_flowers=False):
    try:
        if jin is not None:
            policy(jin)
        if len(melds) > 5 or any(type(t) is not int for t in hand) or any(t not in FLOWERS for t in flowers):
            return False
        allowed = NUMBERS + FLOWERS if allow_flowers else NUMBERS
        if any(t not in allowed for t in hand):
            return False
        physical = [t for m in melds for t in meld_tiles(m)]
        if jin in physical:
            return False
        counts = Counter(list(hand) + physical + list(flowers))
        return all(n <= (1 if t >= 51 else 3 if t == jin else 4) for t, n in counts.items())
    except (ValueError, TypeError):
        return False


def is_complete(hand, melds=(), *, jin):
    return valid_tiles(hand, melds, jin=jin) and can_win(hand, melds, policy=policy(jin))


def waiting_tiles(hand, melds=(), *, jin):
    if not valid_tiles(hand, melds, jin=jin):
        return frozenset()
    return structural_waits(hand, melds, policy=policy(jin))


def flower_points(flowers):
    counts = Counter(flowers)
    if any(type(t) is not int or t not in FLOWERS or n > (4 if t in HONORS else 1)
           for t, n in counts.items()):
        raise ValueError("花牌记账无效")
    sets = sum(counts[t] == 4 for t in HONORS)
    sets += sum(all(counts[t] == 1 for t in group) for group in (range(51, 55), range(55, 59)))
    return len(flowers) + sets * 2


def reserved_tiles(players):
    """Four identical honour flowers extend the wall once, without double fees."""
    return 18 + sum(sum(1 if m[0] == "g" else 2 if m[0] == "G" else 0 for m in melds)
                    + 2 * sum(Counter(flowers)[t] == 4 for t in HONORS)
                    for melds, flowers in players)


def can_youjin(hand, melds=(), *, jin):
    """A legal declaration discards the extra tile from five melds + gold pair."""
    if not is_complete(hand, melds, jin=jin) or jin not in hand:
        return frozenset()
    cuts = set()
    for tile in set(hand):
        remaining = list(hand)
        remaining.remove(tile)
        if jin not in remaining:
            continue
        remaining.remove(jin)
        if can_form_melds(remaining, melds, policy=policy(jin), meld_count=5):
            cuts.add(tile)
    return frozenset(cuts)


def golden_middle_wait(hand, melds=(), *, jin):
    """Declaration is natural golden-centre completion, not any wild win."""
    if jin % 10 in (1, 9) or not valid_tiles(hand, melds, jin=jin):
        return False
    if hand.count(jin) >= 3:
        return False
    cards = list(hand) + [jin]
    for shape in iter_winning_shapes(cards, melds, policy=policy(jin), winning_index=len(cards) - 1):
        g = shape.groups[shape.winning_group]
        if g.kind == "sequence" and g.logical == (jin - 1, jin, jin + 1):
            at = g.indices.index(len(cards) - 1)
            if g.logical[at] == jin:
                return True
    return False


@dataclass(frozen=True)
class WinContext:
    source: str = "self_draw"
    winning_tile: int | None = None
    dealer_streak: int = 0
    after_ready_kong: bool = False
    heavenly: bool = False
    qiangjin: bool = False
    golden_middle: bool = False
    youjin: int = 0


def _shape_patterns(shape, hand, melds, jin, context):
    patterns = []
    if all(g.kind in ("triplet", "kong") for g in shape.melds):
        patterns.append(("all_pungs", 15, "碰碰胡"))
    if len(hand) == 2 and len(melds) == 5:
        patterns.append(("single_pair", 60, "大吊车"))
    if any(g.kind == "triplet" and g.logical == (jin,) * 3 and g.physical == (jin,) * 3
           and not g.external for g in shape.groups):
        patterns.append(("gold_dragon", 120, "金龙"))
    if context.youjin != 2 and any(g.kind == "pair" and g.physical == (jin, jin) for g in shape.groups):
        patterns.append(("gold_pair", 60, "金雀"))
    physical = list(hand) + [t for m in melds for t in meld_tiles(m)]
    natural_suits = {t // 10 for t in physical if t != jin}
    if len(natural_suits) == 1:
        patterns.append(("mixed_suit", 30, "混一色") if jin in physical else ("pure_suit", 60, "清一色"))
    return patterns


def score_hand(hand, melds=(), flowers=(), *, jin, context=None, base_score=10, special=None):
    """Return a full decomposition-backed additive quote, or None.

    Special automatic-shape wins are explicit so the state machine owns the
    choice and precedence. They suppress other special patterns, not base items.
    """
    context = context or WinContext()
    if context.source not in ("self_draw", "discard", "rob_kong", "opening"):
        return None
    if type(base_score) is not int or base_score not in (10, 15, 20):
        raise ValueError("非法底分")
    if type(context.dealer_streak) is not int or context.dealer_streak < 0 or context.youjin not in (0, 1, 2):
        raise ValueError("非法连庄或游金状态")
    if not valid_tiles(hand, melds, flowers, jin=jin, allow_flowers=special == "flower"):
        return None
    if context.source in ("discard", "rob_kong") and context.winning_tile == jin:
        return None
    patterns, witness = [], None
    if special == "flower":
        if len(flowers) < 20:
            return None
        patterns = [("flower_win", 60, "花胡")]
    elif special == "three_gold":
        if hand.count(jin) != 3 or len(hand) + len(melds) * 3 not in (16, 17):
            return None
        patterns = [("three_gold", 40, "三金倒")]
    elif special is not None:
        return None
    else:
        if not is_complete(hand, melds, jin=jin):
            return None
        winning_index = len(hand) - 1 if context.winning_tile is not None and hand[-1] == context.winning_tile else None
        best = -1
        for shape in iter_winning_shapes(hand, melds, policy=policy(jin), winning_index=winning_index):
            candidate = _shape_patterns(shape, hand, melds, jin, context)
            value = sum(row[1] for row in candidate)
            if value > best:
                patterns, best = candidate, value
                witness = dict(assignment=list(shape.assignment),
                               groups=[dict(kind=g.kind, physical=list(g.physical), logical=list(g.logical)) for g in shape.groups])
        if context.after_ready_kong:
            patterns.append(("kong_draw", 10, "杠上开花"))
        if context.source == "rob_kong":
            patterns.append(("rob_kong", 15, "抢杠"))
        if len(flowers) == 1:
            patterns.append(("one_flower", 15, "一张花"))
        if not flowers and not any(m[0] in "gG" for m in melds):
            patterns.append(("no_flowers_kongs", 30, "无花无杠"))
        if context.heavenly:
            patterns.append(("heavenly", 40, "天胡"))
        if context.qiangjin:
            patterns.append(("qiangjin", 30, "抢金"))
        if context.golden_middle:
            patterns.append(("gold_middle", 40, "听金坎"))
        if context.youjin:
            patterns.append(("youjin" + str(context.youjin), 60 * context.youjin, "一游" if context.youjin == 1 else "二游"))
    items = [("base", base_score, "底分"), ("flowers", flower_points(flowers), "花分"),
             ("gold", hand.count(jin), "金牌分"),
             ("kongs", sum(2 if m[0] == "G" else 1 if m[0] == "g" else 0 for m in melds), "杠分"),
             ("dealer", context.dealer_streak * 3, "连庄分")] + patterns
    return dict(points=sum(value for _, value, _ in items), pattern_points=sum(value for _, value, _ in patterns),
                items=[dict(id=key, points=value, name=name) for key, value, name in items],
                special=special, witness=witness, rule_version=VERSION)


def payments(winner, points, payer=None):
    if type(winner) is not int or winner not in range(4) or type(points) is not int or points < 1:
        raise ValueError("非法和家或分数")
    if payer is not None and (type(payer) is not int or payer not in range(4) or payer == winner):
        raise ValueError("非法放铳者")
    result = [0] * 4
    for i in ([payer] if payer is not None else range(4)):
        if i != winner:
            result[i] -= points
            result[winner] += points
    return result
