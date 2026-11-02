from collections import Counter
from dataclasses import dataclass
from functools import lru_cache

from ..gb_tingpai_check import Chinese_Tingpai_Check
from ..guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles
from . import tiles as t


@dataclass(frozen=True)
class Context:
    tsumo: bool = False
    seat_wind: int = 42
    round_wind: int = 41
    riichi: int = 0
    ippatsu: bool = False
    rinshan: bool = False
    chankan: bool = False
    last: bool = False
    tenhou: bool = False
    chiihou: bool = False
    last_copy: bool = False
    indicators: tuple = ()
    ura_indicators: tuple = ()


@dataclass(frozen=True)
class ShapeScore:
    yaku_fan: int
    fu: int
    yakuman: int
    entries: tuple
    physical_bonus: int = 0
    dora: int = 0
    ura: int = 0
    pure_open: int = 0
    pure_closed: int = 0

    @property
    def visible(self):
        return self.yakuman * 13 if self.yakuman else self.yaku_fan + self.physical_bonus + self.dora

    @property
    def total(self):
        return self.visible if self.yakuman else self.visible + self.ura


KNITTED = (
    (11, 14, 17, 22, 25, 28, 33, 36, 39),
    (11, 14, 17, 23, 26, 29, 32, 35, 38),
    (12, 15, 18, 21, 24, 27, 33, 36, 39),
    (13, 16, 19, 21, 24, 27, 32, 35, 38),
    (13, 16, 19, 22, 25, 28, 31, 34, 37),
    (12, 15, 18, 23, 26, 29, 31, 34, 37),
)


def _physical(shape):
    return shape.physical + tuple(v for g in shape.groups if g.external for v in g.physical)


def _pure_kongs(shape):
    groups = [g for g in shape.groups if g.kind == 'kong' and all(t.joker(v) for v in g.physical)]
    return sum(not g.concealed for g in groups), sum(g.concealed for g in groups)


def _wait(shape):
    if shape.winning_group < 0:
        return ''
    group = shape.groups[shape.winning_group]
    if group.kind in ('pair', 'seven_pair'):
        return 'pair'
    if group.kind == 'triplet':
        return 'triplet'
    if group.kind != 'sequence':
        return ''
    start, win = group.logical[0], shape.winning_face
    if win == start + 1:
        return 'closed'
    if start % 10 == 1 and win == start + 2 or start % 10 == 7 and win == start:
        return 'edge'
    return 'two_sided'


@lru_cache(maxsize=8192)
def _single_wait(before, melds, winner):
    return Chinese_Tingpai_Check().tingpai_check(list(before), list(melds)) == {winner}


def guobiao_shape(shape, ctx=Context(), physical=()):
    checker = Chinese_Hepai_Check()
    codes = []
    fans = []
    for group in shape.groups:
        if group.kind == 'knitted':
            codes.append('z' + str(KNITTED.index(tuple(sorted(group.logical)))))
            fans.append('zuhelong')
        elif shape.kind != 'seven_pairs':
            codes.append(group.code)
    player = PlayerTiles([], codes, 14)
    player.initial_combination_count = sum(g.external for g in shape.groups)
    player.fan_list = fans
    if shape.kind == 'seven_pairs':
        player.hand_tiles = sorted(shape.assignment)
        faces = sorted(set(shape.assignment))
        consecutive = len(faces) == 7 and faces[0] < 40 and faces[-1] // 10 == faces[0] // 10 and faces[-1] - faces[0] == 6
        player.fan_list = ['lianqidui' if consecutive else 'qiduizi']
    elif shape.kind == 'thirteen_orphans':
        player.fan_list = ['shisanyao']
    elif shape.kind == 'unconnected':
        honors = sum(v >= 40 for v in shape.assignment)
        player.fan_list = ['qixingbukao'] if honors == 7 else ['quanbukao'] + (['zuhelong'] if honors == 5 else [])

    way = ['自摸' if ctx.tsumo else '点和', '自风' + '东南西北'[ctx.seat_wind - 41], '场风' + '东南西北'[ctx.round_wind - 41]]
    if ctx.rinshan:
        way.append('杠上开花')
    elif ctx.chankan:
        way.append('抢杠和')
    elif ctx.last:
        way.append('last_deal' if ctx.tsumo else 'last_cut')
    if ctx.last_copy:
        way.append('和绝张')
    if not ctx.tsumo and shape.winning_group >= 0 and shape.groups[shape.winning_group].kind == 'triplet':
        player.combination_list[shape.winning_group] = player.combination_list[shape.winning_group].lower()
        way.append('暗转明')
    wait = _wait(shape)
    external = tuple(g.code for g in shape.groups if g.external)
    if wait in ('pair', 'closed', 'edge') and shape.kind not in ('seven_pairs', 'thirteen_orphans') and _single_wait(tuple(sorted(shape.assignment[:-1])), external, shape.winning_face):
        player.fan_list.append({'pair': 'dandiaojiang', 'closed': 'qianzhang', 'edge': 'bianzhang'}[wait])
    fan, names = checker.fan_count(player, shape.winning_face, way, resolved_ron=True)
    pure_open, pure_closed = _pure_kongs(shape)
    return ShapeScore(fan, 0, 0, tuple(names), sum(t.bonus(v) for v in (physical or _physical(shape))), pure_open=pure_open, pure_closed=pure_closed)


def riichi_base_points(han, fu, yakuman=0):
    if yakuman:
        return 8000 * yakuman
    if han <= 0:
        return 0
    if han >= 13:
        return 8000
    if han >= 11:
        return 6000
    if han >= 8:
        return 4000
    if han >= 6:
        return 3000
    raw = fu * (1 << (han + 2))
    return 2000 if han >= 5 or raw >= 1920 else raw


def conditions(physical, visible, fu, variant):
    for value in physical:
        if value == t.ODD and visible % 2 != 1 or value == t.EVEN and visible % 2 != 0:
            return False
        if value == t.MANGAN and (visible < 24 if variant == 'guobiao' else riichi_base_points(visible, fu) < 2000):
            return False
    return True


def _dora_count(shape, physical, indicators):
    logical = list(shape.assignment)
    logical.extend(v for g in shape.groups if g.external for v in g.logical)
    counts = Counter(logical)
    jokers = sum(t.joker(v) for v in physical)
    return sum(jokers if t.joker(indicator) else counts[t.dora(indicator)] for indicator in indicators)


def riichi_shape(shape, ctx=Context(), physical=()):
    physical = tuple(physical) or _physical(shape)
    groups = shape.groups
    closed = all(g.concealed for g in groups)
    logical = list(shape.assignment) + [v for g in groups if g.external for v in g.logical]
    counts = Counter(logical)
    faces = set(logical)
    sequences = [g for g in groups if g.kind == 'sequence']
    triplets = [g for g in groups if g.kind in ('triplet', 'kong')]
    kongs = [g for g in triplets if g.kind == 'kong']
    pair = next((g.logical[0] for g in groups if g.kind == 'pair'), 0)
    concealed = sum(g.concealed and (ctx.tsumo or gi != shape.winning_group or g.kind == 'kong') for gi, g in enumerate(groups) if g.kind in ('triplet', 'kong'))
    dragons = {g.logical[0] for g in triplets if g.logical[0] >= 45}
    winds = {g.logical[0] for g in triplets if 41 <= g.logical[0] <= 44}
    wait = _wait(shape)
    yakuman, yaku = [], []
    suits = {v // 10 for v in faces if v < 40}
    has_honors = any(v >= 40 for v in faces)
    terminal = lambda v: v < 40 and v % 10 in (1, 9)
    orphan = lambda v: v >= 40 or terminal(v)

    if shape.kind == 'thirteen_orphans':
        thirteen = len(set(shape.assignment[:-1])) == 13
        yakuman.append('国士无双十三面' if thirteen else '国士无双')
    if all(v >= 40 for v in faces):
        yakuman.append('字一色')
    if all(terminal(v) for v in faces):
        yakuman.append('清老头')
    if faces <= {32, 33, 34, 36, 38, 47}:
        yakuman.append('绿一色')
    if len(dragons) == 3:
        yakuman.append('大三元')
    if len(winds) == 4:
        yakuman.append('大四喜')
    elif len(winds) == 3 and 41 <= pair <= 44:
        yakuman.append('小四喜')
    if concealed == 4:
        yakuman.append('四暗刻单骑' if wait == 'pair' else '四暗刻')
    if len(kongs) == 4:
        yakuman.append('四杠子')
    if closed and not any(g.external for g in groups) and len(suits) == 1 and not has_honors:
        suit = next(iter(suits)) * 10
        target = Counter([suit + 1] * 3 + [suit + i for i in range(2, 9)] + [suit + 9] * 3)
        if counts >= target:
            yakuman.append('纯正九莲宝灯' if Counter(shape.assignment[:-1]) == target else '九莲宝灯')
    if ctx.tsumo and not any(g.external for g in groups):
        if ctx.tenhou and ctx.seat_wind == 41:
            yakuman.append('天和')
        elif ctx.chiihou and ctx.seat_wind != 41:
            yakuman.append('地和')

    pinfu = closed and len(sequences) == 4 and wait == 'two_sided' and pair not in (45, 46, 47, ctx.seat_wind, ctx.round_wind)
    if shape.kind == 'seven_pairs':
        yaku.append(('七对子', 2))
    if all(not orphan(v) for v in faces):
        yaku.append(('断幺九', 1))
    if len(suits) == 1:
        yaku.append(('混一色', 3 if closed else 2) if has_honors else ('清一色', 6 if closed else 5))
    if all(orphan(v) for v in faces):
        yaku.append(('混老头', 2))
    if shape.kind == 'standard':
        for group in triplets:
            value = group.logical[0]
            if value >= 45:
                yaku.append(('役牌·' + {45: '中', 46: '白', 47: '发'}[value], 1))
            for wind, name in ((ctx.seat_wind, '自风'), (ctx.round_wind, '场风')):
                if value == wind:
                    yaku.append((name, 1))
        if pinfu:
            yaku.append(('平和', 1))
        if len(triplets) == 4:
            yaku.append(('对对和', 2))
        if concealed == 3:
            yaku.append(('三暗刻', 2))
        if len(kongs) == 3:
            yaku.append(('三杠子', 2))
        if len(dragons) == 2 and pair >= 45:
            yaku.append(('小三元', 2))
        seq = Counter(g.logical[0] for g in sequences)
        trip = {g.logical[0] for g in triplets}
        identical = sum(n // 2 for n in seq.values())
        if closed and identical:
            yaku.append(('二杯口', 3) if identical >= 2 else ('一杯口', 1))
        if any(all(s * 10 + r in seq for s in (1, 2, 3)) for r in range(1, 8)):
            yaku.append(('三色同顺', 2 if closed else 1))
        if any(all(s * 10 + r in trip for s in (1, 2, 3)) for r in range(1, 10)):
            yaku.append(('三色同刻', 2))
        if any(all(s * 10 + r in seq for r in (1, 4, 7)) for s in (1, 2, 3)):
            yaku.append(('一气通贯', 2 if closed else 1))
        if sequences and all(any(orphan(v) for v in g.logical) for g in groups):
            yaku.append(('混全带幺九', 2 if closed else 1) if has_honors else ('纯全带幺九', 3 if closed else 2))
    if closed:
        if ctx.riichi:
            yaku.append(('双立直', 2) if ctx.riichi == 2 else ('立直', 1))
            if ctx.ippatsu:
                yaku.append(('一发', 1))
        if ctx.tsumo:
            yaku.append(('门前清自摸和', 1))
    if ctx.rinshan and ctx.tsumo:
        yaku.append(('杠上开花', 1))
    elif ctx.chankan and not ctx.tsumo:
        yaku.append(('抢杠', 1))
    elif ctx.last:
        yaku.append(('海底捞月' if ctx.tsumo else '河底捞鱼', 1))

    if shape.kind in ('seven_pairs', 'thirteen_orphans'):
        fu = 25
    elif pinfu:
        fu = 20 if ctx.tsumo else 30
    else:
        fu = 20 + (10 if closed and not ctx.tsumo else 0) + (2 if ctx.tsumo else 0)
        fu += 2 * (int(pair >= 45) + int(pair == ctx.seat_wind) + int(pair == ctx.round_wind))
        fu += 2 if wait in ('pair', 'closed', 'edge') else 0
        for gi, group in enumerate(groups):
            if group.kind not in ('triplet', 'kong'):
                continue
            hidden = group.concealed and (ctx.tsumo or gi != shape.winning_group or group.kind == 'kong')
            value = 2 * (2 if orphan(group.logical[0]) else 1) * (2 if hidden else 1)
            if group.kind == 'kong':
                value *= 4 * (1 << (len(group.logical) - 4))
            fu += value
        fu = max(30, (fu + 9) // 10 * 10)
    pure_open, pure_closed = _pure_kongs(shape)
    if yakuman:
        return ShapeScore(13 * len(yakuman), 25, len(yakuman), tuple((name, 13) for name in yakuman), pure_open=pure_open, pure_closed=pure_closed)
    return ShapeScore(sum(n for _, n in yaku), fu, 0, tuple(yaku),
                      sum(t.bonus(v) for v in physical), _dora_count(shape, physical, ctx.indicators),
                      _dora_count(shape, physical, ctx.ura_indicators) if ctx.riichi and closed else 0,
                      pure_open, pure_closed)
