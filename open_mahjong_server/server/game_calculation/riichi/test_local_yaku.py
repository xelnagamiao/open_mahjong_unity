import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3]))

from server.game_calculation.riichi.riichi_hepai_check import Riichi_Hepai_Check


def tile_id(suit, text):
    base = {"m": 11, "p": 21, "s": 31}[suit]
    return [base + int(char) - 1 for char in text]


def check(hand, *, flags=None, combinations=None, win_tile=None, is_tsumo=True):
    checker = Riichi_Hepai_Check()
    win_tile = win_tile if win_tile is not None else hand[-1]
    context = {
        "is_tsumo": is_tsumo,
        "player_wind": 1,
        "round_wind": 0,
        "dora_indicators": [],
    }
    context.update(flags or {})
    return checker.hepai_check(hand, combinations or [], [], win_tile, context)


def test_three_identical_sequences_replaces_peikou():
    result = check(
        tile_id("m", "123123123") + tile_id("p", "55") + tile_id("s", "789"),
        flags={"local_three_identical_sequences": True},
    )
    assert result["is_valid"]
    assert "一色三同顺" in result["yaku"]
    assert "一杯口" not in result["yaku"] and "二杯口" not in result["yaku"]
    assert result["yaku_han"] == 5  # 门前清自摸和 + 平和 + 一色三同顺


def test_daisharin_is_a_single_yakuman():
    result = check(tile_id("p", "22334455667788"), flags={"local_daisharin": True})
    assert result["is_valid"] and result["yaku"] == ["大车轮"]
    assert result["yakuman_multiplier"] == 1


def test_event_local_yaku_requires_the_event_flag():
    hand = [11, 11, 11, 22, 23, 24, 32, 33, 34, 36, 37, 38, 45, 45]
    result = check(
        hand,
        is_tsumo=False,
        flags={"local_riichi_discard_ron": True, "is_riichi_declaration_ron": True},
    )
    assert result["is_valid"] and result["yaku"] == ["燕返"]


def test_twelve_falling_pillows_requires_four_open_melds_and_tanki():
    result = check(
        [22, 22],
        combinations=["k11", "k21", "k31", "k41"],
        flags={"local_four_open_melds": True},
    )
    assert result["is_valid"] and "十二落抬" in result["yaku"]
