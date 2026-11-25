"""Comprehensive unit tests and C# DLL parity tests for pure-Python Qingque 13-tile calculator."""

from __future__ import annotations

import os
import random
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Set, Tuple

import pytest

from server.game_calculation.qingque import (
    qingque_get_base_point,
    qingque_hepai_check,
    qingque_tingpai_check,
)

ALL_TILE_IDS: List[int] = (
    list(range(11, 20))
    + list(range(21, 30))
    + list(range(31, 40))
    + list(range(41, 48))
)
SUIT_TILE_IDS: List[int] = (
    list(range(11, 20)) + list(range(21, 30)) + list(range(31, 40))
)
TERMINAL_HONOUR_IDS: List[int] = [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47]
HONOUR_IDS: List[int] = list(range(41, 48))
WIND_IDS: List[int] = [41, 42, 43, 44]
DRAGON_IDS: List[int] = [45, 46, 47]

SEAT_WINDS = ["自风东", "自风南", "自风西", "自风北"]
PREVALENT_WINDS = ["场风东", "场风南", "场风西", "场风北", "圈风东", "圈风南"]
OCCASIONAL_COMBOS: List[List[str]] = [
    [],
    ["自摸"],
    ["海底捞月"],
    ["河底捞鱼"],
    ["岭上开花"],
    ["抢杠"],
    ["天和"],
    ["地和"],
    ["自摸", "海底捞月"],
    ["自摸", "岭上开花"],
    ["自摸", "天和"],
]


# ---------------------------------------------------------------------------
# Hand generators for targeted + randomized parity testing
# ---------------------------------------------------------------------------


def _make_targeted_hepai_cases() -> (
    List[Tuple[str, List[int], List[str], List[str], int]]
):
    """Curated hands covering every major Qingque fan family & edge case."""
    cases: List[Tuple[str, List[int], List[str], List[str], int]] = [
        # 1. Basic concealed hand (门前清)
        (
            "basic_concealed",
            [11, 12, 13, 22, 23, 24, 34, 35, 36, 17, 18, 19, 25],
            [],
            ["自摸", "自风东"],
            25,
        ),
        # 2. Pure straight + full flush + concealed (清一色 + 一气贯通)
        (
            "full_flush_pure_straight",
            [11, 12, 13, 14, 15, 16, 17, 18, 19, 12, 13, 14, 15],
            [],
            ["自风东"],
            15,
        ),
        # 3. Nine gates (九莲宝灯)
        (
            "nine_gates_1m",
            [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19],
            [],
            ["自摸", "自风东"],
            15,
        ),
        (
            "nine_gates_9p",
            [21, 21, 21, 22, 23, 24, 25, 26, 27, 28, 29, 29, 29],
            [],
            ["自风南"],
            29,
        ),
        # 4. Thirteen orphans (十三幺) with fan tile pair & non-fan tile pair
        (
            "thirteen_orphans_1m_pair",
            [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47],
            [],
            ["自摸", "自风东"],
            11,
        ),
        (
            "thirteen_orphans_dragon_pair",
            [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47],
            [],
            ["自风南"],
            45,
        ),
        (
            "thirteen_orphans_seat_wind_pair_heavenly",
            [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47],
            [],
            ["天和", "自风西"],
            43,
        ),
        (
            "thirteen_orphans_earthly",
            [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47],
            [],
            ["地和", "自风北"],
            19,
        ),
        (
            "thirteen_orphans_robbing_kong",
            [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47],
            [],
            ["抢杠", "自风东"],
            47,
        ),
        # 5. Seven pairs variants (七对, 叠对, 二叠对, 三叠对, 七连对, 风牌七对, 三元六对, 镜同三对, 三色二对)
        (
            "seven_pairs_basic",
            [11, 11, 13, 13, 22, 22, 25, 25, 33, 33, 38, 38, 41],
            [],
            ["自摸", "自风东"],
            41,
        ),
        (
            "seven_pairs_three_double_pairs",
            [11, 11, 11, 11, 22, 22, 22, 22, 33, 33, 33, 33, 45],
            [],
            ["自风东"],
            45,
        ),
        (
            "seven_pairs_seven_shifted",
            [12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18],
            [],
            ["自摸", "自风东"],
            18,
        ),
        (
            "seven_pairs_seven_wind_pairs",
            [41, 41, 41, 41, 42, 42, 42, 42, 43, 43, 43, 43, 44],
            [],
            ["自风东"],
            44,
        ),
        (
            "seven_pairs_six_wind_pairs",
            [41, 41, 41, 41, 42, 42, 42, 42, 43, 43, 44, 44, 15],
            [],
            ["自风南"],
            15,
        ),
        (
            "seven_pairs_six_dragon_pairs",
            [45, 45, 45, 45, 46, 46, 46, 46, 47, 47, 47, 47, 11],
            [],
            ["自风东"],
            11,
        ),
        (
            "seven_pairs_fan_tile_7p",
            [41, 41, 41, 41, 45, 45, 45, 45, 46, 46, 46, 46, 47],
            [],
            ["自风东"],
            47,
        ),
        (
            "seven_pairs_three_mirrored_pairs",
            [11, 11, 13, 13, 15, 15, 21, 21, 23, 23, 25, 25, 39],
            [],
            ["自风东"],
            39,
        ),
        (
            "seven_pairs_three_mirrored_pairs_double_low",
            [12, 12, 12, 12, 15, 15, 22, 22, 22, 22, 25, 25, 39],
            [],
            ["自风东"],
            39,
        ),
        (
            "seven_pairs_two_triple_pairs",
            [12, 12, 22, 22, 32, 32, 17, 17, 27, 27, 37, 37, 41],
            [],
            ["自风东"],
            41,
        ),
        (
            "seven_pairs_reflected_2",
            [11, 11, 12, 12, 13, 13, 27, 27, 28, 28, 29, 29, 35],
            [],
            ["自风东"],
            35,
        ),
        # 6. Four kongs / concealed kongs / triplets
        (
            "four_concealed_kongs",
            [45],
            ["G11", "G19", "G21", "G29"],
            ["自摸", "岭上开花", "自风东"],
            45,
        ),
        (
            "three_concealed_kongs_one_open",
            [15],
            ["G11", "G12", "G13", "g14"],
            ["自摸", "自风东"],
            15,
        ),
        (
            "two_concealed_kongs",
            [22, 23, 24, 32, 33, 34, 41],
            ["G15", "G25"],
            ["自风东"],
            41,
        ),
        (
            "four_open_kongs",
            [35],
            ["g41", "g42", "g43", "g44"],
            ["岭上开花", "自风东"],
            35,
        ),
        # 7. Four concealed triplets (ron vs tsumo vs 4th-copy concealed)
        (
            "four_concealed_triplets_tsumo",
            [11, 11, 11, 22, 22, 22, 33, 33, 33, 45, 45, 45, 19],
            [],
            ["自摸", "自风东"],
            19,
        ),
        (
            "four_concealed_triplets_ron_on_triplet",
            [11, 11, 11, 22, 22, 22, 33, 33, 33, 45, 45, 19, 19],
            [],
            ["自风东"],
            45,
        ),
        (
            "four_concealed_triplets_ron_on_pair",
            [11, 11, 11, 22, 22, 22, 33, 33, 33, 45, 45, 45, 19],
            [],
            ["自风东"],
            19,
        ),
        # 8. Winds & Dragons
        (
            "big_four_winds_all_honours",
            [41, 41, 41, 42, 42, 42, 45],
            ["k43", "k44"],
            ["自风东"],
            45,
        ),
        (
            "little_four_winds",
            [41, 41, 42, 42, 42, 12, 13, 14],
            ["k43", "k44"],
            ["自风南"],
            41,
        ),
        (
            "big_three_dragons",
            [45, 45, 45, 46, 46, 46, 12, 13, 14, 25],
            ["k47"],
            ["自风东"],
            25,
        ),
        (
            "little_three_dragons",
            [45, 45, 46, 46, 46, 12, 13, 14, 25, 26],
            ["k47"],
            ["自风东"],
            27,
        ),
        (
            "fan_tile_4t",
            [42, 42, 42, 45, 45, 45, 15],
            ["k46", "k47"],
            ["自风南"],
            15,
        ),
        # 9. Terminals & Outside hands
        (
            "all_terminals",
            [11, 11, 11, 19, 19, 19, 21, 21, 21, 39],
            ["k29"],
            ["自风东"],
            39,
        ),
        (
            "pure_outside_hand",
            [11, 12, 13, 17, 18, 19, 21, 22, 23, 39],
            ["s38"],
            ["自风东"],
            39,
        ),
        (
            "mixed_outside_hand",
            [11, 12, 13, 27, 28, 29, 41, 41, 41, 45],
            ["s32"],
            ["自风东"],
            45,
        ),
        # 10. Number & Symmetry fans
        (
            "all_types",
            [12, 13, 14, 24, 25, 26, 36, 37, 38, 41, 41, 41, 45],
            [],
            ["自风南"],
            45,
        ),
        (
            "common_number_5",
            [13, 14, 15, 24, 25, 26, 35, 36, 37, 15, 15, 25, 25],
            [],
            ["自风东"],
            25,
        ),
        (
            "quadruple_sequence",
            [12, 13, 14, 12, 13, 14, 12, 13, 14, 12, 13, 14, 19],
            [],
            ["自摸", "自风东"],
            19,
        ),
        (
            "triple_sequence",
            [12, 13, 14, 12, 13, 14, 12, 13, 14, 25, 26, 27, 39],
            [],
            ["自风东"],
            39,
        ),
        (
            "two_double_sequences",
            [12, 13, 14, 12, 13, 14, 25, 26, 27, 25, 26, 27, 39],
            [],
            ["自风东"],
            39,
        ),
        (
            "four_shifted_triplets",
            [12, 12, 12, 13, 13, 13, 14, 14, 14, 15, 15, 15, 29],
            [],
            ["自摸", "自风东"],
            29,
        ),
        (
            "four_shifted_sequences",
            [11, 12, 13, 12, 13, 14, 13, 14, 15, 14, 15, 16, 29],
            [],
            ["自风东"],
            29,
        ),
        (
            "four_chained_sequences",
            [11, 12, 13, 13, 14, 15, 15, 16, 17, 17, 18, 19, 25],
            [],
            ["自风东"],
            25,
        ),
        (
            "mixed_triple_triplet",
            [15, 15, 15, 25, 25, 25, 35, 35, 35, 12, 13, 14, 41],
            [],
            ["自风东"],
            41,
        ),
        (
            "mixed_triple_sequence",
            [12, 13, 14, 22, 23, 24, 32, 33, 34, 18, 18, 18, 41],
            [],
            ["自风东"],
            41,
        ),
        (
            "mixed_shifted_triplets",
            [12, 12, 12, 23, 23, 23, 34, 34, 34, 17, 18, 19, 41],
            [],
            ["自风东"],
            41,
        ),
        (
            "mixed_straight",
            [11, 12, 13, 24, 25, 26, 37, 38, 39, 41, 41, 41, 22],
            [],
            ["自风东"],
            22,
        ),
        (
            "mirrored_hand",
            [11, 12, 13, 21, 22, 23, 17, 18, 19, 27, 28, 29, 35],
            [],
            ["自风东"],
            35,
        ),
        (
            "two_short_straights",
            [11, 12, 13, 14, 15, 16, 22, 23, 24, 25, 26, 27, 39],
            [],
            ["自风东"],
            39,
        ),
        (
            "reflected_hand_standard",
            [11, 12, 13, 17, 18, 19, 23, 23, 23, 27, 27, 27, 35],
            [],
            ["自风东"],
            35,
        ),
        (
            "reflected_hand_2_standard",
            [11, 12, 13, 27, 28, 29, 14, 14, 14, 26, 26, 26, 35],
            [],
            ["自风东"],
            35,
        ),
        (
            "gapped_numbers_odd",
            [11, 11, 11, 23, 23, 23, 35, 35, 35, 17, 17, 17, 29],
            [],
            ["自风东"],
            29,
        ),
        (
            "gapped_numbers_step3",
            [11, 11, 11, 24, 24, 24, 37, 37, 37, 14, 14, 14, 21],
            [],
            ["自风东"],
            21,
        ),
        (
            "connected_numbers_standard",
            [12, 13, 14, 25, 25, 25, 36, 36, 36, 17, 17, 17, 28],
            [],
            ["自风东"],
            28,
        ),
        (
            "two_consecutive_numbers",
            [14, 14, 14, 24, 24, 24, 35, 35, 35, 15, 15, 15, 25],
            [],
            ["自风东"],
            25,
        ),
        (
            "two_numbers_non_consecutive",
            [12, 12, 12, 22, 22, 22, 37, 37, 37, 17, 17, 17, 27],
            [],
            ["自风东"],
            27,
        ),
        (
            "mixed_one_number",
            [15, 15, 15, 25, 25, 25, 35, 35, 35, 41, 41, 41, 45],
            [],
            ["自风东"],
            45,
        ),
        # 11. Invalid hands
        (
            "invalid_wrong_count_short",
            [11, 12, 13, 22, 23, 24, 34, 35, 36],
            [],
            ["自风东"],
            25,
        ),
        (
            "invalid_fifth_tile",
            [11, 11, 11, 11, 12, 13, 14, 22, 23, 24, 34, 35, 36],
            [],
            ["自风东"],
            11,
        ),
        (
            "invalid_non_winning_14",
            [11, 13, 15, 17, 19, 22, 24, 26, 28, 31, 33, 35, 41],
            [],
            ["自风东"],
            45,
        ),
        (
            "invalid_tile_id_zero",
            [11, 12, 13, 22, 23, 24, 34, 35, 36, 17, 18, 19, 25],
            [],
            ["自风东"],
            0,
        ),
    ]
    return cases


def _generate_random_hepai_cases(
    count: int = 550, seed: int = 20261125
) -> List[Tuple[str, List[int], List[str], List[str], int]]:
    """Generate diverse random winning (and some invalid) 13-tile hands."""
    rng = random.Random(seed)
    cases: List[Tuple[str, List[int], List[str], List[str], int]] = []

    def try_build_standard(
        pool_suits: Sequence[int], allow_honours: bool, max_open: int
    ) -> Optional[Tuple[List[int], List[str], int]]:
        counts: Dict[int, int] = {}
        closed_tiles: List[int] = []
        open_melds: List[str] = []

        valid_tiles: List[int] = []
        for s in pool_suits:
            valid_tiles.extend(range(s * 10 + 1, s * 10 + 10))
        if allow_honours:
            valid_tiles.extend(HONOUR_IDS)
        if not valid_tiles:
            return None

        num_open = rng.randint(0, max_open)
        for m_idx in range(4):
            is_open = m_idx < num_open
            for _ in range(30):
                kind = rng.choice(["seq", "trip", "kong"] if is_open else ["seq", "trip"])
                if kind == "seq":
                    if not pool_suits:
                        continue
                    s = rng.choice(pool_suits)
                    mid = s * 10 + rng.randint(2, 8)
                    t1, t2, t3 = mid - 1, mid, mid + 1
                    if (
                        counts.get(t1, 0) < 4
                        and counts.get(t2, 0) < 4
                        and counts.get(t3, 0) < 4
                    ):
                        counts[t1] = counts.get(t1, 0) + 1
                        counts[t2] = counts.get(t2, 0) + 1
                        counts[t3] = counts.get(t3, 0) + 1
                        if is_open:
                            open_melds.append(f"s{mid}")
                        else:
                            closed_tiles.extend([t1, t2, t3])
                        break
                elif kind == "trip":
                    t = rng.choice(valid_tiles)
                    if counts.get(t, 0) <= 1:
                        counts[t] = counts.get(t, 0) + 3
                        if is_open:
                            open_melds.append(f"k{t}")
                        else:
                            closed_tiles.extend([t, t, t])
                        break
                else:  # kong
                    t = rng.choice(valid_tiles)
                    if counts.get(t, 0) == 0:
                        counts[t] = 4
                        prefix = rng.choice(["g", "G"])
                        open_melds.append(f"{prefix}{t}")
                        break
            else:
                return None

        # Pair
        for _ in range(30):
            p = rng.choice(valid_tiles)
            if counts.get(p, 0) <= 2:
                counts[p] = counts.get(p, 0) + 2
                closed_tiles.extend([p, p])
                break
        else:
            return None

        win_tile = rng.choice(closed_tiles)
        closed_tiles.remove(win_tile)
        return closed_tiles, open_melds, win_tile

    def try_build_seven_pairs(
        pool_tiles: Sequence[int],
    ) -> Optional[Tuple[List[int], List[str], int]]:
        counts: Dict[int, int] = {}
        tiles: List[int] = []
        for _ in range(7):
            for _ in range(30):
                t = rng.choice(pool_tiles)
                if counts.get(t, 0) <= 2:
                    counts[t] = counts.get(t, 0) + 2
                    tiles.extend([t, t])
                    break
            else:
                return None
        win_tile = rng.choice(tiles)
        tiles.remove(win_tile)
        return tiles, [], win_tile

    idx = 0
    while len(cases) < count:
        idx += 1
        mode = idx % 10
        way = list(rng.choice(OCCASIONAL_COMBOS))
        way.append(rng.choice(SEAT_WINDS))
        if rng.random() < 0.5:
            way.append(rng.choice(PREVALENT_WINDS))

        if mode in (0, 1, 2):
            # Mixed suits + honours standard
            built = try_build_standard([1, 2, 3], True, 4)
        elif mode == 3:
            # Full flush
            s = rng.choice([1, 2, 3])
            built = try_build_standard([s], False, 3)
        elif mode == 4:
            # Half flush
            s = rng.choice([1, 2, 3])
            built = try_build_standard([s], True, 3)
        elif mode == 5:
            # Two suits (good for mirrored/reflected)
            s1, s2 = rng.sample([1, 2, 3], 2)
            built = try_build_standard([s1, s2], False, 2)
        elif mode == 6:
            # All honours or terminals+honours
            if rng.random() < 0.5:
                built = try_build_standard([], True, 4)
            else:
                built = try_build_seven_pairs(TERMINAL_HONOUR_IDS)
        elif mode in (7, 8):
            # Seven pairs (full, flush, or honours)
            sub = rng.choice([ALL_TILE_IDS, list(range(11, 20)), HONOUR_IDS, list(range(11, 20)) + list(range(21, 30))])
            built = try_build_seven_pairs(sub)
        else:
            # Thirteen orphans or invalid hand
            if rng.random() < 0.5:
                t_list = list(TERMINAL_HONOUR_IDS)
                win_tile = rng.choice(TERMINAL_HONOUR_IDS)
                built = (t_list, [], win_tile)
            else:
                # Random non-winning / possibly invalid hand
                t_list = [rng.choice(ALL_TILE_IDS) for _ in range(13)]
                win_tile = rng.choice(ALL_TILE_IDS)
                built = (t_list, [], win_tile)

        if built is None:
            continue
        hand_tiles, melds, win_tile = built
        cases.append((f"rand_{idx}", hand_tiles, melds, way, win_tile))

    return cases


def _generate_tingpai_cases(seed: int = 20261126) -> List[Tuple[str, List[int], List[str]]]:
    """Curated + randomized tingpai cases including 4-copy physical limits."""
    cases: List[Tuple[str, List[int], List[str]]] = [
        # Nine gates 9-way wait
        ("tp_nine_gates", [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19], []),
        # Thirteen orphans 13-way wait
        ("tp_13_orphans_13way", [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47], []),
        # Thirteen orphans 1-way wait
        ("tp_13_orphans_1way", [11, 11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46], []),
        # Seven pairs wait (including 4-copy tile in hand)
        ("tp_seven_pairs_double", [11, 11, 11, 11, 22, 22, 33, 33, 41, 41, 45, 45, 47], []),
        # Seven pairs waiting for 4th copy of a triplet
        ("tp_seven_pairs_wait_4th", [11, 11, 11, 22, 22, 33, 33, 41, 41, 45, 45, 47, 47], []),
        # 4-copy physical limit: tanki wait on tile already having 4 copies in hand -> excluded!
        ("tp_4copy_in_hand", [11, 11, 11, 11, 12, 13, 22, 23, 24, 34, 35, 36, 11], []),
        # 4-copy physical limit across hand + open kong
        ("tp_4copy_with_kong", [11, 12, 13, 22, 23, 24, 34, 35, 36, 45], ["g45"]),
        # 4-copy physical limit across hand + open triplet + 1 in hand
        ("tp_4copy_with_pon", [12, 13, 22, 23, 24, 34, 35, 36, 11, 11], ["k11"]),
        # Edge boundary waits (11,12 -> only 13, not 10; 18,19 -> only 17, not 20; 38,39 -> only 37, not 40)
        ("tp_edge_11_12", [11, 12, 22, 23, 24, 34, 35, 36, 41, 41, 45, 45, 45], []),
        ("tp_edge_18_19", [18, 19, 22, 23, 24, 34, 35, 36, 41, 41, 45, 45, 45], []),
        ("tp_edge_38_39", [38, 39, 22, 23, 24, 14, 15, 16, 41, 41, 45, 45, 45], []),
        # Honor tiles cannot form sequence waits (41,42 or 41,43)
        ("tp_honors_no_seq", [41, 42, 22, 23, 24, 14, 15, 16, 31, 31, 45, 45, 45], []),
        # Cross-suit boundary (19, 21 must not wait 20)
        ("tp_cross_suit_19_21", [19, 21, 22, 23, 24, 14, 15, 16, 31, 31, 45, 45, 45], []),
        # Multiple open melds
        ("tp_3_melds_tanki", [15, 16, 17, 22], ["k41", "s25", "G33"]),
        ("tp_4_melds_tanki", [47], ["k41", "s25", "G33", "g19"]),
    ]

    # Add 150 random tenpai hands derived from random winning hands
    for name, hand_tiles, melds, _, _ in _generate_random_hepai_cases(count=150, seed=seed):
        cases.append((f"tp_{name}", hand_tiles, melds))

    return cases


# ---------------------------------------------------------------------------
# Pure-Python unit tests (always run under pytest)
# ---------------------------------------------------------------------------


def test_base_point_bankers_rounding():
    assert qingque_get_base_point(0.0) == 0
    assert qingque_get_base_point(1.0) == 1
    assert qingque_get_base_point(2.5) == 6  # 6.25 -> 6
    assert qingque_get_base_point(3.0) == 9
    # Banker's rounding checks on exact halves
    # sqrt(2.5), sqrt(3.5) etc.
    for x in [0.0, 1.5, 2.5, 4.0, 8.1234, 16.5, 32.75, 50.6422]:
        assert qingque_get_base_point(x) == int(round(x * x))


def test_hepai_known_hands():
    # Nine gates
    score, names = qingque_hepai_check(
        [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19],
        [],
        ["自摸", "自风东"],
        15,
    )
    assert score > 0
    assert "九莲宝灯" in names
    assert "清一色" not in names  # covered by 九莲宝灯
    assert "门前清" not in names  # covered by 九莲宝灯

    # Thirteen orphans with dragon pair
    score, names = qingque_hepai_check(
        [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47],
        [],
        ["自风东"],
        45,
    )
    assert score > 0
    assert "十三幺" in names
    assert "番牌" in names
    assert "门前清" not in names  # covered by 十三幺

    # Invalid hand
    score, names = qingque_hepai_check([11, 12, 13], [], ["自风东"], 14)
    assert score == 0.0
    assert names == ["Invalid hand"]


def test_tingpai_known_hands():
    # Nine gates 9-way wait
    waits = qingque_tingpai_check(
        [11, 11, 11, 12, 13, 14, 15, 16, 17, 18, 19, 19, 19], []
    )
    assert waits == set(range(11, 20))

    # Thirteen orphans 13-way wait
    waits = qingque_tingpai_check(TERMINAL_HONOUR_IDS, [])
    assert waits == set(TERMINAL_HONOUR_IDS)

    # 4-copy physical limit with kong
    waits = qingque_tingpai_check(
        [11, 12, 13, 22, 23, 24, 34, 35, 36, 45], ["g45"]
    )
    assert waits == set()


# ---------------------------------------------------------------------------
# C# DLL Parity Test (runs when pythonnet + Qingque13Calc.dll are present)
# ---------------------------------------------------------------------------


def _load_csharp_dll():
    """Load Qingque13Calc.dll via pythonnet if available, else return None."""
    try:
        if "PYTHONNET_RUNTIME" not in os.environ:
            os.environ["PYTHONNET_RUNTIME"] = "coreclr"
        import clr  # type: ignore
        from System import Boolean, Int32  # type: ignore
        from System.Collections.Generic import List  # type: ignore
        from System.Reflection import BindingFlags  # type: ignore
    except Exception:
        return None

    base = Path(__file__).resolve().parent.parent / "Qingque13Calc"
    dll_candidates = [
        base / "Qingque13Calc.dll",
        base / "archive" / "Qingque13Calc.dll",
    ]
    dll_path = next((p for p in dll_candidates if p.is_file()), None)
    if dll_path is None:
        return None

    asm = clr.AddReference(str(dll_path))
    hepai_type = asm.GetType("Qingque13.Qingque13Hepai")
    tingpai_type = asm.GetType("Qingque13.Qingque13Tingpai")
    scoring_type = asm.GetType("Qingque13.QingqueScoring")

    fan_cache_path = base / "fan_cache.json"
    if scoring_type is not None and fan_cache_path.is_file():
        load_m = scoring_type.GetMethod(
            "LoadFanCacheFromFile", BindingFlags.Public | BindingFlags.Static
        )
        if load_m is not None:
            load_m.Invoke(None, [str(fan_cache_path)])

    hepai_m = hepai_type.GetMethod(
        "HepaiCheck", BindingFlags.Public | BindingFlags.Static
    )
    base_pt_m = hepai_type.GetMethod(
        "GetBasePoint", BindingFlags.Public | BindingFlags.Static
    )
    tingpai_m = tingpai_type.GetMethod(
        "TingpaiCheck", BindingFlags.Public | BindingFlags.Static
    )

    def cs_hepai(hand_list, melds, way, get_tile, debug=False):
        h_net = List[int]()
        for x in hand_list:
            h_net.Add(x)
        m_net = List[str]()
        for x in melds:
            m_net.Add(x)
        w_net = List[str]()
        for x in way:
            w_net.Add(x)
        res = hepai_m.Invoke(None, [h_net, m_net, w_net, Int32(get_tile), Boolean(debug)])
        return float(res.Item1), list(res.Item2)

    def cs_tingpai(hand_list, melds, debug=False):
        h_net = List[int]()
        for x in hand_list:
            h_net.Add(x)
        m_net = List[str]()
        for x in melds:
            m_net.Add(x)
        res = tingpai_m.Invoke(None, [h_net, m_net, Boolean(debug)])
        return set(res)

    def cs_base_point(fan: float) -> int:
        return int(base_pt_m.Invoke(None, [float(fan)]))

    return cs_hepai, cs_tingpai, cs_base_point


def test_parity_against_csharp_dll():
    cs = _load_csharp_dll()
    if cs is None:
        pytest.skip("pythonnet or Qingque13Calc.dll not available")

    cs_hepai, cs_tingpai, cs_base_point = cs

    hepai_cases = _make_targeted_hepai_cases() + _generate_random_hepai_cases(count=550)
    mismatches: List[str] = []

    for name, hand_list, melds, way, get_tile in hepai_cases:
        py_score, py_names = qingque_hepai_check(hand_list, melds, way, get_tile)
        cs_score, cs_names = cs_hepai(hand_list, melds, way, get_tile)

        if py_score != cs_score or py_names != cs_names:
            mismatches.append(
                f"HEPAI [{name}] hand={hand_list} melds={melds} way={way} tile={get_tile}\n"
                f"  PY: ({py_score}, {py_names})\n"
                f"  CS: ({cs_score}, {cs_names})"
            )
            if len(mismatches) >= 20:
                break

        if qingque_get_base_point(py_score) != cs_base_point(cs_score):
            mismatches.append(
                f"BASE_POINT [{name}] fan={py_score}: "
                f"PY={qingque_get_base_point(py_score)} CS={cs_base_point(cs_score)}"
            )

    tingpai_cases = _generate_tingpai_cases()
    for name, hand_list, melds in tingpai_cases:
        py_waits = qingque_tingpai_check(hand_list, melds)
        cs_waits = cs_tingpai(hand_list, melds)
        if py_waits != cs_waits:
            mismatches.append(
                f"TINGPAI [{name}] hand={hand_list} melds={melds}\n"
                f"  PY: {sorted(py_waits)}\n"
                f"  CS: {sorted(cs_waits)}"
            )
            if len(mismatches) >= 20:
                break

    # Explicit base point edge values
    for val in [0.0, 0.5, 1.0, 1.5, 2.5, 3.5, 4.5, 12.3456, 25.5, 48.1176, 50.6422]:
        assert qingque_get_base_point(val) == cs_base_point(val)

    assert not mismatches, f"Found {len(mismatches)} mismatches:\n" + "\n".join(mismatches)


if __name__ == "__main__":
    cs = _load_csharp_dll()
    assert cs is not None, "Failed to load C# DLL via pythonnet"
    cs_hepai, cs_tingpai, cs_base_point = cs

    hepai_cases = _make_targeted_hepai_cases() + _generate_random_hepai_cases(count=550)
    tingpai_cases = _generate_tingpai_cases()

    hepai_ok = 0
    hepai_fail = 0
    for name, hand_list, melds, way, get_tile in hepai_cases:
        py_score, py_names = qingque_hepai_check(hand_list, melds, way, get_tile)
        cs_score, cs_names = cs_hepai(hand_list, melds, way, get_tile)
        py_bp = qingque_get_base_point(py_score)
        cs_bp = cs_base_point(cs_score)
        if py_score == cs_score and py_names == cs_names and py_bp == cs_bp:
            hepai_ok += 1
        else:
            hepai_fail += 1
            if hepai_fail <= 10:
                print(
                    f"MISMATCH HEPAI [{name}]: hand={hand_list} melds={melds} way={way} tile={get_tile}\n"
                    f"  PY: ({py_score}, {py_names}, bp={py_bp})\n"
                    f"  CS: ({cs_score}, {cs_names}, bp={cs_bp})"
                )

    tp_ok = 0
    tp_fail = 0
    for name, hand_list, melds in tingpai_cases:
        py_waits = qingque_tingpai_check(hand_list, melds)
        cs_waits = cs_tingpai(hand_list, melds)
        if py_waits == cs_waits:
            tp_ok += 1
        else:
            tp_fail += 1
            if tp_fail <= 10:
                print(
                    f"MISMATCH TINGPAI [{name}]: hand={hand_list} melds={melds}\n"
                    f"  PY: {sorted(py_waits)}\n"
                    f"  CS: {sorted(cs_waits)}"
                )

    print(
        f"PARITY SUMMARY: hepai {hepai_ok}/{len(hepai_cases)} passed ({hepai_fail} failed); "
        f"tingpai {tp_ok}/{len(tingpai_cases)} passed ({tp_fail} failed)"
    )
    if hepai_fail or tp_fail:
        raise SystemExit(1)
