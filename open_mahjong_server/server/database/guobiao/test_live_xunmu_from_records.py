"""新格式牌谱 ticks 走 reconstruct_round_win_turns（内部即 player_index_go_to）。"""
from typing import Any, Dict

from server.database.guobiao.record_analyzer import reconstruct_round_win_turns


def _new_format_sample_round() -> Dict[str, Any]:
    """新格式：reset + bh/bd 带座位 + d/c 无行动者。"""
    return {
        "current_round": 1,
        "seats": [0, 1, 2, 3],
        "start_player_index": 0,
        "dealer_index": 0,
        "action_ticks": [
            ["bh", 51, 0, "F"],
            ["bd", 12, 0],
            ["bh", 52, 2, "F"],
            ["bd", 13, 2],
            ["reset", 0],
            ["c", 11, "F"],
            ["d", 21],
            ["c", 21, "T"],
            ["d", 22],
            ["c", 22, "T"],
            ["d", 23],
            ["p", 23, 1],
            ["c", 24, "F"],
            ["d", 25],
            ["c", 25, "T"],
            ["d", 26],
            ["c", 26, "T"],
            ["d", 27],
            ["hu_self", 0, 8, ["平胡"], [8, -8, 0, 0]],
            ["end"],
        ],
    }


def test_new_format_dealer_zimo_is_xunmu_3():
    assert reconstruct_round_win_turns(_new_format_sample_round())[0] == 3


def _real_new_format_round_bwtc57fs2k() -> Dict[str, Any]:
    """2026-08-12 对局 BwTc57fS2K 第 1 局 ticks（新格式：seats + reset + bd 带座位）。"""
    return {
        "current_round": 1,
        "seats": [0, 1, 2, 3],
        "start_player_index": 0,
        "dealer_index": 0,
        "action_ticks": [
            ["bh", 54, 1, "F"],
            ["bd", 26, 1],
            ["bh", 57, 2, "F"],
            ["bd", 17, 2],
            ["bh", 51, 2, "F"],
            ["bd", 14, 2],
            ["bh", 58, 3, "F"],
            ["bd", 11, 3],
            ["reset", 0],
            ["c", 47, "F"],
            ["d", 56],
            ["bh", 56, 1, "T"],
            ["bd", 24, 1],
            ["c", 46, "F"],
            ["p", 46, 3, 46, 46],
            ["c", 26, "F"],
            ["d", 16],
            ["c", 44, "F"],
            ["d", 32],
            ["c", 44, "F"],
            ["d", 25],
            ["c", 33, "F"],
            ["d", 44],
            ["c", 44, "T"],
            ["d", 18],
            ["c", 38, "F"],
            ["d", 35],
            ["hu_self", 1, 11, ["平胡"], [-19, 57, -19, -19], 35],
            ["end"],
        ],
    }


def test_real_new_format_round_south_zimo_is_xunmu_3():
    assert reconstruct_round_win_turns(_real_new_format_round_bwtc57fs2k())[1] == 3


def test_real_round_without_reset_undercounts():
    """对照：故意剥掉 reset 后，补花后指针留在北家，少计 1 巡。
    生产库已在 2026-08-31 用 guobiao_record_reset_xunmu_v2 回填，现存国标小局都有 reset。
    """
    rd = _real_new_format_round_bwtc57fs2k()
    stripped = {
        **rd,
        "action_ticks": [t for t in rd["action_ticks"] if t[0] != "reset"],
    }
    assert reconstruct_round_win_turns(stripped)[1] == 2


def test_full_cycle_without_claim_is_xunmu_2():
    rd = {
        "start_player_index": 0,
        "seats": [0, 1, 2, 3],
        "action_ticks": [
            ["reset", 0],
            ["c", 11, "F"],
            ["d", 12],
            ["c", 12, "T"],
            ["d", 13],
            ["c", 13, "T"],
            ["d", 14],
            ["c", 14, "T"],
            ["d", 15],
            ["hu_self", 0, 8, ["平胡"], [8, 0, 0, 0]],
        ],
    }
    assert reconstruct_round_win_turns(rd)[0] == 2
