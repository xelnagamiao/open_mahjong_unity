"""Build MMCR-style winning-tile hints from the authoritative GB calculator."""

from collections import Counter
from typing import Dict, Iterable, List, Optional


FLOWERS = set(range(51, 59))


def _meld_tiles(target: str) -> List[int]:
    if not target or len(target) < 2:
        return []
    try:
        tile = int(target[1:])
    except (TypeError, ValueError):
        return []
    prefix = target[0]
    if prefix.lower() == "s":
        return [tile - 1, tile, tile + 1]
    if prefix == "k":
        return [tile] * 3
    if prefix in ("g", "G"):
        return [tile] * 4
    return []


def _table_counts(game_state) -> Counter:
    counts: Counter = Counter()
    for player in game_state.player_list:
        counts.update(int(tile) for tile in player.discard_tiles)
        for target in player.combination_tiles:
            counts.update(_meld_tiles(target))
    return counts


def _remaining_count(game_state, hand: Iterable[int], tile: int, pending_cut: Optional[int]) -> int:
    visible = _table_counts(game_state)
    visible.update(int(value) for value in hand)
    if pending_cut is not None:
        visible.update([pending_cut])
    return max(0, 4 - visible[tile])


def _base_way(game_state, player_index: int, wait_count: int) -> List[str]:
    player = game_state.player_list[player_index]
    way = ["花牌"] * len(player.huapai_list)
    round_index = int(getattr(game_state, "current_round", 1))
    round_winds = ("场风东", "场风南", "场风西", "场风北")
    way.append(round_winds[min(3, max(0, (round_index - 1) // 4))])
    seat_winds = ("自风东", "自风南", "自风西", "自风北")
    way.append(seat_winds[int(player.player_index) % 4])
    if wait_count == 1:
        way.append("和单张")
    return way


def _score(game_state, hand: List[int], combinations: List[str], way: List[str], tile: int) -> int:
    service = game_state.calculation_service
    try:
        sub_rule = getattr(game_state, "sub_rule", "guobiao/standard")
        if sub_rule == "guobiao/xiaolin":
            result = service.GB_xiaolin_hepai_check(list(hand), list(combinations), list(way), tile)
        elif sub_rule == "guobiao/kshen":
            result = service.GB_kshen_hepai_check(list(hand), list(combinations), list(way), tile)
        else:
            result = service.GB_hepai_check(list(hand), list(combinations), list(way), tile)
        return max(0, int(result[0]))
    except Exception:
        return 0


def _details_for_hand(
    game_state,
    player_index: int,
    hand: List[int],
    pending_cut: Optional[int] = None,
) -> List[Dict[str, object]]:
    player = game_state.player_list[player_index]
    combinations = list(player.combination_tiles)
    try:
        waiting = sorted(
            int(tile)
            for tile in game_state.calculation_service.GB_tingpai_check(list(hand), combinations)
        )
    except Exception:
        return []
    if not waiting:
        return []

    table = _table_counts(game_state)
    if pending_cut is not None:
        table.update([pending_cut])
    base_way = _base_way(game_state, player_index, len(waiting))
    flower_count = len(player.huapai_list)
    minimum = int(getattr(game_state, "hepai_limit", 8))
    details: List[Dict[str, object]] = []

    for tile in waiting:
        ron_way = list(base_way)
        if table[tile] == 4:
            ron_way.append("和绝张")
        ron_way.append("点和")

        tsumo_way = list(base_way)
        if table[tile] == 3:
            tsumo_way.append("和绝张")
        tsumo_way.append("自摸")

        complete_hand = list(hand) + [tile]
        ron_fan = _score(game_state, complete_hand, combinations, ron_way, tile)
        tsumo_fan = _score(game_state, complete_hand, combinations, tsumo_way, tile)
        ron_allowed = ron_fan - flower_count >= minimum
        tsumo_allowed = tsumo_fan - flower_count >= minimum
        details.append(
            {
                "tile": tile,
                "base_f": ron_fan if ron_allowed else 0,
                "selfdrawn_f": tsumo_fan if tsumo_allowed else 0,
                "remaining_count": _remaining_count(game_state, hand, tile, pending_cut),
            }
        )
    return details


def _build_wait_data(game_state, player_index: int, *, include_discards: bool) -> Optional[Dict[str, object]]:
    if not bool(getattr(game_state, "tips", False)):
        return None
    player = game_state.player_list[player_index]
    hand = list(player.hand_tiles)
    if any(tile in FLOWERS for tile in hand):
        return None

    if not include_discards:
        details = _details_for_hand(game_state, player_index, hand)
        return {"type": "waits", "details": details} if details else None

    by_discard: List[Dict[str, object]] = []
    seen = set()
    for index, discard in enumerate(hand):
        if discard in seen or discard in FLOWERS:
            continue
        seen.add(discard)
        remaining_hand = hand[:index] + hand[index + 1 :]
        adds = _details_for_hand(game_state, player_index, remaining_hand, pending_cut=discard)
        if adds:
            by_discard.append({"discard_tile": discard, "adds": adds})
    return {"type": "waits_all", "details": by_discard} if by_discard else None


def build_wait_data(game_state, player_index: int, *, include_discards: bool) -> Optional[Dict[str, object]]:
    """Return hints without ever allowing an optional UI calculation to stop play."""
    try:
        return _build_wait_data(game_state, player_index, include_discards=include_discards)
    except Exception:
        return None
