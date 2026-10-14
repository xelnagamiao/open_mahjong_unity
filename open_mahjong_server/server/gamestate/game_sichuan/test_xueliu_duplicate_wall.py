"""JJ fixed-eight wall, rollover, public counts and persisted replay contract."""
from collections import Counter
from copy import deepcopy
import random
from types import SimpleNamespace

import pytest

from ...database.duplicate_walls import validate_duplicate_wall, apply_duplicate_room_config
from ...game_calculation.sichuan.duplicate_rules import TILES, make_board, flatten_board, board_from_tiles, validate_board
from ..public.duplicate_wall import configure_duplicate_state, duplicate_remaining_tile_counts
from ..public.game_record_manager import init_game_record, init_game_round, player_action_record_deal
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from ..verifier.record_sim.goto_action import RecordSim
from .SichuanGameState import SichuanPlayer
from .XueliuGameState import XueliuGameState
from .duplicate_wall import XueliuDuplicateWall, init_duplicate_board, can_draw, wall_exhausted
from .init_tiles import init_sichuan_tiles
from .boardcast import _base_game_info


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
def test_ordinary_key_room_deals_every_board_and_reconnects_without_secrets(monkeypatch, sub, kind):
    rounds = [flatten_board(make_board("ordinary", n, layout="xueliu")) for n in range(1, 5)]
    saved = dict(id=90, key="dup_" + "1" * 32, rule="sichuan/xueliu", wall_type=kind,
                 tiles=rounds[0], round_tiles=rounds, round_count=4, seed="ordinary", use_flowers=False)
    monkeypatch.setattr("server.database.duplicate_walls.load_duplicate_wall", lambda *_: deepcopy(saved))
    monkeypatch.setattr("server.database.duplicate_walls.reserve_duplicate_game", lambda *_: "ordinary-record")
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=0, room_id="912311", hepai_limit=0)
    room.update(room_rule="sichuan", sub_rule=sub)
    apply_duplicate_room_config(room, deepcopy(saved))
    state = XueliuGameState(server, room, server.calculation_service, db, "ordinary-duplicate")
    for i, p in enumerate(state.player_list):
        p.original_player_index = p.player_index = i
    for n in range(1, 5):
        state.current_round = state.round_index = n
        init_sichuan_tiles(state)
        state.current_player_index = state.dealer_index
        assert isinstance(state.tiles_list, XueliuDuplicateWall)
        assert not state.spectator_enabled
        base = _base_game_info(state)
        assert base["is_duplicate"] and base["salt"] is None and base["commitment"] is None
        assert base["duplicate_remaining_tiles"] == duplicate_remaining_tile_counts(state)
        assert not any(key in base for key in ("duplicate_key", "duplicate_seed", "duplicate_tiles", "duplicate_common_wall"))
        assert state._is_match_end() == (n == 4)
        assert state._should_play_round()
        with pytest.raises(ValueError, match="已发牌"):
            init_sichuan_tiles(state)
    state.current_round = 5
    assert not state._should_play_round()
    with pytest.raises(ValueError, match="范围"):
        init_sichuan_tiles(state)


@pytest.mark.parametrize("bad", ["guobiao", "sichuan/standard", "sichuan/doubles", "sichuan/xueliu_doubles"])
def test_incompatible_rooms_cannot_reinterpret_a_bloodflow_wall(bad):
    wall = dict(rule="sichuan/xueliu", tiles=list(TILES), use_flowers=False)
    room = dict(room_rule=bad.split('/')[0], sub_rule=bad)
    with pytest.raises(ValueError):
        validate_duplicate_wall(wall, room)


@pytest.mark.parametrize("sub", ["sichuan/xueliu", "sichuan/xueliu_exchange"])
@pytest.mark.parametrize("seed", [1807, 2409])
def test_four_clients_finish_ordinary_duplicate_and_replay_every_draw(monkeypatch, sub, seed):
    import asyncio
    from .test_xueliu_integration import full_state
    from . import test_xueliu_doubles_full_match as driver
    state, messages = full_state()
    state.sub_rule = sub
    state._configure_rule()
    board = make_board(seed, 1, layout="xueliu")
    state.duplicate_key = "dup_test_ordinary"
    state.duplicate_wall_type = "key"
    state.duplicate_round_count = 1
    state._duplicate_seed = str(seed)
    state._duplicate_tiles = flatten_board(board)
    state._duplicate_round_tiles = [state._duplicate_tiles]
    state._duplicate_game_id = "duplicate-test"
    state.spectator_enabled = state.spectator_manager.enabled = False
    monkeypatch.setattr(driver, "make_state", lambda: (state, messages))
    state, messages = asyncio.run(driver.play_match(seed))
    assert len(state.game_record["game_round"]) == 1
    round_data = state.game_record["game_round"]["round_index_1"]
    assert round_data["duplicate_wall_layout"] == "xueliu"
    draws = [tick for tick in round_data["action_ticks"] if tick[0] in ("d", "gd")]
    assert len(draws) == 55
    assert len([tick for tick in draws if tick[3] == -1]) == 24
    assert len(state.tiles_list) == 0 and state.tiles_list.common_started
    replay = RecordSim(state.game_record)
    frames = replay.apply_all("round_index_1")
    for node in (0, len(frames)//2, len(frames)-1):
        assert replay.goto_action("round_index_1", node) == frames[node]
    endings = [m["message"]["game_end_info"] for m in messages if m["type"].endswith("/game_end")]
    assert len(endings) == 4
    assert all("record_detail" not in e and "master_seed" not in e for e in endings)
