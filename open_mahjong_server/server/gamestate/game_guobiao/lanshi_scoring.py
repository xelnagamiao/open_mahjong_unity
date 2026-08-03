"""蓝十改的零和分数收支。"""

from typing import Dict, Iterable, Optional


def calculate_lanshi_score_changes(
    player_indices: Iterable[int],
    winner_index: int,
    basic_score: int,
    discarder_index: Optional[int] = None,
) -> Dict[int, int]:
    """按人数 n 计算自摸或点和的分数变化，结果之和恒为 0。"""
    indices = list(player_indices)
    player_count = len(indices)
    if player_count < 2 or winner_index not in indices:
        raise ValueError("蓝十改计分需要至少两名玩家且赢家必须在座")
    if basic_score < 0:
        raise ValueError("基本分不能为负数")

    changes = {index: 0 for index in indices}
    changes[winner_index] = (player_count - 1) * 2 * basic_score
    if discarder_index is None:
        for index in indices:
            if index != winner_index:
                changes[index] = -2 * basic_score
    else:
        if discarder_index not in indices or discarder_index == winner_index:
            raise ValueError("放铳者必须是在座的非赢家")
        for index in indices:
            if index == discarder_index:
                changes[index] = -player_count * basic_score
            elif index != winner_index:
                changes[index] = -basic_score
    return changes

