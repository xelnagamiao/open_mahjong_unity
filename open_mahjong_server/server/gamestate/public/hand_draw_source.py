"""Authoritative draw source for the current hand-action decision window."""

from typing import Iterable, Optional


DEAL_ACTIONS = frozenset({"deal_tile", "deal_gang_tile", "deal_buhua_tile"})


def reset_hand_draw_source(game_state) -> None:
    game_state._hand_draw_source = None
    game_state._hand_draw_player_index = None
    game_state._hand_draw_source_round = getattr(game_state, "current_round", None)


def ensure_hand_draw_source_round(game_state) -> None:
    """Reset at a new hand, but preserve a still-open decision during reconnect."""
    status = getattr(game_state, "game_status", None)
    current_player = getattr(game_state, "current_player_index", None)
    actions = getattr(game_state, "action_dict", {}) or {}
    reconnecting_open_hand = (
        status == "waiting_hand_action"
        and current_player is not None
        and bool(actions.get(current_player, []))
    )
    if not reconnecting_open_hand:
        reset_hand_draw_source(game_state)


def update_hand_draw_source(
    game_state,
    action_list: Iterable[str],
    action_player: int,
    *,
    is_claim: bool = False,
) -> None:
    """Advance the source alongside real actions; claim previews do not change state."""
    if is_claim:
        return
    source = next((action for action in reversed(list(action_list)) if action in DEAL_ACTIONS), None)
    game_state._hand_draw_source = source
    game_state._hand_draw_player_index = action_player if source is not None else None


def get_hand_draw_source(game_state, player_index: int) -> Optional[str]:
    if getattr(game_state, "_hand_draw_player_index", None) != player_index:
        return None
    source = getattr(game_state, "_hand_draw_source", None)
    return source if source in DEAL_ACTIONS else None
