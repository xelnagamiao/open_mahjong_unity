"""JJ fixed-eight wall, rollover, public counts and persisted replay contract."""
from collections import Counter
from copy import deepcopy
import random
from types import SimpleNamespace

import pytest

from ...database.duplicate_walls import validate_duplicate_wall, apply_duplicate_room_config
from ...game_calculation.sichuan.duplicate_rules import TILES, make_board, flatten_board, board_from_tiles, validate_board
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from .SichuanGameState import SichuanPlayer
from .XueliuGameState import XueliuGameState
from .duplicate_wall import XueliuDuplicateWall, init_duplicate_board, can_draw, wall_exhausted
from .init_tiles import init_sichuan_tiles


def wall_state(number=1):
    players = [SichuanPlayer(i, str(i), [], 0) for i in range(4)]
    for i, p in enumerate(players):
        p.original_player_index = p.player_index = i
    state = SimpleNamespace(player_list=players, current_player_index=0, is_xueliu=True, dead_wall_count=0)
    board = make_board("fixed-eight", number, layout="xueliu")
    init_duplicate_board(state, board)
    return state, board


@pytest.mark.parametrize("number", range(1, 17))
def test_dealer_draw_is_one_of_eight_and_format_roundtrips(number):
    state, board = wall_state(number)
    assert [len(p.hand_tiles) for p in state.player_list] == [14 if s == board["dealer"] else 13 for s in range(4)]
    assert state.tiles_list.remaining_counts() == [7 if s == board["dealer"] else 8 for s in range(4)]
    assert state.player_list[board["dealer"]].hand_tiles[-1] == board["walls"][board["dealer"]][0]
    assert len(state.tiles_list) == 55 and len(state.tiles_list.common) == 24
    assert board_from_tiles(flatten_board(board), number) == board
    assert Counter(t for p in state.player_list for t in p.hand_tiles) + Counter(state.tiles_list) == Counter(TILES)


@pytest.mark.parametrize("first_empty", range(4))
@pytest.mark.parametrize("dealer", range(4))
def test_first_empty_wall_rolls_all_remainders_in_eswn_order(first_empty, dealer):
    state, board = wall_state(dealer + 1)
    wall = state.tiles_list
    # Different draw counts model extra kong draws and claims skipping seats.
    for seat in range(4):
        if seat != first_empty:
            for _ in range(seat + 1):
                wall.pop_for_player(state.player_list[seat])
    expected = [tile for seat, part in enumerate(wall.parts) if seat != first_empty for tile in part] + board["common_wall"]
    while not wall.common_started:
        wall.pop_for_player(state.player_list[first_empty])
    assert wall.parts == [[], [], [], []]
    assert wall.common == list(wall) == expected
    assert all(can_draw(state, seat) for seat in range(4))
    assert not wall_exhausted(state)
    before = deepcopy((wall.parts, wall.common, wall.common_sources, list(wall)))
    for _ in range(3):
        for seat in range(4):
            assert wall.can_draw(state.player_list[seat])
    assert before == (wall.parts, wall.common, wall.common_sources, list(wall))
    actual = []
    for i in range(len(expected)):
        p = state.player_list[(i * 3) % 4]
        p.has_won = p.post_hu_lock = True
        actual.append(wall.pop_for_player(p))
    assert actual == expected
    assert wall_exhausted(state) and not any(can_draw(state, seat) for seat in range(4))
    with pytest.raises(ValueError, match="摸完"):
        wall.pop_for_player(state.player_list[0])


@pytest.mark.parametrize("seed", range(25))
def test_arbitrary_draw_and_kong_schedules_conserve_all_108_tiles(seed):
    state, board = wall_state(seed % 16 + 1)
    rng = random.Random(seed)
    wall = state.tiles_list
    initial = [t for p in state.player_list for t in p.hand_tiles]
    drawn, origins = [], []
    while wall:
        drawn.append(wall.pop_for_player(state.player_list[rng.randrange(4)]))
        source = state.duplicate_last_draw
        origins.append((source["draw_from_seat"], source["wall_index"]))
        assert len(wall) == sum(wall.remaining_counts()) + len(wall.common)
    assert len(drawn) == len(set(origins)) == 55
    assert Counter(initial + drawn) == Counter(TILES)
    assert [board["common_wall"][i] if s == -1 else board["walls"][s][i] for s, i in origins] == drawn
    assert len([s for s, _ in origins if s == -1]) == 24


def test_wrong_layout_and_back_draw_are_rejected_without_mutation():
    state, _ = wall_state()
    with pytest.raises(ValueError):
        XueliuDuplicateWall(state, make_board(1, 1))
    before = list(state.tiles_list)
    with pytest.raises(ValueError):
        state.tiles_list.pop(-1)
    assert state.tiles_list == before
    for mutate in (lambda b: b["common_wall"].pop(), lambda b: b["walls"][0].append(11),
                   lambda b: b.update(wall_layout="bad"), lambda b: b["common_wall"].__setitem__(0, True)):
        board = make_board(2, 1, layout="xueliu")
        mutate(board)
        with pytest.raises(ValueError):
            validate_board(board)


@pytest.mark.parametrize("sub", ["sichuan/xueliu", "sichuan/xueliu_exchange"])
@pytest.mark.parametrize("kind", ["manual", "seed", "key"])
def test_ordinary_bloodflow_rejects_keys_before_loading_or_dealing(monkeypatch, sub, kind):
    saved = dict(id=90, key="dup_" + "1" * 32, rule="sichuan/xueliu", wall_type=kind,
                 tiles=list(TILES), round_tiles=[list(TILES)], round_count=1,
                 event_id="test-event", use_flowers=False)
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=0, room_id="912311", hepai_limit=0)
    room.update(room_rule="sichuan", sub_rule=sub, event_id="test-event")
    before = deepcopy(room)
    with pytest.raises(ValueError, match="双打复式"):
        apply_duplicate_room_config(room, saved)
    assert room == before

    def unexpected_load(*_):
        pytest.fail("ordinary Sichuan must not load or reserve a duplicate wall")
    monkeypatch.setattr("server.database.duplicate_walls.load_duplicate_wall", unexpected_load)
    monkeypatch.setattr("server.database.duplicate_walls.reserve_duplicate_game", unexpected_load)
    room["duplicate_key"] = saved["key"]
    with pytest.raises(ValueError, match="成对创建"):
        XueliuGameState(server, room, server.calculation_service, db, "invalid-duplicate")


@pytest.mark.parametrize("bad", ["guobiao", "sichuan/standard", "sichuan/doubles", "sichuan/xueliu_doubles"])
def test_incompatible_rooms_cannot_reinterpret_a_bloodflow_wall(bad):
    wall = dict(rule="sichuan/xueliu", tiles=list(TILES), use_flowers=False)
    room = dict(room_rule=bad.split('/')[0], sub_rule=bad)
    with pytest.raises(ValueError):
        validate_duplicate_wall(wall, room)


@pytest.mark.parametrize("sub", ["sichuan/xueliu", "sichuan/xueliu_exchange"])
def test_ordinary_bloodflow_keeps_seeded_dealing_and_rejects_injected_duplicate_keys(sub):
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=0, room_id="912311", hepai_limit=0)
    room.update(room_rule="sichuan", sub_rule=sub)
    hands = []
    for _ in range(2):
        state = XueliuGameState(server, room, server.calculation_service, db, "seeded-normal")
        state.master_seed = 12345
        for index, player in enumerate(state.player_list):
            player.original_player_index = player.player_index = index
        init_sichuan_tiles(state)
        assert type(state.tiles_list) is list
        assert [len(p.hand_tiles) for p in state.player_list] == [14, 13, 13, 13]
        assert len(state.tiles_list) == 55
        hands.append(([list(p.hand_tiles) for p in state.player_list], list(state.tiles_list)))
    assert hands[0] == hands[1]
    before = deepcopy(hands[-1])
    state.duplicate_key = "dup_" + "1" * 32
    with pytest.raises(ValueError, match="成对创建"):
        init_sichuan_tiles(state)
    assert ([list(p.hand_tiles) for p in state.player_list], list(state.tiles_list)) == before
