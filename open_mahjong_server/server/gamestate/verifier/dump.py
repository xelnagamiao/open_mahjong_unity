"""把 GuobiaoGameState 收成检查器可用的 JSON。"""
from __future__ import annotations

from typing import Any, Dict, List, Optional

from server.gamestate.public.critical_log import build_gamestate_snapshot
from server.gamestate.public.game_record_manager import jsonable_game_record
from server.gamestate.public.hand_slot_utils import normalize_tile


def _jsonable(value: Any) -> Any:
    if isinstance(value, dict):
        return {str(key): _jsonable(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [_jsonable(item) for item in value]
    if isinstance(value, set):
        return sorted(_jsonable(item) for item in value)
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    return str(value)


def _angang_tiles(player) -> List[int]:
    seen = []
    for tile in getattr(player, "hand_tiles", []) or []:
        if tile in seen:
            continue
        if (getattr(player, "hand_tiles", []) or []).count(tile) >= 4:
            seen.append(tile)
    return seen


def _jiagang_tiles(player) -> List[int]:
    out = []
    hand = getattr(player, "hand_tiles", []) or []
    for combo in getattr(player, "combination_tiles", []) or []:
        if not isinstance(combo, str) or not combo.startswith("k"):
            continue
        try:
            target = normalize_tile(int(combo[1:]))
        except (TypeError, ValueError):
            continue
        if any(normalize_tile(tile) == target for tile in hand) and target not in out:
            out.append(target)
    return out


def dump_gamestate(game, *, extra: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    base = build_gamestate_snapshot(game, event="verifier")
    players: List[Dict[str, Any]] = []
    for player in getattr(game, "player_list", []) or []:
        players.append(
            {
                "index": getattr(player, "player_index", None),
                "original_player_index": getattr(player, "original_player_index", None),
                "user_id": getattr(player, "user_id", None),
                "username": getattr(player, "username", None),
                "hand_tiles": list(getattr(player, "hand_tiles", []) or []),
                "has_draw_slot": bool(getattr(player, "has_draw_slot", False)),
                "last_drawn_tile": getattr(player, "last_drawn_tile", None),
                "discard_tiles": list(getattr(player, "discard_tiles", []) or []),
                "combination_tiles": list(getattr(player, "combination_tiles", []) or []),
                "combination_mask": list(getattr(player, "combination_mask", []) or []),
                "huapai_list": list(getattr(player, "huapai_list", []) or []),
                "score": getattr(player, "score", None),
                "remaining_time": getattr(player, "remaining_time", None),
                "waiting_tiles": sorted(getattr(player, "waiting_tiles", None) or []),
                "tag_list": list(getattr(player, "tag_list", []) or []),
                "score_history": list(getattr(player, "score_history", []) or []),
                "round_number_history": list(getattr(player, "round_number_history", []) or []),
                "angang_tiles": _angang_tiles(player),
                "jiagang_tiles": _jiagang_tiles(player),
            }
        )

    action_dict = {
        str(seat): list(actions or [])
        for seat, actions in (getattr(game, "action_dict", {}) or {}).items()
    }
    payload = {
        **base,
        "dealer_index": getattr(game, "dealer_index", None),
        "current_round": getattr(game, "current_round", None),
        "max_round": getattr(game, "max_round", None),
        "sub_rule": getattr(game, "sub_rule", None),
        "hepai_limit": getattr(game, "hepai_limit", None),
        "tactical_call": getattr(game, "tactical_call", None),
        "server_action_tick": getattr(game, "server_action_tick", None),
        "waiting_players_list": list(getattr(game, "waiting_players_list", []) or []),
        "action_dict": action_dict,
        "tiles_list": list(getattr(game, "tiles_list", []) or []),
        "hu_class": getattr(game, "hu_class", None),
        "debug": bool(getattr(game, "Debug", False)),
        "debug_scenario": getattr(game, "debug_scenario", None),
        "players": players,
        "game_record": jsonable_game_record(getattr(game, "game_record", None) or {}),
    }
    if extra:
        payload["extra"] = extra
    return _jsonable(payload)
