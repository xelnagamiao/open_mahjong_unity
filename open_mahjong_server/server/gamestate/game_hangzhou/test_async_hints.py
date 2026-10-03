"""Live hint computation uses immutable snapshots off the room event loop."""

import asyncio
from dataclasses import FrozenInstanceError
import threading
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from . import hints
from .test_flow import FLOAT, turn, act


def live():
    s = turn(FLOAT, tips=True)
    s._async_hints_enabled = True
    s.outbound_payloads = []
    s.outbound_send_cursor = 0
    s._recorded_hint_keys = {}
    return s


def test_live_wire_hints_equal_sync_result_and_batch_has_only_frozen_own_hands(monkeypatch):
    async def run():
        s = live()
        expected = hints.to_wire(hints.current_request(s, 0), hints.compute(hints.current_request(s, 0)))
        worker = hints.compute_batch
        calls = []
        main = threading.get_ident()
        def checked(requests):
            assert threading.get_ident() != main
            assert all(isinstance(r, hints.HintRequest) for r in requests)
            with pytest.raises(FrozenInstanceError):
                requests[0].hand = ()
            calls.append(requests)
            return worker(requests)
        monkeypatch.setattr(hints, "compute_batch", checked)
        s.emit_game_start_payloads()
        s.emit_window_payloads(s.live_pending_window)
        assert "source_hand_tiles" not in s.outbound_payloads[0]["game_info"]["hangzhou_info"]
        s._send_claim_protection_payload = AsyncMock()
        await s.flush_outbound_payloads()
        assert len(calls) == 1 and len(calls[0]) == 4
        for payload in s.outbound_payloads:
            index = payload["player_index"]
            info = payload["game_info"]["hangzhou_info"]
            assert info["source_hand_tiles"] == s.player_list[index].hand_tiles
            if index == 0:
                assert all(info[k] == v for k, v in expected.items())
        assert hints.private_hints(s, 0) == expected
        # A cache-only reconnect must not schedule another solver job.
        s.send_payload_to_player = AsyncMock()
        await s.player_reconnect(101)
        assert len(calls) == 1 and s.send_payload_to_player.await_count == 2
        s.game_server = SimpleNamespace(user_id_to_connection={999: SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))})
        await s.send_realtime_spectator_snapshot(999, 0)
        assert len(calls) == 1
    asyncio.run(run())


@pytest.mark.parametrize("change", ["tick", "round"])
def test_old_worker_result_is_discarded_before_publication_or_replay(monkeypatch, change):
    async def run():
        s = live()
        payload = s.build_game_start_payload(0)
        started, release = threading.Event(), threading.Event()
        worker = hints.compute_batch
        def slow(requests):
            started.set()
            assert release.wait(2)
            return worker(requests)
        monkeypatch.setattr(hints, "compute_batch", slow)
        pending = asyncio.create_task(hints.prepare(s, payloads=[payload]))
        while not started.is_set():
            await asyncio.sleep(.001)
        if change == "tick":
            s.server_action_tick += 1
        else:
            s.round_index += 1
        release.set()
        assert not await pending
        assert not s._prepared_hints and not s._recorded_hint_keys
        assert "source_hand_tiles" not in payload["game_info"]["hangzhou_info"]
    asyncio.run(run())


def test_concurrent_rooms_keep_loop_responsive_and_caches_isolated(monkeypatch):
    async def run():
        rooms = [live() for _ in range(4)]
        rooms[1].player_list[0].hand_tiles[-1] = 43
        started, release = threading.Event(), threading.Event()
        worker = hints.compute_batch
        def slow(requests):
            started.set()
            assert release.wait(2)
            return worker(requests)
        monkeypatch.setattr(hints, "compute_batch", slow)
        pending = [asyncio.create_task(hints.prepare(s, indices=(0,))) for s in rooms]
        while not started.is_set():
            await asyncio.sleep(.001)
        # This coroutine can run while every room's hint worker is blocked.
        for _ in range(5):
            await asyncio.sleep(.001)
        release.set()
        assert all(await asyncio.gather(*pending))
        assert hints.private_hints(rooms[0], 0)["source_hand_tiles"][-1] == 46
        assert hints.private_hints(rooms[1], 0)["source_hand_tiles"][-1] == 43
        wire = hints.private_hints(rooms[0], 0)
        wire["waiting_by_discard"].clear()
        assert hints.private_hints(rooms[0], 0)["waiting_by_discard"]
    asyncio.run(run())


def test_pending_snapshot_uses_its_own_hand_and_lock_policy():
    async def run():
        s = live()
        s.cai_discard_locks = {1}
        before = s.build_game_start_payload(0)
        act(s, 0, "cut", TileId=46, cutClass=True)
        after = s.build_game_start_payload(0)
        packets = [before, after, {"_pause": 0}, {"player_index": 0}]
        assert await hints.prepare(s, payloads=packets)
        assert set(before["game_info"]["hangzhou_info"]["waiting_by_discard"]) == {46}
        assert len(before["game_info"]["hangzhou_info"]["source_hand_tiles"]) == 14
        assert len(after["game_info"]["hangzhou_info"]["source_hand_tiles"]) == 13
        assert after["game_info"]["hangzhou_info"]["waiting_tiles"]
        assert hints._snapshot_request(s, after).cuts == ()
        assert all(len(key[0]) == 13 for index, key in s._recorded_hint_keys.items() if index == 0)
        s.end_draw()
        end = s.build_game_start_payload(0)
        assert await hints.prepare(s, payloads=[end], indices=(0,))
        assert hints.private_hints(s, 0) == {}
        s = live()
        s.tips = False
        assert await hints.prepare(s, payloads=[s.build_game_start_payload(0)], indices=(0,))
        s.tips = True
        s.player_list[0].user_id = 1
        assert await hints.prepare(s, payloads=[s.build_game_start_payload(0)], indices=(0,))
    asyncio.run(run())


@pytest.mark.parametrize("tips,bot", [(False, False), (False, True), (True, True)])
def test_replay_hints_survive_live_tips_gate_and_bot_seats(tips, bot):
    async def run():
        s = live()
        s.tips = tips
        if bot:
            s.player_list[0].user_id = 1
        s.cai_discard_locks = {1}
        payload = s.build_game_start_payload(0)
        assert await hints.prepare(s, payloads=[payload], indices=(0,))
        assert hints.private_hints(s, 0) == {}
        assert "source_hand_tiles" not in payload["game_info"]["hangzhou_info"]
        ticks = s.game_record["game_round"]["round_index_1"]["action_ticks"]
        snapshots = [tick for tick in ticks if tick[:2] == ["hangzhou", "hints"]]
        assert {tick[2] for tick in snapshots} == {0, 1, 2, 3}
        own = [tick[3] for tick in snapshots if tick[2] == 0][-1]
        assert own["source_hand_tiles"] == FLOAT and own["source_melds"] == []
        assert set(own["waiting_by_discard"]) == {46}
        assert own["waiting_by_discard"][46]
        before = len(snapshots)
        assert await hints.prepare(s)
        assert len([tick for tick in ticks if tick[:2] == ["hangzhou", "hints"]]) == before
        act(s, 0, "cut", TileId=46, cutClass=True)
        assert await hints.prepare(s)
        own = [tick[3] for tick in ticks if tick[:3] == ["hangzhou", "hints", 0]][-1]
        assert own["source_hand_tiles"] == sorted(FLOAT[:-1])
        assert own["waiting_tiles"] and own["waiting_by_discard"] == {}
    asyncio.run(run())
