"""MIL p.8-9: an initial kong waits for the OTHER three first discards."""
import pytest
from server.gamestate.game_guizhou.test_flow import configured_turn, act


@pytest.mark.parametrize("seat,initial,discarded,hidden", [
    (0, True, [0, 0, 0, 0], True),
    (1, True, [1, 0, 0, 0], True),
    (2, True, [1, 1, 0, 0], True),
    (3, True, [1, 1, 1, 0], False),
    (1, False, [1, 0, 0, 0], False),
    (0, False, [1, 1, 1, 1], False),
])
def test_concealed_kong_visibility_uses_other_first_discards(seat, initial, discarded, hidden):
    hand = [12,13,14,21,22,23,31,32,33,39] + [11]*4
    s = configured_turn(hand, index=seat)
    for i,p in enumerate(s.player_list):
        p.discard_count = discarded[i]
        p.initial_quads = {11} if initial and i == seat else set()
    s.open_action_window(s.begin_turn(seat))
    act(s, seat, "angang", target_tile=11)
    s.apply_action_results(s.live_pending_window, {})
    assert ((seat, 0) in s.hidden_opening_kongs) == hidden
    expected = [2,11]*4 if hidden else [2,11,0,11,0,11,2,11]
    assert s.player_list[seat].combination_mask == [expected]
    for viewer in range(4):
        payload = s.build_game_start_payload(viewer)["game_info"]
        concealed = hidden and viewer != seat
        assert payload["players_info"][seat]["combination_tiles"] == ["G0" if concealed else "G11"]
        assert payload["guizhou_info"]["kongs"][0]["tile"] == (0 if concealed else 11)


def test_initial_kong_is_visible_even_when_first_discards_were_claimed():
    s = configured_turn([11]*4+[12,13,14,21,22,23,31,32,33,39], index=3)
    for i,p in enumerate(s.player_list):
        p.discard_count = 1 if i != 3 else 0
        p.discard_tiles.clear()  # A claimed tile does not undo its first discard.
    s.player_list[3].initial_quads = {11}
    s.open_action_window(s.begin_turn(3))
    act(s, 3, "angang", target_tile=11)
    s.apply_action_results(s.live_pending_window, {})
    assert not s.hidden_opening_kongs
    assert s.player_list[3].combination_mask == [[2,11,0,11,0,11,2,11]]
