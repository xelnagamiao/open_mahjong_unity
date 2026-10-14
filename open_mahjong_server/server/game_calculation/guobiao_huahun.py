"""国标花混：季节花作癞子，结构求解复用四癞子，结算政策独立。"""
from collections import Counter
from dataclasses import replace
from functools import lru_cache

from . import guobiao_four_jokers as shared
from .guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles
from .jokers import TILES, can_win as structural_win, iter_winning_shapes
from .jokers.solver import _prepare

SUB_RULE = "guobiao/huahun"
JOKERS = (51, 52, 53, 54)
FLOWERS = (55, 56, 57, 58)
FLOWER_KONG = "G51"  # Wire format; the mask retains all four distinct seasons.


def enabled(state):
    return getattr(state, "sub_rule", "") == SUB_RULE


def normalize(hand):
    return tuple(sorted(shared.JOKER if t in JOKERS else t for t in hand))


def joker_count(hand, melds=()):
    return sum(t in JOKERS for t in hand) + 4 * tuple(melds).count(FLOWER_KONG)


def minimum(hand, melds=()):
    return (8, 8, 16, 32, 64)[min(4, joker_count(hand, melds))]


def is_flower(tile):
    return tile in FLOWERS


def can_flower_kong(hand):
    return all(hand.count(t) == 1 for t in JOKERS)


def legal_cut(player, tile, cut_class):
    return tile not in JOKERS or (cut_class is True
        and getattr(player, "has_draw_slot", False)
        and bool(player.hand_tiles) and player.hand_tiles[-1] == tile
        and getattr(player, "last_drawn_tile", None) == tile)


def timeout_cut(player, tile, cut_class):
    if legal_cut(player, tile, cut_class):
        return tile, cut_class
    return next(t for t in reversed(player.hand_tiles) if t not in JOKERS), False


def auto_cut(player, selection):
    """Retain normal draw-discard behavior, but never cut a locked season."""
    tile, index, cut_class = selection
    legal_tile, legal_class = timeout_cut(player, tile, cut_class)
    if legal_tile != tile:
        index = max(i for i, t in enumerate(player.hand_tiles) if t == legal_tile)
    return legal_tile, index, legal_class


def forbidden_cuts(player):
    return [t for t in JOKERS if t in player.hand_tiles and not legal_cut(player, t, True)]


def _valid(hand, melds):
    if tuple(melds).count(FLOWER_KONG) > 1:
        return False
    counts = Counter(hand)
    if any(t not in TILES + JOKERS or n > (1 if t in JOKERS else 4) for t, n in counts.items()):
        return False
    return not (FLOWER_KONG in melds and any(t in JOKERS for t in hand))


@lru_cache(maxsize=256)
def _kong_policy(melds):
    base = shared.policy_for(melds)
    return replace(base, meld_count=3, shapes=("standard",)) if base else None


@lru_cache(maxsize=4096)
def _can_win(hand, melds):
    if not _valid(hand, melds):
        return False
    if FLOWER_KONG not in melds:
        return shared.can_win(normalize(hand), melds)
    ordinary = tuple(m for m in melds if m != FLOWER_KONG)
    policy = _kong_policy(ordinary)
    return bool(policy and len(hand) + 3 * len(ordinary) == 11
                and structural_win(hand, ordinary, policy))


def can_win(hand, melds=()):
    return _can_win(tuple(sorted(hand)), tuple(melds))


@lru_cache(maxsize=4096)
def _waiting(hand, melds):
    if not _valid(hand, melds) or len(hand) + 3 * len(melds) != 13:
        return frozenset()
    if FLOWER_KONG not in melds:
        waits = shared.waiting_tiles(normalize(hand), melds)
        if shared.JOKER in waits:
            waits.remove(shared.JOKER)
            waits.update(t for t in JOKERS if t not in hand)
        return frozenset(waits)
    return frozenset(t for t in TILES if _can_win(tuple(sorted(hand + (t,))), melds))


def waiting_tiles(hand, melds=()):
    return set(_waiting(tuple(sorted(hand)), tuple(melds)))


class _FlowerKongScorer(Chinese_Hepai_Check):
    # A flower kong occupies a group, but is neither an ordinary pung/kong nor
    # any suit/honor. Never invent a logical tile to make the scorer accept it.
    combination_to_tiles_dict = {**Chinese_Hepai_Check.combination_to_tiles_dict, "H51": []}
    count_model_dict = {**Chinese_Hepai_Check.count_model_dict, "huagang": 88}
    eng_to_chinese_dict = {**Chinese_Hepai_Check.eng_to_chinese_dict, "huagang": "花杠"}
    repel_model_dict = {**Chinese_Hepai_Check.repel_model_dict, "huagang": []}

    def fan_count_output(self, player, combination_str, zimo, way):
        player.fan_list.append("huagang")
        return super().fan_count_output(player, combination_str, zimo, way)


@lru_cache(maxsize=1024)
def _base_score(hand, melds, way, tile, multi_wait, allow_single_wait):
    if tile not in hand or not _can_win(hand, melds):
        return 0, ()
    if FLOWER_KONG not in melds:
        score, fans = shared.hepai_check(normalize(hand), melds, way,
            shared.JOKER if tile in JOKERS else tile, multi_wait, allow_single_wait)
        return score, tuple(fans)
    ordinary = tuple(m for m in melds if m != FLOWER_KONG)
    before = list(hand); before.remove(tile)
    way = [w for w in way if w != "和单张"]
    if allow_single_wait and waiting_tiles(before, melds) == {tile}:
        way.append("和单张")
    best = (0, ())
    scorer = _FlowerKongScorer()
    for shape in iter_winning_shapes(hand, ordinary, _kong_policy(ordinary), tile,
                                     equivalence="logical_structure"):
        closed = [shared._code(g) for g in shape.groups if not g.external]
        player = PlayerTiles([], list(ordinary) + ["H51"] + closed, 14)
        player.initial_combination_count = len(ordinary)
        score, fans = scorer.fan_count(player, tile, list(way))
        if score > best[0]:
            best = score, tuple(fans)
    return best


def flower_score(count, progressive=True):
    return (0, 4, 8, 16, 32)[count] if progressive else count


def hepai_check(hand, melds, way, tile, joker_multi_wait=False, allow_single_wait=True,
                no_joker_bonus="add8", black_flower_progressive=True):
    flowers = way.count("花牌")
    if flowers > 4 or no_joker_bonus not in ("add8", "double", "none"):
        return 0, []
    # Only the physical winning tile can be the fourth visible copy. A season
    # cannot inherit this fan from its replacement; strip it before cache lookup.
    winning_joker = tile in JOKERS
    score, fans = _base_score(tuple(sorted(hand)), tuple(melds),
        tuple(sorted(w for w in way if w != "花牌"
                     and not (winning_joker and w == "和绝张"))),
        tile, joker_multi_wait, allow_single_wait)
    if not score:
        return 0, []
    fans = list(fans)
    if flowers:
        score += flower_score(flowers, black_flower_progressive)
        fans.append(f"黑花{flowers}朵" if black_flower_progressive else f"花牌*{flowers}")
    if no_joker_bonus != "none" and joker_count(hand, melds) == 0:
        bonus = 8 if no_joker_bonus == "add8" else score
        fans.append("无癞子加番" if no_joker_bonus == "add8" else f"无癞子翻倍*{bonus}")
        score += bonus
    return score, fans


def qualifying_fan(result):
    score, fans = result
    for fan in fans:
        if fan.startswith("花牌*") or fan.startswith("无癞子翻倍*"):
            score -= int(fan.split("*")[1])
        elif fan == "无癞子加番":
            score -= 8
        elif fan.startswith("黑花") and fan.endswith("朵"):
            score -= flower_score(int(fan[2:-1]))
    return score


def hint_snapshot(hand, melds):
    result = dict(sub_rule=SUB_RULE, hand=list(hand), melds=list(melds), waits=[], discard_waits={})
    if len(hand) + 3 * len(melds) == 13:
        result["waits"] = sorted(waiting_tiles(hand, melds))
    elif len(hand) + 3 * len(melds) == 14:
        # The authoritative snapshot filters retained jokers using draw-slot
        # state. The stateless endpoint includes only ordinary discard rows.
        for cut in sorted(set(hand) - set(JOKERS)):
            rest = list(hand); rest.remove(cut)
            result["discard_waits"][cut] = sorted(waiting_tiles(rest, melds))
    return result


def calculate_hint(hand, melds, ron_way, self_way, tile, limit=8, joker_multi_wait=False,
                   allow_single_wait=True, no_joker_bonus="add8", black_flower_progressive=True):
    scores = []
    for way, kind, prefix in ((ron_way, "dianhe", ""), (self_way, "zimo", "自摸")):
        result = hepai_check(hand, melds, way, tile, joker_multi_wait, allow_single_wait,
                             no_joker_bonus, black_flower_progressive)
        scores.append(result[0])
        if result[0] and qualifying_fan(result) >= minimum(hand, melds):
            return dict(kind=kind, label=f"{prefix}{result[0]}番", score=result[0])
    return dict(kind="wuyi", label="未起和", score=max(scores))
