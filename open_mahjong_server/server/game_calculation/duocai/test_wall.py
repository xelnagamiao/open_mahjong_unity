from collections import Counter

import pytest

from . import tiles as t
from .wall import TwinDecks


@pytest.mark.parametrize('seed', range(12))
def test_alternating_decks_replace_only_revealed_without_leaking_next_deck(seed):
    decks = TwinDecks(seed)
    first_selected = [set(v for v in pack if t.joker(v)) for pack in decks.packs]
    for n in range(4):
        wall = decks.next_wall()
        assert wall.deck == n % 2
        assert len(wall.public()) == 144
        assert Counter(v['back'] for v in wall.public()) == {n % 2: 131, 1 - n % 2: 13}
        assert all(v['tile'] is None for v in wall.public())
        drawn = [wall.draw()[0] for _ in range(70)]
        remaining = {wall.tiles[i] for i in wall.live if t.joker(wall.tiles[i])}
        removed = set(ti for ti in drawn if t.joker(ti))
        result = decks.replace_revealed(wall.revealed_jokers())
        assert result['removed'] == sorted(removed)
        assert remaining <= set(decks.packs[n % 2])
        assert removed.isdisjoint(decks.packs[n % 2])
        assert removed.isdisjoint(decks.reserves[n % 2])
        if n == 0:
            assert first_selected[1] == {v for v in decks.packs[1] if t.joker(v)}


def test_insufficient_reserve_ends_match_atomically():
    decks = TwinDecks(4)
    wall = decks.next_wall()
    removed = [v for v in wall.tiles if t.joker(v)]
    before = list(decks.packs[0])
    decks.reserves[0] = decks.reserves[0][:7]
    assert decks.replace_revealed(removed)['exhausted']
    assert decks.packs[0] == before
    with pytest.raises(ValueError):
        decks.next_wall()


def test_four_tile_kong_draws_rinshan_and_fifth_tile_kong_draws_front():
    wall = TwinDecks(5).next_wall(dice=[2, 3])
    wall.set_riichi()
    assert len(wall.live) == 130 and len(wall.indicators()) == 1
    for _ in range(53):
        wall.draw()
    start = len(wall.live)
    dead_draw = wall.dead[0]
    assert wall.draw(fourth_kong=True)[1] == dead_draw
    assert len(wall.live) == start - 1
    assert len(wall.indicators()) == len(wall.indicators(ura=True)) == 2
    front = wall.live[0]
    assert wall.draw()[1] == front
    assert len(wall.indicators()) == 2
    visible = [entry for entry in wall.public() if entry['tile'] is not None]
    assert len(visible) == 2


def test_wall_entity_conservation_and_secret_dead_wall():
    wall = TwinDecks(8).next_wall()
    wall.set_riichi()
    drawn = [wall.draw()[0] for _ in range(53)]
    for _ in range(4):
        drawn.append(wall.draw(fourth_kong=True)[0])
    while wall.live:
        drawn.append(wall.draw()[0])
    hidden = [v for i, v in enumerate(wall.tiles) if i not in wall.removed]
    assert Counter(drawn + hidden) == Counter(wall.tiles)
    assert len(hidden) == 14
    assert len([v for v in wall.public() if v['tile'] is not None]) == 5
