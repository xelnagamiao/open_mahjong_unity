"""Physical drawn-tile identity must survive first snapshot and reconnect."""
import asyncio
from collections import Counter
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_flow import HAND, make_state
from ...game_calculation.hongzhong import rules as book
from ..public.game_record_manager import init_game_round
from ..game_taiwan.boardcast import (broadcast_game_start, broadcast_ask_hand_action,
                                    send_realtime_spectator_snapshot, _build_do_action_payload)

SEQ = [21, 22, 23, 24, 25, 26, 31, 32, 33, 39]
STAGES = ("initial", "normal_draw_duplicate", "after_pung", "concealed_replacement", "direct_replacement")


def fixed_complete_deal(state, hands, *, front=(), tail=()):
    """Set only a complete legal initial wall; subsequent moves use the state."""
    pool = Counter({tile: 4 for tile in book.TILES})
    for tiles in hands.values(): pool.subtract(tiles)
    pool.subtract(front); pool.subtract(tail)
    assert min(pool.values()) >= 0
    rest = list(pool.elements())
    for i, player in enumerate(state.player_list):
        player.hand_tiles = list(hands.get(i, []))
        needed = (14 if i == 0 else 13) - len(player.hand_tiles)
        player.hand_tiles += rest[:needed]; del rest[:needed]
        player.has_draw_slot = i == 0
        player.last_drawn_tile = player.hand_tiles[-1] if i == 0 else None
    state.tiles_list = list(front) + rest + list(tail)
    assert Counter(state.tiles_list + [t for p in state.player_list for t in p.hand_tiles]) == Counter({tile: 4 for tile in book.TILES})


async def prepare_stage(stage, tips=True):
    state = make_state(room_id=987020, tips=tips, random_seed=2024)
    for uid in [p.user_id for p in state.player_list] + [999]:
        state.game_server.user_id_to_connection[uid] = SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
    state.master_seed = 2024
    if stage == "initial":
        # Real random dealing, before _opening_flower_replacement. This is
        # where the original client received a false/missing draw identity.
        state.init_tiles()
    elif stage == "normal_draw_duplicate":
        fixed_complete_deal(state, {0: HAND + [45], 1: HAND}, front=[28])
    elif stage == "after_pung":
        fixed_complete_deal(state, {0: HAND + [11], 1: [11, 11, 22, 23, 24, 31, 32, 33, 35, 36, 37, 39, 45]})
    elif stage == "concealed_replacement":
        fixed_complete_deal(state, {0: [11] * 3 + SEQ + [11]}, tail=[39, 28])
    elif stage == "direct_replacement":
        fixed_complete_deal(state, {0: [19] + HAND[1:] + [11], 1: [11] * 3 + SEQ}, tail=[39, 28])
    else: raise AssertionError(stage)
    init_game_round(state)
    if stage == "normal_draw_duplicate":
        await state.execute_cut(0, {"TileId": 45, "cutClass": True})
        await state._deal_normal()
    elif stage == "after_pung":
        await state.execute_cut(0, {"TileId": 11, "cutClass": True})
        await state.execute_claim(1, "peng")
    elif stage == "concealed_replacement":
        await state.execute_angang(0, 11)
        await state._deal_supplement()
    elif stage == "direct_replacement":
        await state.execute_cut(0, {"TileId": 11, "cutClass": True})
        await state.execute_claim(1, "gang")
        await state._deal_supplement()
    await state._prepare_hand_action_after_draw()
    for index in range(4): await state._warm_hints(index)
    return state


def messages(state, uid):
    return [call.args[0] for call in state.game_server.user_id_to_connection[uid].websocket.send_json.call_args_list]


def latest_game(state, uid):
    return next(m for m in reversed(messages(state, uid)) if m["type"].endswith("/game_start"))


def assert_view(state, response, index):
    info = response["game_info"]
    player = state.player_list[index]
    expected = {"self_has_draw_slot": bool(player.has_draw_slot),
                "last_drawn_tile": player.last_drawn_tile if player.has_draw_slot else None}
    assert info["hongzhong_info"] == expected
    assert [p["player_index"] for p in info["players_info"] if "hand_tiles" in p] == [index]
    assert next(p["hand_tiles"] for p in info["players_info"] if p["player_index"] == index) == player.hand_tiles
    if player.has_draw_slot:
        assert player.hand_tiles[-1] == info["hongzhong_info"]["last_drawn_tile"]
    if not state.tips: assert "hongzhong_hints" not in info


@pytest.mark.parametrize("tips", [True, False])
@pytest.mark.parametrize("stage", STAGES)
def test_all_views_reconnect_and_authorized_spectator_keep_private_draw_identity(stage, tips):
    async def scenario():
        state = await prepare_stage(stage, tips)
        await broadcast_game_start(state)
        await broadcast_ask_hand_action(state)
        for index, player in enumerate(state.player_list):
            assert_view(state, latest_game(state, player.user_id), index)
            player.tag_list.append("offline")
            await state.player_reconnect(player.user_id)
            assert "offline" not in player.tag_list
            assert_view(state, latest_game(state, player.user_id), index)
            await send_realtime_spectator_snapshot(state, 999, index)
            spectator = latest_game(state, 999)
            assert spectator["game_info"]["view_player_index"] == index
            assert_view(state, spectator, index)
        current = state.current_player_index
        player = state.player_list[current]
        assert player.has_draw_slot == (stage != "after_pung")
        if stage == "after_pung":
            assert len(player.hand_tiles) == 11 and player.combination_tiles == ["k11"]
            assert state.build_private_game_info_fields(current)["hongzhong_info"]["last_drawn_tile"] is None
        for viewer in range(4):
            payload = _build_do_action_payload(state, viewer_index=viewer,
                action_list=["deal_tile"], action_player=current, deal_tile=player.last_drawn_tile or 0)
            if viewer != current:
                assert payload.get("deal_tile") in (None, 0)
                assert "hongzhong_info" not in payload and "hongzhong_hints" not in payload
        await send_realtime_spectator_snapshot(state, 999, -1)
    asyncio.run(scenario())


@pytest.mark.parametrize("cut_drawn", [True, False])
def test_duplicate_tile_old_and_drawn_cuts_use_distinct_physical_indices(cut_drawn):
    async def scenario():
        state = await prepare_stage("normal_draw_duplicate", tips=False)
        player = state.player_list[state.current_player_index]
        before = deepcopy(player.hand_tiles)
        index = len(before)-1 if cut_drawn else before.index(player.last_drawn_tile)
        assert index == (13 if cut_drawn else 12)
        assert before[index] == 28
        await state.execute_cut(player.player_index, {"TileId": 28, "cutIndex": index, "cutClass": cut_drawn})
        assert player.hand_tiles == before[:index] + before[index+1:]
        assert not player.has_draw_slot
        assert state.build_private_game_info_fields(player.player_index)["hongzhong_info"] == {
            "self_has_draw_slot": False, "last_drawn_tile": None}
    asyncio.run(scenario())
