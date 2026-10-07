import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from .test_state import hand, make_state, ticks


@pytest.mark.parametrize("owner", [0, 2])
@pytest.mark.parametrize("kind", ["concealed", "direct", "added"])
def test_kong_score_event_respects_each_viewers_kong_visibility(owner, kind):
    state = make_state()
    state.current_player_index = owner
    state.game_status = "waiting_hand_action"
    code = "G31" if kind == "concealed" else "g31"
    hand(state, owner, [11, 12], [code])
    state.send_to_realtime_spectators = AsyncMock()
    sockets = {
        player.user_id: SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
        for player in state.player_list
    }
    state.game_server.user_id_to_connection = sockets

    asyncio.run(state._pay_kong(owner, kind, 31))

    for viewer, player in enumerate(state.player_list):
        packet = sockets[player.user_id].websocket.send_json.await_args.args[0]
        info = packet["do_action_info"]["changchun"]
        event = info["event"]
        hidden = kind == "concealed" and viewer != owner
        assert event["tile"] == (0 if hidden else 31)
        assert event["kind"] == "kong_score" and event["kong_kind"] == kind
        assert event["player"] == owner
        assert event["delta"][viewer] == player.score
        meld = info["players"][owner]
        assert meld["melds"] == (["G0"] if hidden else [code])
        observer_view, observer_packet = state.send_to_realtime_spectators.await_args_list[viewer].args
        assert observer_view == viewer
        assert observer_packet.model_dump(exclude_none=True) == packet

    assert state.cc_event is None
    assert state.kong_ledger[-1]["tile"] == 31
    record = next(tick[1] for tick in ticks(state) if tick[0] == "cc")
    assert record["tile"] == 31 and record["kong_kind"] == kind
