from collections import Counter
from functools import lru_cache
from itertools import combinations

RULE_VERSION = "shangrao-v0.2"
LEGACY_VERSION = "shangrao-v1"
SUB_RULE = "shangrao/standard"
DESCRIPTION = "上饶麻将是流行于江西上饶及周边的玩法，使用136张麻将牌（无花牌），只能自摸。除了基本的屁和牌型，还有碰碰胡、七对、九幺和十三烂特色牌型。宝牌（即“癞子”）为核心特色，单宝仅可作将，多余宝才能作万能牌，打出宝牌（“飞宝”）和牌可翻倍，因此又称为“飞宝麻将”。特色牌型和翻倍机制配合只能自摸的规则，使牌局兼具趣味性和策略性。"
NUMBERS = tuple(s * 10 + n for s in (1, 2, 3) for n in range(1, 10))
HONORS = tuple(range(41, 48))
TILES = NUMBERS + HONORS
INDEX = {tile: i for i, tile in enumerate(TILES)}
TERMINALS = frozenset(t for t in TILES if t >= 40 or t % 10 in (1, 9))
SEQUENCES = tuple((t, t + 1, t + 2) for t in NUMBERS if t % 10 <= 7)
SEQUENCES_BY_TILE = {t: tuple(s for s in SEQUENCES if t in s) for t in TILES}
SPACED = tuple(c for n in range(4) for c in combinations(range(1, 10), n)
               if all(b - a >= 3 for a, b in zip(c, c[1:])))
CONFIG = dict(rule_version=RULE_VERSION, qingyise=False, ziyise=False,
              allow_special_bao=False, four_bao_reward=False, flying_seven_stars=False)


def normalize_config(value=None):
    if value is None:
        return dict(CONFIG)
    if not isinstance(value, dict) or set(value) - CONFIG.keys():
        raise ValueError("上饶麻将配置包含未知选项")
    if value.get("rule_version", RULE_VERSION) not in (RULE_VERSION, LEGACY_VERSION):
        raise ValueError("不支持的上饶麻将规则版本")
    for key in CONFIG.keys() - {"rule_version"}:
        if key in value and type(value[key]) is not bool:
            raise ValueError(f"{key} 必须为布尔值")
    return {**CONFIG, **value}


def bao_tile(indicator):
    if type(indicator) is not int or indicator not in TILES:
        raise ValueError("宝指示牌无效")
    if indicator < 40:
        return indicator // 10 * 10 + indicator % 10 % 9 + 1
    cycle = (41, 42, 43, 44) if indicator < 45 else (45, 47, 46)
    return cycle[(cycle.index(indicator) + 1) % len(cycle)]


def meld_tiles(code):
    if not isinstance(code, str) or len(code) != 3 or code[0] not in "skgG":
        raise ValueError("副露编码无效")
    tile = int(code[1:])
    if tile not in TILES:
        raise ValueError("副露牌张无效")
    if code[0] == "s":
        group = (tile - 1, tile, tile + 1)
        if group not in SEQUENCES:
            raise ValueError("不能吃字牌或跨花色吃牌")
        return group
    return (tile,) * (4 if code[0] in "gG" else 3)


def valid_hand(hand, melds=(), *, indicator=11, complete=True):
    try:
        if indicator not in TILES or len(melds) > 4 or len(hand) + len(melds) * 3 != (14 if complete else 13):
            return False
        physical = list(hand)
        for code in melds:
            physical.extend(meld_tiles(code))
        counts = Counter(physical)
        return all(type(t) is int and t in TILES and n <= (3 if t == indicator else 4)
                   for t, n in counts.items())
    except (ValueError, TypeError):
        return False


@lru_cache(maxsize=50000)
def groups(counts, wild, count, triplets=False):
    if wild < 0 or count < 0 or sum(counts) + wild != count * 3:
        return False
    first = next((i for i, n in enumerate(counts) if n), None)
    if first is None:
        return True
    rest = list(counts)
    used = min(3, rest[first])
    rest[first] -= used
    if groups(tuple(rest), wild - (3 - used), count - 1, triplets):
        return True
    if not triplets:
        for sequence in SEQUENCES_BY_TILE[TILES[first]]:
            rest, needed = list(counts), 0
            for tile in sequence:
                i = INDEX[tile]
                if rest[i]:
                    rest[i] -= 1
                else:
                    needed += 1
            if groups(tuple(rest), wild - needed, count - 1):
                return True
    return False


def standard_pairs(hand, melds=(), *, indicator=11, triplets=False):
    if not valid_hand(hand, melds, indicator=indicator):
        return ()
    if triplets and any(code[0] == "s" for code in melds):
        return ()
    bao = bao_tile(indicator)
    wild = hand.count(bao)
    counts = tuple(hand.count(tile) if tile != bao else 0 for tile in TILES)
    choices = []
    if wild:
        if wild >= 2 and groups(counts, wild - 2, 4 - len(melds), triplets):
            choices.append((bao, 2))
        for i, n in enumerate(counts):
            if n:
                rest = list(counts)
                rest[i] -= 1
                if groups(tuple(rest), wild - 1, 4 - len(melds), triplets):
                    choices.append((TILES[i], 1))
    else:
        for i, n in enumerate(counts):
            if n >= 2:
                rest = list(counts)
                rest[i] -= 2
                if groups(tuple(rest), 0, 4 - len(melds), triplets):
                    choices.append((TILES[i], 0))
    return tuple(choices)


def nine_yao(hand, melds=(), *, indicator=11, allow_bao=False):
    if not valid_hand(hand, melds, indicator=indicator):
        return False
    bao = bao_tile(indicator)
    return (all(t in TERMINALS or allow_bao and t == bao for t in hand)
            and all(t in TERMINALS for code in melds for t in meld_tiles(code)))


def thirteen_lan(hand, melds=(), *, indicator=11, allow_bao=False):
    if melds or not valid_hand(hand, indicator=indicator):
        return False
    bao = bao_tile(indicator)
    rest = [t for t in hand if not allow_bao or t != bao]
    if len(set(rest)) != len(rest):
        return False
    capacity = 7
    for suit in (1, 2, 3):
        ranks = {t % 10 for t in rest if t // 10 == suit}
        choices = [len(c) for c in SPACED if ranks <= set(c)]
        if not choices:
            return False
        capacity += max(choices)
    return capacity >= 14


def seven_stars(hand, melds=(), *, indicator=11, allow_bao=False, flown=False):
    bao = bao_tile(indicator)
    physical = [*hand, *(t for code in melds for t in meld_tiles(code))]
    natural = set(t for t in physical if not allow_bao or t != bao)
    missing = set(HONORS) - natural
    if flown and bao in HONORS and bao not in physical:
        missing.discard(bao)
    return len(missing) <= (hand.count(bao) if allow_bao else 0)


def seven_pairs(hand, melds=(), *, indicator=11, winning_tile=None):
    if melds or not valid_hand(hand, indicator=indicator):
        return False
    bao = bao_tile(indicator)
    rest = list(hand)
    if bao in rest and winning_tile is not None:
        if winning_tile not in rest:
            return False
        rest.remove(winning_tile)
        if bao not in rest:
            return False
        rest.remove(bao)
    wild = rest.count(bao)
    odd = sum(n % 2 for t, n in Counter(rest).items() if t != bao)
    return odd <= wild and (wild - odd) % 2 == 0


def score_hand(hand, melds=(), *, indicator=11, winning_tile=None, config=None,
               flying_bao=0, flying_seven_stars=False, after_kong=False, opening=False):
    config = normalize_config(config)
    legacy = config["rule_version"] == LEGACY_VERSION
    if not valid_hand(hand, melds, indicator=indicator) or winning_tile not in hand:
        return None
    if type(flying_bao) is not int or not 0 <= flying_bao <= 4:
        raise ValueError("飞宝次数无效")
    bao = bao_tile(indicator)
    wild = hand.count(bao)
    first_bao = not opening and winning_tile == bao and wild == 1
    pairs = () if first_bao else standard_pairs(hand, melds, indicator=indicator)
    triplets = () if first_bao else standard_pairs(hand, melds, indicator=indicator, triplets=True)
    seven = not first_bao and seven_pairs(hand, melds, indicator=indicator,
                                        winning_tile=None if opening else winning_tile)
    luxury = seven and any(n == 4 for t, n in Counter(hand).items() if t != bao)
    nine = nine_yao(hand, melds, indicator=indicator, allow_bao=config["allow_special_bao"])
    lan = thirteen_lan(hand, melds, indicator=indicator, allow_bao=legacy and config["allow_special_bao"])
    stars = seven_stars(hand, melds, indicator=indicator, allow_bao=legacy and config["allow_special_bao"],
                        flown=config["flying_seven_stars"] and flying_seven_stars)
    candidates = []
    if pairs:
        candidates.append(("钓头" if wild else "平和", 1, "standard", pairs))
    if triplets:
        candidates.append(("碰碰和" if wild else "清碰", 2 if wild else 4, "triplets", triplets))
    if seven:
        odd = sum(n % 2 for t, n in Counter(hand).items() if t != bao)
        pair_bao = 2 if wild >= 2 and odd <= wild - 2 else min(1, wild)
        candidates.append((("豪华" if luxury else "") + ("七对" if wild else "硬七对"),
                           (2 if wild else 4) * (2 if luxury else 1), "seven", ((bao, pair_bao),)))
    if nine:
        candidates.append(("七星九幺" if stars else "九幺", 2 if stars else 1, "nine", ()))
        if seven and not wild:
            candidates.append(("九幺硬七", 8, "seven", ()))
        if triplets and not wild:
            candidates.append(("九幺清碰", 8, "triplets", triplets))
        if not legacy and config["allow_special_bao"] and wild and bao not in TERMINALS and winning_tile in TERMINALS:
            if seven:
                candidates.append(("九幺七对", 4, "seven", ((bao, pair_bao),)))
            if triplets:
                candidates.append(("九幺碰碰和", 4, "triplets", triplets))
    if lan:
        candidates.append(("七星十三烂" if stars else "十三烂", 4 if stars else 2, "lan", ()))
    if not candidates:
        return None
    natural = [t for t in hand if t != bao] + [t for code in melds for t in meld_tiles(code)]
    suits = {t // 10 for t in natural}
    qing = (config["qingyise"] and len(suits) == 1 and next(iter(suits), 4) in (1, 2, 3)
            and (not wild or winning_tile // 10 in suits))
    zi = config["ziyise"] and nine and all(t in HONORS for t in natural) and (legacy or not wild or winning_tile in HONORS)
    if not legacy:
        if qing:
            for name, base, shape, pair_choices in list(candidates):
                if shape in ("standard", "triplets", "seven"):
                    label = ("清一色" if wild else "无宝清一色") if shape == "standard" else "清一色" + name
                    candidates.append((label, 4 if shape == "standard" and not wild else base * 2, shape, pair_choices))
        if zi:
            candidates.insert(0, ("字一色", 2, "nine", ()))
            if not wild or winning_tile in HONORS:
                if seven:
                    candidates.append(("字一色七对" if wild else "字一色硬七对", 8 if wild else 16, "seven", ((bao, pair_bao),)))
                if triplets:
                    candidates.append(("字一色碰碰和" if wild else "字一色清碰", 8 if wild else 16, "triplets", triplets))
    results = []
    for name, base, shape, pair_choices in candidates:
        values, multipliers = {name: base}, {}
        if legacy and qing and shape in ("standard", "triplets", "seven"):
            multipliers["清一色"] = 2
        if legacy and zi:
            multipliers["字一色"] = 2
        if not melds and shape not in ("seven", "lan"):
            multipliers["门清"] = 2
        if len(melds) == 4:
            multipliers["单钓"] = 2
        if after_kong:
            multipliers["杠上开花"] = 2
        if pair_choices and wild and winning_tile == bao:
            multipliers["宝钓宝"] = 2
        if winning_tile != bao and any(used == 2 for _, used in pair_choices):
            multipliers["宝还原"] = 2
        if flying_bao:
            multipliers["飞宝"] = 2 ** flying_bao
        points = base
        for multiplier in multipliers.values():
            points *= multiplier
        results.append(dict(points=points, base=base, pattern=name, shape=shape,
                            fan_values=values, multipliers=multipliers,
                            fan_names=[name, *multipliers]))
    return max(results, key=lambda result: (result["points"], result["base"]))


def payments(winner, points):
    if type(winner) is not int or winner not in range(4) or type(points) is not int or points < 0:
        raise ValueError("结算座位或点数无效")
    return [points * 3 if i == winner else -points for i in range(4)]


def match_results(scores):
    return [dict(score=score, rank=1 + sum(other > score for other in scores)) for score in scores]
