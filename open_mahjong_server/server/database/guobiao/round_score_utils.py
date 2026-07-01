"""
国标麻将局均点：从牌谱 JSON 提取每位玩家每小局净得分。
"""
import json
import logging
from typing import Any, Dict, List, Optional

logger = logging.getLogger(__name__)

_HU_ACTIONS = frozenset({"hu_self", "hu_first", "hu_second", "hu_third"})


def _parse_score_changes(raw: Any) -> Optional[List[int]]:
    if raw is None:
        return None
    if isinstance(raw, list):
        try:
            return [int(x) for x in raw]
        except (TypeError, ValueError):
            return None
    if isinstance(raw, str):
        try:
            parsed = json.loads(raw)
            if isinstance(parsed, list):
                return [int(x) for x in parsed]
        except (json.JSONDecodeError, TypeError, ValueError):
            return None
    return None


def _seat_index_for_original(seats: List[int], original_player_index: int) -> int:
    if not seats or original_player_index < 0 or original_player_index >= len(seats):
        return original_player_index
    return int(seats[original_player_index])


def extract_player_round_deltas(record: Dict[str, Any], original_player_index: int) -> List[int]:
    """
    返回该玩家在本场牌谱中每一小局（round_index_*）的净得分列表。
    同一小局内多次错和与最终和牌的 hu_* tick 分值累加。
    """
    game_round = record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return []

    round_keys = sorted(
        game_round.keys(),
        key=lambda k: int(k.split("_")[-1]) if isinstance(k, str) and k.split("_")[-1].isdigit() else k,
    )
    deltas: List[int] = []
    for round_key in round_keys:
        round_data = game_round.get(round_key) or {}
        seats = round_data.get("seats") or [0, 1, 2, 3]
        seat_idx = _seat_index_for_original(seats, original_player_index)
        round_delta = 0
        for tick in round_data.get("action_ticks") or []:
            if not tick:
                continue
            action = tick[0]
            if action not in _HU_ACTIONS or len(tick) < 5:
                continue
            score_changes = _parse_score_changes(tick[4])
            if score_changes is None or seat_idx < 0 or seat_idx >= len(score_changes):
                continue
            round_delta += score_changes[seat_idx]
        deltas.append(round_delta)
    return deltas


def sum_player_round_score(record: Dict[str, Any], original_player_index: int) -> int:
    """本场牌谱该玩家所有小局净得分之和。"""
    return sum(extract_player_round_deltas(record, original_player_index))
