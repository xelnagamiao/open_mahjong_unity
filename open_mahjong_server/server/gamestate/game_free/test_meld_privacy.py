import asyncio
from copy import deepcopy

import pytest

from . import boardcast
from .test_free_state import _make_state


@pytest.mark.parametrize("owner", [0, 2])
@pytest.mark.parametrize("revealed", [False, True])
@pytest.mark.parametrize("mask", [
    [2, 13] * 4,
    [0, 11, 2, 12, 1, 13],
    [0, 13] * 4,
])
def test_face_down_meld_tiles_are_private_in_broadcast_reconnect_and_recall(owner, mask, revealed):
    async def scenario():
        state, sockets = _make_state()
        player = state.player_list[owner]
        player.revealed = revealed
        player.hand_tiles = mask[1::2]
        await state.handle_command(player.user_id, {"action": "create_meld", "mask": mask})
        assert player.combination_mask == [mask] and player.hand_tiles == []
        original = deepcopy(player.combination_mask)
        for viewer, socket in enumerate(sockets.values()):
            expected = [value if viewer == owner or revealed or index % 2 == 0 or mask[index - 1] != 2 else 0
                        for index, value in enumerate(mask)]
            assert socket.messages[-1]["do_action_info"]["combination_mask"] == expected
            snapshot = boardcast.game_info_payload(state, viewer)["players_info"][owner]
            snapshot_mask = [value if viewer == owner or index % 2 == 0 or mask[index - 1] != 2 else 0
                             for index, value in enumerate(mask)]
            assert snapshot["combination_mask"] == [snapshot_mask]

        assert player.combination_mask == original
        for viewer, (user_id, socket) in enumerate(sockets.items()):
            await state.player_reconnect(user_id)
            expected = [value if viewer == owner or index % 2 == 0 or mask[index - 1] != 2 else 0
                        for index, value in enumerate(mask)]
            assert socket.messages[-1]["game_info"]["players_info"][owner]["combination_mask"] == [expected]
        await state.handle_command(player.user_id, {"action": "recall_meld", "index": 0})
        assert player.hand_tiles == mask[1::2] and player.combination_mask == []
        for viewer, socket in enumerate(sockets.values()):
            expected = [value if viewer == owner or revealed or index % 2 == 0 or mask[index - 1] != 2 else 0
                        for index, value in enumerate(mask)]
            info = socket.messages[-1]["do_action_info"]
            assert info["combination_mask"] == expected
            assert info.get("deal_tiles") == (mask[1::2] if viewer == owner or revealed else None)

    asyncio.run(scenario())


@pytest.mark.parametrize("owner", [0, 2])
def test_revealing_remaining_hand_does_not_reveal_existing_face_down_meld(owner):
    async def scenario():
        state, sockets = _make_state()
        player = state.player_list[owner]
        player.hand_tiles = [13] * 4 + [14, 15]
        await state.handle_command(player.user_id, {"action": "create_meld", "mask": [2, 13] * 4})
        await state.handle_command(player.user_id, {"action": "reveal"})
        assert player.revealed
        for viewer, (user_id, socket) in enumerate(sockets.items()):
            assert socket.messages[-1]["free_table_info"]["revealed_hand"] == [14, 15]
            await state.player_reconnect(user_id)
            snapshot = socket.messages[-1]["game_info"]["players_info"][owner]
            assert snapshot["hand_tiles"] == [14, 15]
            assert snapshot["combination_mask"] == [[2, 13 if viewer == owner else 0] * 4]
        assert player.combination_mask == [[2, 13] * 4]

    asyncio.run(scenario())
