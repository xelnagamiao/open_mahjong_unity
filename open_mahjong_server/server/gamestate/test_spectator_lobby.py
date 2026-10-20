"""Public spectator-room metadata stays aligned with the displayed player order."""
import asyncio
import json
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock

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


@pytest.mark.parametrize("rule,sub_rule,players,circles,current,expected", [
    ("guobiao", "guobiao/standard", 4, 4, 2, 16),
    ("guobiao", "guobiao/standard", 4, 1, 4, 4),
    ("guobiao", "guobiao/standard", 4, 2, 5, 8),
    ("guobiao", "guobiao/sanma", 3, 4, 2, 12),
    ("riichi", "riichi/sanma", 3, 2, 4, 6),
    ("riichi", "riichi/standard", 4, 2, 9, 8),
    ("hongque", "hongque/v1.6", 4, 8, 2, 8),
])
def test_round_progress_uses_rule_schedule(rule, sub_rule, players, circles, current, expected):
    game = state(room_rule=rule, sub_rule=sub_rule, player_list=state().player_list[:players],
                 current_round=current, max_round=circles, round_index=current + 5)
    info = listing(game)[0]
    assert (info.current_round, info.total_rounds) == (current, expected)
    payload = info.model_dump(exclude_none=True)
    assert (payload["current_round"], payload["total_rounds"]) == (current, expected)


def test_round_progress_refreshes_without_counting_renchan():
    game = state(room_rule="riichi", sub_rule="riichi/standard",
                 current_round=2, max_round=4, round_index=3)
    assert listing(game)[0].current_round == 2
    game.round_index += 1
    assert listing(game)[0].current_round == 2
    game.current_round += 1
    assert listing(game)[0].current_round == 3


def test_unlimited_round_progress_does_not_report_a_fixed_total():
    info = listing(state(room_rule="qingque", sub_rule="qingque/standard",
                         current_round=19, max_round=4, unlimited_rounds=True))[0]
    assert (info.current_round, info.total_rounds) == (19, 0)
    assert info.model_dump(exclude_none=True)["total_rounds"] == 0


def test_missing_round_metadata_is_not_invented():
    payload = listing(state())[0].model_dump(exclude_none=True)
    assert "current_round" not in payload and "total_rounds" not in payload


@pytest.mark.parametrize("rule", ["sichuan/doubles", "sichuan/xueliu_doubles"])
def test_teams_follow_rotated_player_order_not_original_seats(rule):
    players = [NS(username=name, user_id=uid, team_id=team) for name, uid, team in
               [("Red A", 21, 1), ("Blue A", 22, 0), ("Red B", 23, 1), ("Blue B", 24, 0)]]
    info = listing(state(room_rule="sichuan", sub_rule=rule, player_list=players))[0]
    assert info.team_by_player_index == [1, 0, 1, 0]
    assert [info.player1_name, info.player2_name, info.player3_name, info.player4_name] == [p.username for p in players]


def test_doubles_missing_team_metadata_is_not_invented():
    assert listing(state(sub_rule="sichuan/doubles"))[0].team_by_player_index == [-1] * 4


@pytest.mark.parametrize("sub_rule,count", [
    ("guobiao/standard", 4), ("riichi/standard", 4),
    ("guobiao/sanma", 3), ("riichi/sanma", 3),
])
@pytest.mark.parametrize("room_type", ["custom", "match"])
def test_list_supports_rule_player_count_and_preserves_wire_names(sub_rule, count, room_type):
    players = state().player_list[:count]
    players[1].username = ""
    infos = listing(state(room_rule=sub_rule.split("/")[0], sub_rule=sub_rule,
                         room_type=room_type, player_list=players, match_tier="advanced"))
    assert len(infos) == 1
    payload = infos[0].model_dump(exclude_none=True)
    assert [payload[f"player{i}_name"] for i in range(1, 5)] == [
        "P0", "21", "P2", "P3" if count == 4 else "",
    ]
    assert payload["sub_rule"] == sub_rule and payload["gamestate_id"] == "game"
    assert payload["room_type"] == room_type
    assert payload.get("match_tier") == ("advanced" if room_type == "match" else None)
    assert "team_by_player_index" not in payload


@pytest.mark.parametrize("sub_rule,count", [
    ("guobiao/standard", 3), ("riichi/standard", 3),
    ("guobiao/sanma", 2), ("riichi/sanma", 2),
])
def test_incomplete_tables_stay_out_of_spectator_list(sub_rule, count):
    assert listing(state(sub_rule=sub_rule, player_list=state().player_list[:count])) == []


@pytest.mark.parametrize("sub_rule", ["guobiao/sanma", "riichi/sanma"])
@pytest.mark.parametrize("ai_count", [0, 1, 2, 3])
def test_sanma_uses_existing_ai_visibility_limit(sub_rule, ai_count):
    players = [NS(username=f"P{i}", user_id=i + 1 if i < ai_count else 20 + i)
               for i in range(3)]
    assert bool(listing(state(sub_rule=sub_rule, player_list=players))) == (ai_count < 3)


@pytest.mark.parametrize("sub_rule", ["guobiao/standard", "guobiao/sanma", "riichi/sanma"])
@pytest.mark.parametrize("changes", [dict(lifecycle_state="finished"), dict(spectator_enabled=False),
    dict(player_list=[NS(username="AI", user_id=i) for i in range(4)])])
def test_visibility_guards_remain(changes, sub_rule):
    count = 3 if sub_rule.endswith("/sanma") else 4
    game = state(sub_rule=sub_rule, player_list=state().player_list[:count])
    for key, value in changes.items():
        setattr(game, key, value)
    assert listing(game) == []


@pytest.mark.parametrize("rule", ["guobiao", "riichi"])
def test_sanma_list_to_delayed_spectator_keeps_three_seats_and_delay(rule, monkeypatch):
    from .game_guobiao.GuobiaoGameState import GuobiaoGameState
    from .game_riichi.RiichiGameState import RiichiGameState
    from .gamestate_router import handle_add_spectator, handle_get_spectator_list

    now = [1000.0]
    monkeypatch.setattr("server.gamestate.public.spectator_manager.time", NS(time=lambda: now[0]))

    async def run():
        socket = NS(send_json=AsyncMock())
        viewer = NS(user_id=999, websocket=socket)
        server = NS(room_manager=NS(rooms={}), players={"viewer": viewer},
                    user_id_to_connection={999: viewer},
                    match_manager=NS(blocks_spectator=lambda uid: False))
        server.gamestate_manager = GameStateManager(server)
        room = dict(player_list=[101, 102, 103], room_id="room", room_rule=rule,
                    sub_rule=f"{rule}/sanma", room_type="custom", round_timer=20,
                    step_timer=5, game_round=1, tips=False, allow_spectator=True)
        game_class = GuobiaoGameState if rule == "guobiao" else RiichiGameState
        game = game_class(server, room, NS(), NS(get_rank_data=lambda uid: None), "sanma-game")
        for seat, player in enumerate(game.player_list):
            player.player_index = player.original_player_index = seat
        game.lifecycle_state = "running"
        server.gamestate_manager.gamestate_id_to_game_state[game.gamestate_id] = game
        spectator = game.spectator_manager
        spectator.record_game_title()
        spectator.record_round_start()
        spectator.record_tick(["c", 21, "F"])
        try:
            await handle_get_spectator_list(server, "viewer", {}, socket)
            response = socket.send_json.call_args.args[0]
            assert response["success"] and len(response["spectator_list"]) == 1
            info = response["spectator_list"][0]
            assert info["player4_name"] == "" and info["sub_rule"] == room["sub_rule"]
            assert (info["current_round"], info["total_rounds"]) == (1, 3)
            request = {"gamestate_id": info["gamestate_id"]}

            await handle_add_spectator(server, "viewer", request, socket)
            assert socket.send_json.call_args.args[0]["success"] is False
            assert not spectator.spectator_connections
            assert spectator.spectator_delay == 180

            now[0] += 100
            spectator.record_tick(["d", 22])
            now[0] += 81
            socket.send_json.reset_mock()
            await handle_add_spectator(server, "viewer", request, socket)
            messages = [call.args[0] for call in socket.send_json.call_args_list]
            assert messages[0]["type"] == "spectator/add_spectator" and messages[0]["success"]
            assert messages[1]["type"] == "spectator/record_init"
            record = json.loads(messages[1]["message_info"]["content"])
            assert record["game_title"]["player_count"] == 3
            assert record["game_title"]["sub_rule"] == room["sub_rule"]
            assert "p3_uid" not in record["game_title"] and len(record["players_settings"]) == 3
            header = record["game_round"][f"round_index_{game.round_index}"]
            assert header["seats"] == [0, 1, 2] and "p3_tiles" not in header
            assert header["action_ticks"] == [["c", 21, "F"]]
            assert spectator._get_new_updates(viewer.user_id, 1101) == [
                {"type": "ticks", "round_index": game.round_index, "new_ticks": [["d", 22]]},
            ]
        finally:
            await spectator.cleanup()

    asyncio.run(run())
