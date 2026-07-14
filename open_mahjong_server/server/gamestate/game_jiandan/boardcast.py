from __future__ import annotations

from typing import Any, Optional

from ..public.deal_tile_view import sanitize_deal_tile_for_viewer


def _is_full_concealed_kong_mask(mask: Optional[list[int]]) -> bool:
    if not mask or len(mask) != 8:
        return False
    return all(mask[i] == 2 for i in range(0, 8, 2))


def _public_concealed_kong_mask(
    mask: Optional[list[int]],
    owner_index: Optional[int],
    viewer_index: Optional[int],
    *,
    reveal_final: bool = False,
) -> Optional[list[int]]:
    if mask is None:
        return None
    if reveal_final or owner_index == viewer_index or not _is_full_concealed_kong_mask(mask):
        return list(mask)
    public_mask = list(mask)
    for i in range(1, len(public_mask), 2):
        public_mask[i] = 0
    return public_mask


def public_melds_for_viewer(player: Any, viewer_index: Optional[int], *, reveal_final: bool = False) -> list[str]:
    melds: list[str] = []
    for meld in player.combination_tiles:
        if isinstance(meld, str) and meld.startswith("G") and not reveal_final and viewer_index != player.player_index:
            melds.append("G0")
        else:
            melds.append(meld)
    return melds


def public_combination_masks_for_viewer(
    player: Any,
    viewer_index: Optional[int],
    *,
    reveal_final: bool = False,
) -> list[list[int]]:
    return [
        _public_concealed_kong_mask(
            mask,
            player.player_index,
            viewer_index,
            reveal_final=reveal_final,
        )
        for mask in player.combination_mask
    ]


def _safe_room_id(room_id: Any) -> int:
    try:
        return int(room_id)
    except (TypeError, ValueError):
        return 0


def player_info_payload(
    game_state: Any,
    player_index: int,
    viewer_index: Optional[int],
    *,
    reveal_final: bool = False,
) -> dict:
    player = game_state.player_list[player_index]
    include_hand = reveal_final or viewer_index == player_index
    hand_tiles = list(player.hand_tiles) if include_hand else None
    return {
        "username": player.username,
        "user_id": player.user_id,
        "hand_tiles_count": len(player.hand_tiles),
        "hand_tiles": hand_tiles,
        "discard_tiles": list(player.discard_tiles),
        "discard_origin_tiles": list(player.discard_origin_tiles),
        "combination_tiles": public_melds_for_viewer(player, viewer_index, reveal_final=reveal_final),
        "combination_mask": public_combination_masks_for_viewer(player, viewer_index, reveal_final=reveal_final),
        "remaining_time": player.remaining_time,
        "player_index": player_index,
        "original_player_index": player.original_player_index,
        "score": player.score,
        "huapai_list": [],
        "title_used": player.title_used,
        "character_used": player.character_used,
        "profile_used": player.profile_used,
        "voice_used": player.voice_used,
        "score_history": list(player.score_history),
        "round_number_history": list(player.round_number_history),
        "tag_list": list(player.tag_list),
        "discard_riichi_flags": [],
        "dingque_suit": 0,
        "is_hu": player.is_hu,
    }


def game_info_payload(game_state: Any, viewer_index: Optional[int], *, reveal_final: bool = False) -> dict:
    return {
        "room_id": _safe_room_id(game_state.room_id),
        "gamestate_id": game_state.gamestate_id,
        "tips": game_state.tips,
        "current_player_index": game_state.current_player_index,
        "action_tick": game_state.server_action_tick,
        "max_round": game_state.max_round,
        "tile_count": len(game_state.tiles_list),
        "commitment": game_state.commitment,
        "salt": game_state.salt,
        "current_round": game_state.current_round,
        "step_time": game_state.step_time,
        "round_time": game_state.round_time,
        "room_type": game_state.room_type,
        "room_rule": game_state.room_rule,
        "sub_rule": game_state.sub_rule,
        "hepai_limit": 0,
        "open_cuohe": False,
        "show_moqie_hint": False,
        "tactical_call": game_state.tactical_call,
        "claim_protection": game_state.claim_protection,
        "isPlayerSetRandomSeed": game_state.isPlayerSetRandomSeed,
        "player_entry_order": [player.user_id for player in game_state.player_list],
        "players_info": [
            player_info_payload(game_state, idx, viewer_index, reveal_final=reveal_final)
            for idx in range(len(game_state.player_list))
        ],
        "self_hand_tiles": list(game_state.player_list[viewer_index].hand_tiles)
        if viewer_index is not None and viewer_index in range(len(game_state.player_list))
        else None,
        "dealer_index": game_state.dealer_index,
        "view_player_index": viewer_index,
    }


def game_start_payload(game_state: Any, viewer_index: int, *, reveal_final: bool = False) -> dict:
    if reveal_final:
        game_state.apply_deferred_score_changes()
    return {
        "type": "gamestate/jiandan/game_start",
        "success": True,
        "player_index": viewer_index,
        "message": "game start",
        "game_info": game_info_payload(game_state, viewer_index, reveal_final=reveal_final),
    }


def ask_action_payload(
    game_state: Any,
    viewer_index: int,
    action_list: list[str],
    *,
    action_player_index: Optional[int] = None,
    cut_tile: Optional[int] = None,
    rob_kong_tile: Optional[int] = None,
) -> dict:
    is_hand_action = cut_tile is None and rob_kong_tile is None
    if action_player_index is None:
        action_player_index = viewer_index
    return {
        "type": "gamestate/jiandan/broadcast_hand_action" if is_hand_action else "gamestate/jiandan/ask_other_action",
        "success": True,
        "game_info": game_info_payload(game_state, viewer_index),
        "player_index": viewer_index,
        "action_list": list(action_list),
        "action_tick": game_state.server_action_tick,
        "cut_tile": cut_tile,
        "rob_kong_tile": rob_kong_tile,
        "ask_hand_action_info": {
            "action_list": list(action_list),
            "remaining_time": game_state.player_list[viewer_index].remaining_time,
            "player_index": action_player_index,
            "remain_tiles": len(game_state.tiles_list),
            "action_tick": game_state.server_action_tick,
        } if is_hand_action else None,
        "ask_other_action_info": {
            "action_list": list(action_list),
            "remaining_time": game_state.player_list[viewer_index].remaining_time,
            "cut_tile": cut_tile if cut_tile is not None else rob_kong_tile,
            "action_tick": game_state.server_action_tick,
        } if cut_tile is not None or rob_kong_tile is not None else None,
    }


def pending_action_payload(game_state: Any, viewer_index: int) -> Optional[dict]:
    action_list = list(game_state.action_dict.get(viewer_index, []))
    if not action_list:
        return None

    status = getattr(game_state, "game_status", None)
    pending_window = getattr(game_state, "live_pending_window", None) or {}
    cut_tile = pending_window.get("tile") if status == "waiting_action_after_cut" else None
    rob_kong_tile = pending_window.get("tile") if status == "waiting_action_qianggang" else None
    return ask_action_payload(
        game_state,
        viewer_index,
        action_list,
        cut_tile=cut_tile,
        rob_kong_tile=rob_kong_tile,
    )


def visible_action_payload(
    game_state: Any,
    viewer_index: Optional[int],
    action_info: dict,
    *,
    reveal_final: bool = False,
) -> dict:
    action = action_info.get("action")
    actor = action_info.get("player")
    deal_tile = action_info.get("tile") if action in {"deal_tile", "deal_gang_tile", "deal_buhua_tile"} else None
    viewer_deal_tile = sanitize_deal_tile_for_viewer(deal_tile, actor, viewer_index) if actor is not None and viewer_index is not None else deal_tile
    public_tile = viewer_deal_tile if deal_tile is not None else action_info.get("tile")
    combination_mask = _public_concealed_kong_mask(
        action_info.get("combination_mask"),
        actor,
        viewer_index,
        reveal_final=reveal_final,
    )
    combination_target = action_info.get("meld_code")
    if action == "angang" and not reveal_final and viewer_index != actor:
        combination_target = "G0"
    payload = {
        "type": "gamestate/jiandan/do_action",
        "success": True,
        "action": action,
        "player": actor,
        "tile": public_tile,
        "game_info": game_info_payload(game_state, viewer_index, reveal_final=reveal_final),
        "do_action_info": {
            "action_list": [action] if action else [],
            "action_player": actor if actor is not None else -1,
            "action_tick": game_state.server_action_tick,
            "cut_tile": action_info.get("tile") if action == "cut" else None,
            "cut_tile_index": action_info.get("cutIndex"),
            "cut_class": action_info.get("cutClass", False),
            "deal_tile": viewer_deal_tile,
            "combination_target": combination_target,
            "combination_mask": combination_mask,
            "is_mo_gang": action_info.get("is_mo_gang", action == "angang"),
            "cut_from_player": action_info.get("cut_from_player"),
        },
    }

    if action == "angang" and not reveal_final and viewer_index != actor:
        payload["meld_code"] = "G0"
    elif "meld_code" in action_info:
        payload["meld_code"] = action_info["meld_code"]

    if action in {"hu", "hu_self", "rob_kong"} and not reveal_final:
        if action == "hu_self" and viewer_index != actor:
            payload["tile"] = None
        payload["fan_ids"] = None
        payload["fan_names"] = None
        payload["points"] = None
    else:
        for key in ("fan_ids", "fan_names", "points", "score_changes"):
            if key in action_info:
                payload[key] = action_info[key]
    return payload


def _final_scores(game_state: Any) -> dict[int, int]:
    return {
        player.player_index: player.score
        for player in game_state.player_list
    }


def _total_score_changes(game_state: Any) -> dict[int, int]:
    totals = {player.player_index: 0 for player in game_state.player_list}
    for settlement in getattr(game_state, "deferred_hu_settlements", []):
        changes = settlement.get("score_changes") or []
        for player_index, change in enumerate(changes):
            if player_index in totals:
                totals[player_index] += change
    return totals


def _settlement_score_changes(game_state: Any, settlement: dict) -> dict[int, int]:
    changes = settlement.get("score_changes") or []
    return {
        # The shared result UI resolves score_changes by the stable identity
        # index.  This differs from player_to_score, whose keys are the
        # current hand's seats.  Keeping that contract matters after seats
        # rotate, when player_index and original_player_index no longer match.
        player.original_player_index: changes[player.player_index]
        if player.player_index < len(changes)
        else 0
        for player in game_state.player_list
    }


def _score_changes_through(game_state: Any, settlement_index: int) -> dict[int, int]:
    totals = {player.player_index: 0 for player in game_state.player_list}
    settlements = list(getattr(game_state, "deferred_hu_settlements", []))
    for settlement in settlements[: settlement_index + 1]:
        changes = settlement.get("score_changes") or []
        for player_index, change in enumerate(changes):
            if player_index in totals:
                totals[player_index] += change
    return totals


def _scores_after_settlement(game_state: Any, settlement_index: int) -> dict[int, int]:
    final_scores = _final_scores(game_state)
    total_changes = _total_score_changes(game_state)
    panel_changes = _score_changes_through(game_state, settlement_index)
    return {
        player_index: final_scores[player_index] - total_changes.get(player_index, 0) + panel_changes.get(player_index, 0)
        for player_index in final_scores
    }


def _final_show_result_info(game_state: Any, settlement_index: Optional[int] = None) -> dict:
    settlements = list(getattr(game_state, "deferred_hu_settlements", []))
    player_to_score = _final_scores(game_state)
    score_changes = _total_score_changes(game_state)
    action_tick = getattr(game_state, "server_action_tick", 0)

    if not settlements:
        return {
            "hepai_player_index": -1,
            "player_to_score": player_to_score,
            "hu_score": 0,
            "hu_fan": [],
            "hu_class": "liuju",
            "hepai_player_hand": [],
            "hepai_player_huapai": [],
            "hepai_player_combination_mask": [],
            "action_tick": action_tick,
            "score_changes": score_changes,
        }

    if settlement_index is None:
        settlement_index = len(settlements) - 1
    settlement = settlements[settlement_index]
    player_to_score = _scores_after_settlement(game_state, settlement_index)
    score_changes = _settlement_score_changes(game_state, settlement)
    winner_index = settlement.get("winner", -1)
    winner = game_state.player_list[winner_index] if winner_index in range(len(game_state.player_list)) else None
    hand = list(winner.hand_tiles) if winner is not None else []
    win_tile = settlement.get("tile")
    if settlement.get("source") != "self_draw" and win_tile is not None:
        hand = hand + [win_tile]
    source = settlement.get("source")
    hu_class = _settlement_hu_class(settlement)

    return {
        "hepai_player_index": winner_index,
        "player_to_score": player_to_score,
        # The ordinary client has no Jiandan-specific display multiplier.
        # Send the actual six-points-per-fan payment value for display while
        # keeping detailed fan identities in hu_fan.
        "hu_score": int(settlement.get("points", 0) or 0) * 6,
        "hu_fan": list(settlement.get("fan_ids") or settlement.get("fan_names") or []),
        "hu_class": hu_class,
        "hepai_player_hand": hand,
        "hepai_player_huapai": [],
        "hepai_player_combination_mask": list(winner.combination_mask) if winner is not None else [],
        "action_tick": action_tick,
        "score_changes": score_changes,
        "hepai_tile": win_tile,
        "is_qianggang": source == "rob_kong",
        "ron_discarder_index": _settlement_payer_index(settlement),
    }


def _settlement_payer_index(settlement: dict) -> Optional[int]:
    payer_index = settlement.get("discarder")
    if payer_index is not None:
        return payer_index
    return settlement.get("kong_player")


def _settlement_hu_class(settlement: dict) -> str:
    if settlement.get("source") == "self_draw":
        return "hu_self"
    payer_index = _settlement_payer_index(settlement)
    winner_index = settlement.get("winner")
    if payer_index is None or winner_index is None:
        return "hu_first"
    distance = (int(winner_index) - int(payer_index)) % 4
    if distance == 2:
        return "hu_second"
    if distance == 3:
        return "hu_third"
    return "hu_first"


def final_settlement_payload(game_state: Any, viewer_index: Optional[int]) -> dict:
    game_state.apply_deferred_score_changes()
    return {
        "type": "gamestate/jiandan/show_result",
        "success": True,
        "player_index": viewer_index,
        "game_info": game_info_payload(game_state, viewer_index, reveal_final=True),
        "show_result_info": _final_show_result_info(game_state),
    }


def final_settlement_payloads(game_state: Any, viewer_index: Optional[int]) -> list[dict]:
    """Return exactly one ordinary result payload for the first-win hand."""
    return [final_settlement_payload(game_state, viewer_index)]


def ready_status_payload(game_state: Any, viewer_index: Optional[int]) -> dict:
    player_to_ready = {}
    for player in game_state.player_list:
        pending = game_state.action_dict.get(player.player_index, [])
        player_to_ready[player.player_index] = "ready" not in pending
    return {
        "type": "gamestate/jiandan/ready_status",
        "success": True,
        "player_index": viewer_index,
        "message": "ready status update",
        "ready_status_info": {
            "player_to_ready": player_to_ready,
        },
    }


def game_end_payload(game_state: Any, viewer_index: Optional[int]) -> dict:
    player_final_data = {}
    for player in game_state.player_list:
        player_final_data[str(player.player_index)] = {
            "rank": player.record_counter.rank_result,
            "score": player.score,
            "pt": 0,
            "username": player.username,
            "original_player_index": player.original_player_index,
        }
    return {
        "type": "gamestate/jiandan/game_end",
        "success": True,
        "player_index": viewer_index,
        "message": "game end",
        "game_end_info": {
            "master_seed": str(getattr(game_state, "master_seed", "")),
            "commitment": str(getattr(game_state, "commitment", "")),
            "salt": str(getattr(game_state, "salt", "")),
            "player_final_data": player_final_data,
        },
    }
