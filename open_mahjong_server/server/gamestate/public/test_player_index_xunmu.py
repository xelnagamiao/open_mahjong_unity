"""国标巡目：指针转移/历时跨东家一周。"""
from types import SimpleNamespace

from server.gamestate.public.logic_common import player_index_go_to, player_index_next


def _state():
    return SimpleNamespace(
        current_player_index=0,
        xunmu=1,
        action_history=[],
        player_list=[SimpleNamespace(discard_tiles=[]) for _ in range(4)],
    )


def _discard(state, player_index):
    state.player_list[player_index].discard_tiles.append(11)


def test_opening_stays_xunmu_1():
    state = _state()
    player_index_go_to(state, 0)
    assert state.xunmu == 1
    assert state.action_history == [0]
    assert state.current_player_index == 0


def test_south_buhua_skips():
    state = _state()
    player_index_go_to(state, 0)
    player_index_next(state)
    player_index_go_to(state, 1)
    assert state.xunmu == 1
    assert state.action_history == [0, 1, 1]


def test_dealer_buhua_skips():
    state = _state()
    player_index_go_to(state, 0)
    player_index_go_to(state, 0)
    assert state.xunmu == 1
    assert state.action_history == [0, 0]


def test_opening_wrap_without_dealer_discard_does_not_increment():
    state = _state()
    for i in range(4):
        player_index_go_to(state, i)
    player_index_go_to(state, 0)
    assert state.xunmu == 1
    assert state.action_history == [0, 1, 2, 3, 0]


def test_south_peng_north_increments():
    state = _state()
    player_index_go_to(state, 0)
    _discard(state, 0)
    player_index_go_to(state, 3)
    player_index_go_to(state, 1)
    assert state.xunmu == 2
    assert state.action_history[-2:] == [3, 1]


def test_dealer_draw_after_north_increments():
    state = _state()
    player_index_go_to(state, 0)
    _discard(state, 0)
    player_index_go_to(state, 3)
    player_index_next(state)
    assert state.current_player_index == 0
    assert state.xunmu == 2


def test_first_cycle_stays_one_until_return_to_dealer():
    state = _state()
    player_index_go_to(state, 0)
    _discard(state, 0)
    player_index_next(state)
    _discard(state, 1)
    player_index_next(state)
    _discard(state, 2)
    player_index_next(state)
    assert state.current_player_index == 3
    assert state.xunmu == 1
    _discard(state, 3)
    player_index_next(state)
    assert state.current_player_index == 0
    assert state.xunmu == 2


def test_forward_peng_does_not_increment():
    state = _state()
    player_index_go_to(state, 0)
    _discard(state, 0)
    player_index_go_to(state, 2)
    assert state.xunmu == 1


def test_ankan_loop_does_not_increment():
    state = _state()
    player_index_go_to(state, 0)
    _discard(state, 0)
    player_index_next(state)
    player_index_next(state)
    player_index_next(state)
    player_index_next(state)
    assert state.xunmu == 2
    player_index_go_to(state, 0)
    player_index_go_to(state, 0)
    assert state.xunmu == 2
