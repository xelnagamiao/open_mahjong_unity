"""
国标向听（一般型 + 七对 / 十三幺 / 全不靠 / 组合龙）。

一般型向听动态规划改编自 @kobalab/majiang-core
（https://github.com/kobalab/majiang-core）。
Copyright (c) Satoshi Kobayashi. Licensed under the MIT License.
详见同目录 ATTRIBUTION.md。

牌面编码与本仓库一致：
  11-19 万 / 21-29 筒 / 31-39 索 / 41-44 东南西北 / 45 中 / 46 发 / 47 白
"""
from __future__ import annotations

from typing import Dict, Iterable, List, Optional, Sequence, Set, Tuple

# ── 常量 ──────────────────────────────────────────────────────────────────────

ALL_TILE_IDS: Tuple[int, ...] = tuple(
    list(range(11, 20))
    + list(range(21, 30))
    + list(range(31, 40))
    + [41, 42, 43, 44, 45, 46, 47]
)

HONOUR_IDS: Tuple[int, ...] = (41, 42, 43, 44, 45, 46, 47)

SHISANYAO_IDS: Tuple[int, ...] = (
    11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47,
)

# 组合龙 6 种图案（各 9 张数牌）
ZUHELONG_PATTERNS: Tuple[Tuple[int, ...], ...] = (
    (11, 14, 17, 22, 25, 28, 33, 36, 39),
    (11, 14, 17, 32, 35, 38, 23, 26, 29),
    (21, 24, 27, 12, 15, 18, 33, 36, 39),
    (21, 24, 27, 32, 35, 38, 13, 16, 19),
    (31, 34, 37, 22, 25, 28, 13, 16, 19),
    (31, 34, 37, 12, 15, 18, 23, 26, 29),
)

Counts = Dict[int, int]


def normalize_tile(tile_id: int) -> int:
    if tile_id == 105:
        return 15
    if tile_id == 205:
        return 25
    if tile_id == 305:
        return 35
    return tile_id


def counts_from_tiles(tiles: Iterable[int]) -> Counts:
    counts: Counts = {}
    for t in tiles:
        tid = normalize_tile(int(t))
        if tid < 11 or tid > 47 or tid // 10 == 5:
            continue  # 花牌等跳过
        counts[tid] = counts.get(tid, 0) + 1
    return counts


def copy_counts(counts: Counts) -> Counts:
    return dict(counts)


def add_tile(counts: Counts, tile_id: int) -> Counts:
    tid = normalize_tile(tile_id)
    out = dict(counts)
    out[tid] = out.get(tid, 0) + 1
    return out


def remove_tile(counts: Counts, tile_id: int) -> Counts:
    tid = normalize_tile(tile_id)
    out = dict(counts)
    out[tid] = out.get(tid, 0) - 1
    if out[tid] <= 0:
        del out[tid]
    return out


# ── 一般型（kobalab yiban DP）─────────────────────────────────────────────────

def _suited_max_rank(bingpai: Sequence[int]) -> int:
    return len(bingpai) - 1


def _xiangting_core(m: int, d: int, g: int, has_jiang: bool) -> int:
    n = 4 if has_jiang else 5
    if m > 4:
        d += m - 4
        m = 4
    if m + d > 4:
        g += m + d - 4
        d = 4 - m
    if m + d + g > n:
        g = n - m - d
    if has_jiang:
        d += 1
    return 13 - m * 3 - d * 2 - g


def _dazi(bingpai: Sequence[int]) -> Tuple[List[int], List[int]]:
    n_pai = 0
    n_dazi = 0
    n_guli = 0
    max_r = _suited_max_rank(bingpai)
    for n in range(1, max_r + 1):
        n_pai += bingpai[n]
        if n <= max_r - 2 and bingpai[n + 1] == 0 and bingpai[n + 2] == 0:
            n_dazi += n_pai >> 1
            n_guli += n_pai % 2
            n_pai = 0
    n_dazi += n_pai >> 1
    n_guli += n_pai % 2
    triple = [0, n_dazi, n_guli]
    return triple[:], triple[:]


def _mianzi_suit(bingpai: List[int], n: int = 1) -> Tuple[List[int], List[int]]:
    max_r = _suited_max_rank(bingpai)
    if n > max_r:
        return _dazi(bingpai)

    max_shun_start = max_r - 2
    max_a, max_b = _mianzi_suit(bingpai, n + 1)

    if (
        n <= max_shun_start
        and bingpai[n] > 0
        and bingpai[n + 1] > 0
        and bingpai[n + 2] > 0
    ):
        bingpai[n] -= 1
        bingpai[n + 1] -= 1
        bingpai[n + 2] -= 1
        ra, rb = _mianzi_suit(bingpai, n)
        bingpai[n] += 1
        bingpai[n + 1] += 1
        bingpai[n + 2] += 1
        ra[0] += 1
        rb[0] += 1
        if ra[2] < max_a[2] or (ra[2] == max_a[2] and ra[1] < max_a[1]):
            max_a = ra
        if rb[0] > max_b[0] or (rb[0] == max_b[0] and rb[1] > max_b[1]):
            max_b = rb

    if bingpai[n] >= 3:
        bingpai[n] -= 3
        ra, rb = _mianzi_suit(bingpai, n + 1)
        bingpai[n] += 3
        ra[0] += 1
        rb[0] += 1
        if ra[2] < max_a[2] or (ra[2] == max_a[2] and ra[1] < max_a[1]):
            max_a = ra
        if rb[0] > max_b[0] or (rb[0] == max_b[0] and rb[1] > max_b[1]):
            max_b = rb

    return max_a, max_b


SuitSides = Tuple[Tuple[int, int, int], Tuple[int, int, int]]

_SUIT_CACHE: Dict[int, SuitSides] = {}


def _pack_suit9(arr: Sequence[int]) -> int:
    """数牌 1–9 各 3 bit。"""
    return (
        (arr[1] & 7)
        | ((arr[2] & 7) << 3)
        | ((arr[3] & 7) << 6)
        | ((arr[4] & 7) << 9)
        | ((arr[5] & 7) << 12)
        | ((arr[6] & 7) << 15)
        | ((arr[7] & 7) << 18)
        | ((arr[8] & 7) << 21)
        | ((arr[9] & 7) << 24)
    )


def _mianzi_suit_cached(arr: List[int]) -> SuitSides:
    key = _pack_suit9(arr)
    hit = _SUIT_CACHE.get(key)
    if hit is not None:
        return hit
    a, b = _mianzi_suit(arr[:], 1)
    result: SuitSides = ((a[0], a[1], a[2]), (b[0], b[1], b[2]))
    _SUIT_CACHE[key] = result
    return result


def _honour_triple(z: Sequence[int]) -> Tuple[int, int, int]:
    mianzi = dazi = guli = 0
    for n in range(1, 8):
        c = z[n]
        if c >= 3:
            mianzi += 1
        elif c == 2:
            dazi += 1
        elif c == 1:
            guli += 1
    return mianzi, dazi, guli


def _xiangting_from_sides(
    rm: SuitSides,
    rp: SuitSides,
    rs: SuitSides,
    zz: Tuple[int, int, int],
    n_fulou: int,
    has_jiang: bool,
) -> int:
    best = 13
    z0, z1, z2 = zz
    for m_side in rm:
        for p_side in rp:
            for s_side in rs:
                cand = _xiangting_core(
                    n_fulou + m_side[0] + p_side[0] + s_side[0] + z0,
                    m_side[1] + p_side[1] + s_side[1] + z1,
                    m_side[2] + p_side[2] + s_side[2] + z2,
                    has_jiang,
                )
                if cand < best:
                    best = cand
    return best


def _mianzi_all(m: List[int], p: List[int], s: List[int], z: List[int],
                n_fulou: int, has_jiang: bool) -> int:
    return _xiangting_from_sides(
        _mianzi_suit_cached(m),
        _mianzi_suit_cached(p),
        _mianzi_suit_cached(s),
        _honour_triple(z),
        n_fulou,
        has_jiang,
    )


def _counts_to_bingpai(counts: Counts) -> Tuple[List[int], List[int], List[int], List[int]]:
    m = [0] * 10
    p = [0] * 10
    s = [0] * 10
    z = [0] * 8
    for tid, c in counts.items():
        if c <= 0:
            continue
        suit = tid // 10
        num = tid % 10
        if suit == 1:
            m[num] += c
        elif suit == 2:
            p[num] += c
        elif suit == 3:
            s[num] += c
        elif suit == 4:
            # 41东→1 … 44北→4；45中→5；46发→6；47白→7
            z[num] += c
    return m, p, s, z


_SHANTEN_CACHE: Dict[Tuple[bytes, int, int], int] = {}
_YIBAN_CACHE: Dict[Tuple[bytes, int], int] = {}
_EFF_CACHE: Dict[Tuple[bytes, int, int], Tuple[int, ...]] = {}

_TILE_INDEX: Dict[int, int] = {tid: i for i, tid in enumerate(ALL_TILE_IDS)}


def pack_counts(counts: Counts) -> bytes:
    """34 种牌各 1 字节；只遍历手牌中出现的牌种（比扫全 34 快）。"""
    out = bytearray(34)
    for tid, c in counts.items():
        if c:
            idx = _TILE_INDEX.get(tid)
            if idx is not None:
                out[idx] = c & 7
    return bytes(out)


def pack_adjust(packed: bytes, tid: int, delta: int) -> bytes:
    """在已有打包键上对单牌 ±1（用于摸/切增量）。"""
    idx = _TILE_INDEX[tid]
    ba = bytearray(packed)
    ba[idx] = ba[idx] + delta
    return bytes(ba)


# 旧名兼容
_pack_counts = pack_counts


def clear_shanten_cache() -> None:
    _SHANTEN_CACHE.clear()
    _YIBAN_CACHE.clear()
    _EFF_CACHE.clear()
    _SUIT_CACHE.clear()


def xiangting_yiban(counts: Counts, n_melds: int = 0, *, packed: Optional[bytes] = None) -> int:
    if packed is None:
        packed = pack_counts(counts)
    key = (packed, n_melds)
    hit = _YIBAN_CACHE.get(key)
    if hit is not None:
        return hit
    m, p, s, z = _counts_to_bingpai(counts)
    rm = _mianzi_suit_cached(m)
    rp = _mianzi_suit_cached(p)
    rs = _mianzi_suit_cached(s)
    zz = _honour_triple(z)
    best = _xiangting_from_sides(rm, rp, rs, zz, n_melds, False)

    # 拆雀头时只重算受影响花色，其余套装 DP 复用
    for n in range(1, 10):
        if m[n] >= 2:
            m[n] -= 2
            cand = _xiangting_from_sides(_mianzi_suit_cached(m), rp, rs, zz, n_melds, True)
            m[n] += 2
            if cand < best:
                best = cand
        if p[n] >= 2:
            p[n] -= 2
            cand = _xiangting_from_sides(rm, _mianzi_suit_cached(p), rs, zz, n_melds, True)
            p[n] += 2
            if cand < best:
                best = cand
        if s[n] >= 2:
            s[n] -= 2
            cand = _xiangting_from_sides(rm, rp, _mianzi_suit_cached(s), zz, n_melds, True)
            s[n] += 2
            if cand < best:
                best = cand
    for n in range(1, 8):
        if z[n] >= 2:
            z[n] -= 2
            cand = _xiangting_from_sides(rm, rp, rs, _honour_triple(z), n_melds, True)
            z[n] += 2
            if cand < best:
                best = cand

    _YIBAN_CACHE[key] = best
    return best


# ── 特殊型 ────────────────────────────────────────────────────────────────────

def shanten_qidui(counts: Counts) -> int:
    """国标七对：4 同张算两对。"""
    pairs = 0
    for c in counts.values():
        pairs += c >> 1
    return 6 - pairs


def shanten_shisanyao(counts: Counts) -> int:
    kinds = 0
    has_pair = False
    for tid in SHISANYAO_IDS:
        c = counts.get(tid, 0)
        if c >= 1:
            kinds += 1
        if c >= 2:
            has_pair = True
    return 13 - kinds - (1 if has_pair else 0)


def shanten_quanbukao(counts: Counts) -> int:
    best = 13
    for pattern in ZUHELONG_PATTERNS:
        useful = 0
        for tid in list(pattern) + list(HONOUR_IDS):
            if counts.get(tid, 0) >= 1:
                useful += 1
        s = 13 - useful
        if s < best:
            best = s
    return best


def shanten_zuhelong(counts: Counts, n_melds: int = 0, upper_bound: int = 99) -> int:
    if n_melds > 1:
        return 99
    best = upper_bound
    for pattern in ZUHELONG_PATTERNS:
        missing = 0
        for tid in pattern:
            if counts.get(tid, 0) < 1:
                missing += 1
        # rest 最低 -1；missing-1 已无法优于当前 best 则跳过
        if missing - 1 >= best:
            continue
        remaining = dict(counts)
        for tid in pattern:
            if remaining.get(tid, 0) >= 1:
                remaining[tid] -= 1
                if remaining[tid] <= 0:
                    del remaining[tid]
        # remaining 已去掉图案牌；需再凑 1 面子 + 雀头（n_fulou=3+n_melds）
        rest = xiangting_yiban(remaining, 3 + n_melds)
        total = missing + rest
        if total < best:
            best = total
    return best


def guobiao_shanten(
    counts: Counts,
    n_melds: int = 0,
    *,
    specials: bool = True,
    packed: Optional[bytes] = None,
) -> int:
    """总体向听；已完成和型返回 -1。

    结果按 (牌计数, n_melds, specials) 缓存：启发式热路径（effective_tiles / 一向听前瞻）
    会对数万次重复手形反复求向听。

    specials=False 时跳过七对/国士/全不靠/组合龙，仅用于死一向听重塑厚度等
    非决胜热路径，避免组合龙 DP 爆炸。
    """
    if packed is None:
        packed = pack_counts(counts)
    key = (packed, n_melds, 1 if specials else 0)
    hit = _SHANTEN_CACHE.get(key)
    if hit is not None:
        return hit
    best = xiangting_yiban(counts, n_melds, packed=packed)
    if specials and best > -1 and n_melds == 0:
        best = min(
            best,
            shanten_qidui(counts),
            shanten_shisanyao(counts),
            shanten_quanbukao(counts),
        )
    if specials and best > -1 and n_melds <= 1:
        best = min(best, shanten_zuhelong(counts, n_melds, upper_bound=best))
    _SHANTEN_CACHE[key] = best
    return best


def guobiao_shanten_from_tiles(tiles: Iterable[int], n_melds: int = 0) -> int:
    return guobiao_shanten(counts_from_tiles(tiles), n_melds)


def _yiban_candidate_tiles(counts: Counts) -> List[int]:
    """一般型有效进张候选：已有牌 + 数牌邻张（±1/±2）。孤立新牌无法单独降向听。"""
    cands: Set[int] = set()
    for tid, c in counts.items():
        if c <= 0:
            continue
        if c < 4:
            cands.add(tid)
        suit = tid // 10
        if suit > 3:
            continue
        num = tid % 10
        for d in (-2, -1, 1, 2):
            n2 = num + d
            if 1 <= n2 <= 9:
                nid = suit * 10 + n2
                if counts.get(nid, 0) < 4:
                    cands.add(nid)
    return list(cands)


def _special_candidate_tiles(counts: Counts) -> List[int]:
    """特殊型可能降向听的牌种（与一般型候选取并）。"""
    cands: Set[int] = set(_yiban_candidate_tiles(counts))
    for tid in SHISANYAO_IDS:
        if counts.get(tid, 0) < 4:
            cands.add(tid)
    for tid in HONOUR_IDS:
        if counts.get(tid, 0) < 4:
            cands.add(tid)
    for pattern in ZUHELONG_PATTERNS:
        for tid in pattern:
            if counts.get(tid, 0) < 4:
                cands.add(tid)
    # 七对：奇数张再摸可成对
    for tid, c in counts.items():
        if c > 0 and (c & 1) and c < 4:
            cands.add(tid)
    return list(cands)


def effective_tiles(
    counts: Counts,
    n_melds: int = 0,
    *,
    specials: bool = True,
    packed: Optional[bytes] = None,
) -> List[int]:
    """有效进张：摸入后向听下降的牌种。"""
    if packed is None:
        packed = pack_counts(counts)
    ekey = (packed, n_melds, 1 if specials else 0)
    hit = _EFF_CACHE.get(ekey)
    if hit is not None:
        return list(hit)

    base = guobiao_shanten(counts, n_melds, specials=specials, packed=packed)
    candidates = (
        _special_candidate_tiles(counts) if specials else _yiban_candidate_tiles(counts)
    )
    result: List[int] = []
    for tid in candidates:
        if counts.get(tid, 0) >= 4:
            continue
        nxt = add_tile(counts, tid)
        if guobiao_shanten(nxt, n_melds, specials=specials) < base:
            result.append(tid)
    result.sort()
    _EFF_CACHE[ekey] = tuple(result)
    return result


def live_copies(tile_ids: Iterable[int], counts: Counts, visible: Counts) -> int:
    total = 0
    for tid in tile_ids:
        total += max(0, 4 - visible.get(tid, counts.get(tid, 0)))
    return total
