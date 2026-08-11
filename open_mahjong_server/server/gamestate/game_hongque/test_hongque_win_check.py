from server.gamestate.game_hongque.tenpai_check import waiting_tiles
from server.gamestate.game_hongque.win_check import is_winning_hand, winning_decompositions


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


def test_same_number_pair_is_not_a_winning_shape() -> None:
    # Rulebook 5.1.1: every tile must belong to a group; a same-number pair
    # left outside every group is not a winning shape.
    assert not is_winning_hand(["BX5", "CY5"], OPEN_SEQUENCE)
    assert winning_decompositions(["BX5", "CY5"], OPEN_SEQUENCE) == []


def test_two_unmatched_tiles_are_not_a_win() -> None:
    assert not is_winning_hand(["BX5", "CY6"], OPEN_SEQUENCE)


def test_group_completion_wait_lists_tiles_that_form_a_legal_group() -> None:
    waits = waiting_tiles(["BX4", "BX5"], OPEN_SEQUENCE)
    assert "BX3" in waits
    assert "BX6" in waits
    assert "CY6" not in waits
    # 单一同数字牌只能凑对子，不能组成牌组，因此不是听牌。
    assert waiting_tiles(["BX5"], OPEN_SEQUENCE) == []
