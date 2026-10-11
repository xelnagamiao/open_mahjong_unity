import asyncio
import copy

import pytest

from . import boardcast
from .test_free_state import _make_state


def selection(items):
    return [{"slot": item["slot"], "token": item["token"]} for item in items]


def test_transfer_numbers_preserve_order_and_reuse_vacancy_at_end():
    async def scenario():
        state, sockets = _make_state()
        state.player_list[0].hand_tiles.extend([11, 12, 13, 14])
        for tile in (11, 12, 13):
            await state.handle_command(101, {"action": "transfer_put", "tile": tile, "face_down": True})
        assert [p["slot"] for p in state.transfer_tiles] == [1, 2, 3]
        old_second = copy.deepcopy(state.transfer_tiles[1])
        await state.handle_command(102, {"action": "transfer_take", "tiles": selection([old_second])})
        assert [p["slot"] for p in state.transfer_tiles] == [1, 3]
        await state.handle_command(101, {"action": "transfer_put", "tile": 14, "face_down": True})
        assert [p["slot"] for p in state.transfer_tiles] == [1, 3, 2]
        assert [p["tile"] for p in state.transfer_tiles] == [11, 13, 14]
        tick = state.action_tick
        await state.handle_command(103, {"action": "transfer_take", "tiles": selection([old_second])})
        assert state.action_tick == tick and not state.player_list[2].hand_tiles
        for uid, socket in sockets.items():
            await state.player_reconnect(uid)
            cards = socket.messages[-1]["free_table_info"]["transfer_tiles"]
            assert [p["slot"] for p in cards] == [1, 3, 2]
            assert [p["tile"] for p in cards] == [0, 0, 0]
    asyncio.run(scenario())


@pytest.mark.parametrize("chosen", [(0,), (1,), (2,), (0, 1), (0, 2), (1, 2), (0, 1, 2)])
def test_transfer_any_subset_is_atomic_and_only_reveals_hidden_faces_to_taker(chosen):
    async def scenario():
        state, sockets = _make_state()
        state.player_list[0].hand_tiles.extend([11, 22, 33])
        for tile, hidden in [(11, True), (22, False), (33, True)]:
            await state.handle_command(101, {"action": "transfer_put", "tile": tile, "face_down": hidden})
        original = copy.deepcopy(state.transfer_tiles)
        state.player_list[1].revealed = True
        # Reverse selection order: hand receipt and surviving table entries still use table order.
        await state.handle_command(102, {"action": "transfer_take", "tiles": selection([original[i] for i in reversed(chosen)])})
        expected = [original[i]["tile"] for i in chosen]
        assert state.player_list[1].hand_tiles == expected
        assert state.transfer_tiles == [p for i, p in enumerate(original) if i not in chosen]
        hidden = any(original[i]["face_down"] for i in chosen)
        assert state.player_list[1].revealed == (not hidden)
        for index, socket in enumerate(sockets.values()):
            msg = socket.messages[-1]
            assert msg["do_action_info"]["deal_tiles"] == [
                original[i]["tile"] if index == 1 or not original[i]["face_down"] else 0 for i in chosen]
            assert msg["do_action_info"]["transfer_slots"] == [i + 1 for i in chosen]
            assert msg["free_table_info"]["hand_counts"]["1"] == len(chosen)
            hand = boardcast.game_info_payload(state, index)["players_info"][1]["hand_tiles"]
            assert hand == (expected if index == 1 or not hidden else None)
    asyncio.run(scenario())


@pytest.mark.parametrize("bad", [[], [{"slot": 1, "token": 999}], [{"slot": True, "token": 1}],
                                 [{"slot": 1, "token": True}], [{"slot": 1, "token": 1}, {"slot": 1, "token": 1}],
                                 [{"slot": 1, "token": 1}, {"slot": 3, "token": 999}]])
def test_invalid_transfer_selection_changes_nothing(bad):
    async def scenario():
        state, sockets = _make_state()
        state.player_list[0].hand_tiles.extend([11, 12])
        for tile in (11, 12):
            await state.handle_command(101, {"action": "transfer_put", "tile": tile})
        before = copy.deepcopy(state.transfer_tiles)
        tick = state.action_tick
        await state.handle_command(102, {"action": "transfer_take", "tiles": bad})
        assert state.transfer_tiles == before and state.action_tick == tick
        assert not state.player_list[1].hand_tiles
        assert all(len(s.messages) == 2 for s in sockets.values())
    asyncio.run(scenario())


def test_overlapping_batch_takes_are_all_or_nothing():
    async def scenario():
        state, _ = _make_state()
        state.player_list[0].hand_tiles.extend([11, 12, 13])
        for tile in (11, 12, 13):
            await state.handle_command(101, {"action": "transfer_put", "tile": tile})
        first = selection(state.transfer_tiles[:2])
        second = selection(state.transfer_tiles[1:])
        await asyncio.gather(state.handle_command(102, {"action": "transfer_take", "tiles": first}),
                             state.handle_command(103, {"action": "transfer_take", "tiles": second}))
        assert state.player_list[1].hand_tiles == [11, 12]
        assert not state.player_list[2].hand_tiles
        assert [p["slot"] for p in state.transfer_tiles] == [3]
    asyncio.run(scenario())


@pytest.mark.parametrize("flower", [False, True])
def test_hidden_area_placement_recall_reconnect_and_stale_tokens(flower):
    async def scenario():
        state, sockets = _make_state()
        player = state.player_list[0]
        player.hand_tiles.extend([19, 28, 37])
        put = "to_flower" if flower else "cut"
        recall = "recall_flower" if flower else "recall_river"
        key = "huapai_list" if flower else "discard_tiles"
        token_key = "flower_tokens" if flower else "river_tokens"
        tile_key = "buhua_tile" if flower else "cut_tile"
        for tile in (19, 28):
            await state.handle_command(101, {"action": put, "tile": tile, "face_down": True})
            for index, socket in enumerate(sockets.values()):
                assert socket.messages[-1]["do_action_info"][tile_key] == (tile if index == 0 else 0)
                assert socket.messages[-1]["free_table_info"]["hand_counts"]["0"] == len(player.hand_tiles)
        for uid, socket in sockets.items():
            await state.player_reconnect(uid)
            snap = socket.messages[-1]
            assert snap["game_info"]["players_info"][0][key] == [0, 0]
            if not flower:
                assert snap["free_table_info"]["last_river_tile"] == 0
        tokens = boardcast.free_table_payload(state)[token_key]["0"]
        await state.handle_command(101, {"action": "reveal"})
        await state.handle_command(101, {"action": recall, "index": 0, "tile": 0, "token": tokens[0]})
        assert not player.revealed and player.hand_tiles == [37, 19]
        for index, socket in enumerate(sockets.values()):
            action = socket.messages[-1]["do_action_info"]
            assert action[tile_key] == 0
            assert action["deal_tile"] == (19 if index == 0 else None)
        tick = state.action_tick
        for message in [{"tile": 0, "token": tokens[0]}, {"tile": 0}, {"tile": 28, "token": tokens[1]}]:
            await state.handle_command(101, {"action": recall, "index": 0, **message})
            assert state.action_tick == tick
        await state.handle_command(101, {"action": recall, "index": 0, "tile": 0, "token": tokens[1]})
        assert player.hand_tiles == [37, 19, 28]
        assert boardcast.player_info_payload(state, 0, 1)[key] == []
    asyncio.run(scenario())


def test_hidden_river_cannot_be_guessed_into_meld_and_duplicate_faces_remain_aligned():
    async def scenario():
        state, _ = _make_state()
        state.player_list[0].hand_tiles.extend([11, 11])
        state.player_list[1].hand_tiles.extend([11, 11])
        await state.handle_command(101, {"action": "cut", "tile": 11, "face_down": True})
        await state.handle_command(102, {"action": "create_meld", "mask": [0, 11, 0, 11, 1, 11], "include_river": True})
        assert not state.player_list[1].combination_mask
        await state.handle_command(101, {"action": "cut", "tile": 11})
        await state.handle_command(102, {"action": "create_meld", "mask": [0, 11, 0, 11, 1, 11], "include_river": True})
        assert len(state.player_list[1].combination_mask) == 1
        assert boardcast.player_info_payload(state, 0, 1)["discard_tiles"] == [0]
        assert boardcast.free_table_payload(state)["last_river_tile"] == 0
        assert boardcast.free_table_payload(state)["hand_counts"] == {"0": 0, "1": 0, "2": 0, "3": 0}
    asyncio.run(scenario())
