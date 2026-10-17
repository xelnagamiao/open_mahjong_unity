"""Five-meld MCR extension, based on the platform's 新编 MCR (2025.08).

See /rulebooks/guobiao-sixteen.html for the deliberate 17-tile interpretations.
The shared exact hand solver supplies witnesses; this module scores whole
witnesses and winning-tile placements, never selected 14-tile subsets.
"""
from collections import Counter
from dataclasses import replace
from functools import lru_cache
from itertools import combinations

from .guobiao_hepai_check import Chinese_Hepai_Check as MCR
from .jokers import JokerPolicy, ResolvedGroup, TILES, can_win as standard_win, iter_winning_shapes
from .jokers.solver import _prepare

SUB_RULE = "guobiao/sixteen"
HAND_SIZE = 16
WIN_SIZE = 17
POLICY = JokerPolicy(meld_count=5)
FORMAL_POLICY = replace(POLICY, physical_limits=tuple((t, 5) for t in TILES))
KNITTED = tuple(tuple(sorted(MCR.combination_to_tiles_dict[f"z{i}"])) for i in range(6))
REPEATABLE = frozenset(("siguiyi", "shuangtongke", "yibangao", "xixiangfeng",
                        "lianliu", "yaojiuke", "huapai"))
FIXED_FOURTEEN = frozenset(("jiulianbaodeng", "lianqidui", "shisanyao", "qiduizi",
                           "qixingbukao", "quanbukao", "yiseshuanglonghui", "sanseshuanglonghui"))


def enabled(state):
    return getattr(state, "sub_rule", "") == SUB_RULE


def _knitted_remainders(hand, melds, policy):
    if len(melds) > 2:
        return
    counts = Counter(hand)
    for pattern in KNITTED:
        if all(counts[t] for t in pattern):
            rest = counts.copy()
            rest.subtract(pattern)
            yield pattern, tuple(sorted(rest.elements())), replace(policy, meld_count=2)


@lru_cache(maxsize=16384)
def _can_win(hand, melds, formal=False):
    policy = FORMAL_POLICY if formal else POLICY
    if len(hand) + 3 * len(melds) != WIN_SIZE or _prepare(hand, melds, policy) is None:
        return False
    return (standard_win(hand, melds, policy)
            or any(standard_win(rest, melds, rest_policy)
                   for _, rest, rest_policy in _knitted_remainders(hand, melds, policy)))


def can_win(hand, melds=()):
    return _can_win(tuple(sorted(hand)), tuple(melds))


@lru_cache(maxsize=8192)
def _waiting(hand, melds, formal=False):
    # Even formal waits begin with a physically valid actual 16-tile hand.
    if len(hand) + 3 * len(melds) != HAND_SIZE or _prepare(hand, melds, POLICY) is None:
        return frozenset()
    return frozenset(t for t in TILES if _can_win(tuple(sorted(hand + (t,))), melds, formal))


def waiting_tiles(hand, melds=()):
    return set(_waiting(tuple(sorted(hand)), tuple(melds)))


def _shapes(hand, melds, winning_tile):
    yield from iter_winning_shapes(hand, melds, POLICY, winning_tile,
                                  equivalence="full")
    for pattern, rest, policy in _knitted_remainders(hand, melds, POLICY):
        knitted = ResolvedGroup("knitted", pattern, pattern, ())
        # The winning copy may be assigned to the knitted part or an ordinary
        # group of the SAME complete decomposition. Both are genuine choices.
        if winning_tile in pattern:
            for shape in iter_winning_shapes(rest, melds, policy, equivalence="full"):
                yield replace(shape, kind="knitted", groups=(knitted,) + shape.groups,
                              winning_group=0)
        if winning_tile in rest:
            for shape in iter_winning_shapes(rest, melds, policy, winning_tile,
                                             equivalence="full"):
                yield replace(shape, kind="knitted", groups=(knitted,) + shape.groups,
                              winning_group=shape.winning_group + 1)


def _binary(a, b):
    if a == b:
        return "yibangao"
    if a % 10 == b % 10:
        return "xixiangfeng"
    if a // 10 == b // 10:
        if abs(a - b) == 3:
            return "lianliu"
        if {a % 10, b % 10} == {2, 8}:
            return "laoshaofu"
    return None


def _double_chows(chows, contracted=()):
    """Maximum legal forest of two-chow relations (all are one point).

    A scored three-chow group is contracted to one vertex. Each remaining
    chow can introduce at most one further relation; cycles never manufacture
    points from an unrelated fifth chow. 老少副 is nonrepeatable.
    """
    ids = tuple(i for i, _ in chows)
    parents = {i: i for i in ids}
    for i in contracted[1:]:
        parents[i] = contracted[0]

    def root(p, x):
        while p[x] != x:
            x = p[x]
        return x

    edges = [(i, j, key) for (i, a), (j, b) in combinations(chows, 2)
             if (key := _binary(a, b))]
    best = Counter()

    def search(pos, p, chosen):
        nonlocal best
        if sum(chosen.values()) > sum(best.values()):
            best = chosen.copy()
        if pos == len(edges) or sum(chosen.values()) + len(edges) - pos <= sum(best.values()):
            return
        i, j, key = edges[pos]
        ri, rj = root(p, i), root(p, j)
        if ri != rj and not (key == "laoshaofu" and chosen[key]):
            linked = p.copy()
            linked[rj] = ri
            chosen[key] += 1
            search(pos + 1, linked, chosen)
            chosen[key] -= 1
        search(pos + 1, p, chosen)

    search(0, parents, Counter())
    return +best


def _major_combinations(chows, pungs):
    """The book permits just one of these major combination fans globally."""
    yield None, ()
    for n in (3, 4):
        for selected in combinations(chows, n):
            ids, values = zip(*selected)
            suits = {v // 10 for v in values}
            ranks = sorted(v % 10 for v in values)
            if len(suits) == 1:
                if len(set(ranks)) == 1:
                    yield ("yisesantongshun" if n == 3 else "yisesitongshun"), ids
                if len(set(ranks)) == n and len(set(b - a for a, b in zip(ranks, ranks[1:]))) == 1:
                    if ranks[1] - ranks[0] in (1, 2):
                        yield ("yisesanbugao" if n == 3 else "yisesibugao"), ids
                    if n == 3 and ranks == [2, 5, 8]:
                        yield "qinglong", ids
            elif n == 3 and len(suits) == 3:
                if len(set(ranks)) == 1:
                    yield "sansesantongshun", ids
                if ranks == [2, 5, 8]:
                    yield "hualong", ids
                if ranks == list(range(ranks[0], ranks[0] + 3)):
                    yield "sansesanbugao", ids
        for selected in combinations(pungs, n):
            ids, values = zip(*selected)
            if max(values) < 40 and len({v // 10 for v in values}) == 1:
                ranks = sorted(v % 10 for v in values)
                if ranks == list(range(ranks[0], ranks[0] + n)):
                    yield ("yisesanjiegao" if n == 3 else "yisesijiegao"), ()


# Explicit whole-hand exclusions, independent of the number of melds.
EXCLUDES = {
    "dasixi": ("pengpenghe", "quanfengke", "menfengke", "yaojiuke"),
    "lvyise": ("hunyise",), "sigang": ("pengpenghe", "dandiaojiang"),
    "qingyaojiu": ("pengpenghe", "quandaiyao", "shuangtongke", "yaojiuke", "wuzi"),
    "ziyise": ("pengpenghe", "quandaiyao", "yaojiuke"),
    "sianke": ("pengpenghe",), "yisesitongshun": ("siguiyi",),
    "yisesijiegao": ("pengpenghe",), "hunyaojiu": ("pengpenghe", "quandaiyao", "yaojiuke"),
    "quanshuangke": ("pengpenghe", "duanyao", "wuzi"), "qingyise": ("wuzi",),
    "quanda": ("dayuwu", "wuzi"), "quanzhong": ("duanyao", "wuzi"),
    "quanxiao": ("xiaoyuwu", "wuzi"), "quandaiwu": ("duanyao", "wuzi"),
    "santongke": ("shuangtongke",), "dayuwu": ("wuzi",), "xiaoyuwu": ("wuzi",),
    "tuibudao": ("queyimen",), "miaoshouhuichun": ("zimo",),
    "gangshangkaihua": ("zimo",), "qiangganghe": ("hejuezhang",),
    "quanqiuren": ("dandiaojiang",), "shuangangang": ("shuanganke",),
    "buqiuren": ("zimo",), "pinghe": ("wuzi",), "duanyao": ("wuzi",),
}


def _score_shape(shape, way, win, single_wait):
    groups = shape.groups
    tiles = tuple(t for g in groups for t in g.logical)
    domain, counts = set(tiles), Counter(tiles)
    chows = tuple((i, g.tile) for i, g in enumerate(groups) if g.kind == "sequence")
    pungs = tuple((i, g.tile) for i, g in enumerate(groups) if g.kind in ("triplet", "kong"))
    kongs = [g for g in groups if g.kind == "kong"]
    concealed_kongs = sum(g.concealed for g in kongs)
    pair = shape.pair
    self_draw = any(w in way for w in ("自摸", "杠上开花", "妙手回春", "last_deal"))
    concealed = len(shape.concealed_pungs(self_draw))
    closed = all(g.concealed for g in groups)
    ordinary = shape.kind == "standard"
    fan = Counter()

    def add(key, condition=True, count=1):
        if condition:
            fan[key] = count

    for key, allowed in (("lvyise", MCR.lvyise_set), ("qingyaojiu", MCR.qingyaojiu_set),
                         ("ziyise", MCR.zipai_set), ("quanda", MCR.quanda_set),
                         ("quanzhong", MCR.quanzhong_set), ("quanxiao", MCR.quanxiao_set),
                         ("dayuwu", MCR.dayuwu_set), ("xiaoyuwu", MCR.xiaoyuwu_set),
                         ("tuibudao", MCR.tuibudao_set), ("duanyao", MCR.duanyao_set)):
        add(key, domain <= allowed)
    suits = {t // 10 for t in domain if t < 40}
    honors = domain & MCR.zipai_set
    add("qingyise", len(suits) == 1 and not honors)
    add("hunyise", len(suits) == 1 and bool(honors))
    add("hunyaojiu", bool(honors) and bool(suits) and domain <= MCR.hunyaojiu_set)
    add("wumenqi", len(suits) == 3 and bool(domain & MCR.feng_set) and bool(domain & MCR.zhongbaifa_set))
    add("queyimen", len(suits) == 2)
    add("wuzi", not honors)
    add("quanshuangke", ordinary and len(pungs) == 5 and all(t < 40 and t % 2 == 0 for t in domain))
    add("quandaiwu", ordinary and all(any(t < 40 and t % 10 == 5 for t in g.logical) for g in groups))
    add("quandaiyao", ordinary and all(any(t in MCR.hunyaojiu_set for t in g.logical) for g in groups))
    add("pengpenghe", ordinary and len(pungs) == 5)
    add("pinghe", pair < 40 and len(chows) == (5 if ordinary else 2))
    add("zuhelong", shape.kind == "knitted")
    add("sianke", concealed >= 4)
    add("sananke", concealed == 3)
    add("shuanganke", concealed == 2)
    add("sigang", len(kongs) >= 4)
    add("sangang", len(kongs) == 3)
    if len(kongs) == 2:
        add(("shuangminggang", "mingangang", "shuangangang")[concealed_kongs])
    if len(kongs) == 1:
        add("angang" if concealed_kongs else "minggang")
    kong_tiles = {g.tile for g in kongs}
    add("siguiyi", True, sum(n == 4 and t not in kong_tiles for t, n in counts.items()))

    winds = {t for _, t in pungs if t in MCR.feng_set}
    dragons = {t for _, t in pungs if t in MCR.zhongbaifa_set}
    add("dasixi", len(winds) == 4)
    add("xiaosixi", len(winds) == 3 and pair in MCR.feng_set - winds)
    add("sanfengke", len(winds) == 3 and not fan["xiaosixi"])
    add("dasanyuan", len(dragons) == 3)
    add("xiaosanyuan", len(dragons) == 2 and pair in MCR.zhongbaifa_set - dragons)
    add("shuangjianke", len(dragons) == 2 and not fan["xiaosanyuan"])
    add("jianke", len(dragons) == 1)
    excluded_terminals = set(dragons)
    if len(winds) >= 3:
        excluded_terminals.update(winds)
    for prefix, key in (("场风", "quanfengke"), ("自风", "menfengke")):
        for n, label in enumerate("东南西北", 41):
            if prefix + label in way and n in winds:
                add(key)
                excluded_terminals.add(n)
    add("yaojiuke", True, sum(t in MCR.hunyaojiu_set and t not in excluded_terminals for _, t in pungs))
    add("santongke", any(len({t // 10 for _, t in pungs if t < 40 and t % 10 == rank}) == 3
                         for rank in range(1, 10)))
    add("shuangtongke", True, sum(a < 40 and b < 40 and a // 10 != b // 10 and a % 10 == b % 10
                                 for (_, a), (_, b) in combinations(pungs, 2)))
    add("sansesanjiegao", any(max(values) < 40 and len({v // 10 for v in values}) == 3
                             and sorted(v % 10 for v in values) == list(range(min(v % 10 for v in values), min(v % 10 for v in values) + 3))
                             for chosen in combinations(pungs, 3) for values in [tuple(v for _, v in chosen)]))

    add("quanqiuren", ordinary and not self_draw and sum(g.external and not g.concealed for g in groups) == 5)
    # 四暗刻 is no longer necessarily closed: the fifth meld can be exposed.
    # The book's 門清 principle therefore follows actual closure here.
    add("buqiuren" if self_draw else "menqianqing", closed)
    add("zimo", self_draw)
    for label, key in (("天和", "tianhe"), ("地和", "dihe"), ("人和", "renhe"),
                       ("妙手回春", "miaoshouhuichun"), ("last_deal", "miaoshouhuichun"),
                       ("海底捞月", "haidilaoyue"), ("last_cut", "haidilaoyue"),
                       ("杠上开花", "gangshangkaihua"), ("抢杠和", "qiangganghe"), ("和绝张", "hejuezhang")):
        add(key, label in way)
    add("huapai", True, way.count("花牌"))
    if single_wait and shape.winning_group >= 0:
        g = groups[shape.winning_group]
        if g.kind == "pair":
            add("dandiaojiang")
        elif g.kind == "sequence" and not g.external:
            if win == g.tile:
                add("qianzhang")
            elif (g.tile % 10, win - g.tile) in ((2, 1), (8, -1)):
                add("bianzhang")

    best = (0, Counter())
    for key, selected in _major_combinations(chows, pungs):
        current = +fan
        if key:
            current[key] = 1
        if len(selected) != 4:
            current.update(_double_chows(chows, selected))
        for high, exclusions in EXCLUDES.items():
            if current[high]:
                for low in exclusions:
                    current.pop(low, None)
        # Flowers alone do not prevent chicken hand, but any other fan does.
        if not any(n for k, n in current.items() if k != "huapai"):
            current["wufanhe"] = 1
        score = sum(MCR.count_model_dict[k] * n for k, n in current.items())
        if score > best[0]:
            best = score, current
    return best


def _code(g):
    if g.kind == "knitted":
        return "z" + str(KNITTED.index(g.logical))
    prefix = ("q" if g.kind == "pair" else ("G" if g.concealed else "g") if g.kind == "kong"
              else ("S" if g.concealed else "s") if g.kind == "sequence" else ("K" if g.concealed else "k"))
    return prefix + str(g.tile)


def _fan_labels(fans):
    return [MCR.eng_to_chinese_dict[k] + ("*" + str(fans[k]) if k in REPEATABLE else "")
            for k in MCR.count_model_dict if fans[k]]


def hepai_decompose(hand, melds, way, winning_tile, allow_single_wait=True):
    hand, melds, way = tuple(sorted(hand)), tuple(melds), tuple(way)
    if winning_tile not in hand or not _can_win(hand, melds):
        return []
    before = list(hand)
    before.remove(winning_tile)
    single = allow_single_wait and "天和" not in way and _waiting(tuple(before), melds, True) == {winning_tile}
    result, seen = [], set()
    for shape in _shapes(hand, melds, winning_tile):
        codes = tuple(_code(g) for g in shape.groups)
        identity = codes, shape.winning_group
        if identity in seen:
            continue
        seen.add(identity)
        score, fans = _score_shape(shape, way, winning_tile, single)
        result.append(dict(score=score, fan_list=_fan_labels(fans), fan_keys=list(+fans),
                           combinations=list(codes), winning_group=shape.winning_group,
                           shape=shape.kind))
    return sorted(result, key=lambda entry: -entry["score"])


@lru_cache(maxsize=4096)
def _score(hand, melds, way, winning_tile, allow_single_wait):
    choices = hepai_decompose(hand, melds, way, winning_tile, allow_single_wait)
    return (choices[0]["score"], tuple(choices[0]["fan_list"])) if choices else (0, ())


def hepai_check(hand, melds, way, winning_tile, allow_single_wait=True):
    score, fans = _score(tuple(sorted(hand)), tuple(melds), tuple(sorted(way)), winning_tile, allow_single_wait)
    return score, list(fans)


def hint_snapshot(hand, melds):
    """Web calculator API only; gameplay clients build their own previews."""
    result = dict(hand=list(hand), melds=list(melds), waits=[], discard_waits={})
    size = len(hand) + 3 * len(melds)
    if size == HAND_SIZE:
        result["waits"] = sorted(waiting_tiles(hand, melds))
    elif size == WIN_SIZE:
        for cut in sorted(set(hand)):
            rest = list(hand)
            rest.remove(cut)
            result["discard_waits"][cut] = sorted(waiting_tiles(rest, melds))
    return result


def calculate_hint(hand, melds, ron_way, self_way, tile, limit, allow_single_wait=True):
    """Web calculator API only; not a gameplay broadcast or Unity hint service."""
    ron, _ = hepai_check(hand, melds, ron_way, tile, allow_single_wait)
    if ron > 0 and ron - ron_way.count("花牌") >= limit:
        return dict(kind="dianhe", label=f"{ron}番", score=ron)
    own, _ = hepai_check(hand, melds, self_way, tile, allow_single_wait)
    if own > 0 and own - self_way.count("花牌") >= limit:
        return dict(kind="zimo", label=f"自摸{own}番", score=own)
    return dict(kind="wuyi", label="未起和", score=max(ron, own))
