"""Duplicate series: original seats, saved walls, room privacy and reconnects."""
import asyncio
import json
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from server.database.duplicate_walls import apply_duplicate_room_config, validate_duplicate_wall
from server.gamestate.game_guobiao.init_tiles import init_guobiao_tiles
from server.gamestate.game_guobiao.boardcast import _build_game_start_payload_for_viewer
from server.gamestate.public.game_record_manager import init_game_record, init_game_round, end_game_record
from server.gamestate.public.next_game_round import next_game_round_guobiao_switchseat
from server.gamestate.public.test_duplicate_wall import game, wall_tiles
from server.response import GameInfo, Response
from server.room.room_manager import RoomManager


@pytest.mark.parametrize("count", [1, 4, 8, 12, 16])
@pytest.mark.parametrize("flowers", [False, True])
def test_each_saved_round_deals_exact_hands_and_walls_after_guobiao_rotation(count, flowers):
    state, _, _ = game("guobiao", "Guobiao", use_flowers=flowers)
    source = list(state._duplicate_tiles)
    rounds = [source[index:] + source[:index] for index in range(count)]
    state._duplicate_round_tiles = tuple(tuple(tiles) for tiles in rounds)
    state.duplicate_round_count = count
    state.max_round = max(1, count // 4)
    init_game_record(state)
    dealers = [0, 1, 2, 3, 1, 0, 3, 2, 2, 3, 1, 0, 3, 2, 0, 1]
    for index, tiles in enumerate(rounds):
        init_guobiao_tiles(state)
        init_game_round(state)
        assert state.player_list[0].original_player_index == dealers[index]
        assert state.tiles_list.supplement_positions == [2, 2, 2, 2]
        size = len(tiles) // 4
        for player in state.player_list:
            seat = player.original_player_index
            hand = 14 if player.player_index == 0 else 13
            assert player.hand_tiles == tiles[seat * size:seat * size + hand]
            assert state.tiles_list.parts[seat] == tiles[seat * size + hand:(seat + 1) * size]
        live = GameInfo(**_build_game_start_payload_for_viewer(state, 0)).model_dump(exclude_none=True)
        assert live["is_duplicate"] and live["duplicate_round_count"] == count
        assert not set(live) & {"duplicate_key", "duplicate_tiles", "duplicate_round_tiles", "duplicate_seed"}
        state.player_list[0].get_gang_tile(state.tiles_list, state)
        if index + 1 < count:
            next_game_round_guobiao_switchseat(state)
    end_game_record(state)
    assert state.game_record["game_title"]["duplicate_key"] == state.duplicate_key
    assert state.game_record["game_title"]["duplicate_round_tiles"] == rounds
    assert len(state.game_record["game_round"]) == count


@pytest.mark.parametrize("count", [1, 4, 8, 12, 16])
def test_room_uses_saved_count_and_checks_every_round(count):
    tiles = wall_tiles("guobiao/lanshi")
    wall = dict(key="dup_secret", rule="guobiao", wall_type="manual", use_flowers=False,
                tiles=tiles, round_tiles=[tiles[:] for _ in range(count)], round_count=count)
    room = dict(room_rule="guobiao", sub_rule="guobiao/standard", game_round=4)
    apply_duplicate_room_config(room, wall)
    assert room["duplicate_round_count"] == count
    assert room["game_round"] == max(1, count // 4)
    corrupted = deepcopy(wall)
    corrupted["round_tiles"][-1].pop()
    with pytest.raises(ValueError):
        validate_duplicate_wall(corrupted, room)
    with pytest.raises(ValueError):
        validate_duplicate_wall({**wall, "round_count": 2}, room)
    if count > 1:
        with pytest.raises(ValueError):
            validate_duplicate_wall({**wall, "round_tiles": None}, room)


def test_room_snapshots_strip_keys_without_changing_internal_room_and_event_config():
    room = dict(room_id="100", room_rule="guobiao", duplicate_key="dup_private", is_duplicate=True,
                duplicate_wall_type="key", duplicate_round_count=16, event_id="evt")
    config = {"auto_match": {"room_config": {"duplicate_key": "dup_preset"}}}
    snapshot = Response(type="room/refresh_room_info", success=True, message="ok", room_info=room,
                        room_list=[room], event_detail=config).model_dump(exclude_none=True)
    assert "dup_private" not in json.dumps(snapshot) and "dup_preset" not in json.dumps(snapshot)
    assert snapshot["room_info"]["duplicate_round_count"] == 16
    assert room["duplicate_key"] == "dup_private"
    assert config["auto_match"]["room_config"]["duplicate_key"] == "dup_preset"
    manager = RoomManager(SimpleNamespace())
    manager.rooms["100"] = room
    assert "duplicate_key" not in manager.list_event_rooms("evt")[0]


def test_reconnect_keeps_counts_but_never_returns_key_or_saved_wall():
    state, _, _ = game("guobiao", "Guobiao", use_flowers=False)
    state.duplicate_round_count = 16
    init_guobiao_tiles(state)
    init_game_record(state)
    init_game_round(state)
    socket = SimpleNamespace(send_json=AsyncMock())
    # The reconnect path sends its snapshot directly to the returning player.
    state.broadcast_ask_hand_action = AsyncMock()
    state.broadcast_ask_other_action = AsyncMock()
    state.send_to_realtime_spectators = AsyncMock()
    player = state.player_list[0]
    state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=socket)
    asyncio.run(state.player_reconnect(player.user_id))
    data = [call.args[0] for call in socket.send_json.call_args_list]
    assert data
    assert state.duplicate_key not in json.dumps(data, default=str)
    assert "duplicate_round_tiles" not in json.dumps(data, default=str)
