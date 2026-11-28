"""Tests for server-authoritative timeout auto-moqie hosting and resync snapshots."""
from __future__ import annotations

import asyncio
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from . import timeout_hosting as timeout_hosting_mod
from .ai import get_action as get_action_mod
from .ask_timing import begin_ask_round, note_ask_delivered
from .settlement_sync import send_game_packet
from .timeout_hosting import (
    clear_timeout_hosting,
    get_timeout_cut_count,
    is_timeout_hosted,
    record_do_action_from_packet,
)


class _FakeWebSocket:
    def __init__(self):
        self.sent = []

    async def send_json(self, payload):
        self.sent.append(payload)


def _make_state(uid0=1001, uid1=1002):
    ws0 = _FakeWebSocket()
    ws1 = _FakeWebSocket()
    conn0 = SimpleNamespace(user_id=uid0, websocket=ws0)
    conn1 = SimpleNamespace(user_id=uid1, websocket=ws1)
    p0 = SimpleNamespace(
        player_index=0,
        user_id=uid0,
        username="alice",
        hand_tiles=[11, 12, 13, 14],
        discard_tiles=[],
        tag_list=[],
        has_draw_slot=True,
    )
    p1 = SimpleNamespace(
        player_index=1,
        user_id=uid1,
        username="bob",
        hand_tiles=[21, 22, 23, 24],
        discard_tiles=[],
        tag_list=[],
        has_draw_slot=False,
    )
    state = SimpleNamespace(
        gamestate_id="game-timeout-1",
        current_round=1,
        server_action_tick=1,
        game_status="waiting_hand_action",
        current_player_index=0,
        waiting_players_list=[0],
        action_dict={0: ["cut"]},
        player_list=[p0, p1],
        claim_protection=False,
        sub_rule="guobiao/standard",
        game_server=SimpleNamespace(user_id_to_connection={uid0: conn0, uid1: conn1}),
    )
    return state, ws0, ws1


def _cut_packet(seat=0, tile=14, is_timeout=True):
    return {
        "type": "gamestate/guobiao/do_action",
        "gamestate_id": "game-timeout-1",
        "do_action_info": {
            "action_list": ["cut"],
            "action_player": seat,
            "cut_tile": tile,
            "is_timeout_action": is_timeout,
        },
    }


def test_three_consecutive_timeout_cuts_enable_hosting_and_deduplicate_per_viewer():
    state, _, _ = _make_state()
    p0 = state.player_list[0]

    for turn in range(1, 3):
        state.server_action_tick = turn
        p0.discard_tiles.append(10 + turn)
        pkt = _cut_packet(seat=0, tile=10 + turn, is_timeout=True)
        # Same broadcast delivered to 4 viewers must count once.
        assert record_do_action_from_packet(state, pkt) is None
        assert record_do_action_from_packet(state, pkt) is None
        assert get_timeout_cut_count(state, 1001) == turn
        assert not is_timeout_hosted(state, 1001)

    # 3rd timeout cut transitions to hosted mode exactly once.
    state.server_action_tick = 3
    p0.discard_tiles.append(13)
    pkt3 = _cut_packet(seat=0, tile=13, is_timeout=True)
    assert record_do_action_from_packet(state, pkt3) == (1001, True)
    assert record_do_action_from_packet(state, pkt3) is None
    assert is_timeout_hosted(state, 1001)

    # Subsequent server-hosted cuts (without is_timeout_action) must NOT clear active hosting.
    state.server_action_tick = 4
    p0.discard_tiles.append(14)
    assert record_do_action_from_packet(state, _cut_packet(seat=0, tile=14, is_timeout=False)) is None
    assert is_timeout_hosted(state, 1001)

    # Explicit clear resets both flag and counter.
    assert clear_timeout_hosting(state, 1001) is True
    assert not is_timeout_hosted(state, 1001)
    assert get_timeout_cut_count(state, 1001) == 0


def test_manual_cut_before_threshold_resets_counter():
    state, _, _ = _make_state()
    p0 = state.player_list[0]

    for turn in (1, 2):
        state.server_action_tick = turn
        p0.discard_tiles.append(10 + turn)
        record_do_action_from_packet(state, _cut_packet(seat=0, tile=10 + turn, is_timeout=True))
    assert get_timeout_cut_count(state, 1001) == 2

    state.server_action_tick = 3
    p0.discard_tiles.append(15)
    record_do_action_from_packet(state, _cut_packet(seat=0, tile=15, is_timeout=False))
    assert get_timeout_cut_count(state, 1001) == 0
    assert not is_timeout_hosted(state, 1001)


def test_send_game_packet_emits_transition_and_attaches_snapshot_state():
    async def _run():
        state, ws0, ws1 = _make_state()
        p0 = state.player_list[0]

        for turn in (1, 2, 3):
            state.server_action_tick = turn
            p0.discard_tiles.append(10 + turn)
            pkt = _cut_packet(seat=0, tile=10 + turn, is_timeout=True)
            await send_game_packet(state, ws0, pkt, viewer_index=0)
            await send_game_packet(state, ws1, pkt, viewer_index=1)

        await asyncio.sleep(0)
        hosting_msgs = [m for m in ws0.sent if m.get("type") == "gamestate/timeout_auto_moqie"]
        assert len(hosting_msgs) == 1
        assert hosting_msgs[0]["active"] is True
        assert hosting_msgs[0]["timeout_auto_moqie"] is True
        assert not any(m.get("type") == "gamestate/timeout_auto_moqie" for m in ws1.sent)

        # Full snapshot (game_start / reconnect / resync) carries authoritative timeout_auto_moqie.
        ws0.sent.clear()
        ws1.sent.clear()
        snap = {"type": "gamestate/guobiao/game_start", "gamestate_id": "game-timeout-1", "game_info": {}}
        await send_game_packet(state, ws0, snap, viewer_index=0)
        await send_game_packet(state, ws1, snap, viewer_index=1)
        assert ws0.sent[-1]["timeout_auto_moqie"] is True
        assert ws1.sent[-1]["timeout_auto_moqie"] is False

    asyncio.run(_run())


def test_hosted_seat_auto_executes_fast_cut_and_cancels_on_return():
    async def _run():
        state, _, _ = _make_state()
        p0 = state.player_list[0]
        for turn in (1, 2, 3):
            state.server_action_tick = turn
            p0.discard_tiles.append(10 + turn)
            record_do_action_from_packet(state, _cut_packet(seat=0, tile=10 + turn, is_timeout=True))
        assert is_timeout_hosted(state, 1001)

        mock_ai = AsyncMock()
        with patch.object(timeout_hosting_mod, "HOSTED_ACTION_DELAY_SECONDS", 0.01), \
             patch.object(get_action_mod, "get_ai_action", mock_ai):
            state.server_action_tick = 10
            begin_ask_round(state)
            note_ask_delivered(state, 0)
            await asyncio.sleep(0.05)
            mock_ai.assert_awaited_once()
            args = mock_ai.call_args.args
            assert args[1] == 0 and args[2] == "cut" and args[4] == 14

            # If player cancels hosting during the 0.01s window, auto-cut aborts.
            mock_ai.reset_mock()
            state.server_action_tick = 11
            begin_ask_round(state)
            note_ask_delivered(state, 0)
            clear_timeout_hosting(state, 1001)
            await asyncio.sleep(0.05)
            mock_ai.assert_not_awaited()

    asyncio.run(_run())
