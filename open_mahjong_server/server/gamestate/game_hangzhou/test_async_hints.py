"""Transport own-hand inputs without running a future-tile solver on the server."""
import asyncio
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from server.gamestate.game_hangzhou import client_context as hints
from .test_flow import FLOAT, turn, act


def live():
    s = turn(FLOAT, tips=True)
    s._client_context_enabled = True
    s.outbound_payloads = []
    s.outbound_send_cursor = 0
    s._recorded_hint_keys = {}
    return s


@pytest.fixture(autouse=True)
def no_server_hint_search(monkeypatch):
    def forbidden(*args, **kwargs):
        pytest.fail("Transport must not calculate wait or fan hints")
    from ...game_calculation.hangzhou import rules
    for name in ("compute_batch", "compute", "_waits", "_wait_details"):
        assert not hasattr(hints, name)
    monkeypatch.setattr(rules, "waiting_tiles", forbidden)


def test_live_reconnect_and_spectator_send_only_own_hand_inputs():
    async def run():
        s = live()
        expected = hints.local_context(hints.current_request(s, 0))
        s.emit_game_start_payloads()
        s.emit_window_payloads(s.live_pending_window)
        s._send_claim_protection_payload = AsyncMock()
        await s.flush_outbound_payloads()
        for payload in s.outbound_payloads:
            if "game_info" not in payload:
                continue
            index = payload["player_index"]
            info = payload["game_info"]["hangzhou_info"]
            assert info["source_hand_tiles"] == s.player_list[index].hand_tiles
            assert not {"waiting_tiles", "waiting_by_discard"} & info.keys()
            if index == 0:
                assert all(info[k] == v for k, v in expected.items())
        assert hints.private_context(s, 0) == expected
        s.send_payload_to_player = AsyncMock()
        await s.player_reconnect(101)
        assert s.send_payload_to_player.await_count == 2
        s.game_server = SimpleNamespace(user_id_to_connection={999: SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))})
        await s.send_realtime_spectator_snapshot(999, 0)
    asyncio.run(run())


def test_concurrent_rooms_and_returned_snapshots_are_isolated():
    async def run():
        rooms = [live() for _ in range(4)]
        rooms[1].player_list[0].hand_tiles[-1] = 43
        assert all(await asyncio.gather(*(hints.attach_contexts(s, indices=(0,)) for s in rooms)))
        assert hints.private_context(rooms[0], 0)["local_request"]["hand"][-1] == 46
        assert hints.private_context(rooms[1], 0)["local_request"]["hand"][-1] == 43
        wire = hints.private_context(rooms[0], 0)
        wire["local_request"]["hand"].clear()
        assert hints.private_context(rooms[0], 0)["local_request"]["hand"] == FLOAT
    asyncio.run(run())


def test_pending_snapshot_uses_its_own_hand_and_lock_policy():
    async def run():
        s = live()
        s.cai_discard_locks = {1}
        before = s.build_game_start_payload(0)
        act(s, 0, "cut", TileId=46, cutClass=True)
        after = s.build_game_start_payload(0)
        assert await hints.attach_contexts(s, payloads=[before, after, {"_pause": 0}, {"player_index": 0}])
        old = before["game_info"]["hangzhou_info"]["local_request"]
        new = after["game_info"]["hangzhou_info"]["local_request"]
        assert old["cuts"] == [46] and len(old["hand"]) == 14
        assert len(new["hand"]) == 13 and new["cuts"] == []
        frozen = deepcopy(old)
        s.player_list[0].hand_tiles.clear()
        assert old == frozen
        s.end_draw()
        assert await hints.attach_contexts(s, payloads=[s.build_game_start_payload(0)], indices=(0,))
        assert hints.private_context(s, 0) == {}
    asyncio.run(run())


@pytest.mark.parametrize("tips,bot", [(False, False), (False, True), (True, True)])
def test_replay_inputs_survive_live_tips_gate_and_bot_seats(tips, bot):
    async def run():
        s = live()
        s.tips = tips
        if bot:
            s.player_list[0].user_id = 1
        s.cai_discard_locks = {1}
        payload = s.build_game_start_payload(0)
        assert await hints.attach_contexts(s, payloads=[payload], indices=(0,))
        assert hints.private_context(s, 0) == {}
        assert "local_request" not in payload["game_info"]["hangzhou_info"]
        ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
        snapshots = [tick for tick in ticks if tick[:2] == ["hangzhou", "hints"]]
        assert {tick[2] for tick in snapshots} == {0, 1, 2, 3}
        own = [tick[3] for tick in snapshots if tick[2] == 0][-1]
        assert own["local_request"]["hand"] == FLOAT
        assert own["local_request"]["cuts"] == [46]
        assert "waiting_by_discard" not in own
        before = len(snapshots)
        assert await hints.attach_contexts(s)
        assert len([tick for tick in ticks if tick[:2] == ["hangzhou", "hints"]]) == before
        act(s, 0, "cut", TileId=46, cutClass=True)
        assert await hints.attach_contexts(s)
        own = [tick[3] for tick in ticks if tick[:3] == ["hangzhou", "hints", 0]][-1]
        assert own["source_hand_tiles"] == sorted(FLOAT[:-1])
    asyncio.run(run())
