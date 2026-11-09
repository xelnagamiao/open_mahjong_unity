"""Every played hand gets a fresh, reproducible wall, including dealer repeats."""

import pytest

from .BeijingGameState import BeijingGameState
from .state_machine import Phase as P


def wall_sequence(seed):
    room = dict(BeijingGameState._default_room_data(), random_seed=seed)
    game = BeijingGameState(room_data=room, calculation_service=object())
    snapshots = []
    for winners in ([0], [0], [1], []):
        game.initialize_round()
        snapshots.append((game.round_random_seed, tuple(game.dice), game.indicator,
                          tuple(tuple(p.hand_tiles) for p in game.player_list), tuple(game.tiles_list)))
        game.open_action_window(game.begin_turn(0))
        game.finish_round(winners)
        game.machine.transition(P.READY)
        game.advance_round_after_ready()
    return snapshots


@pytest.mark.parametrize("seed", [1, 2, 20261103, 0xDEADBEEF])
def test_repeated_dealer_walls_are_fresh_and_match_seed_reproduces_sequence(seed):
    snapshots = wall_sequence(seed)
    assert snapshots == wall_sequence(seed)
    assert len({s[0] for s in snapshots}) == len(snapshots)
    assert len({s[4] for s in snapshots}) == len(snapshots)


def test_different_match_seeds_produce_different_walls():
    assert wall_sequence(1) != wall_sequence(2)
