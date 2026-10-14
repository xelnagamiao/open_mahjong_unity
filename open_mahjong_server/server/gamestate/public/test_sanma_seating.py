"""Physical meld origins must agree across players, reconnects and record playback."""
import asyncio
import importlib
from copy import deepcopy
from itertools import permutations
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .ai.get_action import get_ai_action
from .logic_common import get_index_relative_position, next_current_num
from ..game_riichi.test_sanma import sanma as riichi_sanma
from ..game_riichi.test_double_riichi import make_game
from ..game_guobiao.test_sanma import sanma as guobiao_game
from ..verifier.record_sim.goto_action import relative_position
from ..verifier.record_sim.meld_codec import build_mingpai_mask


DIRECTIONS = (
    ("self", "right", "top", "left"),
    ("left", "self", "right", "top"),
    ("top", "left", "self", "right"),
    ("right", "top", "left", "self"),
)


@pytest.mark.parametrize("count", [3, 4])
def test_physical_geometry_is_independent_of_turn_order(count):
    for viewer in range(count):
        for player in range(count):
            expected = ("self", "right", "left")[(player-viewer)%3] if count == 3 else DIRECTIONS[viewer][player]
            assert get_index_relative_position(viewer, player, count) == expected
            assert relative_position(viewer, player, count) == expected
        assert next_current_num(viewer, count) == (viewer + 1) % count


def new_game(rule, count):
    if rule == "riichi":
        return riichi_sanma() if count == 3 else make_game(0)[0]
    return guobiao_game(sub_rule="guobiao/sanma" if count == 3 else "guobiao/standard")


def sideways_slot(actor, source, tile_count, count=3):
    direction = ("self", "right", "left")[(source-actor)%3] if count == 3 else DIRECTIONS[actor][source]
    return {"left": 0, "top": 1, "right": tile_count - 1}[direction]


@pytest.mark.parametrize("rule", ["riichi", "guobiao"])
@pytest.mark.parametrize("count,actor,source", [(n, a, s) for n in (3, 4) for a, s in permutations(range(n), 2)])
@pytest.mark.parametrize("action", ["peng", "gang"])
def test_authoritative_calls_reconnect_and_replay_share_actual_source(rule, count, actor, source, action):
    async def run():
        game = new_game(rule, count)
        game.room_id = 825009
        game.game_server.user_id_to_connection = {p.user_id: SimpleNamespace(
            websocket=SimpleNamespace(send_json=AsyncMock())) for p in game.player_list}
        module = importlib.import_module(f"{game.__class__.__module__.rsplit('.', 1)[0]}.wait_action")
        game.current_player_index = source
        game.game_status = "waiting_action_after_cut"
        game.game_record = {"game_title": {}, "game_round": {"round_index_1": {"action_ticks": []}}}
        game.player_list[source].discard_tiles = [25]
        # Preserve red physical tile IDs through the displayed mask and the stored record.
        hand = ([205, 25] if rule == "riichi" else [25, 25]) + ([25] if action == "gang" else [])
        game.player_list[actor].hand_tiles = hand + [21, 22, 23, 31, 32, 33, 41, 42, 43, 45]
        game.action_dict = {i: [action, "pass"] if i == actor else [] for i in range(count)}
        with patch.object(module, "broadcast_do_action", new=AsyncMock()) as sent:
            task = asyncio.create_task(module.wait_action(game))
            await asyncio.sleep(0)
            await get_ai_action(game, actor, action, False, None, None, None)
            await asyncio.wait_for(task, 3)
        mask = game.player_list[actor].combination_mask[-1]
        tile_count = 4 if action == "gang" else 3
        assert mask[::2].index(1) == sideways_slot(actor, source, tile_count, count)
        assert sorted(mask[1::2]) == sorted(hand + [25])
        assert not game.player_list[source].discard_tiles
        assert game.current_player_index == actor
        payload = sent.await_args.kwargs
        assert payload["combination_mask"] == mask
        assert payload["cut_from_player"] == source and payload["cut_tile"] == 25
        tick = game.game_record["game_round"]["round_index_1"]["action_ticks"][-1]
        assert build_mingpai_mask(tick[0], tick[1], tick[3:], relative_position(actor, source, count)) == mask
        # Actual reconnect snapshots to all players retain this exact origin and real tile IDs.
        for player in game.player_list:
            ws = game.game_server.user_id_to_connection[player.user_id].websocket
            ws.send_json.reset_mock()
            await game.player_reconnect(player.user_id)
            messages = [call.args[0] for call in ws.send_json.await_args_list]
            info = next(m["game_info"] for m in messages if "game_info" in m)
            meld_owner = next(p for p in info["players_info"] if p["player_index"] == actor)
            assert meld_owner["combination_mask"] == [mask]
        if action == "peng" and rule == "riichi":
            # Adding a fourth tile must stack above the called tile, including an opposite call.
            from ..game_riichi.kan_actions import commit_kan
            game.player_list[actor].hand_tiles.append(25)
            game.player_list[actor].has_draw_slot = True
            game._pending_kan = dict(kind="jiagang", normal=25, actual=25, is_mo_gang=True)
            before = deepcopy(mask)
            await commit_kan(game)
            added = game.player_list[actor].combination_mask[-1]
            slot = sideways_slot(actor, source, 3, count)
            assert added == before[:slot * 2] + [3, 25] + before[slot * 2:]
    asyncio.run(run())


@pytest.mark.parametrize("source", range(3))
@pytest.mark.parametrize("action,tile,hand,code", [
    ("chi_right", 21, [22, 23], "cr"),
    ("chi_mid", 22, [21, 23], "cm"),
    ("chi_left", 23, [21, 22], "cl"),
])
def test_guobiao_chow_skips_empty_turn_but_points_at_actual_source(source, action, tile, hand, code):
    async def run():
        game = guobiao_game()
        actor = (source + 1) % 3
        module = importlib.import_module(game.__class__.__module__.rsplit('.', 1)[0] + ".wait_action")
        game.current_player_index = source
        game.game_status = "waiting_action_after_cut"
        game.game_record = {"game_title": {}, "game_round": {"round_index_1": {"action_ticks": []}}}
        game.player_list[source].discard_tiles = [tile]
        game.player_list[actor].hand_tiles = hand + [31, 32, 33, 41, 42, 43, 45]
        game.action_dict = {i: [action, "pass"] if i == actor else [] for i in range(3)}
        with patch.object(module, "broadcast_do_action", new=AsyncMock()):
            task = asyncio.create_task(module.wait_action(game))
            await asyncio.sleep(0)
            await get_ai_action(game, actor, action, False, None, None, None)
            await asyncio.wait_for(task, 3)
        mask = game.player_list[actor].combination_mask[-1]
        assert mask[::2].index(1) == sideways_slot(actor, source, 3)
        assert sorted(mask[1::2]) == sorted(hand + [tile])
        tick = game.game_record["game_round"]["round_index_1"]["action_ticks"][-1]
        assert tick[0] == code
        assert build_mingpai_mask(code, tile, tick[3:], relative_position(actor, source, 3)) == mask
    asyncio.run(run())
