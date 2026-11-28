"""Core data types and hand decomposition for Qingque 13-tile mahjong.

Pure Python, stateless port of Qingque13.Core (QingqueTile, QingqueMeld,
QingqueWinType, QingqueTileCounter, QingqueHand, QingqueDecomposition).
"""

from __future__ import annotations

from collections import deque
from enum import IntEnum
from typing import Iterable, List, Optional, Sequence, Tuple


class QingqueSuit(IntEnum):
    M = 0b01000000  # Characters (万)
    P = 0b01100000  # Dots (饼)
    S = 0b11000000  # Bamboo (条)
    Z = 0b10100000  # Winds (风)
    D = 0b10110000  # Dragons (箭)


class SuitType(IntEnum):
    M = 0  # Characters (万)
    P = 1  # Dots (饼)
    S = 2  # Bamboo (条)
    Z = 3  # Honors (字)


# Honor tile byte constants
HONOUR_E = 0b10100001  # East
HONOUR_S = 0b10100010  # South
HONOUR_W = 0b10100011  # West
HONOUR_N = 0b10100100  # North
HONOUR_C = 0b10110101  # Red (中)
HONOUR_F = 0b10110110  # Green (发)
HONOUR_P = 0b10110111  # White (白)

_HONOURS_BY_NUM: Tuple[int, ...] = (
    0,
    HONOUR_E,
    HONOUR_S,
    HONOUR_W,
    HONOUR_N,
    HONOUR_C,
    HONOUR_F,
    HONOUR_P,
)

# Precomputed byte values for all tile groups
CHARACTER_TILES: Tuple[int, ...] = tuple(QingqueSuit.M + n for n in range(1, 10))
DOT_TILES: Tuple[int, ...] = tuple(QingqueSuit.P + n for n in range(1, 10))
BAMBOO_TILES: Tuple[int, ...] = tuple(QingqueSuit.S + n for n in range(1, 10))
WIND_TILES: Tuple[int, ...] = (HONOUR_E, HONOUR_S, HONOUR_W, HONOUR_N)
DRAGON_TILES: Tuple[int, ...] = (HONOUR_C, HONOUR_F, HONOUR_P)
HONOUR_TILES: Tuple[int, ...] = WIND_TILES + DRAGON_TILES
NUMBERED_TILES: Tuple[int, ...] = CHARACTER_TILES + DOT_TILES + BAMBOO_TILES
ALL_TILES: Tuple[int, ...] = NUMBERED_TILES + HONOUR_TILES
TERMINAL_TILES: Tuple[int, ...] = (
    QingqueSuit.M + 1,
    QingqueSuit.M + 9,
    QingqueSuit.P + 1,
    QingqueSuit.P + 9,
    QingqueSuit.S + 1,
    QingqueSuit.S + 9,
)
TERMINAL_HONOUR_TILES: Tuple[int, ...] = TERMINAL_TILES + HONOUR_TILES


def make_tile(suit: SuitType, num: int) -> int:
    """Create a tile byte from SuitType and 1-based number."""
    if suit == SuitType.M:
        return QingqueSuit.M + num
    if suit == SuitType.P:
        return QingqueSuit.P + num
    if suit == SuitType.S:
        return QingqueSuit.S + num
    if suit == SuitType.Z:
        return _HONOURS_BY_NUM[num] if 1 <= num <= 7 else 0
    return 0


def tile_num(val: int) -> int:
    """Number of tile (1-9 for numbered tiles, 0 for honors/invalid)."""
    return (val & 0b00001111) if (val & 0b01000000) != 0 else 0


def tile_suit(val: int, distinct_dragons: bool = False) -> int:
    mask = 0b11110000 if distinct_dragons else 0b11100000
    return val & mask


def tile_suit_type(val: int) -> SuitType:
    s = val & 0b11100000
    if s == QingqueSuit.M:
        return SuitType.M
    if s == QingqueSuit.P:
        return SuitType.P
    if s == QingqueSuit.S:
        return SuitType.S
    return SuitType.Z


def tile_is_honor(val: int) -> bool:
    return (val & 0b01000000) == 0 and val != 0


def tile_is_numbered(val: int) -> bool:
    return (val & 0b01000000) != 0


def tile_is_terminal(val: int) -> bool:
    if (val & 0b01000000) == 0:
        return False
    n = val & 0b00001111
    return n == 1 or n == 9


def tile_is_wind(val: int) -> bool:
    return HONOUR_E <= val <= HONOUR_N


def tile_is_dragon(val: int) -> bool:
    return HONOUR_C <= val <= HONOUR_P


def tile_to_str(val: int) -> str:
    if not tile_is_numbered(val):
        return "_ESWNCFP"[val & 0b00000111]
    suit_char = "__mp__s"[tile_suit(val) >> 5]
    return f"{tile_num(val)}{suit_char}"


class QingqueMeldType(IntEnum):
    SEQUENCE = 0b0000000000  # Chi (顺子)
    TRIPLET = 0b0100000000   # Pon/Pung (刻子)
    KONG = 0b1100000000      # Kong/Kan (杠)


class QingqueMeld:
    """Represents a meld (sequence, triplet, or kong), matching C# QingqueMeld."""

    __slots__ = ("value", "tile", "meld_type", "concealed", "fixed")

    def __init__(
        self,
        tile: int,
        meld_type: QingqueMeldType,
        concealed: bool = True,
        fixed: bool = True,
    ) -> None:
        tile_val = tile & 0xFF
        concealed_bit = (1 << 10) if concealed else 0
        fixed_bit = (1 << 11) if fixed else 0
        self.value: int = tile_val | int(meld_type) | concealed_bit | fixed_bit
        self.tile: int = tile_val
        self.meld_type: QingqueMeldType = meld_type
        self.concealed: bool = concealed
        self.fixed: bool = fixed

    @staticmethod
    def sequence(tile: int, concealed: bool = False, fixed: bool = False) -> QingqueMeld:
        return QingqueMeld(tile, QingqueMeldType.SEQUENCE, concealed, fixed)

    @staticmethod
    def triplet(tile: int, concealed: bool = True, fixed: bool = False) -> QingqueMeld:
        return QingqueMeld(tile, QingqueMeldType.TRIPLET, concealed, fixed)

    @staticmethod
    def kong(tile: int, concealed: bool = True, fixed: bool = False) -> QingqueMeld:
        return QingqueMeld(tile, QingqueMeldType.KONG, concealed, fixed)

    def get_tiles(self) -> Tuple[int, ...]:
        if self.meld_type == QingqueMeldType.SEQUENCE:
            return ((self.tile - 1) & 0xFF, self.tile, (self.tile + 1) & 0xFF)
        if self.meld_type == QingqueMeldType.TRIPLET:
            return (self.tile, self.tile, self.tile)
        if self.meld_type == QingqueMeldType.KONG:
            return (self.tile, self.tile, self.tile, self.tile)
        return ()

    def __repr__(self) -> str:
        if self.meld_type == QingqueMeldType.SEQUENCE:
            n = tile_num(self.tile)
            suit = "__mp__s"[tile_suit(self.tile) >> 5]
            return f"{n - 1}{n}{n + 1}{suit}"
        if self.meld_type == QingqueMeldType.TRIPLET:
            if tile_is_honor(self.tile):
                s = tile_to_str(self.tile)
                return s * 3
            s = tile_to_str(self.tile)
            return f"{tile_num(self.tile)}" * 3 + s[1]
        if self.meld_type == QingqueMeldType.KONG:
            if tile_is_honor(self.tile):
                s = tile_to_str(self.tile)
                return s * 4
            s = tile_to_str(self.tile)
            return f"{tile_num(self.tile)}" * 4 + s[1]
        return "Invalid"


class QingqueWinType:
    """Represents win conditions and winds, matching C# QingqueWinType."""

    __slots__ = ("value",)

    SELF_DRAWN = 0b00010000
    FINAL_TILE = 0b00100000
    KONG_RELATED = 0b01000000
    HEAVENLY_OR_EARTHLY_HAND = 0b10000000

    SEAT_WIND_FILTER = 0b00000011
    PREVALENT_WIND_FILTER = 0b00001100

    def __init__(
        self,
        self_drawn: bool = False,
        final_tile: bool = False,
        kong_related: bool = False,
        heavenly_or_earthly_hand: bool = False,
        seat_wind: int = 0,
        prevalent_wind: int = 0,
        upper_bits: int = 0,
    ) -> None:
        val = 0
        if self_drawn:
            val |= self.SELF_DRAWN
        if final_tile:
            val |= self.FINAL_TILE
        if kong_related:
            val |= self.KONG_RELATED
        if heavenly_or_earthly_hand:
            val |= self.HEAVENLY_OR_EARTHLY_HAND

        val |= (upper_bits & 0xFF) << 8

        sw = seat_wind if seat_wind != 0 else HONOUR_E
        if sw == HONOUR_S:
            val |= 0b00000001
        elif sw == HONOUR_W:
            val |= 0b00000010
        elif sw == HONOUR_N:
            val |= 0b00000011

        pw = prevalent_wind if prevalent_wind != 0 else HONOUR_E
        if pw == HONOUR_S:
            val |= 0b00000100
        elif pw == HONOUR_W:
            val |= 0b00001000
        elif pw == HONOUR_N:
            val |= 0b00001100

        self.value: int = val

    def has_flags(self, required_flags: int, excluded_flags: int = 0) -> bool:
        return (self.value & required_flags) == required_flags and (
            self.value & excluded_flags
        ) == 0

    def seat_wind(self) -> int:
        return WIND_TILES[self.value & self.SEAT_WIND_FILTER]

    def prevalent_wind(self) -> int:
        return WIND_TILES[(self.value & self.PREVALENT_WIND_FILTER) >> 2]

    @property
    def is_self_drawn(self) -> bool:
        return (self.value & self.SELF_DRAWN) != 0

    @property
    def is_final_tile(self) -> bool:
        return (self.value & self.FINAL_TILE) != 0

    @property
    def is_kong_related(self) -> bool:
        return (self.value & self.KONG_RELATED) != 0

    @property
    def is_heavenly_or_earthly(self) -> bool:
        return (self.value & self.HEAVENLY_OR_EARTHLY_HAND) != 0

    def __repr__(self) -> str:
        parts: List[str] = []
        if self.is_self_drawn:
            parts.append("SelfDrawn")
        if self.is_final_tile:
            parts.append("FinalTile")
        if self.is_kong_related:
            parts.append("KongRelated")
        if self.is_heavenly_or_earthly:
            parts.append("HeavenlyOrEarthly")
        parts.append(f"SeatWind:{tile_to_str(self.seat_wind())}")
        parts.append(f"PrevalentWind:{tile_to_str(self.prevalent_wind())}")
        return "|".join(parts)


class QingqueTileCounter:
    """Bit-packed tile counter matching C# QingqueTileCounter (3 bits per tile)."""

    __slots__ = ("mp_count", "sz_count")

    def __init__(self, mp_count: int = 0, sz_count: int = 0) -> None:
        self.mp_count: int = mp_count
        self.sz_count: int = sz_count

    @classmethod
    def from_tiles(
        cls,
        tiles: Iterable[int],
        melds: Iterable[QingqueMeld] = (),
    ) -> QingqueTileCounter:
        c = cls()
        for t in tiles:
            c.add_tile(t)
        for m in melds:
            c.add_meld(m)
        return c

    def copy(self) -> QingqueTileCounter:
        return QingqueTileCounter(self.mp_count, self.sz_count)

    def add_tile(self, tile: int, count: int = 1) -> None:
        if tile == 0:
            return
        shift = (tile & 0b00100000) + 3 * (tile & 0b00001111)
        if (tile & 0b10000000) != 0:
            self.sz_count = (self.sz_count + (count << shift)) & 0xFFFFFFFFFFFFFFFF
        else:
            self.mp_count = (self.mp_count + (count << shift)) & 0xFFFFFFFFFFFFFFFF

    def add_meld(self, meld: QingqueMeld, count: int = 1) -> None:
        if meld.meld_type == QingqueMeldType.SEQUENCE:
            self.add_tile((meld.tile - 1) & 0xFF, count)
            self.add_tile(meld.tile, count)
            self.add_tile((meld.tile + 1) & 0xFF, count)
        elif meld.meld_type == QingqueMeldType.TRIPLET:
            self.add_tile(meld.tile, count * 3)
        elif meld.meld_type == QingqueMeldType.KONG:
            self.add_tile(meld.tile, count * 4)

    def sub_meld(self, meld: QingqueMeld) -> QingqueTileCounter:
        res = self.copy()
        res.add_meld(meld, -1)
        return res

    def count_tile(self, tile: int) -> int:
        if tile == 0:
            return 0
        shift = (tile & 0b00100000) + 3 * (tile & 0b00001111)
        if (tile & 0b10000000) != 0:
            return (self.sz_count >> shift) & 0b111
        return (self.mp_count >> shift) & 0b111

    def total_count(self) -> int:
        total = 0
        mp = self.mp_count
        sz = self.sz_count
        # Sum 3-bit slots for M (1..9), P (1..9), S (1..9), Z (1..7)
        for n in range(1, 10):
            s = 3 * n
            total += ((mp >> s) & 7) + ((mp >> (32 + s)) & 7) + ((sz >> s) & 7)
        for n in range(1, 8):
            total += (sz >> (32 + 3 * n)) & 7
        return total

    @property
    def is_empty(self) -> bool:
        return self.mp_count == 0 and self.sz_count == 0


class QingqueDecomposition:
    """Single decomposition of a hand (either 4 melds + 1 pair, or 7 pairs)."""

    __slots__ = (
        "original_hand",
        "pair",
        "melds",
        "remaining_counter",
        "is_seven_pairs",
        "seven_pair_tiles",
        "_cached_counter_open",
        "_cached_counter_closed",
    )

    def __init__(
        self,
        original_hand: QingqueHand,
        pair: int = 0,
        melds: Optional[List[QingqueMeld]] = None,
        remaining_counter: Optional[QingqueTileCounter] = None,
        is_seven_pairs: bool = False,
        seven_pair_tiles: Optional[List[int]] = None,
    ) -> None:
        self.original_hand: QingqueHand = original_hand
        self.pair: int = pair
        self.melds: List[QingqueMeld] = (
            list(melds) if melds is not None else list(original_hand.open_melds)
        )
        self.remaining_counter: QingqueTileCounter = (
            remaining_counter
            if remaining_counter is not None
            else original_hand.closed_counter.copy()
        )
        self.is_seven_pairs: bool = is_seven_pairs
        self.seven_pair_tiles: Optional[List[int]] = seven_pair_tiles

    @classmethod
    def from_seven_pairs(
        cls, hand: QingqueHand, pairs: List[int]
    ) -> QingqueDecomposition:
        return cls(
            original_hand=hand,
            pair=pairs[0] if pairs else 0,
            melds=list(hand.open_melds),
            remaining_counter=QingqueTileCounter(0, 0),
            is_seven_pairs=True,
            seven_pair_tiles=list(pairs),
        )

    def with_meld(self, meld: QingqueMeld) -> QingqueDecomposition:
        new_melds = list(self.melds)
        new_melds.append(meld)
        return QingqueDecomposition(
            original_hand=self.original_hand,
            pair=self.pair,
            melds=new_melds,
            remaining_counter=self.remaining_counter.sub_meld(meld),
            is_seven_pairs=False,
            seven_pair_tiles=None,
        )

    def with_pair(self, pair: int) -> QingqueDecomposition:
        return QingqueDecomposition(
            original_hand=self.original_hand,
            pair=pair,
            melds=list(self.melds),
            remaining_counter=QingqueTileCounter(0, 0),
            is_seven_pairs=False,
            seven_pair_tiles=None,
        )

    def counter(self, include_open_melds: bool = True) -> QingqueTileCounter:
        if include_open_melds:
            cached = getattr(self, "_cached_counter_open", None)
            if cached is not None:
                return cached
        else:
            cached = getattr(self, "_cached_counter_closed", None)
            if cached is not None:
                return cached

        if self.is_seven_pairs and self.seven_pair_tiles is not None:
            c = QingqueTileCounter(0, 0)
            for t in self.seven_pair_tiles:
                c.add_tile(t, 2)
        elif not self.remaining_counter.is_empty:
            return self.remaining_counter
        elif self.pair != 0:
            c = QingqueTileCounter(0, 0)
            c.add_tile(self.pair, 2)
            for m in self.melds:
                if include_open_melds or not m.fixed:
                    c.add_meld(m)
        else:
            c = self.original_hand.counter(include_open_melds)

        if include_open_melds:
            self._cached_counter_open = c
        else:
            self._cached_counter_closed = c
        return c

    @property
    def winning_type(self) -> QingqueWinType:
        return self.original_hand.win_type

    @property
    def winning_tile(self) -> int:
        return self.original_hand.win_tile

    def is_won_by(self, flags: int, inverse: bool = False) -> bool:
        return self.original_hand.win_type.has_flags(flags) ^ inverse

    def __repr__(self) -> str:
        parts = [repr(m) for m in self.melds]
        if self.pair != 0:
            if tile_is_honor(self.pair):
                s = tile_to_str(self.pair)
                parts.append(s * 2)
            else:
                s = tile_to_str(self.pair)
                parts.append(f"{tile_num(self.pair)}{tile_num(self.pair)}{s[1]}")
        return " ".join(parts)


class QingqueHand:
    """Complete mahjong hand with automatic decomposition, matching C# QingqueHand."""

    __slots__ = (
        "win_type",
        "win_tile",
        "open_melds",
        "closed_counter",
        "total_counter",
        "decompositions",
    )

    def __init__(
        self,
        tiles: Sequence[int],
        melds: Sequence[QingqueMeld],
        winning_tile: int,
        winning_type: Optional[QingqueWinType] = None,
        winning_tile_included: bool = False,
    ) -> None:
        self.open_melds: List[QingqueMeld] = list(melds)
        self.closed_counter: QingqueTileCounter = QingqueTileCounter.from_tiles(tiles)
        self.total_counter: QingqueTileCounter = QingqueTileCounter.from_tiles(
            tiles, melds
        )
        self.win_type: QingqueWinType = (
            winning_type if winning_type is not None else QingqueWinType()
        )
        self.win_tile: int = winning_tile

        if not winning_tile_included:
            self.closed_counter.add_tile(winning_tile)
            self.total_counter.add_tile(winning_tile)

        self.decompositions: List[QingqueDecomposition] = []
        self._decompose_init()

    def counter(self, include_open_melds: bool = True) -> QingqueTileCounter:
        return self.total_counter if include_open_melds else self.closed_counter

    def is_valid(self, check_fifth_tile: bool = True) -> bool:
        if self.closed_counter.total_count() + len(self.open_melds) * 3 != 14:
            return False

        if self.closed_counter.count_tile(self.win_tile) == 0:
            return False

        kong_count = sum(
            1 for m in self.open_melds if m.meld_type == QingqueMeldType.KONG
        )
        if self.total_counter.total_count() - kong_count != 14:
            return False

        if check_fifth_tile:
            for t in ALL_TILES:
                if self.total_counter.count_tile(t) > 4:
                    return False

        return True

    def _decompose_init(self) -> None:
        self._check_seven_pairs()

        queue: deque[Tuple[QingqueDecomposition, int]] = deque()
        queue.append((QingqueDecomposition(self), 0))

        is_self_drawn = self.win_type.is_self_drawn
        win_tile = self.win_tile
        closed_counter = self.closed_counter

        while queue:
            front, index = queue.popleft()
            cnt = front.counter(False)

            if len(front.melds) == 4:
                if cnt.total_count() == 2:
                    for t in ALL_TILES:
                        if cnt.count_tile(t) == 2:
                            self.decompositions.append(front.with_pair(t))
                            break
                continue

            triplet_start = (index + 1) // 2
            for i in range(triplet_start, 34):
                t = ALL_TILES[i]
                if cnt.count_tile(t) >= 3:
                    concealed = (
                        is_self_drawn
                        or t != win_tile
                        or closed_counter.count_tile(t) == 4
                    )
                    triplet = QingqueMeld(t, QingqueMeldType.TRIPLET, concealed, False)
                    queue.append((front.with_meld(triplet), i * 2))

            sequence_start = index // 2
            for i in range(sequence_start, 27):
                t = NUMBERED_TILES[i]
                t1 = (t - 1) & 0xFF
                t2 = (t + 1) & 0xFF
                if (
                    cnt.count_tile(t) > 0
                    and cnt.count_tile(t1) > 0
                    and cnt.count_tile(t2) > 0
                ):
                    seq = QingqueMeld(t, QingqueMeldType.SEQUENCE, False, False)
                    queue.append((front.with_meld(seq), i * 2 + 1))

    def _check_seven_pairs(self) -> None:
        if self.open_melds:
            return
        if self.closed_counter.total_count() != 14:
            return

        pairs: List[int] = []
        for t in ALL_TILES:
            c = self.closed_counter.count_tile(t)
            if (c & 1) != 0:
                return
            if c == 2:
                pairs.append(t)
            elif c == 4:
                pairs.append(t)
                pairs.append(t)

        if len(pairs) == 7:
            self.decompositions.append(QingqueDecomposition.from_seven_pairs(self, pairs))
