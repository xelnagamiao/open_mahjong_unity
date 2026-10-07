"""Final-four tile identities stay private until the authoritative round ends."""
import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_state import hand, make_state
from ...game_calculation.changchun.test_rules import READY
from ..game_taiwan.boardcast import broadcast_do_action, send_realtime_spectator_snapshot


def tail_state(owner, remaining):
    state = make_state()
    state.current_player_index = owner
    state.game_status = "waiting_hand_action"
    state.cc_window = "final_four"
    state.cc_tail_remaining = remaining
    for index in range(4):
        hand(state, index, READY + ([28] if index == owner else []),
             drawn=28 if index == owner else None)
    state.send_to_realtime_spectators = AsyncMock()
    sockets = {
        player.user_id: SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
        for player in state.player_list
    }
    state.game_server.user_id_to_connection = sockets
    return state, sockets


@pytest.mark.parametrize("owner", range(4))
@pytest.mark.parametrize("remaining", range(4))
def test_final_draw_and_pass_wire_payloads_hide_other_seats_tiles(owner, remaining):
    state, sockets = tail_state(owner, remaining)

    async def run():
        await broadcast_do_action(state, ["deal_tile"], owner, deal_tile=28)
        await state.pass_final_tile()

    asyncio.run(run())
    for viewer, player in enumerate(state.player_list):
        packets = sockets[player.user_id].websocket.send_json.await_args_list
        assert len(packets) == 2
        draw = packets[0].args[0]["do_action_info"]
        assert draw.get("deal_tile") == (28 if viewer == owner else None)
        assert draw["changchun"]["self_last_drawn_tile"] == (28 if viewer == owner else 0)

        passed_packet = packets[1].args[0]
        passed = passed_packet["do_action_info"]["changchun"]
        assert passed["phase"] == "waiting_hand_action"
        assert passed["tail_tiles"] == [{"player": owner, "tile": 28 if viewer == owner else 0}]
        assert passed["event"] == {"kind": "tail_pass", "player": owner}
        for snapshot in passed["players"]:
            if snapshot["player"] != viewer:
                assert "hand" not in snapshot and "last_drawn_tile" not in snapshot
        forwarded_view, forwarded_packet = state.send_to_realtime_spectators.await_args_list[4 + viewer].args
        assert forwarded_view == viewer
        assert forwarded_packet.model_dump(exclude_none=True) == passed_packet


@pytest.mark.parametrize("owner", range(4))
@pytest.mark.parametrize("viewer", range(4))
def test_final_pass_rejoin_snapshot_uses_selected_seat_privacy(owner, viewer):
    state, sockets = tail_state(owner, 2)
    asyncio.run(state.pass_final_tile())
    assert state.game_status != "END"
    spectator_id = 9000 + viewer
    socket = SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
    sockets[spectator_id] = socket
    asyncio.run(send_realtime_spectator_snapshot(state, spectator_id, viewer))
    snapshot = socket.websocket.send_json.await_args_list[0].args[0]["game_info"]
    assert snapshot["view_player_index"] == viewer
    assert snapshot["changchun"]["tail_tiles"] == [{"player": owner, "tile": 28 if viewer == owner else 0}]
    for player in snapshot["players_info"]:
        if player["player_index"] != viewer:
            assert "hand_tiles" not in player
