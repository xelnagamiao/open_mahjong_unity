"""虹雀行动合法性检查。

模块职责与国标/川麻 action_check 对齐：只计算动作与候选，不修改对局状态。
"""
from __future__ import annotations

from typing import Sequence

from .rules import call_candidates, kong_candidates, kong_win_candidates
from .win_check import is_winning_hand
from .action_priority import HONGQUE_ACTION_PRIORITY, claim_action_type


def can_self_draw_win(hand: Sequence[str], melds: Sequence[dict]) -> bool:
    """手内成组，或自摸牌可并入既有副露形成虹雀杠和。"""
    return is_winning_hand(hand, melds) or bool(kong_win_candidates(hand, melds))


def check_action_hand_action(game_state, player) -> tuple[list[str], list[dict]]:
    if game_state.phase != "turn" or player.index != game_state.current_player_index:
        return [], []
    if game_state.game_status == "onlycut_after_action":
        return check_only_cut(game_state, player)
    actions = ["discard"] if player.hand else []
    candidates: list[dict] = []
    if can_self_draw_win(player.hand, player.melds):
        actions.append("win")
    # 摸牌后：最后一张手牌杠后直接形成杠和，只下发“和”。
    if len(player.hand) != 1:
        kong = kong_candidates(player.hand, player.melds)
        if kong:
            actions.append("kong")
            candidates.extend(kong)
    if player.supplements < 2 and game_state.wall:
        actions.append("supplement")
    return actions, candidates


def check_only_cut(game_state, player) -> tuple[list[str], list[dict]]:
    """亮牌后手牌检查，对齐青雀 ``check_only_cut``。

    青雀在吃碰后允许切牌 / 暗杠 / 加杠。虹雀没有暗杠，杠是明牌单张增量，
    因此这里允许多一个加杠（含手上只剩一张的加杠），并允许补牌。
    亮牌后未补牌前不得宣称摸和：必须补牌进入 ``waiting_hand_action`` 后才能和。
    """
    if player.index != game_state.current_player_index:
        return [], []
    actions = ["discard"] if player.hand else []
    candidates: list[dict] = []
    kong = kong_candidates(player.hand, player.melds)
    if kong:
        actions.append("kong")
        candidates.extend(kong)
    if player.supplements < 2 and game_state.wall:
        actions.append("supplement")
    return actions, candidates


def check_action_after_cut(game_state) -> dict[int, list[dict]]:
    """生成弃牌后的吃、碰、虹、荣和候选快照，不改变任何牌面。"""
    if game_state.last_discard is None:
        return {}
    discarder = game_state.last_discard["player"]
    discarded = game_state.last_discard["tile"]
    result: dict[int, list[dict]] = {}
    for player in game_state.players:
        if player.index == discarder:
            continue
        options = call_candidates(
            player.hand,
            discarded,
            claimant_index=player.index,
            discarder_index=discarder,
        )
        if is_winning_hand(player.hand + [discarded], player.melds):
            action_type = claim_action_type("win", player.index, discarder)
            options.insert(0, {
                "id": "ron",
                "kind": "win",
                "action_type": action_type,
                "priority": HONGQUE_ACTION_PRIORITY[action_type],
                "tiles": [discarded],
                "hand_tiles": [],
            })
        if options:
            result[player.index] = options
    return result
