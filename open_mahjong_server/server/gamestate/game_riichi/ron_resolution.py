"""立直麻将多家荣和 / 三家和了流局 的收集与结算辅助。"""
import asyncio
import logging

from .boardcast import broadcast_do_action

logger = logging.getLogger(__name__)

RON_HU_ACTIONS = ("hu_first", "hu_second", "hu_third")
RON_ORDER = {"hu_first": 0, "hu_second": 1, "hu_third": 2}

THREE_RON_ABORT_HOLD_SEC = 1.0


def collect_ron_mode(hepai_way: str) -> bool:
    return hepai_way in ("multi_ron", "three_ron_abort")


def should_interrupt_wait_for_action(game_state, action_type: str, waiting_players_list, action_dict) -> bool:
    """多家荣和模式：等所有可荣和玩家均响应后再结束等待；头跳模式保持原优先级截和。"""
    if action_type in RON_HU_ACTIONS and collect_ron_mode(getattr(game_state, "hepai_way", "head_bump")):
        for player_index in waiting_players_list:
            for action in action_dict.get(player_index, []):
                if action in RON_HU_ACTIONS:
                    return False
        return True
    do_interrupt = True
    for player_index in waiting_players_list:
        for action in action_dict.get(player_index, []):
            if game_state.action_priority[action_type] < game_state.action_priority[action]:
                do_interrupt = False
    return do_interrupt


def record_ron_claim(game_state, player_index: int, action_type: str) -> None:
    if action_type not in RON_HU_ACTIONS:
        return
    if not hasattr(game_state, "_pending_ron_claims") or game_state._pending_ron_claims is None:
        game_state._pending_ron_claims = {}
    game_state._pending_ron_claims[player_index] = action_type


def _ordered_ron_claims(pending: dict) -> list[tuple[int, str]]:
    return sorted(pending.items(), key=lambda item: RON_ORDER.get(item[1], 99))


async def _broadcast_ron_claims_together(game_state, claims: list[tuple[int, str]]) -> None:
    for player_index, action_type in claims:
        await broadcast_do_action(
            game_state,
            action_list=[action_type],
            action_player=player_index,
            is_claim=True,
        )


def _remove_discard_tile(game_state) -> None:
    discarder = game_state.player_list[game_state.current_player_index]
    if discarder.discard_tiles:
        discarder.discard_tiles.pop(-1)
    if discarder.discard_riichi_flags:
        discarder.discard_riichi_flags.pop(-1)


async def resolve_collected_rons(game_state, tile_id: int, ron_eligible_indexes: list[int]) -> bool:
    """处理本巡收集到的荣和宣告。返回 True 表示已终结本局（END）。"""
    pending = getattr(game_state, "_pending_ron_claims", None) or {}
    game_state._pending_ron_claims = {}

    if not pending:
        return False

    ordered = _ordered_ron_claims(pending)
    from .wait_action import _apply_passed_ron_furiten

    passed = [pi for pi in ron_eligible_indexes if pi not in pending]
    if passed:
        _apply_passed_ron_furiten(game_state, passed)

    # 三家和了流局：3 家同时荣和 → 仅播发声 → 等待 1 秒 → 流局
    if game_state.hepai_way == "three_ron_abort" and len(ordered) >= 3:
        await _broadcast_ron_claims_together(game_state, ordered)
        await asyncio.sleep(THREE_RON_ABORT_HOLD_SEC)
        game_state.hu_class = "three_ron_abort"
        game_state.game_status = "END"
        return True

    # 单家荣和（含 multi_ron 仅 1 家点和）：走常规单家和牌
    if len(ordered) == 1:
        player_index, action_type = ordered[0]
        from .wait_action import _broadcast_hu_and_end

        await _broadcast_hu_and_end(game_state, player_index, action_type, tile_id)
        return True

    # 多家荣和：一起喊荣 → 依次结算
    await _broadcast_ron_claims_together(game_state, ordered)
    _remove_discard_tile(game_state)
    for player_index, _action_type in ordered:
        game_state.player_list[player_index].hand_tiles.append(tile_id)

    game_state.multi_ron_queue = ordered
    game_state.hu_class = ordered[0][1]
    game_state.ron_player_index = ordered[0][0]
    game_state.game_status = "END"
    return True
