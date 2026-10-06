"""敲麻补花来源必须支持自动连续补花及敲牌后的自动摸切。"""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_shanghai_state import make_state, lock
from ...game_calculation.shanghai.test_qiaoma import PLAIN
from ..game_taiwan.boardcast import (
    broadcast_ask_hand_action, send_reconnect_game_state,
    reconnected_send_pending_ask_for_viewer,
)
from ..public.hand_draw_source import get_hand_draw_source


def connect_players(state):
    for player in state.player_list:
        player.opening_flowers_done = True
        state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=AsyncMock())
    return state.game_server.user_id_to_connection[101].websocket


async def ask_current_player(state, socket):
    await broadcast_ask_hand_action(state)
    return socket.send_json.await_args.args[0]["ask_hand_action_info"]


@pytest.mark.parametrize("declared", [False, True])
@pytest.mark.parametrize("minimum", [0, 1])
def test_flower_draw_is_not_a_kong_decision_and_locked_draw_can_be_discarded(declared, minimum):
    async def run():
        state = make_state(hepai_limit=minimum)
        socket = connect_players(state)
        player = state.player_list[0]
        lock(player, PLAIN[:-1])
        player.declared_ready = player.ready_locked = declared
        player.hand_tiles.append(51)
        player.last_drawn_tile = 51
        player.has_draw_slot = True
        state.tiles_list = [11, 12, 13, 29]

        await state.execute_buhua(0)
        assert player.has_draw_slot and player.last_drawn_tile == 29
        assert player.declared_ready == declared and player.ready_locked == declared
        assert get_hand_draw_source(state, 0) == "deal_buhua_tile"
        ask = await ask_current_player(state, socket)
        assert ask["action_list"] == (["cut"] if declared else ["riichi_cut", "cut"])
        assert ask["deal_tile_type"] == "deal_buhua_tile"
        # 客户端自动摸切提交的补入牌能通过服务端锁手检查。
        await state.execute_cut(0, {"TileId": 29, "CutClass": True, "TileIndex": len(player.hand_tiles) - 1})
        assert player.hand_tiles == PLAIN[:-1]
        assert player.discard_tiles[-1] == 29
    asyncio.run(run())


@pytest.mark.parametrize("declared", [False, True])
def test_consecutive_flower_draws_keep_auto_buhua_source(declared):
    async def run():
        state = make_state()
        socket = connect_players(state)
        player = state.player_list[0]
        lock(player, PLAIN[:-1])
        player.declared_ready = player.ready_locked = declared
        player.hand_tiles.append(51)
        player.last_drawn_tile = 51
        player.has_draw_slot = True
        state.tiles_list = [11, 12, 13, 29, 47, 46]

        final_actions = ["cut"] if declared else ["riichi_cut", "cut"]
        for replacement, expected_actions in ((46, ["buhua"]), (47, ["buhua"]), (29, final_actions)):
            await state.execute_buhua(0)
            assert len(player.hand_tiles) == 14
            assert player.last_drawn_tile == replacement and player.has_draw_slot
            ask = await ask_current_player(state, socket)
            assert ask["action_list"] == expected_actions
            assert ask["deal_tile_type"] == "deal_buhua_tile"
        assert player.huapai_list == [51, 46, 47]
        assert player.hand_tiles == PLAIN[:-1] + [29]
    asyncio.run(run())


def test_flower_replacement_win_is_offered_before_any_discard():
    async def run():
        state = make_state()
        socket = connect_players(state)
        player = state.player_list[0]
        lock(player, PLAIN[:-1])
        player.hand_tiles.append(51)
        player.last_drawn_tile = 51
        player.has_draw_slot = True
        state.tiles_list = [11, 12, 13, 44]

        await state.execute_buhua(0)
        ask = await ask_current_player(state, socket)
        assert ask["action_list"] == ["hu_self"]
        assert ask["deal_tile_type"] == "deal_buhua_tile"
        assert player.hand_tiles == PLAIN
        assert player.ready_locked
    asyncio.run(run())


def test_reconnect_preserves_flower_draw_source_for_pending_discard():
    async def run():
        state = make_state()
        socket = connect_players(state)
        player = state.player_list[0]
        lock(player, PLAIN[:-1])
        player.hand_tiles.append(51)
        player.last_drawn_tile = 51
        player.has_draw_slot = True
        state.tiles_list = [11, 12, 13, 29]

        await state.execute_buhua(0)
        await send_reconnect_game_state(state, player)
        await reconnected_send_pending_ask_for_viewer(state, player.user_id, player.player_index)
        ask = socket.send_json.await_args.args[0]["ask_hand_action_info"]
        assert ask["action_list"] == ["cut"]
        assert ask["deal_tile_type"] == "deal_buhua_tile"
    asyncio.run(run())
