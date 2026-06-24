"""
立直麻将玩家/AI 动作入队：基于 classical 扩展 riichi_cut 处理。
"""
import logging

from ..public.hand_slot_utils import hand_contains_tile, normalize_tile

logger = logging.getLogger(__name__)


async def get_ai_action(game_state, player_index: int, action_type: str, cutClass: bool, TileId: int, cutIndex: int, target_tile: int):
    try:
        if player_index not in (0, 1, 2, 3):
            return
        if player_index not in game_state.waiting_players_list:
            return
        if game_state.game_status == "waiting_hand_action" and player_index != game_state.current_player_index:
            return
        if action_type not in game_state.action_dict.get(player_index, []):
            return

        current_player = game_state.player_list[player_index]

        if action_type in ("cut", "riichi_cut"):
            if not hand_contains_tile(current_player.hand_tiles, TileId):
                return
            await game_state.action_queues[player_index].put({
                "action_type": action_type,
                "cutClass": cutClass,
                "TileId": TileId,
                "cutIndex": cutIndex,
            })
            game_state.action_events[player_index].set()
        else:
            if action_type == "jiagang":
                if f"k{target_tile}" not in current_player.combination_tiles:
                    return
            elif action_type == "angang":
                normal = normalize_tile(target_tile)
                if sum(1 for t in current_player.hand_tiles if normalize_tile(t) == normal) < 4:
                    return
            await game_state.action_queues[player_index].put({
                "action_type": action_type,
                "target_tile": target_tile,
            })
            game_state.action_events[player_index].set()
    except Exception as e:
        logger.error(f"立直机器人动作处理错误: {e}", exc_info=True)
        raise


async def get_action(game_state, player_id: str, action_type: str, cutClass: bool, TileId: int, cutIndex: int, target_tile: int):
    try:
        player_conn = game_state.game_server.players.get(player_id)
        if not player_conn or not player_conn.user_id:
            return
        user_id = player_conn.user_id

        current_player = None
        player_index = -1
        for index, player in enumerate(game_state.player_list):
            if player.user_id == user_id:
                current_player = player
                player_index = index
                break
        if current_player is None:
            return
        if player_index not in (0, 1, 2, 3):
            return
        if player_index not in game_state.waiting_players_list:
            return
        if game_state.game_status == "waiting_hand_action" and player_index != game_state.current_player_index:
            return
        if action_type not in game_state.action_dict.get(player_index, []):
            return

        if action_type in ("cut", "riichi_cut"):
            if not hand_contains_tile(current_player.hand_tiles, TileId):
                return
            await game_state.action_queues[player_index].put({
                "action_type": action_type,
                "cutClass": cutClass,
                "TileId": TileId,
                "cutIndex": cutIndex,
            })
            game_state.action_events[player_index].set()
        elif action_type == "ready":
            if game_state.game_status != "waiting_ready":
                return
            await game_state.action_queues[player_index].put({"action_type": "ready"})
            game_state.action_events[player_index].set()
        else:
            if action_type == "jiagang":
                if f"k{target_tile}" not in current_player.combination_tiles:
                    return
            elif action_type == "angang":
                normal = normalize_tile(target_tile)
                if sum(1 for t in current_player.hand_tiles if normalize_tile(t) == normal) < 4:
                    return
            await game_state.action_queues[player_index].put({
                "action_type": action_type,
                "target_tile": target_tile,
            })
            game_state.action_events[player_index].set()
    except Exception as e:
        logger.error(f"立直玩家动作处理错误: {e}", exc_info=True)
        raise
