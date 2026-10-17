"""Hongque uses shared delayed replay and host-view realtime spectating."""
import asyncio
import json
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, patch

import pytest

from .HongqueGameState import HongqueGameState
from .boardcast import broadcast_game_end
from .record import snapshot_round_header, record_cut
from ...friend.friend_manager import FriendManager
from ..test_spectator_lobby import listing


def fixture(**room_overrides):
    connections = {uid: NS(websocket=NS(send_json=AsyncMock()), event_disconnecting=False)
                   for uid in (101, 102, 103, 104, 201, 202)}
    server = NS(user_id_to_connection=connections, match_manager=None)
    room = dict(room_id=12345, player_list=[101, 102, 103, 104], game_round=8,
                allow_spectator=True, random_seed=7)
    room.update(room_overrides)
    game = HongqueGameState(server, room)
    game.lifecycle_state = "running"
    game.phase = "turn"
    game.wall = ["AX1", "BX2", "CX3"]
    for p in game.players:
        p.hand = [f"{chr(65+p.index)}X5", f"{chr(65+p.index)}Y6"]
    return game, server


@pytest.mark.parametrize("allowed,players,enabled", [
    (True, [101, 102, 103, 104], True), (False, [101, 102, 103, 104], False),
    (True, [1, 2, 103, 104], True), (True, [1, 2, 3, 104], False),
])
def test_spectator_entry_uses_shared_room_and_ai_policy(allowed, players, enabled):
    game, _ = fixture(allow_spectator=allowed, player_list=players)
    assert game.spectator_enabled is enabled
    assert game.spectator_manager.enabled is enabled
    assert bool(listing(game)) is enabled


def test_delayed_entry_round_ticks_and_final_record_match_saved_replay():
    async def run():
        game, server = fixture()
        watcher = server.user_id_to_connection[201]
        with patch("server.gamestate.public.spectator_manager.time.time", return_value=1000):
            snapshot_round_header(game)
        manager = game.spectator_manager
        assert manager.spectator_delay == 180
        assert manager.round_headers[1]["data"]["tiles_list"] == [1043, 1022, 1001]
        with patch("server.gamestate.public.spectator_manager.time.time", return_value=1179):
            await game.add_spectator(201, watcher)
        assert watcher.websocket.send_json.call_args.args[0]["success"] is False
        with patch("server.gamestate.public.spectator_manager.time.time", return_value=1180):
            await game.add_spectator(201, watcher)
        packet = watcher.websocket.send_json.call_args.args[0]
        record = json.loads(packet["message_info"]["content"])
        assert packet["type"] == "spectator/record_init"
        assert record["game_title"]["rule"] == "hongque"
        assert record["game_round"] == game.game_record["game_round"]
        with patch("server.gamestate.public.spectator_manager.time.time", return_value=1181):
            record_cut(game, game.players[0], "AX5", False)
        assert manager._get_new_updates(201, 1180) is None
        updates = manager._get_new_updates(201, 1181)
        assert updates[0]["new_ticks"] == game.game_record["game_round"]["round_index_1"]["action_ticks"]
        await manager.send_final_record_and_close()
        final = watcher.websocket.send_json.call_args.args[0]
        assert final["type"] == "spectator/record_complete"
        assert json.loads(final["message_info"]["content"])["game_round"] == game.game_record["game_round"]
        assert not manager.spectator_connections and not manager.spectator_send_tasks
        await game.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("host_index", range(4))
def test_friend_initial_entry_and_live_events_use_same_private_host_view(host_index):
    async def run():
        game, server = fixture()
        game.current_player_index = host_index
        host = game.players[host_index]
        host.drawn_tile = host.hand[-1]
        game.realtime_spectators = [NS(user_id=201, host_user_id=host.user_id),
                                    NS(user_id=202, host_user_id=game.players[(host_index+1)%4].user_id)]
        friend = FriendManager.__new__(FriendManager)
        friend.game_server = server
        await friend._send_realtime_initial_state(game, NS(from_user_id=201, to_user_id=host.user_id))
        watcher = server.user_id_to_connection[201].websocket.send_json
        initial = watcher.call_args.args[0]
        assert initial["type"] == "gamestate/hongque/reconnect" and "game_info" not in initial
        state = initial["hongque_state"]
        assert state["sync_mode"] == "reconnect" and state["you"] == host_index
        assert state["hand"] == host.hand
        assert state["pending_draw"]["tile"] == host.drawn_tile
        assert all("hand" not in p for p in state["players"])
        game.events = [{"id": 1, "type": "draw", "player": host_index, "tile": host.drawn_tile}]
        watcher.reset_mock()
        await game.broadcast_state()
        watcher.assert_awaited_once()
        assert watcher.call_args.args[0] == server.user_id_to_connection[host.user_id].websocket.send_json.call_args.args[0]
        other = server.user_id_to_connection[202].websocket.send_json.call_args.args[0]["hongque_state"]
        assert other["events"][0]["tile"] is None and "hand" not in other
        assert game.build_state((host_index+1)%4, sync_mode="reconnect")["pending_draw"]["tile"] is None
        # Readiness and final rating/record packets follow the host exactly once.
        watcher.reset_mock()
        game._ready_phase_active = True
        game._ready_players = {host_index}
        await game.broadcast_ready_status()
        watcher.assert_awaited_once()
        assert watcher.call_args.args[0]["ready_status_info"]["player_to_ready"][str(host_index)] is True
        watcher.reset_mock()
        await broadcast_game_end(game)
        watcher.assert_awaited_once()
        assert watcher.call_args.args[0]["type"] == "gamestate/hongque/game_end"
        await game.cleanup_game_state()
    asyncio.run(run())


def test_join_during_claim_has_river_source_and_ready_snapshot_and_cleanup():
    async def run():
        game, server = fixture()
        game.players[0].discards = ["AX5"]
        game.last_discard = {"player": 0, "tile": "AX5"}
        game.phase = "claim"
        game._ready_phase_active = True
        await game.send_realtime_spectator_snapshot(201, 1)
        packets = [c.args[0] for c in server.user_id_to_connection[201].websocket.send_json.call_args_list]
        assert packets[0]["hongque_state"]["last_discard"] == game.last_discard
        assert packets[0]["hongque_state"]["pending_draw"] is None
        assert packets[1]["type"] == "gamestate/hongque/ready_status"
        game.players[0].discards.clear()
        assert game.build_state(1, sync_mode="reconnect")["last_discard"] is None
        await game.remove_spectator(201)
        await game.cleanup_game_state()
    asyncio.run(run())


def test_host_connection_failure_does_not_drop_realtime_spectator_updates():
    async def run():
        game, server = fixture()
        game.realtime_spectators = [NS(user_id=201, host_user_id=101)]
        server.user_id_to_connection[101].websocket.send_json.side_effect = ConnectionError("closed")
        await game.broadcast_state()
        server.user_id_to_connection[201].websocket.send_json.assert_awaited_once()
        server.user_id_to_connection[102].websocket.send_json.assert_awaited_once()
        await game.cleanup_game_state()
    asyncio.run(run())
