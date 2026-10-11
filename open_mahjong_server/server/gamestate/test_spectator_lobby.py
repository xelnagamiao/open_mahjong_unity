"""Public spectator-room metadata stays aligned with the displayed player order."""
from types import SimpleNamespace as NS

import pytest

from server.gamestate.gamestate_manager import GameStateManager


def state(**changes):
    values = dict(room_rule="guobiao", sub_rule="guobiao/standard", room_type="custom",
                  player_list=[NS(username=f"P{i}", user_id=20+i) for i in range(4)],
                  room_id="room", lifecycle_state="running", spectator_enabled=True)
    values.update(changes)
    return NS(**values)


def listing(game, room=None):
    manager = GameStateManager.__new__(GameStateManager)
    manager.game_server = NS(room_manager=NS(rooms={"room": room or {}}))
    manager.gamestate_id_to_game_state = {"game": game}
    return manager.get_spectator_list()


def test_custom_does_not_leak_stale_ranked_metadata():
    info = listing(state(match_queue_type="advanced_quanzhuang", match_tier="advanced"))[0]
    assert info.room_type == "custom"
    assert info.match_queue_type is None and info.match_tier is None
    assert info.team_by_player_index is None


def test_ranked_metadata_uses_game_state_even_when_room_has_closed():
    info = listing(state(room_type="match", match_queue_type="riichi_advanced_banzhuang",
                         match_tier="advanced", event_id="event"))[0]
    assert (info.room_type, info.match_queue_type, info.match_tier, info.event_id) == (
        "match", "riichi_advanced_banzhuang", "advanced", "event")


def test_room_metadata_fallback():
    info = listing(state(room_type="match"), {"match_queue_type": "beginner_quanzhuang",
                   "match_tier": "beginner", "event_id": "fallback"})[0]
    assert info.match_queue_type == "beginner_quanzhuang"
    assert info.match_tier == "beginner" and info.event_id == "fallback"


@pytest.mark.parametrize("rule", ["sichuan/doubles", "sichuan/xueliu_doubles"])
def test_teams_follow_rotated_player_order_not_original_seats(rule):
    players = [NS(username=name, user_id=uid, team_id=team) for name, uid, team in
               [("Red A", 21, 1), ("Blue A", 22, 0), ("Red B", 23, 1), ("Blue B", 24, 0)]]
    info = listing(state(room_rule="sichuan", sub_rule=rule, player_list=players))[0]
    assert info.team_by_player_index == [1, 0, 1, 0]
    assert [info.player1_name, info.player2_name, info.player3_name, info.player4_name] == [p.username for p in players]


def test_doubles_missing_team_metadata_is_not_invented():
    assert listing(state(sub_rule="sichuan/doubles"))[0].team_by_player_index == [-1] * 4


@pytest.mark.parametrize("changes", [dict(lifecycle_state="finished"), dict(spectator_enabled=False),
    dict(player_list=[NS(username="AI", user_id=i) for i in range(4)])])
def test_visibility_guards_remain(changes):
    assert listing(state(**changes)) == []
