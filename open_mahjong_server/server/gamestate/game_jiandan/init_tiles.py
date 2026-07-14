from __future__ import annotations

import random


STANDARD_TILES = tuple(
    list(range(11, 20))
    + list(range(21, 30))
    + list(range(31, 40))
    + [41, 42, 43, 44, 45, 46, 47]
)


def build_wall() -> list[int]:
    wall: list[int] = []
    for tile in STANDARD_TILES:
        wall.extend([tile] * 4)
    return wall


def init_jiandan_tiles(game_state) -> None:
    """Initialize a 136-tile no-flower live wall and deal the opening hands."""
    game_state.tiles_list = build_wall()
    rng = random.Random(game_state.round_random_seed)
    rng.shuffle(game_state.tiles_list)

    for player in game_state.player_list:
        player.hand_tiles.clear()
        for _ in range(13):
            player.get_tile(game_state.tiles_list, mark_draw_slot=False)

    # Dealer starts with 14 tiles. This matches Guobiao-style opening while the
    # The action policy decides whether the first action is win/kong/discard.
    game_state.player_list[game_state.dealer_index].get_tile(game_state.tiles_list, mark_draw_slot=False)
