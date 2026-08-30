"""和巡重建与牌谱分析。"""
from server.database.guobiao.record_analyzer import (
    analyze_record_for_player,
    reconstruct_round_win_turns,
)
from server.database.guobiao.round_score_utils import infer_guobiao_seats, resolve_round_seats


def test_north_first_cycle_ron_is_xunmu_1():
    rd = {
        "start_player_index": 0,
        "action_ticks": [
            ["reset", 0],
            ["c", 11, "F"],
            ["d", 12],
            ["c", 12, "F"],
            ["d", 13],
            ["c", 13, "F"],
            ["d", 14],
            ["c", 14, "F"],
            ["hu_first", 0, 8, ["平胡"], [8, 0, 0, -8]],
        ],
    }
    assert reconstruct_round_win_turns(rd)[0] == 1


def test_dealer_second_cycle_zimo_is_xunmu_2():
    rd = {
        "start_player_index": 0,
        "action_ticks": [
            ["reset", 0],
            ["c", 11, "F"],
            ["d", 12],
            ["c", 12, "F"],
            ["d", 13],
            ["c", 13, "F"],
            ["d", 14],
            ["c", 14, "F"],
            ["d", 15],
            ["hu_self", 0, 8, ["平胡"], [8, -8, 0, 0]],
        ],
    }
    assert reconstruct_round_win_turns(rd)[0] == 2


def test_south_peng_north_then_win_increments():
    rd = {
        "start_player_index": 0,
        "action_ticks": [
            ["reset", 0],
            ["c", 11, "F"],
            ["d", 12],
            ["c", 12, "F"],
            ["d", 13],
            ["c", 13, "F"],
            ["d", 14],
            ["p", 14, 1, 14, 14],
            ["hu_self", 1, 8, ["平胡"], [0, 8, 0, 0]],
        ],
    }
    assert reconstruct_round_win_turns(rd)[1] == 2


def test_opening_flowers_then_reset_keep_xunmu_1():
    rd = {
        "start_player_index": 0,
        "action_ticks": [
            ["bh", 51, 0, "F"],
            ["bd", 12, 0],
            ["bh", 52, 3, "F"],
            ["bd", 13, 3],
            ["reset", 0],
            ["c", 11, "F"],
            ["hu_first", 1, 8, ["平胡"], [0, 8, 0, 0]],
        ],
    }
    assert reconstruct_round_win_turns(rd)[1] == 1


def test_infer_guobiao_seats_matches_switchseat():
    assert infer_guobiao_seats(1) == [0, 1, 2, 3]
    assert infer_guobiao_seats(2) == [3, 0, 1, 2]
    assert infer_guobiao_seats(4) == [1, 2, 3, 0]
    assert infer_guobiao_seats(5) == [1, 0, 3, 2]
    assert infer_guobiao_seats(9) == [3, 2, 0, 1]
    assert infer_guobiao_seats(13) == [2, 3, 1, 0]


def test_resolve_round_seats_prefers_explicit():
    assert resolve_round_seats({"seats": [2, 3, 0, 1], "current_round": 2}) == [2, 3, 0, 1]


def test_missing_seats_round2_win_turn_uses_rotated_seat():
    record = {
        "game_title": {"rule": "guobiao"},
        "game_round": {
            "round_index_2": {
                "current_round": 2,
                "action_ticks": [
                    ["reset", 0],
                    ["c", 11, "F"],
                    ["hu_first", 3, 8, ["平胡"], [-8, 0, 0, 8]],
                ],
            }
        },
    }
    original_east = analyze_record_for_player(record, 0)
    assert original_east["dianhe"] == 1
    assert original_east["win_turn"] == 1
    assert analyze_record_for_player(record, 1)["dianhe"] == 0


def test_hu_seat_as_string_still_counts_win_turn():
    rd = {
        "start_player_index": 0,
        "action_ticks": [
            ["reset", 0],
            ["c", 11, "F"],
            ["hu_first", "1", 8, ["平胡"], [0, 8, 0, -8]],
        ],
    }
    assert reconstruct_round_win_turns(rd)[1] == 1
