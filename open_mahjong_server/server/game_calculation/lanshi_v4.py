"""蓝十第4版（MCR 1001—2025）四人计分，独立于其他国标子规则。

牌型、番种、面子关联和最终结算分四阶段计算。面子用实例编号，
以无环超图表达§3.5.1的不循环组合；和牌张归属也属于拆分的一部分。
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from functools import lru_cache
from itertools import combinations, permutations

TILES = tuple(s * 10 + r for s in (1, 2, 3) for r in range(1, 10)) + tuple(range(41, 48))
VALID_TILES = frozenset(TILES)
HONORS = frozenset(range(41, 48))
TERMINALS = frozenset((11, 19, 21, 29, 31, 39))
ORPHANS = TERMINALS | HONORS
REPEATABLE = frozenset(("siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "laoshaofu", "yaojiuke"))
OCCASIONAL = ("miaoshouhuichun", "haidilaoyue", "gangshangkaihua", "qiangganghe", "tianhe", "dihe")
UNRELATED = tuple(frozenset(s * 10 + r for s, offset in zip((1, 2, 3), offsets)
                            for r in range(offset, 10, 3)) | HONORS
                  for offsets in permutations((1, 2, 3)))

RULE_VERSION = 'lanshi-v4-2026'
FAN_VALUES = {'qixingdui': 100,
 'sitongshun': 100,
 'jiulianbaodeng': 100,
 'sigang': 100,
 'dasixi': 72,
 'qingyaojiu': 72,
 'sianke': 48,
 'shisanyao': 48,
 'ziyise': 40,
 'silianshun': 40,
 'silianke': 40,
 'xiaosixi': 32,
 'sangang': 32,
 'dasanyuan': 24,
 'shunwang': 24,
 'santongshun': 24,
 'shunlian': 24,
 'hunyaojiu': 16,
 'quanda': 16,
 'quanzhong': 16,
 'quanxiao': 16,
 'quandaiwu': 16,
 'santongke': 16,
 'xiaosanyuan': 16,
 'quanbukao': 16,
 'sanfengke': 12,
 'sananke': 12,
 'sanlianke': 12,
 'qingyise': 12,
 'sanlianshun': 12,
 'sanselianke': 8,
 'qingquandaiyao': 8,
 'shuanggang': 8,
 'dayuwu': 8,
 'xiaoyuwu': 8,
 'qiduizi': 8,
 'shunhuan': 8,
 'shuangjianke': 6,
 'qinglong': 6,
 'miaoshouhuichun': 5,
 'haidilaoyue': 5,
 'gangshangkaihua': 5,
 'qiangganghe': 5,
 'tianhe': 5,
 'dihe': 5,
 'hualong': 4,
 'sansetongshun': 4,
 'pengpenghe': 3,
 'hunquandaiyao': 3,
 'hunyise': 3,
 'sanselianshun': 3,
 'angang': 2,
 'shuanganke': 2,
 'wumenqi': 2,
 'shuangtongke': 2,
 'quanqiuren': 2,
 'siguiyi': 2,
 'yibangao': 2,
 'hejuezhang': 2,
 'jianke': 2,
 'quanfengke': 2,
 'menfengke': 2,
 'menqianqing': 1,
 'minggang': 1,
 'duanyao': 1,
 'xixiangfeng': 1,
 'lianliu': 1,
 'laoshaofu': 1,
 'yaojiuke': 1,
 'zimo': 1}
FAN_NAMES = {'qixingdui': '七星对',
 'sitongshun': '四同顺',
 'jiulianbaodeng': '九莲宝灯',
 'sigang': '四杠',
 'dasixi': '大四喜',
 'qingyaojiu': '清幺九',
 'sianke': '四暗刻',
 'shisanyao': '十三幺',
 'ziyise': '字一色',
 'silianshun': '四连顺',
 'silianke': '四连刻',
 'xiaosixi': '小四喜',
 'sangang': '三杠',
 'dasanyuan': '大三元',
 'shunwang': '顺网',
 'santongshun': '三同顺',
 'shunlian': '顺链',
 'hunyaojiu': '混幺九',
 'quanda': '全大',
 'quanzhong': '全中',
 'quanxiao': '全小',
 'quandaiwu': '全带五',
 'santongke': '三同刻',
 'xiaosanyuan': '小三元',
 'quanbukao': '全不靠',
 'sanfengke': '三风刻',
 'sananke': '三暗刻',
 'sanlianke': '三连刻',
 'qingyise': '清一色',
 'sanlianshun': '三连顺',
 'sanselianke': '三色连刻',
 'qingquandaiyao': '清全带幺',
 'shuanggang': '双杠',
 'dayuwu': '大于五',
 'xiaoyuwu': '小于五',
 'qiduizi': '七对',
 'shunhuan': '顺环',
 'shuangjianke': '双箭刻',
 'qinglong': '清龙',
 'miaoshouhuichun': '妙手回春',
 'haidilaoyue': '海底捞月',
 'gangshangkaihua': '杠上开花',
 'qiangganghe': '抢杠和',
 'tianhe': '天和',
 'dihe': '地和',
 'hualong': '花龙',
 'sansetongshun': '三色同顺',
 'pengpenghe': '碰碰和',
 'hunquandaiyao': '混全带幺',
 'hunyise': '混一色',
 'sanselianshun': '三色连顺',
 'angang': '暗杠',
 'shuanganke': '双暗刻',
 'wumenqi': '五门齐',
 'shuangtongke': '双同刻',
 'quanqiuren': '全求人',
 'siguiyi': '四归一',
 'yibangao': '一般高',
 'hejuezhang': '和绝张',
 'jianke': '箭刻',
 'quanfengke': '圈风刻',
 'menfengke': '门风刻',
 'menqianqing': '门前清',
 'minggang': '明杠',
 'duanyao': '断幺',
 'xixiangfeng': '喜相逢',
 'lianliu': '连六',
 'laoshaofu': '老少副',
 'yaojiuke': '幺九刻',
 'zimo': '自摸'}


@dataclass(frozen=True)
class Meld:
    kind: str
    tile: int  # 顺子取中张，与现有网络/牌谱编码一致。

    @property
    def token(self):
        return f"{self.kind}{self.tile}"

    @property
    def tiles(self):
        if self.kind in "sS":
            return (self.tile - 1, self.tile, self.tile + 1)
        return (self.tile,) * (4 if self.kind in "gG" else 2 if self.kind == "q" else 3)


def parse_meld(token):
    """只接受已声明副露；K/S/q 是求解器输出，不能伪装为外部副露。"""
    if not isinstance(token, str) or len(token) != 3 or token[0] not in "skgG" or not token[1:].isdigit():
        return None
    kind, tile = token[0], int(token[1:])
    if tile not in VALID_TILES or (kind == "s" and (tile >= 40 or not 2 <= tile % 10 <= 8)):
        return None
    return Meld(kind, tile)


def valid_input(hand, melds, win_tile):
    if hand is None or melds is None or len(melds) > 4 or len(hand) != 14 - 3 * len(melds):
        return None
    if type(win_tile) is not int or any(type(tile) is not int or tile not in VALID_TILES for tile in hand) or win_tile not in hand:
        return None
    parsed = tuple(parse_meld(token) for token in melds)
    if any(meld is None for meld in parsed):
        return None
    physical = Counter(hand)
    for meld in parsed:
        physical.update(meld.tiles)
    if any(count > 4 for count in physical.values()):
        return None
    return parsed


def waiting_tiles(hand, meld_tokens):
    """只检查本规则合法牌形；起和分由含和张及牌桌上下文的 evaluate 判定。"""
    if hand is None or meld_tokens is None or len(hand) + 3 * len(meld_tokens) != 13:
        return set()
    waiting = set()
    for tile in TILES:
        complete = list(hand) + [tile]
        fixed = valid_input(complete, meld_tokens, tile)
        if fixed is not None and next(decompositions(complete, fixed, tile, True), None) is not None:
            waiting.add(tile)
    return waiting


@lru_cache(maxsize=8192)
def _closed_groups(tiles):
    """每步取最小剩余张，避免旧实现按面子排列重复展开。"""
    if not tiles:
        return ((),)
    first, counts = tiles[0], Counter(tiles)
    choices = []
    if counts[first] >= 3:
        choices.append(Meld("K", first))
    if first < 40 and first % 10 <= 7 and counts[first + 1] and counts[first + 2]:
        choices.append(Meld("S", first + 1))
    result = []
    for group in choices:
        remaining = list(tiles)
        for tile in group.tiles:
            remaining.remove(tile)
        for tail in _closed_groups(tuple(remaining)):
            result.append((group,) + tail)
    return tuple(result)


def decompositions(hand, fixed, win_tile, self_draw):
    counts, distinct = Counter(hand), set(hand)
    if not fixed:
        if len(distinct) == 7 and all(n == 2 for n in counts.values()):
            yield ("qixingdui" if distinct == HONORS else "qiduizi", (), -1)
        if distinct == ORPHANS and sorted(counts.values()) == [1] * 12 + [2]:
            yield ("shisanyao", (), -1)
        if len(distinct) == 14 and any(distinct <= case for case in UNRELATED):
            yield ("quanbukao", (), -1)
    for pair in sorted(tile for tile, n in counts.items() if n >= 2):
        remaining = sorted(hand)
        remaining.remove(pair)
        remaining.remove(pair)
        for groups in _closed_groups(tuple(remaining)):
            closed = groups + (Meld("q", pair),)
            # 只在未声明副露中定位和张。暗杠永远保持暗杠。
            for index, group in enumerate(closed):
                if win_tile not in group.tiles:
                    continue
                completed = list(closed)
                if not self_draw and group.kind == "K":
                    completed[index] = Meld("k", group.tile)
                yield ("standard", fixed + tuple(completed), len(fixed) + index)


def _low_relation(a, b):
    if a == b:
        return "yibangao"
    if a[0] != b[0] and a[1] == b[1]:
        return "xixiangfeng"
    if a[0] == b[0] and abs(a[1] - b[1]) == 3:
        return "lianliu"
    if a[0] == b[0] and {a[1], b[1]} == {1, 7}:
        return "laoshaofu"
    return None


@dataclass(frozen=True)
class Relation:
    fan: str
    nodes: tuple[int, ...]


def _relation_candidates(groups):
    result = []
    for size in (2, 3, 4):
        for nodes in combinations(range(len(groups)), size):
            selected = [groups[i] for i in nodes]
            sequences = all(g.kind in "sS" for g in selected)
            triplets = all(g.kind in "kKgG" and g.tile < 40 for g in selected)
            if not sequences and not triplets:
                continue
            values = [(g.tile // 10, g.tile % 10 - int(sequences)) for g in selected]
            suits = {s for s, r in values}
            ranks = sorted(r for s, r in values)
            same_suit, all_suits = len(suits) == 1, len(suits) == 3
            consecutive = ranks == list(range(ranks[0], ranks[0] + size))
            names = []
            if size == 2:
                name = _low_relation(*values) if sequences else ("shuangtongke" if len(suits) == 2 and ranks[0] == ranks[1] else None)
                if name:
                    names.append(name)
            elif size == 3:
                if same_suit and consecutive:
                    names.append("sanlianshun" if sequences else "sanlianke")
                if all_suits and consecutive:
                    names.append("sanselianshun" if sequences else "sanselianke")
                if len(set(ranks)) == 1:
                    if all_suits:
                        names.append("sansetongshun" if sequences else "santongke")
                    elif sequences and same_suit:
                        names.append("santongshun")
                if sequences and ranks == [1, 4, 7]:
                    if same_suit:
                        names.append("qinglong")
                    elif all_suits:
                        names.append("hualong")
            else:
                if same_suit and consecutive:
                    names.append("silianshun" if sequences else "silianke")
                if sequences:
                    frequencies = Counter(values)
                    if len(frequencies) == 1:
                        names.append("sitongshun")
                    if same_suit and ranks == [1, 3, 5, 7]:
                        names.append("shunlian")
                    if len(frequencies) == 2 and set(frequencies.values()) == {2}:
                        if _low_relation(*frequencies) in ("xixiangfeng", "lianliu", "laoshaofu"):
                            names.append("shunwang")
                    if len(suits) == 2 and len(frequencies) == 4:
                        by_suit = [sorted(r for s, r in values if s == suit) for suit in sorted(suits)]
                        if by_suit[0] == by_suit[1] and len(by_suit[0]) == 2 and (
                            by_suit[0][1] - by_suit[0][0] == 3 or by_suit[0] == [1, 7]
                        ):
                            names.append("shunhuan")
            result.extend(Relation(name, nodes) for name in names)
    return tuple(result)


@lru_cache(maxsize=4096)
def relation_options(groups):
    """枚举无环关联：同一超边的节点必须来自不同连通分量。"""
    candidates = _relation_candidates(groups)
    result = []

    def search(start, components, selected):
        result.append(selected)
        for i in range(start, len(candidates)):
            relation = candidates[i]
            roots = {components[node] for node in relation.nodes}
            if len(roots) != len(relation.nodes):
                continue
            new_root = min(roots)
            joined = tuple(new_root if root in roots else root for root in components)
            search(i + 1, joined, selected + (relation,))

    search(0, tuple(range(len(groups))), ())
    return tuple(result)


def _ordered(fans):
    counts = Counter(fans)
    return [name for name in FAN_VALUES for _ in range(counts[name])]


def score_fans(fans):
    counts = Counter(fans)
    score = sum(FAN_VALUES[name] * count for name, count in counts.items())
    display = [FAN_NAMES[name] + (f"*{counts[name]}" if name in REPEATABLE else "")
               for name in FAN_VALUES if counts[name]]
    return min(100, score), display


def _context(way):
    way = frozenset(way or ())
    aliases = {"last_deal": "妙手回春", "last_cut": "海底捞月"}
    way = way | {target for alias, target in aliases.items() if alias in way}
    self_draw = bool(way & {"自摸", "妙手回春", "杠上开花", "天和"})
    occasional = next((key for key in OCCASIONAL if FAN_NAMES[key] in way), None)
    return way, self_draw, occasional


def _tile_fans(hand, fixed, shape):
    tiles = list(hand) + [tile for g in fixed for tile in g.tiles]
    kinds = set(tiles)
    numeric = kinds - HONORS
    suits = {tile // 10 for tile in numeric}
    fans = []
    if len(suits) == 1:
        fans.append("hunyise" if kinds & HONORS else "qingyise")
    if len(suits) == 3 and kinds & frozenset(range(41, 45)) and kinds & frozenset(range(45, 48)) and shape not in ("shisanyao", "quanbukao"):
        fans.append("wumenqi")
    if kinds <= frozenset(tile for tile in TILES if tile < 40 and 2 <= tile % 10 <= 8):
        fans.append("duanyao")
    if not kinds & HONORS:
        ranks = {tile % 10 for tile in kinds}
        if ranks <= {7, 8, 9}:
            fans.append("quanda")
        elif ranks <= {6, 7, 8, 9}:
            fans.append("dayuwu")
        if ranks <= {1, 2, 3}:
            fans.append("quanxiao")
        elif ranks <= {1, 2, 3, 4}:
            fans.append("xiaoyuwu")
        if ranks <= {4, 5, 6}:
            fans.append("quanzhong")
    kongs = {g.tile for g in fixed if g.kind in "gG"}
    fans.extend("siguiyi" for tile, count in Counter(tiles).items() if count == 4 and tile not in kongs)
    return fans


def _honor_fans(groups, pair, way):
    trips = {g.tile for g in groups if g.kind in "kKgG"}
    winds, dragons = trips & frozenset(range(41, 45)), trips & frozenset(range(45, 48))
    fans, covered = [], set()
    if len(winds) == 4:
        fans.append("dasixi")
        covered.update(winds)
    elif len(winds) == 3:
        fans.append("xiaosixi" if pair in range(41, 45) else "sanfengke")
        covered.update(winds)
    if len(dragons) == 3:
        fans.append("dasanyuan")
        covered.update(dragons)
    elif len(dragons) == 2:
        fans.append("xiaosanyuan" if pair in range(45, 48) else "shuangjianke")
        covered.update(dragons)
    elif len(dragons) == 1:
        fans.append("jianke")
        covered.update(dragons)
    if len(winds) != 4:
        for direction, tile in zip("东南西北", range(41, 45)):
            if tile not in winds:
                continue
            if f"场风{direction}" in way:
                fans.append("quanfengke")
                covered.add(tile)
            if f"自风{direction}" in way:
                fans.append("menfengke")
                covered.add(tile)
    fans.extend("yaojiuke" for tile in sorted(trips & ORPHANS - covered))
    return fans


def _standard_fans(hand, fixed, groups, way, self_draw):
    pair = next(g.tile for g in groups if g.kind == "q")
    melds = tuple(g for g in groups if g.kind != "q")
    trips = [g for g in melds if g.kind in "kKgG"]
    kongs = [g for g in fixed if g.kind in "gG"]
    concealed = sum(g.kind in "KG" for g in trips)
    fans = _honor_fans(melds, pair, way)
    if len(trips) == 4:
        fans.append("pengpenghe")
        kinds = {pair} | {g.tile for g in trips}
        if kinds <= HONORS:
            fans.append("ziyise")
        elif kinds <= TERMINALS:
            fans.append("qingyaojiu")
        elif kinds <= ORPHANS:
            fans.append("hunyaojiu")
    if concealed >= 2:
        fans.append({2: "shuanganke", 3: "sananke", 4: "sianke"}[concealed])
    if len(kongs) >= 2:
        fans.append({2: "shuanggang", 3: "sangang", 4: "sigang"}[len(kongs)])
    elif kongs:
        fans.append("angang" if kongs[0].kind == "G" else "minggang")
    if all(any(tile < 40 and tile % 10 == 5 for tile in g.tiles) for g in groups):
        fans.append("quandaiwu")
    if all(any(tile in ORPHANS for tile in g.tiles) for g in groups):
        fans.append("hunquandaiyao" if any(g.tile >= 40 for g in groups) else "qingquandaiyao")
    if not self_draw and len(fixed) == 4 and all(g.kind in "skg" for g in fixed):
        fans.append("quanqiuren")
    if not fixed and len({tile // 10 for tile in hand}) == 1 and hand[0] < 40:
        ranks = Counter(tile % 10 for tile in hand)
        required = Counter((1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9))
        # 庄家起手特例须由状态机明确给出，不能将闲家的首摸天和误作此例。
        if "庄家起手" in way and self_draw and all(ranks[r] >= count for r, count in required.items()):
            fans.append("jiulianbaodeng")
    return fans, melds


def _exclude(fans):
    counts = Counter(fans)
    for name in FAN_VALUES:
        if counts[name] and FAN_VALUES[name] == 100:
            return [name]
    excludes = {
        "dasixi": ("pengpenghe",), "qingyaojiu": ("pengpenghe", "yaojiuke"),
        "sianke": ("pengpenghe", "menqianqing"), "ziyise": ("pengpenghe", "yaojiuke"),
        "silianke": ("pengpenghe",), "hunyaojiu": ("pengpenghe", "yaojiuke"),
        "quanzhong": ("duanyao",), "quandaiwu": ("duanyao",),
    }
    for higher, lower in excludes.items():
        if counts[higher]:
            for name in lower:
                counts[name] = 0
    return [name for name in FAN_VALUES for _ in range(counts[name])]


def evaluate(hand, meld_tokens, way, win_tile):
    fixed = valid_input(hand, meld_tokens, win_tile)
    if fixed is None:
        return []
    way, self_draw, occasional = _context(way)
    common = (["zimo"] if self_draw else [])
    if "和绝张" in way or "抢杠和" in way:
        common.append("hejuezhang")
    results = {}
    for shape, groups, win_index in decompositions(hand, fixed, win_tile, self_draw):
        fans = common + _tile_fans(hand, fixed, shape)
        if shape != "standard":
            fans.append(shape)
            options = ((),)
        else:
            if not any(g.kind in "skg" for g in fixed):
                fans.append("menqianqing")
            extra, melds = _standard_fans(hand, fixed, groups, way, self_draw)
            fans.extend(extra)
            if not fixed and len({tile // 10 for tile in hand}) == 1 and hand[0] < 40:
                before = list(hand)
                before.remove(win_tile)
                if sorted(tile % 10 for tile in before) == [1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9]:
                    fans.append("jiulianbaodeng")
            options = relation_options(melds)
        best = None
        for relations in options:
            regular = _exclude(fans + [r.fan for r in relations])
            raw_score = sum(FAN_VALUES[name] for name in regular)
            final = [occasional] if occasional and raw_score < 5 else regular
            score, display = score_fans(final)
            order = tuple(-tuple(FAN_VALUES).index(name) for name in _ordered(final))
            rank = (score, raw_score, order)
            if best is None or rank > best[0]:
                keys = Counter(final)
                best = (rank, dict(
                    score=score, fan_list=display,
                    fan_keys=[name + (f"*{keys[name]}" if name in REPEATABLE else "") for name in FAN_VALUES if keys[name]],
                    combinations=[g.token for g in groups],
                    shape=shape, winning_combination=win_index,
                    relations=[dict(fan=r.fan, nodes=list(r.nodes)) for r in relations],
                    rule_version=RULE_VERSION,
                ))
        result = best[1]
        key = (shape, tuple(result["combinations"]), tuple(result["fan_keys"]), result["score"])
        results.setdefault(key, result)
    return sorted(results.values(), key=lambda item: (-item["score"], tuple(item["fan_keys"]), tuple(item["combinations"])))
