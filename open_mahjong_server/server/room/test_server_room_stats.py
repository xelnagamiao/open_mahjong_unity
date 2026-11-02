"""The lobby and venue entrance report distinct waiting-room totals."""
import ast
from pathlib import Path
from types import SimpleNamespace
from typing import Dict

from server.response import ServerStatsInfo


def get_server_stats():
    # Load the real method without starting the server, database, or chat service.
    path = Path(__file__).parents[1] / "server.py"
    tree = ast.parse(path.read_text(encoding="utf-8-sig"))
    cls = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "GameServer")
    method = next(node for node in cls.body if getattr(node, "name", None) == "get_server_stats")
    namespace = {"Dict": Dict}
    exec(compile(ast.Module(body=[method], type_ignores=[]), str(path), "exec"), namespace)
    return namespace["get_server_stats"]


def test_room_stats_separate_waiting_rooms_and_keep_playing_total():
    rooms = {
        "ordinary": {"room_type": "custom", "is_game_running": False},
        "ordinary_default": {},
        "venue_type": {"room_type": "events", "is_game_running": False},
        "venue_id": {"room_type": "custom", "event_id": "club-1"},
        "venue_running": {"event_id": "event-1", "is_game_running": True},
        "ordinary_running": {"room_type": "custom", "is_game_running": True},
    }
    server = SimpleNamespace(players={"a": object(), "b": object()},
                             room_manager=SimpleNamespace(rooms=rooms),
                             gamestate_manager=SimpleNamespace(get_match_playing_games_count=lambda: 3))
    stats = get_server_stats()(server)
    assert stats == {"online_players": 2, "waiting_rooms": 2, "venue_waiting_rooms": 2,
                     "playing_rooms": 2, "match_playing_games": 3}
    assert ServerStatsInfo(**stats).model_dump()["venue_waiting_rooms"] == 2


def test_empty_rooms_and_older_stats_payload_default_to_zero():
    server = SimpleNamespace(players={}, room_manager=SimpleNamespace(rooms={}),
                             gamestate_manager=SimpleNamespace(get_match_playing_games_count=lambda: 0))
    stats = get_server_stats()(server)
    assert stats["waiting_rooms"] == stats["venue_waiting_rooms"] == stats["playing_rooms"] == 0
    stats.pop("venue_waiting_rooms")
    assert ServerStatsInfo(**stats).venue_waiting_rooms == 0
