"""虹雀多家荣和收集与结算。

行为与日麻 ron_resolution 对齐：同一弃牌上的全部合法荣和先收集，再按出牌者
顺时针距离排序后一次交给结算层；可选 head_bump 时才截取最近一家。
"""
from __future__ import annotations

from .scoring import best_win_result


async def resolve_collected_rons(game_state, ron_claims) -> bool:
    if not ron_claims:
        return False
    discarder = game_state.last_discard["player"]
    ron_claims = sorted(
        ron_claims,
        key=lambda item: (item.player_index - discarder) % len(game_state.players),
    )
    if game_state.hepai_way == "head_bump":
        ron_claims = ron_claims[:1]
    discarded = game_state.last_discard["tile"]
    winners = []
    for claim in ron_claims:
        winner = game_state.players[claim.player_index]
        result = best_win_result(
            winner.hand + [discarded],
            winner.melds,
            self_draw=False,
            before_first_discard=False,
            wall_empty=not game_state.wall,
        )
        if result is not None:
            result["winning_hand"] = list(winner.hand) + [discarded]
            winners.append((winner, result))
    if not winners:
        return False
    await game_state._finish_round(winners, "ron", silent=True)
    return True

