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

from typing import Dict, Iterable, List, Sequence, Tuple

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


def _mianzi_all(m: List[int], p: List[int], s: List[int], z: List[int],
                n_fulou: int, has_jiang: bool) -> int:
    rm = _mianzi_suit(m[:])
    rp = _mianzi_suit(p[:])
    rs = _mianzi_suit(s[:])

    zz = [0, 0, 0]
    for n in range(1, 8):
        c = z[n]
        if c >= 3:
            zz[0] += 1
        elif c == 2:
            zz[1] += 1
        elif c == 1:
            zz[2] += 1

    best = 13
    for m_side in rm:
        for p_side in rp:
            for s_side in rs:
                x = [n_fulou, 0, 0]
                for i in range(3):
                    x[i] += m_side[i] + p_side[i] + s_side[i] + zz[i]
                cand = _xiangting_core(x[0], x[1], x[2], has_jiang)
                if cand < best:
                    best = cand
    return best


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


def xiangting_yiban(counts: Counts, n_melds: int = 0) -> int:
    m, p, s, z = _counts_to_bingpai(counts)
    working = (m[:], p[:], s[:], z[:])
    best = _mianzi_all(*working, n_melds, False)

    for suit_i, arr in enumerate(working):
        max_n = 7 if suit_i == 3 else _suited_max_rank(arr)
        for n in range(1, max_n + 1):
            if arr[n] >= 2:
                arr[n] -= 2
                cand = _mianzi_all(working[0], working[1], working[2], working[3], n_melds, True)
                arr[n] += 2
                if cand < best:
                    best = cand
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


def shanten_zuhelong(counts: Counts, n_melds: int = 0) -> int:
    if n_melds > 1:
        return 99
    best = 99
    for pattern in ZUHELONG_PATTERNS:
        remaining = dict(counts)
        missing = 0
        for tid in pattern:
            if remaining.get(tid, 0) >= 1:
                remaining[tid] -= 1
                if remaining[tid] <= 0:
                    del remaining[tid]
            else:
                missing += 1
        # remaining 已去掉图案牌；需再凑 1 面子 + 雀头（n_fulou=3+n_melds）
        rest = xiangting_yiban(remaining, 3 + n_melds)
        total = missing + rest
        if total < best:
            best = total
    return best


def guobiao_shanten(counts: Counts, n_melds: int = 0) -> int:
    """总体向听；已完成和型返回 -1。"""
    best = xiangting_yiban(counts, n_melds)
    if n_melds == 0:
        best = min(
            best,
            shanten_qidui(counts),
            shanten_shisanyao(counts),
            shanten_quanbukao(counts),
        )
    if n_melds <= 1:
        best = min(best, shanten_zuhelong(counts, n_melds))
    return best


def guobiao_shanten_from_tiles(tiles: Iterable[int], n_melds: int = 0) -> int:
    return guobiao_shanten(counts_from_tiles(tiles), n_melds)


def effective_tiles(counts: Counts, n_melds: int = 0) -> List[int]:
    """有效进张：摸入后向听下降的牌种。"""
    base = guobiao_shanten(counts, n_melds)
    result: List[int] = []
    for tid in ALL_TILE_IDS:
        if counts.get(tid, 0) >= 4:
            continue
        nxt = add_tile(counts, tid)
        if guobiao_shanten(nxt, n_melds) < base:
            result.append(tid)
    return result


def live_copies(tile_ids: Iterable[int], counts: Counts, visible: Counts) -> int:
    total = 0
    for tid in tile_ids:
        total += max(0, 4 - visible.get(tid, counts.get(tid, 0)))
    return total
