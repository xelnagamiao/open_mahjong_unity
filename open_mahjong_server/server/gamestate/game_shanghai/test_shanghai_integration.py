"""四个协议客户端驱动真实询问队列，覆盖整场、牌谱、重连与建房。"""
import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .test_shanghai_state import make_state
from ..public.ai.get_action import get_ai_action
from ..public.ai.smart_bot_logic import count_visible_tiles, find_best_cut
from ..public.hand_slot_utils import has_draw_slot, infer_bot_cut_class
from ..game_taiwan.boardcast import send_reconnect_game_state
from ...room.room_manager import RoomManager


@pytest.mark.parametrize("seed", [1729, 2024, 144])
def test_four_clients_complete_four_hands(seed):
    async def run():
        state = make_state()
        state.room_random_seed = seed
        state.round_time = 30
        state.step_time = 5
        for player in state.player_list:
            player.remaining_time = 30
        state.game_server.gamestate_manager.cleanup_game_state_complete = AsyncMock()
        state.game_server.room_manager.finish_custom_game_room = AsyncMock()
        messages = []
        submitted = Counter()
        tasks = set()

        async def respond(uid, actions, tick):
            for _ in range(1000):
                player = next(p for p in state.player_list if p.user_id == uid)
                index = player.player_index
                if index in state.waiting_players_list:
                    break
                await asyncio.sleep(0)
            else:
                raise AssertionError("询问未打开")
            assert tick == state.server_action_tick
            action, tile, cut_index, target = None, None, None, None
            if "ready" in actions:
                action = "ready"
            elif "buhua" in actions:
                action = "buhua"
            else:
                action = next((a for a in actions if a.startswith("hu_")), None)
            if action is None and "riichi_cut" in actions:
                action = "riichi_cut"
                tile = max(player.riichi_candidate_cuts, key=lambda t: len(player.riichi_candidate_cuts[t]))
            if action is None and "cut" in actions:
                action = "cut"
                if player.ready_locked:
                    tile = player.hand_tiles[-1]
                else:
                    tile, _ = find_best_cut(player.hand_tiles, len(player.combination_tiles),
                                            count_visible_tiles(state), player.kuikae_forbidden_tiles)
            if action is None:
                # 有合法鸣牌则响应，触发食替、承包和鸣牌后敲牌路径。
                action = next((a for a in actions if a in ("peng", "gang", "chi_left", "chi_mid", "chi_right")), "pass")
            cut_class = False
            if tile is not None:
                cut_index = len(player.hand_tiles) - 1 if player.ready_locked else player.hand_tiles.index(tile)
                cut_class = infer_bot_cut_class(player.hand_tiles, tile, cut_index, draw_slot=has_draw_slot(player))
            submitted[action] += 1
            await get_ai_action(state, index, action, cut_class, tile, cut_index, target)

        class Socket:
            def __init__(self, uid):
                self.uid = uid

            async def send_json(self, payload):
                messages.append((self.uid, payload))
                info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
                if info and info.get("action_list"):
                    task = asyncio.create_task(respond(self.uid, info["action_list"], info["action_tick"]))
                    tasks.add(task)
                elif payload["type"].endswith("/ready_status"):
                    player = next(p for p in state.player_list if p.user_id == self.uid)
                    if state.action_dict.get(player.player_index):
                        tasks.add(asyncio.create_task(respond(self.uid, ["ready"], state.server_action_tick)))

        for player in state.player_list:
            state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=Socket(player.user_id))
        with patch('server.gamestate.game_shanghai.ShanghaiGameState.liuju_ready_wait_seconds', return_value=0):
            await asyncio.wait_for(state.game_loop_chinese(), 45)
        await asyncio.gather(*tasks)
        rounds = state.game_record["game_round"]
        assert len(rounds) == 4
        assert sum(p.score for p in state.player_list) == 0
        assert submitted["riichi_cut"] > 0
        assert submitted["buhua"] > 0
        assert sum(count for action, count in submitted.items() if action.startswith("hu_")) > 0
        assert all(p.round_number_history for p in state.player_list)
        starts = [p for _, p in messages if p["type"] == "gamestate/shanghai/game_start"]
        assert len(starts) == 16
        assert all(p["game_info"]["room_rule"] == "shanghai" for p in starts)
        assert any(p["type"] == "gamestate/shanghai/game_end" for _, p in messages)
        assert not any(p["type"].startswith("gamestate/taiwan/") for _, p in messages)
        state.game_server.gamestate_manager.cleanup_game_state_complete.assert_awaited_once()
        state.game_server.room_manager.finish_custom_game_room.assert_not_awaited()
    asyncio.run(run())


def test_claim_broadcast_and_reconnect_use_three_second_clock():
    from ..game_taiwan.boardcast import broadcast_ask_other_action, reconnected_send_pending_ask_for_viewer
    async def run():
        state = make_state()
        state.game_status = "waiting_action_after_cut"
        state.player_list[0].discard_tiles = [11]
        state.action_dict = {1: ["peng", "pass"]}
        state.step_time = 5
        for player in state.player_list:
            player.remaining_time = 30
            state.game_server.user_id_to_connection[player.user_id] = SimpleNamespace(websocket=AsyncMock())
        await broadcast_ask_other_action(state)
        socket = state.game_server.user_id_to_connection[102].websocket
        info = socket.send_json.await_args.args[0]["ask_other_action_info"]
        assert info["remaining_time"] == 0 and info["step_remaining"] == 3
        state._ask_delivered_at = {1: 100}
        with patch('server.gamestate.game_shanghai.ShanghaiGameState.time.time', return_value=101.2):
            await reconnected_send_pending_ask_for_viewer(state, 102, 1)
        info = socket.send_json.await_args.args[0]["ask_other_action_info"]
        assert info["remaining_time"] == 0 and info["step_remaining"] == 2
    asyncio.run(run())


def test_room_creation_rejects_unavailable_rule_without_side_effects():
    async def run():
        server = SimpleNamespace(
            players={"client": SimpleNamespace(user_id=101, username="host", current_room_id=None)},
            db_manager=SimpleNamespace(get_user_settings=lambda _: {"username": "host"}),
            gamestate_manager=SimpleNamespace(is_user_in_active_game=lambda _: False), match_manager=None,
        )
        manager = RoomManager(server)
        manager._broadcast_room_info = AsyncMock()
        bad = await manager.create_Shanghai_room("client", "上海", 1, "", 30, 3, True, sub_rule="shanghai/unknown")
        assert not bad.success and not manager.rooms
        good = await manager.create_Shanghai_room("client", "上海", 1, "", 30, 3, True)
        assert good.success
        assert good.room_info["room_rule"] == "shanghai"
        assert good.room_info["sub_rule"] == "shanghai/qiaoma"
        manager._broadcast_room_info.assert_awaited_once()
    asyncio.run(run())


def test_reconnect_preserves_ready_and_contract_tags():
    async def run():
        state = make_state()
        state.player_list[0].tag_list = ["declared_ready", "chengbao_2"]
        socket = AsyncMock()
        state.game_server.user_id_to_connection[101] = SimpleNamespace(websocket=socket)
        await send_reconnect_game_state(state, state.player_list[0])
        payload = socket.send_json.await_args.args[0]
        assert payload["type"] == "gamestate/shanghai/game_start"
        assert payload["game_info"]["players_info"][0]["tag_list"] == ["declared_ready", "chengbao_2"]
    asyncio.run(run())
