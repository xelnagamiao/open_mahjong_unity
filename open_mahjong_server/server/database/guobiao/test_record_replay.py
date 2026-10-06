"""Pure record reconstruction contracts, retained after completed migrations are archived."""
import copy
from ..test_rule_ratings import USERS
from .record_replay import replay_win_turns

def record():
    return {
        "game_title": {"rule": "guobiao", "sub_rule": "guobiao/standard", "hepai_limit": 8,
                       **{f"p{i}_uid": uid for i, uid in enumerate(USERS)}},
        "game_round": {"round_index_1": {"current_round": 1, "seats": [0, 1, 2, 3],
            "action_ticks": [["reset", 0], ["c", 11, "F"], ["p", 11, 1, 11, 11],
                ["c", 12, "F"], ["d", 13], ["c", 13, "F"], ["d", 14], ["c", 14, "F"],
                ["d", 15], ["hu_self", 0, 8, ["平胡"], [24, -8, -8, -8]], ["end"]]}}
    }


def test_replay_tracks_claimed_discards_and_rotated_original_players():
    data = record()
    rd = copy.deepcopy(data["game_round"]["round_index_1"])
    rd["current_round"], rd["seats"] = 2, [3, 0, 1, 2]
    data["game_round"]["round_index_2"] = rd
    assert replay_win_turns(data)[1:] == (2, [2, 2, 0, 0], [1, 1, 0, 0])


def test_missing_reset_is_normalized_without_modifying_source_and_cuohe_is_excluded():
    data = record()
    ticks = data["game_round"]["round_index_1"]["action_ticks"]
    ticks[:1] = [["bh", 51, 3, "F"], ["bd", 14, 3]]
    ticks.insert(-1, ["hu_first", 1, 8, ["错和"], [-8, 24, -8, -8]])
    before = copy.deepcopy(data)
    assert replay_win_turns(data)[1:] == (1, [2, 0, 0, 0], [1, 0, 0, 0])
    assert data == before
