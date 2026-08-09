from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache
from itertools import combinations
from typing import Iterable, Optional, Sequence

from .tile import HongqueTile


@dataclass(frozen=True)
class MeldShape:
    kind: str
    base_kind: str
    tiles: tuple[str, ...]
    colour_step: int
    number_step: int
    is_rainbow: bool


def _cyclic_progression(values: Sequence[int], step: int) -> bool:
    if not values:
        return False
    counts = {value: values.count(value) for value in set(values)}
    if any(count > 1 for count in counts.values()):
        return False
    for start in values:
        expected = {(start + step * offset) % 14 for offset in range(len(values))}
        if expected == set(values):
            return True
    return False


def _ordered_colour_progression(tiles: Sequence[HongqueTile], step: int, number_step: int) -> bool:
    ordered = sorted(tiles, key=lambda tile: tile.number, reverse=number_step < 0)
    return all((ordered[i + 1].colour - ordered[i].colour) % 14 == step for i in range(len(ordered) - 1))


def classify_meld(codes: Iterable[str]) -> MeldShape | None:
    normalized = tuple(sorted((HongqueTile.parse(code) for code in codes), key=lambda tile: (tile.number, tile.colour)))
    if len(normalized) < 3 or len(set(normalized)) != len(normalized):
        return None

    numbers = [tile.number for tile in normalized]
    colours = [tile.colour for tile in normalized]
    # 规则第 7 页：彩虹是一组含有七种“不同花色”的牌。
    # 半色不能拆成两个纯色参与凑数，否则随机四五张牌就会被误判为彩虹，
    # 继而让绝大多数随机起手都错误出现“和”。
    # Half-colour tiles cover both adjacent base colours.  A rainbow is formed
    # when the group covers all seven base colours, as shown by the rulebook.
    primary_colours = set().union(*(tile.primary_colours for tile in normalized))
    rainbow = len(primary_colours) == 7

    if len(set(numbers)) == 1:
        for colour_step in (1, 2):
            if _cyclic_progression(colours, colour_step):
                return MeldShape("rainbow" if rainbow else "triplet", "triplet", tuple(tile.code for tile in normalized), colour_step, 0, rainbow)

    for number_step in range(-4, 5):
        if number_step == 0:
            continue
        ordered_numbers = sorted(numbers, reverse=number_step < 0)
        if any(ordered_numbers[i + 1] - ordered_numbers[i] != number_step for i in range(len(ordered_numbers) - 1)):
            continue
        signed_step = number_step
        for colour_step in (0, 1, 2):
            if colour_step == 0:
                if len(set(colours)) == 1:
                    return MeldShape("rainbow" if rainbow else "sequence", "sequence", tuple(tile.code for tile in normalized), 0, signed_step, rainbow)
            elif _ordered_colour_progression(normalized, colour_step, signed_step):
                return MeldShape("rainbow" if rainbow else "sequence", "sequence", tuple(tile.code for tile in normalized), colour_step, signed_step, rainbow)
    return None


@lru_cache(maxsize=4096)
def _partition_cached(sorted_codes: tuple[str, ...]) -> tuple[tuple[tuple[str, ...], ...], ...]:
    if not sorted_codes:
        return ((),)
    anchor = sorted_codes[0]
    results: list[tuple[tuple[str, ...], ...]] = []
    indices = range(1, len(sorted_codes))
    for size in range(3, len(sorted_codes) + 1):
        for rest_indices in combinations(indices, size - 1):
            group_indices = (0,) + rest_indices
            group = tuple(sorted_codes[index] for index in group_indices)
            if classify_meld(group) is None:
                continue
            chosen = set(group_indices)
            remainder = tuple(code for index, code in enumerate(sorted_codes) if index not in chosen)
            for tail in _partition_cached(remainder):
                results.append((group,) + tail)
    return tuple(results)


def winning_partitions(codes: Iterable[str]) -> list[list[list[str]]]:
    parsed = [HongqueTile.parse(code).code for code in codes]
    if len(set(parsed)) != len(parsed):
        return []
    return [[list(group) for group in partition] for partition in _partition_cached(tuple(sorted(parsed)))]


def _could_form_meld(codes: Sequence[str]) -> bool:
    """Cheap number-only rejection before the full colour/shape classifier."""
    numbers = [HongqueTile.parse(code).number for code in codes]
    if len(set(numbers)) == 1:
        return True
    if len(set(numbers)) != len(numbers):
        return False
    ordered = sorted(numbers)
    step = ordered[1] - ordered[0]
    return 1 <= step <= 4 and all(
        ordered[index + 1] - ordered[index] == step
        for index in range(len(ordered) - 1)
    )


def call_candidates(
    hand: Sequence[str],
    discarded: str,
    claimant_index: Optional[int] = None,
    discarder_index: Optional[int] = None,
) -> list[dict]:
    from .group_index import codes_from_mask, group_masks_containing, mask_from_codes

    discarded = HongqueTile.parse(discarded).code
    try:
        hand_mask = mask_from_codes(hand)
        discarded_mask = mask_from_codes((discarded,))
    except ValueError:
        return []
    candidates: list[dict] = []
    for group_mask in group_masks_containing(discarded):
        selected_mask = group_mask ^ discarded_mask
        if selected_mask & hand_mask != selected_mask:
            continue
        selected = codes_from_mask(selected_mask)
        shape = classify_meld(codes_from_mask(group_mask))
        if shape is None:
            continue
        # 解析优先级（权威，与牌效评分无关）：和(7) > 虹(6) > 碰(5) > 吃。
        # 吃按出牌者相对位置分三档，与国标和牌 hu_first/second/third 同构：
        # 出牌者的下家=chi_first(4) > 对家=chi_second(3) > 上家=chi_third(2)。
        if shape.is_rainbow:
            priority = 6
        elif shape.kind == "triplet":
            priority = 5
        elif claimant_index is not None and discarder_index is not None:
            distance = (claimant_index - discarder_index) % 4
            priority = {1: 4, 2: 3, 3: 2}.get(distance, 2)
        else:
            priority = 2  # 无座位上下文（独立调用/测试）按最低档
        candidates.append({
            "kind": shape.kind,
            "base_kind": shape.base_kind,
            "hand_tiles": list(selected),
            "tiles": list(shape.tiles),
            "priority": priority,
        })
    candidates.sort(key=lambda candidate: (-candidate["priority"], len(candidate["tiles"]), candidate["tiles"]))
    for index, candidate in enumerate(candidates):
        candidate["id"] = f"call-{index}"
    return candidates


def kong_candidates(hand: Sequence[str], open_melds: Sequence[dict]) -> list[dict]:
    from .group_index import codes_from_mask, group_masks_containing, mask_from_codes

    try:
        hand_mask = mask_from_codes(hand)
    except ValueError:
        return []
    candidates: list[dict] = []
    for meld_index, meld in enumerate(open_melds):
        existing = tuple(meld.get("tiles", ()))
        if not existing:
            continue
        try:
            existing_mask = mask_from_codes(existing)
        except ValueError:
            continue
        anchor = HongqueTile.parse(existing[0]).code
        for group_mask in group_masks_containing(anchor):
            if group_mask & existing_mask != existing_mask:
                continue
            selected_mask = group_mask ^ existing_mask
            if selected_mask == 0 or selected_mask & hand_mask != selected_mask:
                continue
            selected = codes_from_mask(selected_mask)
            # 虹雀的杠是“副露单张增量”：每次只把一张手牌并入现有副露，
            # 再由后续操作继续 3→4→5→6；不能一次跨级并入多张手牌。
            if len(selected) != 1:
                continue
            shape = classify_meld(codes_from_mask(group_mask))
            if shape is None:
                continue
            candidates.append({
                "id": f"kong-{meld_index}-{len(candidates)}",
                "kind": "kong",
                "meld_index": meld_index,
                "hand_tiles": list(selected),
                "tiles": list(shape.tiles),
            })
    return candidates


def kong_win_candidates(hand: Sequence[str], open_melds: Sequence[dict]) -> list[dict]:
    """杠和候选：杠完（把若干手牌并入明牌）后剩余手牌构成和牌型。

    与普通杠不同，杠和是一个完整动作：一次操作同时完成杠牌与摸和，
    无需先杠再在下一轮宣言和牌。候选 id 带 ``kong_win-`` 前缀，避免与
    同牌型的普通杠候选混淆（两者可同时下发）。
    """
    from .win_check import is_winning_hand

    candidates: list[dict] = []
    for candidate in kong_candidates(hand, open_melds):
        rest = list(hand)
        for code in candidate["hand_tiles"]:
            if code not in rest:
                break
            rest.remove(code)
        else:
            melds_after = [dict(meld) for meld in open_melds]
            melds_after[candidate["meld_index"]]["tiles"] = list(candidate["tiles"])
            if not is_winning_hand(rest, melds_after):
                continue
            candidates.append({
                "id": f"kong_win-{candidate['meld_index']}-{len(candidates)}",
                "kind": "kong_win",
                "meld_index": candidate["meld_index"],
                "hand_tiles": list(candidate["hand_tiles"]),
                "tiles": list(candidate["tiles"]),
            })
    return candidates
