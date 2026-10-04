"""Guobiao duplicate isolation, rejection boundaries, and ordinary seed regression."""
import asyncio
import importlib
import random
from types import SimpleNamespace
from unittest.mock import AsyncMock, Mock

import pytest

from server.database.duplicate_walls import validate_duplicate_wall, apply_duplicate_room_config, duplicate_record_is_visible
from server.gamestate.public.duplicate_wall import DuplicateWall, DuplicateFinished, finish_duplicate_game
from server.gamestate.public.game_record_manager import init_game_record, init_game_round, local_record_detail_for_end, player_action_record_deal


NORMAL_RULES = [
    ("guobiao", "Guobiao"), ("mmcr", "Qingque"), ("classical", "Classical"),
    ("changsha", "Changsha"), ("sichuan", "Sichuan"), ("riichi", "Riichi"),
    ("jiandan", "Jiandan"), ("taiwan", "Taiwan"),
]
RULES = [("guobiao", "Guobiao")]
SUBRULES = ["guobiao/standard", "guobiao/lanshi"]
NON_GUOBIAO = [name.lower() for _, name in NORMAL_RULES[1:]] + ["hongque", "free"]


def wall_tiles(rule):
    ids = list(range(11, 20)) + list(range(21, 30)) + list(range(31, 40))
    if rule not in {"changsha", "sichuan"}:
        ids += list(range(41, 48))
    wall = [tile for tile in ids for _ in range(4)]
    if rule in {"guobiao", "guobiao/standard", "taiwan"}:
        wall += list(range(51, 59))
    random.Random(104729).shuffle(wall)
    return wall


def game(folder, name, sub_rule="guobiao/standard", *, duplicate=True, use_flowers=True):
    rule = name.lower()
    module = importlib.import_module(f"server.gamestate.game_{folder}.{name}GameState")
    room = dict(room_id="123456", room_rule=rule, sub_rule=sub_rule if rule == "guobiao" else f"{rule}/standard", room_type="custom",
                player_list=[10000031, 10000022, 10000013, 10000044], tips=False,
                round_timer=10, step_timer=5, game_round=1, allow_spectator=False, use_flowers=use_flowers)
    server = SimpleNamespace(user_id_to_connection={}, gamestate_manager=SimpleNamespace(cleanup_game_state_complete=AsyncMock()),
                             room_manager=SimpleNamespace(finish_custom_game_room=AsyncMock()))
    db = Mock()
    db.get_rank_data.return_value = None
    state = getattr(module, f"{name}GameState")(server, room, Mock(), db, "duplicate-test")
    if duplicate:
        state.duplicate_key = "DUP_test01234567"
        state.duplicate_wall_type = "key"
        state._duplicate_tiles = tuple(wall_tiles(sub_rule if rule == "guobiao" else rule))
        if rule == "guobiao" and not state.use_flowers:
            state._duplicate_tiles = tuple(tile for tile in state._duplicate_tiles if tile < 50)
        state._duplicate_game_id = "dupgame0123456789"
    for index, player in enumerate(state.player_list):
        player.original_player_index = player.player_index = index
    return state, module, room


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("folder,name", RULES)
def test_real_rule_initializers_deal_from_each_original_seat(folder, name, sub_rule):
    state, _, _ = game(folder, name, sub_rule)
    init = importlib.import_module(f"server.gamestate.game_{folder}.init_tiles")
    getattr(init, f"init_{name.lower()}_tiles")(state)
    size = len(state._duplicate_tiles) // 4
    base = 16 if name == "Taiwan" else 13
    for seat, player in enumerate(state.player_list):
        count = base + int(seat == 0 and name not in {"Qingque", "Classical", "Riichi"})
        assert player.hand_tiles == list(state._duplicate_tiles[seat * size:seat * size + count])
    assert list(state.tiles_list) == sum(state.tiles_list.parts, [])
    if name == "Riichi":
        assert state.dora_indicators == state.ura_dora_indicators == []
        assert state.dead_wall_count == 0
    with pytest.raises(ValueError, match="已发牌"):
        getattr(init, f"init_{name.lower()}_tiles")(state)


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("folder,name", RULES)
def test_regular_and_supplemental_draws_never_take_other_seat_tiles(folder, name, sub_rule):
    state, _, _ = game(folder, name, sub_rule)
    init = importlib.import_module(f"server.gamestate.game_{folder}.init_tiles")
    getattr(init, f"init_{name.lower()}_tiles")(state)
    first, second = state.player_list[:2]
    expected = list(state.tiles_list.parts[0])
    other_before = [list(x) for x in state.tiles_list.parts[1:]]
    first.get_tile(state.tiles_list)
    assert first.hand_tiles[-1] == expected[0]
    if hasattr(first, "get_gang_tile"):
        first.get_gang_tile(state.tiles_list, state)
        assert first.hand_tiles[-1] == expected[-2]
    elif name == "Taiwan":
        assert state._take_supplement_tile(0) == expected[1]
    else:
        first.get_tile(state.tiles_list)
    assert state.tiles_list.parts[1:] == other_before
    assert list(state.tiles_list) == sum(state.tiles_list.parts, [])
    second.get_tile(state.tiles_list)
    first.get_tile(state.tiles_list)
    assert first.hand_tiles[-1] == expected[1]


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("folder,name", RULES)
def test_actual_game_start_retains_room_entry_seats(folder, name, sub_rule, monkeypatch):
    state, module, room = game(folder, name, sub_rule)
    class ReachedStart(Exception):
        pass
    async def reached(*args, **kwargs):
        raise ReachedStart()
    state.broadcast_game_start = reached
    monkeypatch.setattr(module, "broadcast_game_start", reached)
    method = {"Guobiao": "chinese", "Taiwan": "chinese"}.get(name, name.lower())
    with pytest.raises(ReachedStart):
        asyncio.run(getattr(state, f"game_loop_{method}")())
    assert [player.user_id for player in state.player_list] == room["player_list"]
    assert [player.original_player_index for player in state.player_list] == [0, 1, 2, 3]
    assert isinstance(state.tiles_list, DuplicateWall)


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("folder,name", RULES)
def test_exhaustion_finishes_once_and_saves_without_local_record(folder, name, sub_rule):
    state, _, _ = game(folder, name, sub_rule)
    init = importlib.import_module(f"server.gamestate.game_{folder}.init_tiles")
    getattr(init, f"init_{name.lower()}_tiles")(state)
    init_game_record(state)
    init_game_round(state)
    state.broadcast_game_end = AsyncMock()
    state.send_payload_to_player = AsyncMock()
    seat = state.player_list[1]
    # Drive genuine player draw methods to the independent boundary.
    with pytest.raises(DuplicateFinished) as done:
        while True:
            seat.get_tile(state.tiles_list)
            player_action_record_deal(state, seat.hand_tiles[-1], "d", seat.player_index)
    assert len(state.tiles_list.parts[1]) == 0
    assert all(state.tiles_list.parts[index] for index in (0, 2, 3))
    ticks = state.game_record["game_round"]["round_index_1"]["action_ticks"]
    assert ticks[-1] == ["d", seat.hand_tiles[-1]]
    asyncio.run(finish_duplicate_game(state, done.value))
    asyncio.run(finish_duplicate_game(state, done.value))
    assert ticks[-2:] == [["liuju"], ["end"]]
    save = getattr(state.db_manager, f"store_{state.room_rule}_game_record")
    save.assert_called_once()
    assert "master_seed_hex" not in state.game_record["game_title"]
    assert local_record_detail_for_end(state) is None
    state.game_server.room_manager.finish_custom_game_room.assert_not_awaited()


def test_room_rejects_wrong_tiles_seed_and_nondivisible_rule():
    room = dict(room_rule="guobiao", sub_rule="guobiao/standard", random_seed=0)
    wall = dict(key="DUP_test01234567", rule="guobiao", wall_type="key", tiles=wall_tiles("guobiao"))
    apply_duplicate_room_config(room, wall)
    assert room["duplicate_wall_type"] == "key" and room["allow_spectator"] is False
    assert "tiles" not in room and "seed" not in room
    with pytest.raises(ValueError):
        validate_duplicate_wall({**wall, "tiles": wall["tiles"][:-1]}, room)
    with pytest.raises(ValueError):
        apply_duplicate_room_config({**room, "random_seed": 123}, wall)
    with pytest.raises(ValueError, match="仅支持国标"):
        validate_duplicate_wall(wall, {"room_rule": "hongque"})


@pytest.mark.parametrize("row,visible", [(None, False), ((False,), False), ((True,), True)])
def test_record_lock_is_rechecked_for_every_access(row, visible):
    db = Mock()
    cursor = Mock()
    db._get_connection.return_value.cursor.return_value.__enter__ = Mock(return_value=cursor)
    db._get_connection.return_value.cursor.return_value.__exit__ = Mock(return_value=False)
    cursor.fetchone.return_value = row
    assert duplicate_record_is_visible(db, {"game_title": {"duplicate_key": "DUP_test01234567"}}) is visible
    db._put_connection.assert_called_once()


@pytest.mark.parametrize("sub_rule", SUBRULES)
@pytest.mark.parametrize("folder,name", RULES)
def test_actual_end_payload_never_contains_seed_or_local_record(folder, name, sub_rule):
    state, _, _ = game(folder, name, sub_rule)
    for index, player in enumerate(state.player_list):
        player.record_counter.rank_result = index + 1
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
    state._local_record_detail = {"private": "must never be sent"}
    if name == "Jiandan":
        from server.gamestate.game_jiandan.boardcast import game_end_payload
        payload = game_end_payload(state, 0)
    else:
        asyncio.run(state.broadcast_game_end())
        socket = state.game_server.user_id_to_connection[state.player_list[0].user_id].websocket
        socket.send_json.assert_awaited_once()
        payload = socket.send_json.call_args.args[0]
    info = payload["game_end_info"]
    assert info["is_duplicate"] is True and info["duplicate_wall_type"] == "key"
    assert info.get("master_seed") is None and "record_detail" not in info


@pytest.mark.parametrize("name", ["GB", "Qingque", "Classical", "Changsha", "Sichuan", "Riichi", "Jiandan", "Taiwan"])
def test_custom_room_has_only_public_duplicate_metadata_before_broadcast(name):
    from server.room.room_manager import RoomManager
    from server.database.duplicate_walls import duplicate_room_context
    rule = "guobiao" if name == "GB" else name.lower()
    player = SimpleNamespace(user_id=10000001, username="host", current_room_id=None)
    server = SimpleNamespace(players={"connection": player},
                             db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
                             gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False), match_manager=None)
    manager = RoomManager(server)
    manager._generate_room_id = lambda: "123456"
    async def check_broadcast(room_id):
        public = manager.rooms[room_id]
        assert public["is_duplicate"] is True
        assert public["duplicate_wall_type"] == "key"
        assert "tiles" not in public and "seed" not in public
        assert public["random_seed"] == 0 and public["allow_spectator"] is False
    manager._broadcast_room_info = check_broadcast
    wall = dict(key="DUP_test01234567", rule=rule, wall_type="key", tiles=wall_tiles(rule))
    token = duplicate_room_context.set(wall)
    try:
        kwargs = {"red_dora": False} if name == "Riichi" else {}
        result = asyncio.run(getattr(manager, f"create_{name}_room")("connection", "复式测试", 1, "", 20, 5, False, **kwargs))
        if name == "GB":
            assert result.success, result.message
        else:
            assert not result.success and "仅支持国标" in result.message
            assert manager.rooms == {}
    finally:
        duplicate_room_context.reset(token)


@pytest.mark.parametrize("rule", NON_GUOBIAO)
def test_non_guobiao_event_room_rejects_key(rule, monkeypatch):
    from server.room.room_manager import RoomManager
    db = SimpleNamespace(get_event=lambda _: {"status": "active"})
    manager = RoomManager(SimpleNamespace(db_manager=db))
    manager._generate_room_id = lambda: "123456"
    manager._broadcast_room_info = AsyncMock()
    wall = dict(key="DUP_test01234567", rule=rule, wall_type="key", tiles=wall_tiles(rule), event_id="event1")
    monkeypatch.setattr("server.room.room_manager.load_duplicate_wall", lambda *args: wall)
    response = asyncio.run(manager.create_empty_event_room("event1", rule, {"duplicate_key": wall["key"], "red_dora": False}))
    assert not response.success and "仅支持国标" in response.message
    assert manager.rooms == {}


@pytest.mark.parametrize("sub_rule", SUBRULES)
def test_guobiao_event_room_accepts_standard_and_lanshi(sub_rule, monkeypatch):
    from server.room.room_manager import RoomManager
    manager = RoomManager(SimpleNamespace(db_manager=SimpleNamespace(get_event=lambda _: {"status": "active"})))
    manager._broadcast_room_info = AsyncMock()
    wall = dict(key="DUP_test01234567", rule="guobiao/lanshi" if sub_rule.endswith("lanshi") else "guobiao",
                wall_type="key", tiles=wall_tiles(sub_rule), event_id="event1")
    monkeypatch.setattr("server.room.room_manager.load_duplicate_wall", lambda *args: wall)
    response = asyncio.run(manager.create_empty_event_room("event1", "guobiao", {"duplicate_key": wall["key"], "sub_rule": sub_rule}))
    assert response.success, response.message
    assert "duplicate_key" not in response.room_info
    assert response.room_info["is_duplicate"] is True
    assert manager.rooms[response.room_info["room_id"]]["duplicate_key"] == wall["key"]
    assert response.room_info["sub_rule"] == sub_rule


@pytest.mark.parametrize("name", ["Qingque", "Classical", "Changsha", "Sichuan", "Riichi", "Jiandan", "Taiwan", "Hongque", "Free"])
def test_non_guobiao_route_rejects_key_before_db_load(name):
    from server.room.room_router import handle_room_message
    server = SimpleNamespace(db_manager=Mock())
    socket = SimpleNamespace(send_json=AsyncMock())
    asyncio.run(handle_room_message(server, "connection", {"type": f"room/create_{name}_room", "duplicate_key": "DUP_test01234567"}, socket))
    payload = socket.send_json.call_args.args[0]
    assert payload["success"] is False and "仅支持国标" in payload["message"]
    server.db_manager._get_connection.assert_not_called()


@pytest.mark.parametrize("rule", NON_GUOBIAO)
def test_non_guobiao_validation_configuration_and_existing_room_start_reject_key(rule):
    from server.gamestate.gamestate_manager import GameStateManager
    from server.gamestate.public.duplicate_wall import configure_duplicate_state
    room = {"room_rule": rule, "duplicate_key": "DUP_test01234567"}
    with pytest.raises(ValueError, match="仅支持国标"):
        validate_duplicate_wall({"rule": rule, "tiles": []}, room)
    state = SimpleNamespace(db_manager=Mock())
    with pytest.raises(ValueError, match="仅支持国标"):
        configure_duplicate_state(state, room)
    state.db_manager._get_connection.assert_not_called()
    manager = GameStateManager(SimpleNamespace(room_manager=SimpleNamespace(rooms={"123456": room})))
    response = asyncio.run(manager.start_game("connection", "123456"))
    assert not response.success and "仅支持国标" in response.message
    assert not room.get("is_game_running")


@pytest.mark.parametrize("folder,name", NORMAL_RULES)
def test_ordinary_seeded_initialization_stays_reproducible(folder, name):
    first, _, _ = game(folder, name, duplicate=False)
    second, _, _ = game(folder, name, duplicate=False)
    initialize = getattr(importlib.import_module(f"server.gamestate.game_{folder}.init_tiles"), f"init_{name.lower()}_tiles")
    for state in (first, second):
        state.master_seed = 104729
        state.round_random_seed = 123456
        initialize(state)
        assert type(state.tiles_list) is list
    assert first.tiles_list == second.tiles_list
    assert [player.hand_tiles for player in first.player_list] == [player.hand_tiles for player in second.player_list]
    if name == "Riichi":
        assert first.dead_wall_count == 14 and len(first.dora_indicators) == 1


@pytest.mark.parametrize("name", ["GB", "Qingque", "Classical", "Changsha", "Sichuan", "Riichi", "Jiandan", "Taiwan"])
def test_scene_reproduction_room_keeps_original_seed(name):
    from server.room.room_manager import RoomManager
    player = SimpleNamespace(user_id=10000001, username="host", current_room_id=None)
    server = SimpleNamespace(players={"connection": player},
                             db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
                             gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False), match_manager=None)
    manager = RoomManager(server)
    manager._broadcast_room_info = AsyncMock()
    seed = "123456789abcdef0" * 4
    response = asyncio.run(getattr(manager, f"create_{name}_room")("connection", "场景复现", 1, "", 20, 5, False, random_seed=seed))
    assert response.success, response.message
    assert response.room_info["random_seed"] == int(seed, 16)
    assert response.room_info["is_player_set_random_seed"] is True
    assert not response.room_info.get("duplicate_key")
