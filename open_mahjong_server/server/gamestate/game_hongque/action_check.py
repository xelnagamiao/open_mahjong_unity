"""虹雀行动合法性检查。

模块职责与国标/川麻 action_check 对齐：只计算动作与候选，不修改对局状态。
"""
from __future__ import annotations

from typing import Sequence

from .action_priority import HONGQUE_ACTION_PRIORITY, claim_action_type
from .profile import rules_for
from .w_rules import is_w, PRIORITY
from .scoring import all_win_results
from .win_plans import can_supplement, self_draw_plans, win_plans_for_state


def can_self_draw_win(hand: Sequence[str], melds: Sequence[dict]) -> bool:
    """手内成组，或自摸牌可并入既有副露形成虹雀杠和。"""
    return bool(all_win_results(hand, melds, self_draw=True,
        before_first_discard=False, wall_empty=False, allow_kong_win=True))


def check_action_hand_action(game_state, player) -> tuple[list[str], list[dict]]:
    if game_state.phase != "turn" or player.index != game_state.current_player_index:
        return [], []
    if game_state.game_status == "onlycut_after_action":
        return check_only_cut(game_state, player)
    actions = ["discard"] if player.hand else []
    candidates: list[dict] = []
    wins = self_draw_plans(game_state, player)
    if wins:
        actions.append("win")
        candidates.extend(wins)
    # 最后一张只有在还能补牌时才可普通杠；否则应选择杠和。
    if len(player.hand) != 1 or can_supplement(game_state, player):
        kong = rules_for(game_state).kong_candidates(player.hand, player.melds)
        if kong:
            actions.append("kong")
            candidates.extend(kong)
    if can_supplement(game_state, player):
        actions.append("supplement")
    return actions, candidates


def check_only_cut(game_state, player) -> tuple[list[str], list[dict]]:
    """亮牌后手牌检查，对齐青雀 ``check_only_cut``。

    青雀在吃碰后允许切牌 / 暗杠 / 加杠。虹雀没有暗杠，杠是明牌单张增量，
    因此这里允许多一个加杠（含手上只剩一张的加杠），并允许补牌。
    普通亮牌后须先补牌；无补牌机会的最后张杠和、空手和作为退出例外。
    允许普通加杠耗尽手牌时，执行端切回 ``waiting_hand_action`` 供玩家和/补二选一。
    """
    if player.index != game_state.current_player_index:
        return [], []
    actions = ["discard"] if player.hand else []
    candidates: list[dict] = []
    supplement_available = can_supplement(game_state, player)
    wins = self_draw_plans(game_state, player)
    if wins:
        actions.append("win")
        candidates.extend(wins)
    # 亮牌后不能直接和；没有补牌机会时必须留牌可切，避免进入无动作的空转态。
    kong = rules_for(game_state).kong_candidates(player.hand, player.melds) if (
        len(player.hand) > 1 or supplement_available
    ) else []
    if not supplement_available:
        kong = [item for item in kong if len(item["hand_tiles"]) < len(player.hand)]
    if kong:
        actions.append("kong")
        candidates.extend(kong)
    if supplement_available:
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
        options = rules_for(game_state).call_candidates(
            player.hand,
            discarded,
            claimant_index=player.index,
            discarder_index=discarder,
        )
        if player.supplements >= 2 or not game_state.wall:
            options = [option for option in options
                       if len(option["hand_tiles"]) < len(player.hand)]
        wins = win_plans_for_state(game_state, player, player.hand + [discarded], self_draw=False)
        if wins:
            action_type = claim_action_type("win", player.index, discarder)
            options[0:0] = [{
                **plan,
                "action_type": action_type,
                "priority": (PRIORITY if is_w(game_state) else HONGQUE_ACTION_PRIORITY)[action_type],
            } for plan in wins]
        if options:
            result[player.index] = options
    return result
