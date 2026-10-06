"""国标巡目：指针转移/历时跨东家一周。"""
from types import SimpleNamespace

from server.gamestate.public.logic_common import player_index_go_to, player_index_next
from server.gamestate.public.next_game_round import next_game_round_guobiao_switchseat


def _state():
    return SimpleNamespace(
        current_player_index=0,
        xunmu=1,
        action_history=[],
        player_list=[SimpleNamespace(discard_tiles=[], discard_origin_tiles=[]) for _ in range(4)],
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


def test_claimed_dealer_discard_wrap_increments():
    """庄家弃牌被鸣走后河空，回绕仍进入第2巡。"""
    state = _state()
    player_index_go_to(state, 0)
    _discard(state, 0)
    state.player_list[0].discard_origin_tiles.append(state.player_list[0].discard_tiles.pop())
    player_index_go_to(state, 2)
    player_index_next(state)
    player_index_next(state)
    assert state.current_player_index == 0
    assert state.xunmu == 2
    assert not state.player_list[0].discard_tiles
    assert state.player_list[0].discard_origin_tiles == [11]


def test_repeated_claimed_dealer_discards_keep_counting():
    state = _state()
    player_index_go_to(state, 0)
    for expected in (2, 3):
        _discard(state, 0)
        state.player_list[0].discard_origin_tiles.append(state.player_list[0].discard_tiles.pop())
        player_index_go_to(state, 2)
        _discard(state, 2)
        player_index_next(state)
        _discard(state, 3)
        player_index_next(state)
        assert state.xunmu == expected
        assert not state.player_list[0].discard_tiles
    player_index_go_to(state, 0)
    player_index_go_to(state, 0)
    assert state.xunmu == 3


def test_south_peng_north_with_claimed_dealer_discard_increments():
    state = _state()
    player_index_go_to(state, 0)
    _discard(state, 0)
    state.player_list[0].discard_origin_tiles.append(state.player_list[0].discard_tiles.pop())
    player_index_go_to(state, 2)
    player_index_next(state)
    player_index_go_to(state, 1)
    assert state.xunmu == 2


def test_next_round_clears_discards_before_opening_flowers():
    state = _state()
    state.current_round = state.round_index = 4
    state.round_time = 20
    for index, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = index
        player.tag_list = []
        player.discard_tiles = [12]
        player.discard_origin_tiles = [11]
    state.action_history = [0, 1, 2, 3]
    state.xunmu = 5

    next_game_round_guobiao_switchseat(state)

    assert state.current_round == 5
    assert state.action_history == []
    assert all(not player.discard_tiles and not player.discard_origin_tiles for player in state.player_list)
    for index in range(4):
        player_index_go_to(state, index)
    player_index_go_to(state, 0)
    assert state.xunmu == 1


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
