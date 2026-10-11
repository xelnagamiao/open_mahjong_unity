"""自由模式广播：复用经典 GameInfo / Do_action_info 形状。"""
from __future__ import annotations

from typing import Any, Optional

from ..public.deal_tile_view import sanitize_deal_tile_for_viewer


def _safe_room_id(room_id: Any) -> int:
    try:
        return int(room_id)
    except (TypeError, ValueError):
        return 0


def _connection_for(game_state, user_id: int):
    return getattr(game_state.game_server, "user_id_to_connection", {}).get(user_id) if game_state.game_server else None


def _seats(game_state):
    return range(len(game_state.player_list))


def last_discard_location(game_state) -> Optional[tuple[int, int]]:
    for entry in reversed(game_state.discard_log):
        player_index, tile_id = entry[:2]
        player = game_state.player_list[player_index]
        _, tokens = player.area_metadata()
        for index in range(len(player.discard_tiles) - 1, -1, -1):
            if player.discard_tiles[index] == tile_id and (len(entry) == 2 or tokens[index] == entry[2]):
                return player_index, index
    return None


def last_alive_discard(game_state) -> Optional[tuple[int, int]]:
    location = last_discard_location(game_state)
    if location is None:
        return None
    player_index, index = location
    player = game_state.player_list[player_index]
    faces, _ = player.area_metadata()
    return player_index, 0 if faces[index] else player.discard_tiles[index]


def visible_area(player, *, flower: bool = False) -> list[int]:
    faces, _ = player.area_metadata(flower)
    tiles = player.huapai_list if flower else player.discard_tiles
    return [0 if hidden else tile for tile, hidden in zip(tiles, faces)]


def combination_mask_for_viewer(player, mask: list[int], viewer_index: int, *, reveal_tiles: bool = False) -> list[int]:
    visible = list(mask)
    # Only actions moving tiles to/from a public hand may explicitly reveal them.
    # Revealing the remaining hand does not expose older face-down melds in snapshots.
    if viewer_index != player.player_index and not reveal_tiles:
        for index in range(0, len(visible) - 1, 2):
            if visible[index] == 2:
                visible[index + 1] = 0
    return visible


def player_info_payload(game_state, player_index: int, viewer_index: int) -> dict:
    player = game_state.player_list[player_index]
    include_hand = viewer_index == player_index or player.revealed
    return {
        "username": player.username,
        "user_id": player.user_id,
        "hand_tiles_count": len(player.hand_tiles),
        "hand_tiles": list(player.hand_tiles) if include_hand else None,
        "discard_tiles": visible_area(player),
        "discard_origin_tiles": list(player.discard_origin_tiles),
        "combination_tiles": list(player.combination_tiles),
        "combination_mask": [combination_mask_for_viewer(player, mask, viewer_index)
                             for mask in player.combination_mask],
        "remaining_time": 0,
        "player_index": player_index,
        "original_player_index": player.original_player_index,
        "score": player.score,
        "huapai_list": visible_area(player, flower=True),
        "title_used": player.title_used,
        "character_used": player.character_used,
        "profile_used": player.profile_used,
        "avatar_frame_used": getattr(player, "avatar_frame_used", 0),
        "voice_used": player.voice_used,
        "score_history": list(player.score_history),
        "round_number_history": list(player.round_number_history),
        "tag_list": list(player.tag_list),
    }


def free_table_payload(
    game_state,
    *,
    revealed_player_index: Optional[int] = None,
    actor_player_index: Optional[int] = None,
) -> dict:
    revealed_hand = None
    if revealed_player_index is not None and game_state.player_list[revealed_player_index].revealed:
        revealed_hand = list(game_state.player_list[revealed_player_index].hand_tiles)
    last = last_alive_discard(game_state)
    return {
        "votes": {str(idx): game_state.player_list[idx].vote for idx in _seats(game_state)},
        # 旧单牌字段投影首张牌；新客户端按 transfer_tiles 的放入顺序展示，不按编号排序。
        # None 表示空区，0 表示未知牌；所有快照/其他操作广播共用此遮蔽。
        "transfer_tile": 0 if game_state.transfer_face_down and game_state.transfer_tile is not None else game_state.transfer_tile,
        "transfer_face_down": game_state.transfer_face_down,
        "transfer_tiles": [{**item, "tile": 0 if item["face_down"] else item["tile"]}
                           for item in game_state.transfer_tiles],
        "hand_counts": {str(idx): len(game_state.player_list[idx].hand_tiles) for idx in _seats(game_state)},
        "river_tokens": {str(idx): list(game_state.player_list[idx].area_metadata()[1]) for idx in _seats(game_state)},
        "flower_tokens": {str(idx): list(game_state.player_list[idx].area_metadata(True)[1]) for idx in _seats(game_state)},
        "score_revision": game_state.score_revision,
        "actor_player_index": actor_player_index,
        "scores": {str(idx): game_state.player_list[idx].score for idx in _seats(game_state)},
        "revealed": {str(idx): game_state.player_list[idx].revealed for idx in _seats(game_state)},
        "revealed_player_index": revealed_player_index,
        "revealed_hand": revealed_hand,
        "last_river_player": None if last is None else last[0],
        "last_river_tile": None if last is None else last[1],
    }


def detailed_config_payload(game_state) -> dict:
    cfg = {
        "wall_wan": game_state.wall_flags["wall_wan"],
        "wall_tong": game_state.wall_flags["wall_tong"],
        "wall_suo": game_state.wall_flags["wall_suo"],
        "wall_winds": game_state.wall_flags["wall_winds"],
        "wall_dragons": game_state.wall_flags["wall_dragons"],
        "wall_flowers": game_state.wall_flags["wall_flowers"],
    }
    cfg.update(free_table_payload(game_state))
    return cfg


def game_info_payload(game_state, viewer_index: int) -> dict:
    return {
        "room_id": _safe_room_id(game_state.room_id),
        "gamestate_id": game_state.gamestate_id,
        "tips": False,
        "pointer_tips": getattr(game_state, "pointer_tips", True),
        "current_player_index": viewer_index,
        "action_tick": game_state.action_tick,
        "max_round": 1,
        "tile_count": len(game_state.tiles_list),
        "master_seed": game_state.master_seed,
        "commitment": game_state.commitment,
        "salt": game_state.salt,
        "current_round": game_state.current_round,
        "step_time": 0,
        "round_time": 0,
        "room_type": game_state.room_type,
        "room_rule": "free",
        "sub_rule": game_state.sub_rule,
        "hepai_limit": 0,
        "open_cuohe": False,
        "show_moqie_hint": False,
        "tactical_call": False,
        "claim_protection": False,
        "isPlayerSetRandomSeed": game_state.isPlayerSetRandomSeed,
        "player_entry_order": [player.user_id for player in game_state.player_list],
        "players_info": [player_info_payload(game_state, idx, viewer_index) for idx in _seats(game_state)],
        "self_hand_tiles": list(game_state.player_list[viewer_index].hand_tiles),
        "dealer_index": 0,
        "view_player_index": viewer_index,
        "detailed_config": detailed_config_payload(game_state),
    }


async def send_json(game_state, user_id: int, payload: dict) -> None:
    connection = _connection_for(game_state, user_id)
    if connection is None or getattr(connection, "websocket", None) is None:
        return
    await connection.websocket.send_json(payload)


async def broadcast_game_start(game_state) -> None:
    for player in game_state.player_list:
        await send_json(game_state, player.user_id, {
            "type": "gamestate/free/game_start",
            "success": True,
            "message": "game start",
            "gamestate_id": game_state.gamestate_id,
            "player_index": player.player_index,
            "game_info": game_info_payload(game_state, player.player_index),
            "free_table_info": free_table_payload(game_state),
        })


async def broadcast_do_action(game_state, viewer_payloads: dict[int, dict]) -> None:
    for player in game_state.player_list:
        payload = viewer_payloads.get(player.player_index)
        if payload is None:
            continue
        await send_json(game_state, player.user_id, payload)


def do_action_envelope(game_state, action_list: list[str], action_player: int,
                       *, revealed_player_index: Optional[int] = None, **fields) -> dict:
    info = {
        "action_list": action_list,
        "action_player": action_player,
        "action_tick": game_state.action_tick,
    }
    info.update(fields)
    return {
        "type": "gamestate/free/do_action",
        "success": True,
        "message": action_list[0] if action_list else "do_action",
        "gamestate_id": game_state.gamestate_id,
        "do_action_info": info,
        "free_table_info": free_table_payload(game_state, revealed_player_index=revealed_player_index),
    }


def deal_payloads(game_state, action_player: int, tile_id: int) -> dict[int, dict]:
    payloads = {}
    for viewer in _seats(game_state):
        visible = tile_id
        if action_player != viewer and not game_state.player_list[action_player].revealed:
            visible = sanitize_deal_tile_for_viewer(tile_id, action_player, viewer)
        payloads[viewer] = do_action_envelope(
            game_state,
            ["deal_tile"],
            action_player,
            deal_tile=visible,
        )
    return payloads


async def broadcast_free_table(
    game_state,
    suffix: str,
    *,
    revealed_player_index: Optional[int] = None,
    actor_player_index: Optional[int] = None,
) -> None:
    table = free_table_payload(
        game_state,
        revealed_player_index=revealed_player_index,
        actor_player_index=actor_player_index,
    )
    for player in game_state.player_list:
        await send_json(game_state, player.user_id, {
            "type": f"gamestate/free/{suffix}",
            "success": True,
            "message": suffix,
            "gamestate_id": game_state.gamestate_id,
            "free_table_info": table,
        })


async def broadcast_game_end(game_state) -> None:
    ranked = sorted(
        game_state.player_list,
        key=lambda p: (-p.score, p.original_player_index),
    )
    player_final_data = {}
    for rank, player in enumerate(ranked, start=1):
        player_final_data[str(rank)] = {
            "rank": rank,
            "score": player.score,
            "pt": 0.0,
            "username": player.username,
            "original_player_index": player.original_player_index,
            "user_id": player.user_id,
        }
    for player in game_state.player_list:
        await send_json(game_state, player.user_id, {
            "type": "gamestate/free/game_end",
            "success": True,
            "message": "game end",
            "gamestate_id": game_state.gamestate_id,
            "game_end_info": {
                "master_seed": str(game_state.master_seed),
                "commitment": str(game_state.commitment),
                "salt": game_state.salt,
                "player_final_data": player_final_data,
            },
        })
