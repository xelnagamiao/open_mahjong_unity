from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from functools import lru_cache
from itertools import combinations

SUB_RULE = "hongque/w-v1.0"
FACES = tuple(f"{colour}X{rank}" for colour in "ABCDEFGH" for rank in range(1, 10))
FACE_SET = frozenset(FACES)
PRIORITY = {f"{kind}_{seat}": base - offset
            for kind, base in (("double", 14), ("peng", 11), ("hong", 8), ("chi", 5))
            for offset, seat in enumerate(("first", "second", "third"))}
PRIORITY.update({f"hu_{seat}": 15 for seat in ("first", "second", "third")})


def is_w(state) -> bool:
    return getattr(state, "sub_rule", "") == SUB_RULE


def normalize(code: str) -> str:
    code = str(code or "").strip().upper()
    if code not in FACE_SET:
        raise ValueError("无效的虹雀 W 牌张")
    return code


def full_deck(include_colourless=True) -> list[str]:
    return [face for face in FACES if include_colourless or face[0] != "H" for _ in range(2)]


@dataclass(frozen=True)
class Shape:
    kind: str
    base_kind: str
    tiles: tuple[str, ...]
    number_step: int = 0
    is_rainbow: bool = False
    is_double: bool = False


@lru_cache(maxsize=32768)
def _classify(tiles: tuple[str, ...]) -> Shape | None:
    if not tiles or any(tile not in FACE_SET for tile in tiles):
        return None
    counts = Counter(tiles)
    if len(tiles) == 2 and len(counts) == 1:
        return Shape("pair", "pair", tiles)
    multiplicities = set(counts.values())
    if multiplicities not in ({1}, {2}) or len(counts) < 3:
        return None
    double = multiplicities == {2}
    colours = {tile[0] for tile in counts}
    numbers = sorted(int(tile[2]) for tile in counts)
    rainbow = colours == set("ABCDEFG") and len(counts) == 7
    if len(set(numbers)) == 1 and "H" not in colours and len(colours) == len(counts):
        return Shape("double" if double else "rainbow" if rainbow else "triplet",
                     "triplet", tiles, 0, rainbow, double)
    step = numbers[1] - numbers[0]
    progression = step > 0 and all(b - a == step for a, b in zip(numbers, numbers[1:]))
    if progression and (len(colours) == 1 or (rainbow and step == 1)):
        return Shape("double" if double else "rainbow" if rainbow else "sequence",
                     "sequence", tiles, step, rainbow, double)
    return None


def classify_meld(tiles) -> Shape | None:
    return _classify(tuple(sorted(tiles)))


@lru_cache(maxsize=8192)
def available_groups(hand: tuple[str, ...]) -> tuple[tuple[str, ...], ...]:
    counts = Counter(hand)
    if any(tile not in FACE_SET or count > 2 for tile, count in counts.items()):
        return ()
    found = {(tile, tile) for tile, count in counts.items() if count == 2}
    for copies in (1, 2):
        available = {tile for tile, count in counts.items() if count >= copies}
        def add(group):
            found.add(tuple(sorted(tile for tile in group for _ in range(copies))))
        for rank in range(1, 10):
            rank_tiles = sorted(tile for tile in available if tile[2] == str(rank) and tile[0] != "H")
            for size in range(3, len(rank_tiles) + 1):
                for group in combinations(rank_tiles, size):
                    add(group)
        for colour in "ABCDEFGH":
            for step in range(1, 5):
                for start in range(1, 10 - 2 * step):
                    group = []
                    for rank in range(start, 10, step):
                        tile = f"{colour}X{rank}"
                        if tile not in available:
                            break
                        group.append(tile)
                        if len(group) >= 3:
                            add(group)
        for start in range(1, 4):
            options = [sorted(tile for tile in available if tile[0] != "H" and tile[2] == str(rank))
                       for rank in range(start, start + 7)]
            def rainbow(index, used, group):
                if index == 7:
                    add(group)
                    return
                for tile in options[index]:
                    if tile[0] not in used:
                        rainbow(index + 1, used | {tile[0]}, group + [tile])
            if all(options):
                rainbow(0, set(), [])
    return tuple(sorted(found, key=lambda group: (-len(group), group)))


def _subtract(hand, group):
    counts = Counter(hand)
    counts.subtract(group)
    if any(value < 0 for value in counts.values()):
        return None
    return tuple(sorted(counts.elements()))


@lru_cache(maxsize=32768)
def partitions(hand: tuple[str, ...], pair_used: bool = False):
    if not hand:
        return ((),)
    result = []
    for group in available_groups(hand):
        if hand[0] not in group or (len(group) == 2 and pair_used):
            continue
        rest = _subtract(hand, group)
        for tail in partitions(rest, pair_used or len(group) == 2):
            result.append((group,) + tail)
    return tuple(result)


def winning_partitions(hand, open_melds=()):
    hand = tuple(sorted(hand))
    opened = [tuple(meld.get("tiles", ())) for meld in open_melds]
    if not hand and not opened:
        return ()
    total = Counter(hand + tuple(tile for group in opened for tile in group))
    if any(tile not in FACE_SET or count > 2 for tile, count in total.items()):
        return ()
    shapes = [classify_meld(group) for group in opened]
    if any(shape is None or shape.base_kind == "pair" for shape in shapes):
        return ()
    results = [(groups, False) for groups in partitions(hand)]
    if all(count == 2 for count in Counter(hand).values()) and all(shape.is_double for shape in shapes):
        pairs = tuple((tile, tile) for tile in sorted(set(hand)))
        if pairs or opened:
            results.append((pairs, True))
    return tuple(results)


def is_winning_hand(hand, open_melds=()) -> bool:
    return bool(winning_partitions(hand, open_melds))


def call_candidates(hand, discarded, *, claimant_index=None, discarder_index=None):
    from .action_priority import relative_position_suffix
    suffix = relative_position_suffix(claimant_index, discarder_index)
    candidates = []
    for group in available_groups(tuple(sorted([*hand, discarded]))):
        if discarded not in group:
            continue
        shape = classify_meld(group)
        if shape.base_kind == "pair" or (shape.base_kind == "triplet" and not shape.is_double and not shape.is_rainbow):
            continue
        taken = list(group)
        taken.remove(discarded)
        if _subtract(hand, taken) is None:
            continue
        prefix = "double" if shape.is_double else "peng" if shape.base_kind == "triplet" else "hong" if shape.is_rainbow else "chi"
        action_type = f"{prefix}_{suffix}"
        kind = "triplet" if prefix == "peng" else shape.kind
        candidates.append({"id": f"call-{len(candidates)}", "kind": kind,
                           "base_kind": shape.base_kind, "action_type": action_type,
                           "hand_tiles": taken, "tiles": list(group), "priority": PRIORITY[action_type]})
    return candidates


def kong_candidates(hand, open_melds):
    result = []
    for index, meld in enumerate(open_melds):
        before = tuple(meld.get("tiles", ()))
        for group in available_groups(tuple(sorted([*hand, *before]))):
            if len(group) <= len(before):
                continue
            added = _subtract(group, before)
            if added is None or _subtract(hand, added) is None:
                continue
            result.append({"id": f"kong-{len(result)}", "kind": "kong", "meld_index": index,
                           "tiles": list(group), "hand_tiles": list(added)})
    return result


def kong_win_candidates(hand, open_melds):
    result = []
    for candidate in kong_candidates(hand, open_melds):
        remaining = _subtract(hand, candidate["hand_tiles"])
        melds = [dict(meld) for meld in open_melds]
        melds[candidate["meld_index"]]["tiles"] = candidate["tiles"]
        if is_winning_hand(remaining, melds):
            result.append({**candidate, "id": "win-" + candidate["id"], "kind": "kong_win"})
    return result
