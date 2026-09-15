"""对局表情包：共享广播需兼容虹雀玩家模型。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

from server.gamestate.game_hongque.HongqueGameState import HongqueGameState
from server.gamestate.gamestate_router import handle_send_sticker
from server.gamestate.public.sticker import (
    broadcast_sticker,
    resolve_sticker_sender,
)


def _hongque_room(user_ids=(101, 2, 103, 104)):
    return {
        "room_id": "sticker-room",
        "room_rule": "hongque",
        "game_round": 1,
        "player_list": list(user_ids),
        "player_settings": {
            user_id: {"username": f"P{user_id}"} for user_id in user_ids
        },
    }


def _connection():
    websocket = SimpleNamespace(send_json=AsyncMock())
    return SimpleNamespace(websocket=websocket), websocket


def test_resolve_sticker_sender_accepts_hongque_index_only():
    player = SimpleNamespace(index=2, user_id=103)
    assert resolve_sticker_sender(player) == (2, 2)


def test_resolve_sticker_sender_prefers_shared_rule_fields():
    player = SimpleNamespace(
        player_index=1,
        original_player_index=3,
        index=9,
        user_id=101,
    )
    assert resolve_sticker_sender(player) == (1, 3)


def test_hongque_player_exposes_shared_sticker_fields():
    state = HongqueGameState(None, _hongque_room(), gamestate_id="sticker-alias")
    sender = state.players[2]
    sender.online = False
    assert sender.player_index == 2
    assert sender.original_player_index == 2
    assert "offline" in sender.tag_list
    sender.online = True
    assert sender.tag_list == []


def test_broadcast_sticker_reaches_hongque_humans_and_skips_bots():
    async def run():
        human_a, ws_a = _connection()
        human_b, ws_b = _connection()
        game_server = SimpleNamespace(
            user_id_to_connection={101: human_a, 103: human_b},
        )
        state = HongqueGameState(
            game_server, _hongque_room(), gamestate_id="sticker-broadcast"
        )
        await broadcast_sticker(state, 0, 0, "turtle/3")
        assert ws_a.send_json.await_count == 1
        assert ws_b.send_json.await_count == 1
        payload = ws_a.send_json.await_args.args[0]
        assert payload["type"] == "gamestate/broadcast_sticker"
        assert payload["sticker_info"] == {
            "player_index": 0,
            "original_player_index": 0,
            "sticker": "turtle/3",
        }

    asyncio.run(run())


def test_broadcast_sticker_skips_offline_hongque_and_tagged_standard_players():
    async def run():
        online_conn, online_ws = _connection()
        offline_hongque_conn, offline_hongque_ws = _connection()
        tagged_conn, tagged_ws = _connection()
        game_server = SimpleNamespace(
            user_id_to_connection={
                101: online_conn,
                102: offline_hongque_conn,
                103: tagged_conn,
            },
        )
        hongque_online = SimpleNamespace(
            user_id=101, index=0, online=True, is_bot=False, tag_list=[]
        )
        hongque_offline = SimpleNamespace(
            user_id=102, index=1, online=False, is_bot=False
        )
        standard_offline = SimpleNamespace(
            user_id=103,
            player_index=2,
            original_player_index=2,
            tag_list=["offline"],
            is_bot=False,
        )
        state = SimpleNamespace(
            game_server=game_server,
            player_list=[hongque_online, hongque_offline, standard_offline],
            realtime_spectators=[],
        )
        await broadcast_sticker(state, 0, 0, "turtle/1")
        assert online_ws.send_json.await_count == 1
        assert offline_hongque_ws.send_json.await_count == 0
        assert tagged_ws.send_json.await_count == 0

    asyncio.run(run())


def test_handle_send_sticker_accepts_hongque_game_state():
    async def run():
        sender_conn, sender_ws = _connection()
        other_conn, other_ws = _connection()
        game_server = SimpleNamespace(
            user_id_to_connection={101: sender_conn, 103: other_conn},
            players={"c1": SimpleNamespace(user_id=101)},
            gamestate_manager=SimpleNamespace(),
        )
        state = HongqueGameState(
            game_server, _hongque_room(), gamestate_id="sticker-send"
        )
        game_server.gamestate_manager.get_game_state_by_gamestate_id = (
            lambda gamestate_id: state if gamestate_id == "sticker-send" else None
        )
        await handle_send_sticker(
            game_server,
            "c1",
            {"gamestate_id": "sticker-send", "sticker": "turtle/5"},
            SimpleNamespace(),
        )
        assert sender_ws.send_json.await_count == 1
        assert other_ws.send_json.await_count == 1
        payload = sender_ws.send_json.await_args.args[0]
        assert payload["sticker_info"]["player_index"] == 0
        assert payload["sticker_info"]["original_player_index"] == 0
        assert payload["sticker_info"]["sticker"] == "turtle/5"

    asyncio.run(run())
