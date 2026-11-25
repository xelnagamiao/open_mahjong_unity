"""All 90 Qingque 13-tile fan criteria and evaluator.

Pure Python, stateless port of QingqueFanEvaluator.cs, QingquePatterns.cs,
and all 90 Criteria/*.cs classes.
"""

from __future__ import annotations

from typing import Callable, List, Sequence, Tuple

from .fans import OCCASIONAL_FAN_VALUES, QingqueFan
from .core import (
    ALL_TILES,
    DRAGON_TILES,
    HONOUR_C,
    HONOUR_F,
    HONOUR_P,
    HONOUR_TILES,
    NUMBERED_TILES,
    QingqueDecomposition,
    QingqueHand,
    QingqueMeld,
    QingqueMeldType,
    QingqueTileCounter,
    QingqueWinType,
    SuitType,
    TERMINAL_HONOUR_TILES,
    TERMINAL_TILES,
    WIND_TILES,
    make_tile,
    tile_is_honor,
    tile_is_terminal,
    tile_is_wind,
    tile_num,
    tile_suit_type,
)

_NUMBERED_SUITS: Tuple[SuitType, ...] = (SuitType.M, SuitType.P, SuitType.S)
_TERMINAL_SET = frozenset(TERMINAL_TILES)
_SUIT_PERMS: Tuple[Tuple[SuitType, SuitType, SuitType], ...] = (
    (SuitType.M, SuitType.P, SuitType.S),
    (SuitType.P, SuitType.S, SuitType.M),
    (SuitType.S, SuitType.M, SuitType.P),
    (SuitType.M, SuitType.S, SuitType.P),
    (SuitType.P, SuitType.M, SuitType.S),
    (SuitType.S, SuitType.P, SuitType.M),
)

# Precomputed patterns matching QingquePatterns.cs
_MIXED_SHIFTED_TRIPLETS: Tuple[Tuple[int, int, int], ...] = tuple(
    (make_tile(s1, n), make_tile(s2, n + 1), make_tile(s3, n + 2))
    for s1, s2, s3 in _SUIT_PERMS
    for n in range(1, 8)
)

_MIXED_STRAIGHT: Tuple[Tuple[int, int, int], ...] = tuple(
    (make_tile(s1, 2), make_tile(s2, 5), make_tile(s3, 8))
    for s1, s2, s3 in _SUIT_PERMS
)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _count_equivalent_pairs(melds: Sequence[QingqueMeld]) -> int:
    count = 0
    n = len(melds)
    for i in range(n):
        mi = melds[i]
        for j in range(i + 1, n):
            mj = melds[j]
            if mi.meld_type == mj.meld_type and mi.tile == mj.tile:
                count += 1
    return count


def _contains_sequences(
    melds: Sequence[QingqueMeld], suit: SuitType, middle_tiles: Tuple[int, ...]
) -> bool:
    for mid in middle_tiles:
        found = False
        for m in melds:
            if (
                m.meld_type == QingqueMeldType.SEQUENCE
                and tile_suit_type(m.tile) == suit
                and tile_num(m.tile) == mid
            ):
                found = True
                break
        if not found:
            return False
    return True


def _contains_triplets(
    melds: Sequence[QingqueMeld], suit: SuitType, nums: Tuple[int, ...]
) -> bool:
    for num in nums:
        found = False
        for m in melds:
            if (
                m.meld_type != QingqueMeldType.SEQUENCE
                and tile_suit_type(m.tile) == suit
                and tile_num(m.tile) == num
            ):
                found = True
                break
        if not found:
            return False
    return True


def _fan_tiles(d: QingqueDecomposition) -> Tuple[int, int, int, int]:
    return (d.winning_type.seat_wind(), HONOUR_C, HONOUR_F, HONOUR_P)


def _hog_count(d: QingqueDecomposition) -> int:
    cnt = d.counter()
    count = 0
    for t in ALL_TILES:
        if cnt.count_tile(t) == 4:
            count += 1
    for m in d.original_hand.open_melds:
        if m.meld_type == QingqueMeldType.KONG:
            count -= 1
    return count


def _double_pair_count(d: QingqueDecomposition) -> int:
    cnt = d.counter()
    count = 0
    for t in ALL_TILES:
        if cnt.count_tile(t) == 4:
            count += 1
    return count


def _has_shifted_pairs(d: QingqueDecomposition, length: int) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    max_start = 10 - length
    for suit in _NUMBERED_SUITS:
        for start in range(1, max_start + 1):
            valid = True
            for offset in range(length):
                if cnt.count_tile(make_tile(suit, start + offset)) < 2:
                    valid = False
                    break
            if valid:
                return True
    return False


def _has_honours(cnt: QingqueTileCounter) -> bool:
    for t in HONOUR_TILES:
        if cnt.count_tile(t) > 0:
            return True
    return False


def _reflect_suit(tile_suit: SuitType, ref_suit: SuitType) -> SuitType:
    if tile_suit == ref_suit or ref_suit == SuitType.Z:
        return tile_suit
    for s in _NUMBERED_SUITS:
        if s != ref_suit and s != tile_suit:
            return s
    return tile_suit


def _count_suit(cnt: QingqueTileCounter, suit: SuitType) -> int:
    total = 0
    for n in range(1, 10):
        total += cnt.count_tile(make_tile(suit, n))
    return total


# ---------------------------------------------------------------------------
# Public helper used by Qingque13Hepai for Thirteen Orphans
# ---------------------------------------------------------------------------


def is_thirteen_orphans(hand: QingqueHand) -> bool:
    cnt = hand.total_counter
    for t in ALL_TILES:
        if cnt.count_tile(t) == 0:
            continue
        if not tile_is_honor(t) and not tile_is_terminal(t):
            return False

    pair_count = 0
    single_count = 0
    for t in TERMINAL_HONOUR_TILES:
        c = cnt.count_tile(t)
        if c == 1:
            single_count += 1
        elif c == 2:
            pair_count += 1
        elif c != 0:
            return False

    return single_count == 12 and pair_count == 1


# ---------------------------------------------------------------------------
# 90 Criteria functions (in QingqueFan enum order)
# ---------------------------------------------------------------------------


def _c_trivial(d: QingqueDecomposition) -> bool:
    return True


def _c_heavenly_hand(d: QingqueDecomposition) -> bool:
    return d.is_won_by(
        QingqueWinType.HEAVENLY_OR_EARTHLY_HAND | QingqueWinType.SELF_DRAWN
    )


def _c_earthly_hand(d: QingqueDecomposition) -> bool:
    return d.winning_type.has_flags(
        QingqueWinType.HEAVENLY_OR_EARTHLY_HAND, QingqueWinType.SELF_DRAWN
    )


def _c_out_with_replacement_tile(d: QingqueDecomposition) -> bool:
    return d.winning_type.has_flags(
        QingqueWinType.KONG_RELATED | QingqueWinType.SELF_DRAWN
    )


def _c_last_tile_draw(d: QingqueDecomposition) -> bool:
    return d.winning_type.has_flags(
        QingqueWinType.FINAL_TILE | QingqueWinType.SELF_DRAWN
    )


def _c_last_tile_claim(d: QingqueDecomposition) -> bool:
    wt = d.winning_type
    return (not wt.is_self_drawn) and wt.is_final_tile


def _c_robbing_the_kong(d: QingqueDecomposition) -> bool:
    wt = d.winning_type
    return (not wt.is_self_drawn) and wt.has_flags(QingqueWinType.KONG_RELATED)


def _c_thirteen_orphans(d: QingqueDecomposition) -> bool:
    return is_thirteen_orphans(d.original_hand)


def _c_seven_pairs(d: QingqueDecomposition) -> bool:
    return d.is_seven_pairs


def _c_concealed_hand(d: QingqueDecomposition) -> bool:
    return all(m.concealed for m in d.original_hand.open_melds)


def _c_four_concealed_kongs(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return all(m.concealed and m.meld_type == QingqueMeldType.KONG for m in d.melds)


def _c_three_concealed_kongs(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return (
        sum(
            1
            for m in d.melds
            if m.concealed and m.meld_type == QingqueMeldType.KONG
        )
        >= 3
    )


def _c_two_concealed_kongs(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return (
        sum(
            1
            for m in d.melds
            if m.concealed and m.meld_type == QingqueMeldType.KONG
        )
        >= 2
    )


def _c_concealed_kong(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return any(m.concealed and m.meld_type == QingqueMeldType.KONG for m in d.melds)


def _c_four_kongs(d: QingqueDecomposition) -> bool:
    return (not d.is_seven_pairs) and d.counter().total_count() >= 18


def _c_three_kongs(d: QingqueDecomposition) -> bool:
    return (not d.is_seven_pairs) and d.counter().total_count() >= 17


def _c_two_kongs(d: QingqueDecomposition) -> bool:
    return (not d.is_seven_pairs) and d.counter().total_count() >= 16


def _c_four_concealed_triplets(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return all(
        m.concealed and m.meld_type != QingqueMeldType.SEQUENCE for m in d.melds
    )


def _c_three_concealed_triplets(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return (
        sum(
            1
            for m in d.melds
            if m.concealed and m.meld_type != QingqueMeldType.SEQUENCE
        )
        >= 3
    )


def _c_all_triplets(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return all(m.meld_type != QingqueMeldType.SEQUENCE for m in d.melds)


def _c_twelve_hog(d: QingqueDecomposition) -> bool:
    return _hog_count(d) == 3


def _c_eight_hog(d: QingqueDecomposition) -> bool:
    return _hog_count(d) >= 2


def _c_three_double_pairs(d: QingqueDecomposition) -> bool:
    return d.is_seven_pairs and _double_pair_count(d) >= 3


def _c_two_double_pairs(d: QingqueDecomposition) -> bool:
    return d.is_seven_pairs and _double_pair_count(d) >= 2


def _c_double_pair(d: QingqueDecomposition) -> bool:
    return d.is_seven_pairs and _double_pair_count(d) >= 1


def _c_all_honours(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    for t in NUMBERED_TILES:
        if cnt.count_tile(t) > 0:
            return False
    return True


def _c_big_four_winds(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    cnt = d.counter()
    return all(cnt.count_tile(w) >= 3 for w in WIND_TILES)


def _c_little_four_winds(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    cnt = d.counter()
    score = 0
    for w in WIND_TILES:
        c = cnt.count_tile(w)
        if c >= 3:
            score += 1
        if c >= 2:
            score += 1
    return score == 7


def _c_four_wind_pairs(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    for w in WIND_TILES:
        c = cnt.count_tile(w)
        if c != 2 and c != 4:
            return False
    return True


def _c_three_wind_triplets(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return (
        sum(
            1
            for m in d.melds
            if m.meld_type != QingqueMeldType.SEQUENCE and tile_is_wind(m.tile)
        )
        >= 3
    )


def _c_seven_wind_pairs(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    pair_count = 0
    for w in WIND_TILES:
        c = cnt.count_tile(w)
        if c == 2:
            pair_count += 1
        if c == 4:
            pair_count += 2
    return pair_count == 7


def _c_six_wind_pairs(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    return sum(cnt.count_tile(w) // 2 for w in WIND_TILES) >= 6


def _c_big_three_dragons(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    cnt = d.counter()
    return all(cnt.count_tile(dr) >= 3 for dr in DRAGON_TILES)


def _c_little_three_dragons(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    cnt = d.counter()
    score = 0
    for dr in DRAGON_TILES:
        c = cnt.count_tile(dr)
        if c >= 3:
            score += 1
        if c >= 2:
            score += 1
    return score == 5


def _c_six_dragon_pairs(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    pair_count = 0
    for dr in DRAGON_TILES:
        c = cnt.count_tile(dr)
        if c == 2:
            pair_count += 1
        elif c == 4:
            pair_count += 2
    return pair_count == 6


def _c_three_dragon_pairs(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    for dr in DRAGON_TILES:
        c = cnt.count_tile(dr)
        if c != 2 and c != 4:
            return False
    return True


def _fan_tile_triplets(d: QingqueDecomposition) -> int:
    cnt = d.counter()
    return sum(1 for t in _fan_tiles(d) if cnt.count_tile(t) >= 3)


def _fan_tile_groups_p(d: QingqueDecomposition) -> int:
    cnt = d.counter()
    is_sp = d.is_seven_pairs
    total = 0
    for t in _fan_tiles(d):
        c = cnt.count_tile(t)
        if c > 1:
            total += 1
        if c == 4 and is_sp:
            total += 1
    return total


def _fan_tile_seven_pairs(d: QingqueDecomposition) -> int:
    cnt = d.counter()
    return sum(cnt.count_tile(t) // 2 for t in _fan_tiles(d))


def _c_fan_tile_4t(d: QingqueDecomposition) -> bool:
    return (not d.is_seven_pairs) and _fan_tile_triplets(d) >= 4


def _c_fan_tile_3t(d: QingqueDecomposition) -> bool:
    return (not d.is_seven_pairs) and _fan_tile_triplets(d) >= 3


def _c_fan_tile_2t(d: QingqueDecomposition) -> bool:
    return (not d.is_seven_pairs) and _fan_tile_triplets(d) >= 2


def _c_fan_tile_1t(d: QingqueDecomposition) -> bool:
    return (not d.is_seven_pairs) and _fan_tile_triplets(d) >= 1


def _c_fan_tile_7p(d: QingqueDecomposition) -> bool:
    return d.is_seven_pairs and _fan_tile_seven_pairs(d) >= 7


def _c_fan_tile_6p(d: QingqueDecomposition) -> bool:
    return d.is_seven_pairs and _fan_tile_seven_pairs(d) >= 6


def _c_fan_tile_5p(d: QingqueDecomposition) -> bool:
    return d.is_seven_pairs and _fan_tile_seven_pairs(d) >= 5


def _c_fan_tile_4p(d: QingqueDecomposition) -> bool:
    return _fan_tile_groups_p(d) >= 4


def _c_fan_tile_3p(d: QingqueDecomposition) -> bool:
    return _fan_tile_groups_p(d) >= 3


def _c_fan_tile_2p(d: QingqueDecomposition) -> bool:
    return _fan_tile_groups_p(d) >= 2


def check_fan_tile_1p(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    return any(cnt.count_tile(t) > 1 for t in _fan_tiles(d))


def _c_all_terminals(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    for t in ALL_TILES:
        if cnt.count_tile(t) > 0 and t not in _TERMINAL_SET:
            return False
    return True


def _c_all_terminals_and_honours(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    for n in range(2, 9):
        if (
            cnt.count_tile(make_tile(SuitType.M, n)) > 0
            or cnt.count_tile(make_tile(SuitType.P, n)) > 0
            or cnt.count_tile(make_tile(SuitType.S, n)) > 0
        ):
            return False
    return True


def _c_pure_outside_hand(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        assert d.seven_pair_tiles is not None
        return all(tile_is_terminal(t) for t in d.seven_pair_tiles)
    if not tile_is_terminal(d.pair):
        return False
    for m in d.melds:
        if m.meld_type == QingqueMeldType.SEQUENCE:
            n = tile_num(m.tile)
            if n != 2 and n != 8:
                return False
        else:
            if not tile_is_terminal(m.tile):
                return False
    return True


def _c_mixed_outside_hand(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        assert d.seven_pair_tiles is not None
        return all(
            tile_is_honor(t) or tile_is_terminal(t) for t in d.seven_pair_tiles
        )
    if not (tile_is_honor(d.pair) or tile_is_terminal(d.pair)):
        return False
    for m in d.melds:
        if m.meld_type == QingqueMeldType.SEQUENCE:
            n = tile_num(m.tile)
            if n != 2 and n != 8:
                return False
        else:
            if not (tile_is_honor(m.tile) or tile_is_terminal(m.tile)):
                return False
    return True


def _c_nine_gates(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    cnt = d.counter()
    if cnt.total_count() != 14:
        return False

    win_num = tile_num(d.winning_tile)
    win_suit = tile_suit_type(d.winning_tile)

    for suit in _NUMBERED_SUITS:
        all_same_suit = True
        for t in ALL_TILES:
            if cnt.count_tile(t) > 0 and tile_suit_type(t) != suit:
                all_same_suit = False
                break
        if not all_same_suit:
            continue

        valid = True
        for n in range(1, 10):
            c = cnt.count_tile(make_tile(suit, n))
            if n == win_num and win_suit == suit:
                c -= 1
            expected = 3 if (n == 1 or n == 9) else 1
            if c != expected:
                valid = False
                break
        if valid:
            return True

    return False


def _c_full_flush(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    if _has_honours(cnt):
        return False
    has_m = any(cnt.count_tile(make_tile(SuitType.M, n)) > 0 for n in range(1, 10))
    has_p = any(cnt.count_tile(make_tile(SuitType.P, n)) > 0 for n in range(1, 10))
    has_s = any(cnt.count_tile(make_tile(SuitType.S, n)) > 0 for n in range(1, 10))
    return (int(has_m) + int(has_p) + int(has_s)) == 1


def _c_half_flush(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    has_m = any(cnt.count_tile(make_tile(SuitType.M, n)) > 0 for n in range(1, 10))
    has_p = any(cnt.count_tile(make_tile(SuitType.P, n)) > 0 for n in range(1, 10))
    has_s = any(cnt.count_tile(make_tile(SuitType.S, n)) > 0 for n in range(1, 10))
    return (int(has_m) + int(has_p) + int(has_s)) <= 1


def _c_all_types(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    cnt = d.counter()
    has_m = any(cnt.count_tile(make_tile(SuitType.M, n)) > 0 for n in range(1, 10))
    has_p = any(cnt.count_tile(make_tile(SuitType.P, n)) > 0 for n in range(1, 10))
    has_s = any(cnt.count_tile(make_tile(SuitType.S, n)) > 0 for n in range(1, 10))
    has_wind = any(cnt.count_tile(w) > 0 for w in WIND_TILES)
    has_dragon = any(cnt.count_tile(dr) > 0 for dr in DRAGON_TILES)
    return has_m and has_p and has_s and has_wind and has_dragon


def _c_mixed_one_number(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    found_num = 0
    for n in range(1, 10):
        total = (
            cnt.count_tile(make_tile(SuitType.M, n))
            + cnt.count_tile(make_tile(SuitType.P, n))
            + cnt.count_tile(make_tile(SuitType.S, n))
        )
        if total <= 0:
            continue
        if found_num == 0:
            found_num = n
        elif found_num != n:
            return False
    return True


def _c_two_numbers(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    if _has_honours(cnt):
        return False
    numbers = [False] * 9
    for t in NUMBERED_TILES:
        if cnt.count_tile(t) > 0:
            numbers[tile_num(t) - 1] = True
    return sum(numbers) == 2


def _consecutive_numbers_check(
    d: QingqueDecomposition, max_span: int, exact: bool
) -> bool:
    cnt = d.counter()
    if _has_honours(cnt):
        return False
    numbers = [False] * 9
    for t in NUMBERED_TILES:
        if cnt.count_tile(t) > 0:
            numbers[tile_num(t) - 1] = True
    first = -1
    last = -1
    count = 0
    for i in range(9):
        if numbers[i]:
            if first == -1:
                first = i
            last = i
            count += 1
    if exact:
        return (last - first == max_span) and (count == max_span + 1)
    return (last - first <= max_span) and (count <= max_span + 1)


def _c_two_consecutive_numbers(d: QingqueDecomposition) -> bool:
    return _consecutive_numbers_check(d, 1, exact=True)


def _c_three_consecutive_numbers(d: QingqueDecomposition) -> bool:
    return _consecutive_numbers_check(d, 2, exact=False)


def _c_four_consecutive_numbers(d: QingqueDecomposition) -> bool:
    return _consecutive_numbers_check(d, 3, exact=False)


def _check_connected_table(num_table: int) -> bool:
    state = 0  # 0=not started, 1=in sequence, 2=ended
    while num_table > 0:
        c = num_table & 0b111
        if c > 1:
            return False
        if state == 2 and c == 1:
            return False
        if state == 1 and c == 0:
            state = 2
        if state == 0 and c == 1:
            state = 1
        num_table >>= 3
    return True


def _c_connected_numbers(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    if _has_honours(cnt):
        return False

    if d.is_seven_pairs:
        num_table = 0
        for t in NUMBERED_TILES:
            c = cnt.count_tile(t) // 2
            num_table += c << (3 * tile_num(t))
        return _check_connected_table(num_table)

    if tile_is_honor(d.pair):
        return False
    num_table = 1 << (3 * tile_num(d.pair))
    for m in d.melds:
        n = tile_num(m.tile)
        if m.meld_type == QingqueMeldType.SEQUENCE:
            num_table += 1 << (3 * (n - 1))
            num_table += 1 << (3 * n)
            num_table += 1 << (3 * (n + 1))
        else:
            num_table += 1 << (3 * n)
    return _check_connected_table(num_table)


def _c_gapped_numbers(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    num_table = 0
    for t in ALL_TILES:
        if cnt.count_tile(t) > 0:
            n = tile_num(t)
            if 1 <= n <= 9:
                num_table |= 1 << n
            else:
                return False

    if num_table.bit_count() <= 2:
        return False

    # Pattern 1: alternating (1,3,5,7,9 or 2,4,6,8)
    for mask in (0b1010101010, 0b10101010100):
        if (num_table | mask) == mask:
            return True

    # Pattern 2: every third (1,4,7 or 2,5,8 or 3,6,9)
    for mask in (0b10010010, 0b100100100, 0b1001001000):
        if (num_table | mask) == mask:
            return True

    return False


def _c_reflected_hand(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    if _has_honours(cnt):
        return False

    if d.is_seven_pairs:
        min_num = 10
        max_num = 0
        for t in NUMBERED_TILES:
            if cnt.count_tile(t) > 0:
                n = tile_num(t)
                if n < min_num:
                    min_num = n
                if n > max_num:
                    max_num = n
        if min_num == 10:
            return False
        ref_sum = min_num + max_num
        for t in NUMBERED_TILES:
            c = cnt.count_tile(t)
            if c == 0:
                continue
            ref_num = ref_sum - tile_num(t)
            if ref_num < 1 or ref_num > 9:
                return False
            if cnt.count_tile(make_tile(tile_suit_type(t), ref_num)) != c:
                return False
        return True

    pair_num = tile_num(d.pair)
    ref_sum = 2 * pair_num
    pair_ref = ref_sum - pair_num
    if pair_ref < 1 or pair_ref > 9 or pair_num != pair_ref:
        return False

    melds = d.melds
    mcount = len(melds)
    paired = [False] * mcount
    for i in range(mcount):
        if paired[i]:
            continue
        mi = melds[i]
        mi_is_tk = mi.meld_type != QingqueMeldType.SEQUENCE
        mi_suit = tile_suit_type(mi.tile)
        mi_num = tile_num(mi.tile)
        found = False
        for j in range(mcount):
            if paired[j]:
                continue
            mj = melds[j]
            if mi_is_tk != (mj.meld_type != QingqueMeldType.SEQUENCE):
                continue
            if mi_suit != tile_suit_type(mj.tile):
                continue
            ref_num = ref_sum - tile_num(mj.tile)
            if 1 <= ref_num <= 9 and mi_num == ref_num:
                paired[i] = True
                paired[j] = True
                found = True
                break
        if not found:
            return False
    return True


def _c_reflected_hand_2(d: QingqueDecomposition) -> bool:
    cnt = d.counter()
    if _has_honours(cnt):
        return False

    if d.is_seven_pairs:
        min_num = 10
        max_num = 0
        for t in NUMBERED_TILES:
            if cnt.count_tile(t) > 0:
                n = tile_num(t)
                if n < min_num:
                    min_num = n
                if n > max_num:
                    max_num = n
        if min_num == 10:
            return False
        ref_sum = min_num + max_num

        m_count = _count_suit(cnt, SuitType.M)
        p_count = _count_suit(cnt, SuitType.P)
        s_count = _count_suit(cnt, SuitType.S)
        if m_count != p_count and m_count != s_count and p_count != s_count:
            return False

        ref_suit = SuitType.Z
        if m_count == p_count:
            ref_suit = SuitType.S
        if m_count == s_count:
            ref_suit = SuitType.P
        if p_count == s_count:
            ref_suit = SuitType.M

        for t in NUMBERED_TILES:
            c = cnt.count_tile(t)
            if c == 0:
                continue
            ref_num = ref_sum - tile_num(t)
            if ref_num < 1 or ref_num > 9:
                return False
            ref_suit2 = _reflect_suit(tile_suit_type(t), ref_suit)
            if cnt.count_tile(make_tile(ref_suit2, ref_num)) != c:
                return False
        return True

    pair_num = tile_num(d.pair)
    ref_sum = 2 * pair_num
    ref_suit = tile_suit_type(d.pair)
    pair_ref = ref_sum - pair_num
    if pair_ref < 1 or pair_ref > 9 or pair_num != pair_ref:
        return False

    melds = d.melds
    mcount = len(melds)
    paired = [False] * mcount
    for i in range(mcount):
        if paired[i]:
            continue
        mi = melds[i]
        mi_is_tk = mi.meld_type != QingqueMeldType.SEQUENCE
        mi_suit = tile_suit_type(mi.tile)
        mi_num = tile_num(mi.tile)
        found = False
        for j in range(mcount):
            if paired[j]:
                continue
            mj = melds[j]
            if mi_is_tk != (mj.meld_type != QingqueMeldType.SEQUENCE):
                continue
            ref_num = ref_sum - tile_num(mj.tile)
            if ref_num < 1 or ref_num > 9:
                continue
            if mi_num == ref_num and mi_suit == _reflect_suit(
                tile_suit_type(mj.tile), ref_suit
            ):
                paired[i] = True
                paired[j] = True
                found = True
                break
        if not found:
            return False
    return True


def _c_common_number(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    if _has_honours(d.counter()):
        return False
    num = tile_num(d.pair)
    if num == 0:
        return False
    for m in d.melds:
        if not any(tile_num(t) == num for t in m.get_tiles()):
            return False
    return True


def _c_quadruple_sequence(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    if len(melds) < 4:
        return False
    m0 = melds[0]
    for i in range(1, len(melds)):
        if melds[i].meld_type != m0.meld_type or melds[i].tile != m0.tile:
            return False
    return True


def _c_triple_sequence(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return _count_equivalent_pairs(d.melds) >= 3


def _c_two_double_sequences(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    c = _count_equivalent_pairs(d.melds)
    return c == 2 or c > 3


def _c_double_sequence(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    return _count_equivalent_pairs(d.melds) >= 1


def _c_four_shifted_triplets(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    for suit in _NUMBERED_SUITS:
        for num in range(1, 7):
            if _contains_triplets(melds, suit, (num, num + 1, num + 2, num + 3)):
                return True
    return False


def _c_three_shifted_triplets(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    for suit in _NUMBERED_SUITS:
        for num in range(1, 8):
            if _contains_triplets(melds, suit, (num, num + 1, num + 2)):
                return True
    return False


def _c_four_shifted_sequences(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    for suit in _NUMBERED_SUITS:
        for num in range(2, 6):
            if _contains_sequences(melds, suit, (num, num + 1, num + 2, num + 3)):
                return True
    return False


def _c_three_shifted_sequences(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    for suit in _NUMBERED_SUITS:
        for num in range(2, 7):
            if _contains_sequences(melds, suit, (num, num + 1, num + 2)):
                return True
    return False


def _c_four_chained_sequences(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    for suit in _NUMBERED_SUITS:
        if _contains_sequences(melds, suit, (2, 4, 6, 8)):
            return True
    return False


def _c_three_chained_sequences(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    for suit in _NUMBERED_SUITS:
        for start in (2, 3, 4):
            if _contains_sequences(melds, suit, (start, start + 2, start + 4)):
                return True
    return False


def _c_pure_straight(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    for suit in _NUMBERED_SUITS:
        if _contains_sequences(melds, suit, (2, 5, 8)):
            return True
    return False


def _c_seven_shifted_pairs(d: QingqueDecomposition) -> bool:
    return _has_shifted_pairs(d, 7)


def _c_six_shifted_pairs(d: QingqueDecomposition) -> bool:
    return _has_shifted_pairs(d, 6)


def _c_five_shifted_pairs(d: QingqueDecomposition) -> bool:
    return _has_shifted_pairs(d, 5)


def _c_four_shifted_pairs(d: QingqueDecomposition) -> bool:
    return _has_shifted_pairs(d, 4)


def _c_mixed_triple_triplet(d: QingqueDecomposition) -> bool:
    melds = d.melds
    count = 0
    n = len(melds)
    for i in range(n):
        mi = melds[i]
        if mi.meld_type == QingqueMeldType.SEQUENCE:
            continue
        si = tile_suit_type(mi.tile)
        ni = tile_num(mi.tile)
        for j in range(i + 1, n):
            mj = melds[j]
            if mj.meld_type == QingqueMeldType.SEQUENCE:
                continue
            if si != tile_suit_type(mj.tile) and ni == tile_num(mj.tile):
                count += 1
    return count == 3


def _c_mixed_triple_sequence(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    for num in range(2, 9):
        has_m = has_p = has_s = False
        for m in melds:
            if m.meld_type == QingqueMeldType.SEQUENCE and tile_num(m.tile) == num:
                st = tile_suit_type(m.tile)
                if st == SuitType.M:
                    has_m = True
                elif st == SuitType.P:
                    has_p = True
                elif st == SuitType.S:
                    has_s = True
        if has_m and has_p and has_s:
            return True
    return False


def _c_two_triple_pairs(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    for i in range(1, 9):
        i_all = (
            cnt.count_tile(make_tile(SuitType.M, i)) >= 2
            and cnt.count_tile(make_tile(SuitType.P, i)) >= 2
            and cnt.count_tile(make_tile(SuitType.S, i)) >= 2
        )
        if not i_all:
            continue
        for j in range(i + 1, 10):
            if (
                cnt.count_tile(make_tile(SuitType.M, j)) >= 2
                and cnt.count_tile(make_tile(SuitType.P, j)) >= 2
                and cnt.count_tile(make_tile(SuitType.S, j)) >= 2
            ):
                return True

    for i in range(1, 10):
        pair_count = 0
        for suit in _NUMBERED_SUITS:
            c = cnt.count_tile(make_tile(suit, i))
            if c == 2:
                pair_count += 1
            if c == 4:
                pair_count += 2
        if pair_count == 6:
            return True
    return False


def _c_mixed_triple_pair(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    for i in range(1, 10):
        if (
            cnt.count_tile(make_tile(SuitType.M, i)) >= 2
            and cnt.count_tile(make_tile(SuitType.P, i)) >= 2
            and cnt.count_tile(make_tile(SuitType.S, i)) >= 2
        ):
            return True
    return False


def _c_mixed_shifted_triplets(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    triplet_tiles = {
        m.tile for m in d.melds if m.meld_type != QingqueMeldType.SEQUENCE
    }
    if len(triplet_tiles) < 3:
        return False
    for t1, t2, t3 in _MIXED_SHIFTED_TRIPLETS:
        if t1 in triplet_tiles and t2 in triplet_tiles and t3 in triplet_tiles:
            return True
    return False


def _c_mixed_straight(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    seq_tiles = {
        m.tile for m in d.melds if m.meld_type == QingqueMeldType.SEQUENCE
    }
    if len(seq_tiles) < 3:
        return False
    for t1, t2, t3 in _MIXED_STRAIGHT:
        if t1 in seq_tiles and t2 in seq_tiles and t3 in seq_tiles:
            return True
    return False


def _c_mirrored_hand(d: QingqueDecomposition) -> bool:
    if d.is_seven_pairs:
        return False
    melds = d.melds
    suit_dist = 0
    for m in melds:
        st = tile_suit_type(m.tile)
        if st != SuitType.Z:
            suit_dist |= 1 << int(st)
    if suit_dist.bit_count() != 2:
        return False

    match_count = 0
    visited = 0
    n = len(melds)
    for i in range(n):
        mi = melds[i]
        si = tile_suit_type(mi.tile)
        if si == SuitType.Z:
            continue
        mi_seq = mi.meld_type == QingqueMeldType.SEQUENCE
        ni = tile_num(mi.tile)
        for j in range(i + 1, n):
            mj = melds[j]
            sj = tile_suit_type(mj.tile)
            if sj == SuitType.Z or si == sj:
                continue
            if mi_seq == (mj.meld_type == QingqueMeldType.SEQUENCE) and ni == tile_num(
                mj.tile
            ):
                mask = (1 << i) | (1 << j)
                if (visited & mask) == 0:
                    match_count += 1
                    visited |= mask
    return match_count == 2


def _c_three_mirrored_pairs(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    suit_pairs = (
        (SuitType.M, SuitType.P),
        (SuitType.S, SuitType.P),
        (SuitType.M, SuitType.S),
    )

    for i in range(1, 8):
        for j in range(i + 1, 9):
            for k in range(j + 1, 10):
                for s1, s2 in suit_pairs:
                    if (
                        cnt.count_tile(make_tile(s1, i)) >= 2
                        and cnt.count_tile(make_tile(s1, j)) >= 2
                        and cnt.count_tile(make_tile(s1, k)) >= 2
                        and cnt.count_tile(make_tile(s2, i)) >= 2
                        and cnt.count_tile(make_tile(s2, j)) >= 2
                        and cnt.count_tile(make_tile(s2, k)) >= 2
                    ):
                        return True

    for i in range(1, 10):
        for j in range(1, 10):
            if i == j:
                continue
            for s1, s2 in suit_pairs:
                if (
                    cnt.count_tile(make_tile(s1, i)) >= 2
                    and cnt.count_tile(make_tile(s2, i)) >= 2
                ):
                    c1 = cnt.count_tile(make_tile(s1, j))
                    c2 = cnt.count_tile(make_tile(s2, j))
                    pair_count = 0
                    if c1 == 2:
                        pair_count += 1
                    if c1 == 4:
                        pair_count += 2
                    if c2 == 2:
                        pair_count += 1
                    if c2 == 4:
                        pair_count += 2
                    if pair_count == 4:
                        return True
    return False


def _c_two_mirrored_pairs(d: QingqueDecomposition) -> bool:
    if not d.is_seven_pairs:
        return False
    cnt = d.counter()
    suit_pairs = (
        (SuitType.M, SuitType.P),
        (SuitType.S, SuitType.P),
        (SuitType.M, SuitType.S),
    )

    for i in range(1, 9):
        for j in range(i + 1, 10):
            for s1, s2 in suit_pairs:
                if (
                    cnt.count_tile(make_tile(s1, i)) >= 2
                    and cnt.count_tile(make_tile(s1, j)) >= 2
                    and cnt.count_tile(make_tile(s2, i)) >= 2
                    and cnt.count_tile(make_tile(s2, j)) >= 2
                ):
                    return True

    for i in range(1, 10):
        for s1, s2 in suit_pairs:
            c1 = cnt.count_tile(make_tile(s1, i))
            c2 = cnt.count_tile(make_tile(s2, i))
            pair_count = 0
            if c1 == 2:
                pair_count += 1
            if c1 == 4:
                pair_count += 2
            if c2 == 2:
                pair_count += 1
            if c2 == 4:
                pair_count += 2
            if pair_count == 4:
                return True
    return False


def _c_two_short_straights(d: QingqueDecomposition) -> bool:
    melds = d.melds
    count = 0
    visited = 0
    n = len(melds)
    for i in range(n):
        mi = melds[i]
        if mi.meld_type != QingqueMeldType.SEQUENCE:
            continue
        si = tile_suit_type(mi.tile)
        ni = tile_num(mi.tile)
        for j in range(i + 1, n):
            mj = melds[j]
            if mj.meld_type != QingqueMeldType.SEQUENCE:
                continue
            if si == tile_suit_type(mj.tile) and abs(ni - tile_num(mj.tile)) == 3:
                mask = (1 << i) + (1 << j)
                if (visited & mask) == 0:
                    count += 1
                    visited |= mask
    return count == 2


# Ordered table matching QingqueFanEvaluator.cs registration order (0..89)
_CRITERIA: Tuple[Tuple[QingqueFan, Callable[[QingqueDecomposition], bool]], ...] = (
    (QingqueFan.Trivial, _c_trivial),
    (QingqueFan.HeavenlyHand, _c_heavenly_hand),
    (QingqueFan.EarthlyHand, _c_earthly_hand),
    (QingqueFan.OutWithReplacementTile, _c_out_with_replacement_tile),
    (QingqueFan.LastTileDraw, _c_last_tile_draw),
    (QingqueFan.LastTileClaim, _c_last_tile_claim),
    (QingqueFan.RobbingTheKong, _c_robbing_the_kong),
    (QingqueFan.ThirteenOrphans, _c_thirteen_orphans),
    (QingqueFan.SevenPairs, _c_seven_pairs),
    (QingqueFan.ConcealedHand, _c_concealed_hand),
    (QingqueFan.FourConcealedKongs, _c_four_concealed_kongs),
    (QingqueFan.ThreeConcealedKongs, _c_three_concealed_kongs),
    (QingqueFan.TwoConcealedKongs, _c_two_concealed_kongs),
    (QingqueFan.ConcealedKong, _c_concealed_kong),
    (QingqueFan.FourKongs, _c_four_kongs),
    (QingqueFan.ThreeKongs, _c_three_kongs),
    (QingqueFan.TwoKongs, _c_two_kongs),
    (QingqueFan.FourConcealedTriplets, _c_four_concealed_triplets),
    (QingqueFan.ThreeConcealedTriplets, _c_three_concealed_triplets),
    (QingqueFan.AllTriplets, _c_all_triplets),
    (QingqueFan.TwelveHog, _c_twelve_hog),
    (QingqueFan.EightHog, _c_eight_hog),
    (QingqueFan.ThreeDoublePairs, _c_three_double_pairs),
    (QingqueFan.TwoDoublePairs, _c_two_double_pairs),
    (QingqueFan.DoublePair, _c_double_pair),
    (QingqueFan.AllHonours, _c_all_honours),
    (QingqueFan.BigFourWinds, _c_big_four_winds),
    (QingqueFan.LittleFourWinds, _c_little_four_winds),
    (QingqueFan.FourWindPairs, _c_four_wind_pairs),
    (QingqueFan.ThreeWindTriplets, _c_three_wind_triplets),
    (QingqueFan.SevenWindPairs, _c_seven_wind_pairs),
    (QingqueFan.SixWindPairs, _c_six_wind_pairs),
    (QingqueFan.BigThreeDragons, _c_big_three_dragons),
    (QingqueFan.LittleThreeDragons, _c_little_three_dragons),
    (QingqueFan.SixDragonPairs, _c_six_dragon_pairs),
    (QingqueFan.ThreeDragonPairs, _c_three_dragon_pairs),
    (QingqueFan.FanTile4T, _c_fan_tile_4t),
    (QingqueFan.FanTile3T, _c_fan_tile_3t),
    (QingqueFan.FanTile2T, _c_fan_tile_2t),
    (QingqueFan.FanTile1T, _c_fan_tile_1t),
    (QingqueFan.FanTile7P, _c_fan_tile_7p),
    (QingqueFan.FanTile6P, _c_fan_tile_6p),
    (QingqueFan.FanTile5P, _c_fan_tile_5p),
    (QingqueFan.FanTile4P, _c_fan_tile_4p),
    (QingqueFan.FanTile3P, _c_fan_tile_3p),
    (QingqueFan.FanTile2P, _c_fan_tile_2p),
    (QingqueFan.FanTile1P, check_fan_tile_1p),
    (QingqueFan.AllTerminals, _c_all_terminals),
    (QingqueFan.AllTerminalsAndHonours, _c_all_terminals_and_honours),
    (QingqueFan.PureOutsideHand, _c_pure_outside_hand),
    (QingqueFan.MixedOutsideHand, _c_mixed_outside_hand),
    (QingqueFan.NineGates, _c_nine_gates),
    (QingqueFan.FullFlush, _c_full_flush),
    (QingqueFan.HalfFlush, _c_half_flush),
    (QingqueFan.AllTypes, _c_all_types),
    (QingqueFan.MixedOneNumber, _c_mixed_one_number),
    (QingqueFan.TwoNumbers, _c_two_numbers),
    (QingqueFan.TwoConsecutiveNumbers, _c_two_consecutive_numbers),
    (QingqueFan.ThreeConsecutiveNumbers, _c_three_consecutive_numbers),
    (QingqueFan.FourConsecutiveNumbers, _c_four_consecutive_numbers),
    (QingqueFan.ConnectedNumbers, _c_connected_numbers),
    (QingqueFan.GappedNumbers, _c_gapped_numbers),
    (QingqueFan.ReflectedHand, _c_reflected_hand),
    (QingqueFan.ReflectedHand2, _c_reflected_hand_2),
    (QingqueFan.CommonNumber, _c_common_number),
    (QingqueFan.QuadrupleSequence, _c_quadruple_sequence),
    (QingqueFan.TripleSequence, _c_triple_sequence),
    (QingqueFan.TwoDoubleSequences, _c_two_double_sequences),
    (QingqueFan.DoubleSequence, _c_double_sequence),
    (QingqueFan.FourShiftedTriplets, _c_four_shifted_triplets),
    (QingqueFan.ThreeShiftedTriplets, _c_three_shifted_triplets),
    (QingqueFan.FourShiftedSequences, _c_four_shifted_sequences),
    (QingqueFan.ThreeShiftedSequences, _c_three_shifted_sequences),
    (QingqueFan.FourChainedSequences, _c_four_chained_sequences),
    (QingqueFan.ThreeChainedSequences, _c_three_chained_sequences),
    (QingqueFan.PureStraight, _c_pure_straight),
    (QingqueFan.SevenShiftedPairs, _c_seven_shifted_pairs),
    (QingqueFan.SixShiftedPairs, _c_six_shifted_pairs),
    (QingqueFan.FiveShiftedPairs, _c_five_shifted_pairs),
    (QingqueFan.FourShiftedPairs, _c_four_shifted_pairs),
    (QingqueFan.MixedTripleTriplet, _c_mixed_triple_triplet),
    (QingqueFan.MixedTripleSequence, _c_mixed_triple_sequence),
    (QingqueFan.TwoTriplePairs, _c_two_triple_pairs),
    (QingqueFan.MixedTriplePair, _c_mixed_triple_pair),
    (QingqueFan.MixedShiftedTriplets, _c_mixed_shifted_triplets),
    (QingqueFan.MixedStraight, _c_mixed_straight),
    (QingqueFan.MirroredHand, _c_mirrored_hand),
    (QingqueFan.ThreeMirroredPairs, _c_three_mirrored_pairs),
    (QingqueFan.TwoMirroredPairs, _c_two_mirrored_pairs),
    (QingqueFan.TwoShortStraights, _c_two_short_straights),
)


def evaluate_fans(
    hand: QingqueHand, ignore_occasional: bool = False
) -> List[List[QingqueFan]]:
    """Evaluate all criteria for every decomposition of a hand."""
    results: List[List[QingqueFan]] = []
    for decomp in hand.decompositions:
        fans: List[QingqueFan] = []
        for fan, check_fn in _CRITERIA:
            if ignore_occasional and fan in OCCASIONAL_FAN_VALUES:
                continue
            if check_fn(decomp):
                fans.append(fan)
        results.append(fans)
    return results
