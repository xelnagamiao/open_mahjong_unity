from .tenpai_check import waiting_tiles
from .win_check import is_winning_hand, winning_decompositions


OPEN_SEQUENCE = [{
    "kind": "sequence",
    "tiles": ["AX1", "AX2", "AX3"],
    "from_player": 1,
    "claimed_tile": "AX1",
}]


def test_original_all_groups_shape_remains_a_win() -> None:
    assert is_winning_hand(["BX4", "BX5", "BX6"])


def test_exposed_groups_with_no_concealed_tiles_are_complete() -> None:
    assert is_winning_hand([], OPEN_SEQUENCE)


def test_same_number_pair_is_a_mahjong_style_head() -> None:
    decompositions = winning_decompositions(["BX5", "CY5"], OPEN_SEQUENCE)
    assert decompositions
    assert any(set(item["pair"]) == {"BX5", "CY5"} for item in decompositions)


def test_different_number_pair_is_not_a_head() -> None:
    assert not is_winning_hand(["BX5", "CY6"], OPEN_SEQUENCE)


def test_single_number_wait_lists_every_unused_colour_of_that_number() -> None:
    waits = waiting_tiles(["BX5"], OPEN_SEQUENCE)
    assert "CY5" in waits
    assert "GX5" in waits
    assert "CY6" not in waits
