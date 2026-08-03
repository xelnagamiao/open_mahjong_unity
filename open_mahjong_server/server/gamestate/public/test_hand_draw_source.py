from types import SimpleNamespace

from .hand_draw_source import (
    ensure_hand_draw_source_round,
    get_hand_draw_source,
    reset_hand_draw_source,
    update_hand_draw_source,
)


def test_draw_source_survives_reconnect_but_resets_on_new_round():
    state = SimpleNamespace(current_round=1, game_status="waiting", current_player_index=2, action_dict={})
    ensure_hand_draw_source_round(state)
    update_hand_draw_source(state, ["deal_gang_tile"], 2)

    # Re-emitting game_start for the same round must preserve the pending window.
    state.game_status = "waiting_hand_action"
    state.action_dict = {2: ["cut"]}
    ensure_hand_draw_source_round(state)
    assert get_hand_draw_source(state, 2) == "deal_gang_tile"

    state.game_status = "waiting"
    state.action_dict = {}
    ensure_hand_draw_source_round(state)
    assert get_hand_draw_source(state, 2) is None


def test_non_deal_action_consumes_previous_window_source():
    state = SimpleNamespace(current_round=1)
    reset_hand_draw_source(state)
    update_hand_draw_source(state, ["deal_gang_tile"], 0)
    assert get_hand_draw_source(state, 0) == "deal_gang_tile"

    update_hand_draw_source(state, ["cut"], 0)
    assert get_hand_draw_source(state, 0) is None


def test_batched_flower_replacement_uses_last_deal_action():
    state = SimpleNamespace(current_round=1)
    reset_hand_draw_source(state)
    update_hand_draw_source(state, ["buhua", "deal_buhua_tile"], 1)
    assert get_hand_draw_source(state, 1) == "deal_buhua_tile"
