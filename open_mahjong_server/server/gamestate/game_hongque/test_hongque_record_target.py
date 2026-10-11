from types import SimpleNamespace

import pytest

from .record import record_kong


@pytest.mark.parametrize('index', [0, 1, 2, 3])
@pytest.mark.parametrize('drawn', [None, 'AY5'])
def test_kong_record_explicit_target(index, drawn):
    state = SimpleNamespace(
        game_record={'game_title': {'rule': 'hongque'}, 'game_round': {'round_index_1': {'action_ticks': []}}},
        round_index=1, player_action_tick=0,
    )
    record_kong(state, SimpleNamespace(drawn_tile=drawn), {'hand_tiles': ['AY5'], 'meld_index': index})
    assert state.game_record['game_round']['round_index_1']['action_ticks'] == [
        ['jg', 1015, 'T' if drawn else 'F', index]
    ]
