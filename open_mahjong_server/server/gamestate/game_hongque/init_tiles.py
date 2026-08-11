"""虹雀牌山与开局发牌组件。"""
from __future__ import annotations

from .hongque_debug import (
    apply_debug_player_seating,
    apply_hongque_debug_hands,
    get_debug_dealer_index,
)
from .tile import full_deck
from .state_machine import HongqueStatus


async def init_hongque_tiles(game_state) -> None:
    game_state._cancel_bot_claim_tasks()
    game_state._ready_phase_active = False
    game_state._ready_players.clear()
    game_state._ready_event.clear()
    if game_state.Debug:
        apply_debug_player_seating(game_state)
        game_state.dealer_index = get_debug_dealer_index(game_state)

    game_state.wall = full_deck()
    game_state._rng.shuffle(game_state.wall)
    for player in game_state.players:
        player.hand.clear()
        player.discards.clear()
        player.melds.clear()
        player.supplements = 0
        player.drawn_tile = None
        player.last_draw_was_supplement = False
        player.remaining_time = game_state.round_time
    for _ in range(11):
        for offset in range(4):
            game_state.players[(game_state.dealer_index + offset) % 4].hand.append(
                game_state.wall.pop()
            )

    game_state.current_player_index = game_state.dealer_index
    game_state.last_discard = None
    game_state.claim_options.clear()
    game_state.claim_responses.clear()
    game_state.claim_window = None
    game_state._claim_apply_broadcast.clear()
    game_state.round_result = None
    game_state.events = []
    game_state._transition(HongqueStatus.WAITING_HAND_ACTION)
    game_state._start_turn_clock()
    draw_for_current_player(game_state)
    if game_state.Debug:
        apply_hongque_debug_hands(game_state)
    game_state.message = f"第 {game_state.current_round} 局开始"
    game_state._advance_tick()
    await game_state.broadcast_state(sync_mode="round_start")
    game_state._schedule_turn_timeout()
    game_state._schedule_bot_if_needed()


def draw_for_current_player(game_state, reason: str = "draw"):
    if not game_state.wall:
        return None
    player = game_state.players[game_state.current_player_index]
    tile = game_state.wall.pop()
    player.hand.append(tile)
    player.drawn_tile = tile
    player.last_draw_was_supplement = reason == "supplement"
    game_state._record_event(reason, player=player.index, tile=tile)
    return tile
