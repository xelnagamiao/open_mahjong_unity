"""Qingque asks must be actionable as soon as a fast client receives them."""
import asyncio
from types import SimpleNamespace as NS
from unittest.mock import AsyncMock, Mock

import pytest

from .QingqueGameState import QingqueGameState
from . import boardcast
from .action_check import check_only_cut
from .wait_action import wait_action
from ..public.ai.get_action import get_action
from ..public.ask_timing import begin_ask_round, get_ask_elapsed
from ..public.claim_protection import begin_claim_protection_interval
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer, schedule_viewer_send


def game():
    room = dict(player_list=[100, 101, 102, 103], room_id=1, room_rule="qingque",
                room_type="custom", sub_rule="qingque/standard", round_timer=20,
                step_timer=5, game_round=1, tips=False, allow_spectator=False)
    server = NS(user_id_to_connection={uid: NS(user_id=uid, websocket=NS(send_json=AsyncMock()))
                                      for uid in room["player_list"]})
    server.players = {str(uid): conn for uid, conn in server.user_id_to_connection.items()}
    calculation = Mock()
    calculation.Qingque_tingpai_check.return_value = []
    state = QingqueGameState(server, room, calculation, Mock(), "qingque-timing-test")
    state.send_to_realtime_spectators = AsyncMock()
    state.game_record = {"game_round": {"round_index_1": {"action_ticks": []}}}
    state.tiles_list = [19, 29, 39]
    state.server_action_tick = 12
    state.game_status = "waiting_action_after_cut"
    for seat, player in enumerate(state.player_list):
        player.player_index = seat
        player.hand_tiles = [11, 12, 13, 14, 15, 21, 22, 23, 31, 32, 33, 41, 41]
    state.player_list[0].discard_tiles = [13]
    return state


async def reply(state, seat, action, tick):
    cut_index = state.player_list[seat].hand_tiles.index(41) if action == "cut" else None
    await get_action(state, str(state.player_list[seat].user_id), action, False,
                     41 if action == "cut" else None, cut_index, None, action_tick=tick)


async def drain_all(state):
    await asyncio.gather(*(drain_viewer(state, seat) for seat in range(4)))


@pytest.mark.parametrize("phase", ["hand", "claim"])
def test_reply_during_ask_delivery_is_accepted(phase):
    async def run():
        state = game()
        seat, action = (0, "hu_self") if phase == "hand" else (1, "chi_left")
        if phase == "hand":
            state.game_status = "waiting_hand_action"
        state.action_dict = {i: [action] if i == seat else [] for i in range(4)}
        seen = []

        async def send(payload):
            info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
            if not info:
                return
            seen.append((list(state.waiting_players_list), getattr(state, "_waiting_action_tick", None)))
            await reply(state, seat, action, info["action_tick"] - 1)
            assert state.action_queues[seat].empty(), "stale replies must stay rejected"
            await reply(state, seat, action, info["action_tick"])

        state.game_server.user_id_to_connection[100 + seat].websocket.send_json = send
        try:
            if phase == "hand":
                await boardcast.broadcast_ask_hand_action(state)
            else:
                await boardcast.broadcast_ask_other_action(state)
            await drain_all(state)
            assert seen == [([seat], 13)]
            assert state.action_queues[seat].qsize() == 1
            await asyncio.wait_for(wait_action(state), .5)
            assert state.game_status == ("END" if phase == "hand" else "onlycut_after_action")
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("action", ["chi_left", "chi_mid", "chi_right", "peng", "gang", "pass"])
def test_current_reply_already_queued_before_wait_is_preserved(action):
    async def run():
        state = game()
        state.player_list[1].hand_tiles = [11, 12, 13, 13, 13, 14, 15, 21, 22, 23, 31, 32, 33]
        state.action_dict = {i: [action, "pass"] if i == 1 else [] for i in range(4)}
        state.action_queues[1].put_nowait({"action_type": "pass", "_action_tick": 11})
        state.action_queues[1].put_nowait({"action_type": action, "_action_tick": 12})
        state.action_events[1].set()
        try:
            await asyncio.wait_for(wait_action(state), .5)
            expected = "deal_card" if action == "pass" else (
                "deal_card_after_gang" if action == "gang" else "onlycut_after_action")
            assert state.game_status == expected
            if action != "pass":
                assert state.current_player_index == 1
                assert len(state.player_list[1].combination_tiles) == 1
                assert not state.player_list[0].discard_tiles
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("action", ["peng", "gang"])
def test_claim_prompt_is_not_held_behind_protected_observers(action):
    async def run():
        state = game()
        state.claim_protection = True
        state.claim_protect_delay = .8
        state.action_dict = {i: [action, "pass"] if i == 3 else [] for i in range(4)}
        seen = {i: [] for i in range(4)}
        prompt = asyncio.Event()
        for seat, conn in enumerate(state.game_server.user_id_to_connection.values()):
            async def send(payload, seat=seat):
                seen[seat].append(payload)
                if seat == 3 and "ask_other_action_info" in payload:
                    prompt.set()
            conn.websocket.send_json = send
        task = None
        try:
            begin_claim_protection_interval(state, state.action_dict, 0)
            await boardcast.broadcast_do_action(state, ["cut"], 0, cut_tile=13)
            assert seen[3][-1]["do_action_info"]["cut_tile"] == 13
            assert not seen[1]  # This observer is still waiting for the protected discard.
            task = asyncio.create_task(boardcast.broadcast_ask_other_action(state))
            await asyncio.wait_for(prompt.wait(), .3)
            assert not seen[1], "the actor's prompt must precede the observer's delayed discard"
            await task
            await drain_all(state)
            assert [p["type"].rsplit("/", 1)[-1] for p in seen[1]] == ["do_action", "ask_other_action"]
        finally:
            if task is not None:
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


def test_online_claim_clock_starts_on_delivery_not_wait_entry():
    async def run():
        state = game()
        state.action_dict = {i: ["pass"] if i == 1 else [] for i in range(4)}
        gate = asyncio.Event()
        begin_ask_round(state)
        schedule_viewer_send(state, 1, gate.wait)
        wait = None
        try:
            broadcast = asyncio.create_task(boardcast.broadcast_ask_other_action(state))
            await asyncio.wait_for(broadcast, .3)
            wait = asyncio.create_task(wait_action(state))
            await asyncio.sleep(.02)
            assert 1 not in state._ask_delivered_at
            assert get_ask_elapsed(state, 1) == 0
            gate.set()
            await drain_all(state)
            assert 1 in state._ask_delivered_at
            await reply(state, 1, "pass", state.server_action_tick)
            await asyncio.wait_for(wait, .5)
            assert state.player_list[1].remaining_time == 20
        finally:
            if wait is not None:
                wait.cancel()
                await asyncio.gather(wait, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("action", ["chi_left", "chi_mid", "chi_right"])
@pytest.mark.parametrize("protection", [False, True])
@pytest.mark.parametrize("tactical", [False, True])
def test_fast_double_click_chi_then_immediate_discard(action, protection, tactical):
    async def run():
        state = game()
        state.claim_protection = protection
        state.claim_protect_delay = .05
        state.claim_meld_followup_gap = .02
        state.claim_meld_post_gap = .01
        state.tactical_call = tactical
        state.action_dict = {i: [action, "pass"] if i == 1 else [] for i in range(4)}
        wire = []

        async def send(payload):
            wire.append(payload)
            if "ask_other_action_info" in payload and payload["ask_other_action_info"]["action_list"]:
                info = payload["ask_other_action_info"]
                await reply(state, 1, action, info["action_tick"])
                await reply(state, 1, action, info["action_tick"])
            elif "ask_hand_action_info" in payload:
                info = payload["ask_hand_action_info"]
                if "cut" in info["action_list"]:
                    await reply(state, 1, "cut", info["action_tick"])

        state.game_server.user_id_to_connection[101].websocket.send_json = send
        try:
            begin_claim_protection_interval(state, state.action_dict, 0)
            await boardcast.broadcast_do_action(state, ["cut"], 0, cut_tile=13)
            await boardcast.broadcast_ask_other_action(state)
            await asyncio.wait_for(wait_action(state), .5)
            assert state.game_status == "onlycut_after_action"
            assert len(state.player_list[1].combination_tiles) == 1
            state.action_dict = check_only_cut(state, 1)
            state.game_status = "waiting_hand_action"
            await boardcast.broadcast_ask_hand_action(state)
            await asyncio.wait_for(wait_action(state), .5)
            await drain_all(state)
            assert state.player_list[1].discard_tiles == [41]
            assert len(state.player_list[1].hand_tiles) == 10
            assert len(state.player_list[1].combination_tiles) == 1
            assert [p["do_action_info"]["action_list"] for p in wire if "do_action_info" in p] == [
                ["cut"], [action], ["cut"]]
            assert state.game_status == "waiting_action_after_cut"
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("kind", ["bot", "offline", "disconnected", "ready"])
def test_asks_without_websocket_delivery_still_start_their_timeout(kind):
    async def run():
        state = game()
        state.action_dict = {i: ["pass"] if i == 1 else [] for i in range(4)}
        if kind == "bot":
            state.player_list[1].user_id = 0
        elif kind == "offline":
            state.player_list[1].tag_list.append("offline")
        elif kind == "disconnected":
            del state.game_server.user_id_to_connection[101]
        else:
            state.game_status = "waiting_ready"
            state.action_dict[1] = ["ready"]
        begin_ask_round(state)
        task = asyncio.create_task(wait_action(state))
        try:
            await asyncio.sleep(.01)
            assert 1 in state._ask_delivered_at
        finally:
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


def test_failed_ask_delivery_keeps_timeout_fallback(caplog):
    async def run():
        state = game()
        state.action_dict = {i: ["pass"] if i == 1 else [] for i in range(4)}
        state.game_server.user_id_to_connection[101].websocket.send_json = AsyncMock(
            side_effect=ConnectionError("test connection closed"))
        try:
            await boardcast.broadcast_ask_other_action(state)
            await drain_all(state)
            assert 1 in state._ask_delivered_at
            assert "outbound send failed viewer=1" in caplog.text
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
