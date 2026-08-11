from pathlib import Path

import pytest

from server.gamestate.game_hongque.rules import (
    call_candidates,
    classify_meld,
    kong_candidates,
    kong_win_candidates,
    winning_partitions,
)
from server.gamestate.game_hongque.tenpai_check import kong_win_waiting_tiles
from server.gamestate.game_hongque.tile import HongqueTile, full_deck


def test_hq_resource_code_round_trip_and_deck() -> None:
    assert HongqueTile.parse("GY9").code == "GY9"
    assert len(full_deck()) == 126
    assert len(set(full_deck())) == 126
    workspace = Path(__file__).resolve().parents[4]
    image_root = workspace / "open_mahjong_unity" / "Assets" / "Resources" / "image"
    for resource_dir in (image_root / "HQv3.1-hand", image_root / "HQv3.1-table"):
        assert all((resource_dir / f"{code}.png").is_file() for code in full_deck())


def test_triplet_and_sequence_follow_cyclic_colour_levels() -> None:
    assert classify_meld(["CX5", "DX5", "EX5"]).kind == "triplet"
    assert classify_meld(["GX2", "AX2", "BX2"]).kind == "triplet"
    assert classify_meld(["AX1", "AY2", "BX3"]).kind == "sequence"
    assert classify_meld(["AX7", "AY6", "BX5"]).kind == "sequence"
    assert classify_meld(["AX1", "BX2", "CX4"]) is None


@pytest.mark.parametrize("codes", [
    ["AX5", "AY5", "BX5"],
    ["AX5", "BX5", "CX5"],
    ["FX5", "GX5", "AX5"],
])
def test_triplet_accepts_colour_steps_one_or_two_with_wraparound(codes) -> None:
    assert classify_meld(codes).kind == "triplet"


@pytest.mark.parametrize("codes", [
    ["AX1", "AX2", "AX3"],
    ["AX1", "AY3", "BX5"],
    ["BX9", "AY6", "AX3"],
])
def test_sequence_accepts_number_steps_one_to_four_and_colour_steps_zero_to_two(codes) -> None:
    assert classify_meld(codes).kind == "sequence"


@pytest.mark.parametrize("codes", [
    ["AX1", "AX6", "AX9"],
    ["AX1", "BY2", "DX3"],
    ["AX5", "AY5", "CX5"],
])
def test_invalid_sequence_and_triplet_steps_are_rejected(codes) -> None:
    assert classify_meld(codes) is None


def test_rainbow_covers_seven_base_colours_including_half_colours() -> None:
    # Rulebook page 7 sample: number +1, colour level +2, while the half
    # colours collectively cover all seven base colours.
    shape = classify_meld(["GY2", "AY3", "BY4", "CY5", "DY6", "EY7"])
    assert shape is not None
    assert shape.is_rainbow
    assert shape.kind == "rainbow"
    # 四张半色牌虽可覆盖七种基础纯色，但规则中的彩虹要求七张不同花色牌。
    # Covering seven colours alone is not enough: the cards must first form
    # one of the rulebook's two base group types (sequence or triplet).
    assert classify_meld(["AY1", "BY2", "DY3", "FY4"]) is None


def test_win_partition_and_multi_face_call_enumeration() -> None:
    hand = ["AX1", "AY2", "BX3", "CX4", "CY5", "DX6"]
    partitions = winning_partitions(hand)
    assert len(partitions) >= 1
    calls = call_candidates(["AY2", "BX3", "AX4", "BX1", "GX1"], "AX1")
    assert any(candidate["kind"] == "sequence" for candidate in calls)
    assert any(candidate["kind"] == "triplet" for candidate in calls)


def test_call_priority_and_kong_extension_follow_table_order() -> None:
    calls = call_candidates(
        ["AX3", "BX4", "CX5", "DX6", "EX7", "FX8", "AX2", "BX2", "GX3", "GX4"],
        "GX2",
    )
    priorities = {candidate["kind"]: candidate["priority"] for candidate in calls}
    assert priorities["rainbow"] == 9
    assert priorities["triplet"] == 6
    assert priorities["sequence"] == 3  # 无座位上下文按最低档 third

    # 吃按出牌者相对位置分档：出牌者的下家=4 > 对家=3 > 上家=2。
    calls_by_seat = {
        distance: call_candidates(
            ["AX2", "AX3", "BX9"], "AX1",
            claimant_index=(0 + distance) % 4,
            discarder_index=0,
        )[0]["priority"]
        for distance in (1, 2, 3)
    }
    assert calls_by_seat == {1: 5, 2: 4, 3: 3}

    # 碰和虹同样按相对座次分档，不能由网络请求先后决定赢家。
    peng_by_seat = {
        distance: next(candidate for candidate in call_candidates(
            ["BX1", "CX1"], "AX1",
            claimant_index=distance,
            discarder_index=0,
        ) if candidate["kind"] == "triplet")
        for distance in (1, 2, 3)
    }
    assert [peng_by_seat[index]["action_type"] for index in (1, 2, 3)] == [
        "peng_first", "peng_second", "peng_third",
    ]
    assert [peng_by_seat[index]["priority"] for index in (1, 2, 3)] == [8, 7, 6]

    rainbow_hands = {
        1: ["BX1", "CX1", "DX1", "EX1", "FX1", "GX1"],
        2: ["BX2", "CX3", "DX4", "EX5", "FX6", "GX7"],
        3: ["BX7", "CX6", "DX5", "EX4", "FX3", "GX2"],
    }
    hong_by_seat = {
        distance: next(candidate for candidate in call_candidates(
            hand, "AX1", claimant_index=distance, discarder_index=0,
        ) if candidate["kind"] == "rainbow")
        for distance, hand in rainbow_hands.items()
    }
    assert [hong_by_seat[index]["action_type"] for index in (1, 2, 3)] == [
        "hong_first", "hong_second", "hong_third",
    ]
    assert [hong_by_seat[index]["priority"] for index in (1, 2, 3)] == [11, 10, 9]

    melds = [{"kind": "sequence", "tiles": ["AX1", "AX2", "AX3"]}]
    extensions = kong_candidates(["AX4", "GX9"], melds)
    assert extensions
    assert all(len(candidate["hand_tiles"]) == 1 for candidate in extensions)
    assert any(candidate["hand_tiles"] == ["AX4"] for candidate in extensions)


def test_kong_win_candidate_requires_remaining_hand_to_win() -> None:
    melds = [{"kind": "sequence", "tiles": ["AX1", "AX2", "AX3"]}]

    # 手牌只有摸到的 AX4：杠进明牌后空手成和，是杠和。
    kong_wins = kong_win_candidates(["AX4"], melds)
    assert len(kong_wins) == 1
    assert kong_wins[0]["kind"] == "kong_win"
    assert kong_wins[0]["hand_tiles"] == ["AX4"]
    assert kong_wins[0]["tiles"] == ["AX1", "AX2", "AX3", "AX4"]

    # 手牌 abc + 摸到 AX4：杠掉 AX4 后 abc 仍为完整一组，同样可杠和。
    hand = ["AX4", "AX7", "BX7", "CX7"]
    kong_wins = kong_win_candidates(hand, melds)
    assert any(candidate["kind"] == "kong_win" for candidate in kong_wins)

    # 杠掉 AX4 后剩余 AX7 无法成组，不是杠和。
    assert kong_win_candidates(["AX4", "AX7"], melds) == []


def test_kong_win_waiting_tiles_are_self_drawn_only() -> None:
    melds = [{"kind": "sequence", "tiles": ["AX1", "AX2", "AX3"]}]
    # 手牌 AX4：摸到 AX5 后可把 AX4、AX5 一起杠进明牌并空手成和。
    assert kong_win_waiting_tiles(["AX4"], melds) == ["AX5"]
    # 无明牌时不存在杠和听牌。
    assert kong_win_waiting_tiles(["AX4"], []) == []


def test_random_cards_covering_all_colours_are_not_a_win() -> None:
    hand = "GY2 AX8 BX8 DX7 GY5 GX5 AY6 FX9 CX7 DX2 DX8 EX4".split()
    assert winning_partitions(hand) == []
