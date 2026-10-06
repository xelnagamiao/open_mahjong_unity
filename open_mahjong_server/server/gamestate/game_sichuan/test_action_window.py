"""川麻询问到等待交接的回归：真实路由、队列、牌谱和出站 FIFO。"""
import asyncio
from collections import Counter

import pytest

from .SichuanGameState import SichuanGameState
from .XueliuGameState import XueliuGameState
from . import boardcast
from .wait_action import wait_action
from ..gamestate_router import handle_gamestate_message
from ..public.claim_protection import begin_claim_protection_interval, mark_post_meld_gap
from ..public.game_record_manager import init_game_record, init_game_round
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer
from ..verifier.host import build_db_manager, build_game_server, build_room_data


MODES = ("standard", "xueliu", "xueliu_exchange")


def make_state(mode, **options):
    messages = []
    db = build_db_manager()
    server = build_game_server(db, messages=messages)
    room = build_room_data(seed=20261004, room_id="action-window", hepai_limit=0)
    room.update(room_rule="sichuan", sub_rule=f"sichuan/{mode}", step_timer=1, round_timer=2, **options)
    cls = SichuanGameState if mode == "standard" else XueliuGameState
    state = cls(server, room, server.calculation_service, db, "action-window")
    for i, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = i
        player.hand_tiles = [15, 15, 11, 12, 13, 21, 22, 23, 24, 25, 26, 27, 28]
        if mode == "xueliu":
            player.hand_tiles = player.hand_tiles[:10]
        player.dingque_suit = 3
    state.current_player_index = 0
    state.player_list[0].discard_tiles = [15]
    state.tiles_list = [19, 29, 39] * 3
    state.game_status = "waiting_action_after_cut"
    state.action_dict = {0: [], 1: ["peng", "pass"], 2: [], 3: []}
    init_game_record(state)
    init_game_round(state)
    server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    return state, messages


async def submit(state, seat, action, *, tick=None, **fields):
    player = state.player_list[seat]
    conn = state.game_server.user_id_to_connection[player.user_id]
    for python_name, wire_name in (("target_tile", "targetTile"), ("selected_tiles", "selectedTiles")):
        if python_name in fields:
            fields[wire_name] = fields.pop(python_name)
    payload = dict(type="gamestate/GB/send_action", gamestate_id=state.gamestate_id,
                   action=action, action_tick=state.server_action_tick if tick is None else tick,
                   **fields)
    if action == "cut":
        payload["type"] = "gamestate/GB/cut_tile"
    await handle_gamestate_message(state.game_server, f"conn-{player.user_id}", payload, conn.websocket)


def reply_on_ask(state, seat, action, *, repeats=1, **fields):
    socket = state.game_server.user_id_to_connection[state.player_list[seat].user_id].websocket
    original = socket.send_json

    async def send(payload):
        await original(payload)
        info = payload.get("ask_other_action_info") or payload.get("ask_hand_action_info")
        if info and action in info["action_list"]:
            for _ in range(repeats):
                await submit(state, seat, action, tick=info["action_tick"], **fields)

    socket.send_json = send


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("repeats", [1, 5])
@pytest.mark.parametrize("claim_protection", [False, True])
@pytest.mark.parametrize("tactical_call", [False, True])
@pytest.mark.parametrize("seat", [1, 2, 3])
def test_first_peng_click_during_ask_is_executed_once(mode, repeats, claim_protection, tactical_call, seat):
    async def run():
        state, _ = make_state(mode, claim_protection=claim_protection, tactical_call=tactical_call,
                              claim_protect_delay=.02, claim_meld_followup_gap=.01)
        try:
            state.action_dict = {i: ["peng", "pass"] if i == seat else [] for i in range(4)}
            begin_claim_protection_interval(state, state.action_dict, 0)
            await state.broadcast_do_action(action_list=["cut"], action_player=0, cut_tile=15)
            reply_on_ask(state, seat, "peng", repeats=repeats)
            await state.broadcast_ask_other_action()
            assert state.action_queues[seat].qsize() == repeats, "the advertised action must already be receivable"
            await asyncio.wait_for(wait_action(state), 1)
            assert state.current_player_index == seat
            assert state.player_list[seat].combination_tiles == ["k15"]
            assert state.player_list[seat].hand_tiles.count(15) == 0
            assert state.game_status == "onlycut_after_action"
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_current_tick_queued_before_wait_is_not_lost(mode):
    async def run():
        state, _ = make_state(mode)
        try:
            state.waiting_players_list = [1]
            await submit(state, 1, "peng")
            await asyncio.wait_for(wait_action(state), .3)
            assert state.player_list[1].combination_tiles == ["k15"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_observer_post_meld_delay_does_not_block_actor(mode):
    async def run():
        state, messages = make_state(mode, claim_meld_post_gap=.5)
        try:
            mark_post_meld_gap(state, 2)
            reply_on_ask(state, 1, "peng")
            await asyncio.wait_for(state.broadcast_ask_other_action(), .2)
            assert not any(m["user_id"] == state.player_list[2].user_id for m in messages)
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(.05)
            assert state.player_list[1].combination_tiles == ["k15"]
            await asyncio.wait_for(task, 2)
            await asyncio.wait_for(drain_viewer(state, 2), 1)
            types = [m["type"] for m in messages if m["user_id"] == state.player_list[2].user_id]
            assert types == ["gamestate/sichuan/ask_other_action", "gamestate/sichuan/do_action"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("action", ["cut", "angang", "jiagang", "hu_self"])
def test_fast_hand_action_survives_broadcast_to_wait(mode, action):
    async def run():
        state, _ = make_state(mode)
        try:
            state.game_status = "waiting_hand_action"
            state.action_dict = {0: [action], 1: [], 2: [], 3: []}
            player = state.player_list[0]
            player.hand_tiles = [11, 12, 13, 21, 22, 23, 24, 25, 26, 28, 15]
            player.has_draw_slot = True
            fields = {}
            if action == "cut":
                fields = dict(TileId=15, cutClass=True)
            elif action == "angang":
                player.hand_tiles = [15] * 4 + player.hand_tiles[:-1]
                fields = dict(target_tile=15)
            elif action == "jiagang":
                player.combination_tiles = ["k15"]
                player.combination_mask = [[1, 15, 0, 15, 0, 15]]
                fields = dict(target_tile=15)
            reply_on_ask(state, 0, action, **fields)
            await state.broadcast_ask_hand_action()
            assert state.action_queues[0].qsize() == 1
            await asyncio.wait_for(wait_action(state), 2)
            if action == "cut":
                assert player.discard_tiles == [15, 15]
                assert player.hand_tiles.count(15) == 0
            elif action == "hu_self":
                assert state.pending_win["type"] == "zimo"
                assert state.game_status == "settle_win"
            else:
                assert player.combination_tiles == ["G15" if action == "angang" else "g15"]
                assert state.game_status in ("deal_card_after_gang", "waiting_action_qianggang")
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status,action", [
    ("waiting_action_after_cut", "gang"),
    ("waiting_action_after_cut", "hu"),
    ("waiting_action_after_cut", "pass"),
    ("waiting_action_qianggang", "hu"),
    ("waiting_action_qianggang", "pass"),
])
def test_fast_claim_and_robbed_kong_responses(mode, status, action):
    async def run():
        state, _ = make_state(mode)
        try:
            state.game_status = status
            state.action_dict[1] = [action, "pass"] if action != "pass" else ["pass"]
            if action == "gang":
                state.player_list[1].hand_tiles.append(15)
            if action == "hu":
                state.sichuan_hu_results = {1: dict(fan=1, fan_list=["基本胡"], hepai_tile=15)}
            if status == "waiting_action_qianggang":
                state.jiagang_tile = 15
                state.player_list[0].combination_tiles = ["g15"]
                state.player_list[0].combination_mask = [[1, 15, 3, 15, 0, 15, 0, 15]]
            reply_on_ask(state, 1, action)
            await state.broadcast_ask_other_action()
            await asyncio.wait_for(wait_action(state), 2)
            if action == "hu":
                assert state.game_status == "settle_win"
                assert state.pending_win["type"] == ("qianggang" if status.endswith("qianggang") else "ron")
                if status.endswith("qianggang"):
                    assert state.player_list[0].combination_tiles == ["k15"]
            elif action == "gang":
                assert state.player_list[1].combination_tiles == ["g15"]
                assert state.current_player_index == 1
                assert state.game_status == "deal_card_after_gang"
            else:
                assert state.game_status == ("deal_card_after_gang" if status.endswith("qianggang") else "deal_card")
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_ready_click_during_first_status_broadcast_is_not_lost(mode):
    async def run():
        state, _ = make_state(mode)
        try:
            state.game_status = "waiting_ready"
            state.action_dict = {i: ["ready"] for i in range(4)}
            for seat in range(4):
                socket = state.game_server.user_id_to_connection[state.player_list[seat].user_id].websocket
                original = socket.send_json

                async def send(payload, seat=seat, original=original):
                    await original(payload)
                    if payload.get("ready_status_info", {}).get("player_to_ready", {}).get(seat) is False:
                        await submit(state, seat, "ready")

                socket.send_json = send
            await boardcast.broadcast_ready_status(state)
            assert all(q.qsize() == 1 for q in state.action_queues.values())
            assert await asyncio.wait_for(wait_action(state), 1) is True
            while any(state.action_dict.values()):
                await asyncio.wait_for(wait_action(state), 1)
            assert state.waiting_players_list == []
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_stale_wrong_seat_and_illegal_packets_cannot_consume_window(mode):
    async def run():
        state, _ = make_state(mode)
        try:
            await state.broadcast_ask_other_action()
            await submit(state, 1, "peng", tick=state.server_action_tick - 1)
            await submit(state, 2, "peng")
            await submit(state, 1, "gang")
            assert all(q.empty() for q in state.action_queues.values())
            # A stale/invalid queue head must not hide the valid response behind it.
            for packet in ({"action_type": "peng", "_action_tick": state.server_action_tick - 1},
                           {"action_type": "unknown", "_action_tick": state.server_action_tick}):
                await state.action_queues[1].put(packet)
            await submit(state, 1, "peng")
            await asyncio.wait_for(wait_action(state), 1)
            assert state.player_list[1].combination_tiles == ["k15"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("legacy", [False, True])
def test_new_ask_discards_previous_window_backlog_and_accepts_new_response(mode, legacy):
    async def run():
        state, _ = make_state(mode)
        try:
            # Includes old clients without action_tick; no old pass may end the new ask.
            await state.action_queues[1].put({"action_type": "pass", "_action_tick": None if legacy else state.server_action_tick})
            state.action_events[1].set()
            await state.broadcast_ask_other_action()
            assert state.action_queues[1].empty()
            assert not state.action_events[1].is_set()
            if legacy:
                player = state.player_list[1]
                conn = state.game_server.user_id_to_connection[player.user_id]
                await handle_gamestate_message(state.game_server, f"conn-{player.user_id}",
                    dict(type="gamestate/GB/send_action", gamestate_id=state.gamestate_id, action="peng"), conn.websocket)
            else:
                await submit(state, 1, "peng")
            await asyncio.wait_for(wait_action(state), 1)
            assert state.player_list[1].combination_tiles == ["k15"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_peng_waits_for_higher_priority_hu_player_to_pass(mode):
    async def run():
        state, _ = make_state(mode)
        try:
            state.action_dict[2] = ["hu", "pass"]
            state.sichuan_hu_results = {2: dict(fan=1, fan_list=["基本胡"], hepai_tile=15)}
            reply_on_ask(state, 1, "peng")
            await state.broadcast_ask_other_action()
            task = asyncio.create_task(wait_action(state))
            await asyncio.sleep(.02)
            assert not task.done()
            assert not state.player_list[1].combination_tiles
            await submit(state, 2, "pass")
            await asyncio.wait_for(task, 1)
            assert state.player_list[1].combination_tiles == ["k15"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("response", ["hu", "pass", "timeout"])
def test_tactical_recheck_keeps_opening_snapshot_and_accepts_fast_response(mode, response):
    async def run():
        state, messages = make_state(mode, tactical_call=True, tactical_grace_seconds=.03)
        try:
            state.action_dict[2] = ["hu", "pass"]
            state.sichuan_hu_results = {2: dict(fan=1, fan_list=["基本胡"], hepai_tile=15)}
            reply_on_ask(state, 1, "peng")
            socket = state.game_server.user_id_to_connection[state.player_list[2].user_id].websocket
            original = socket.send_json

            async def send(payload):
                await original(payload)
                info = payload.get("ask_other_action_info")
                if info and info.get("is_tactical_recheck"):
                    assert state._tactical_action_snapshot[1] == ["peng", "pass"]
                    if response != "timeout":
                        await submit(state, 2, response, tick=info["action_tick"])
                elif info and info["action_list"]:
                    await submit(state, 2, "pass", tick=info["action_tick"])

            socket.send_json = send
            await state.broadcast_ask_other_action()
            await asyncio.wait_for(wait_action(state), 1)
            assert any(m["message"].get("ask_other_action_info", {}).get("is_tactical_recheck") for m in messages)
            if response == "hu":
                assert state.game_status == "settle_win"
                assert not state.player_list[1].combination_tiles
                assert 2 in state.sichuan_hu_results
            else:
                assert state.player_list[1].combination_tiles == ["k15"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("actor", ["offline", 0, 2, 3])
@pytest.mark.parametrize("status", ["waiting_hand_action", "waiting_action_after_cut", "waiting_action_qianggang"])
def test_offline_and_bot_can_submit_in_the_advertised_window(mode, actor, status):
    async def run():
        state, _ = make_state(mode)
        try:
            seat = 0 if status == "waiting_hand_action" else 1
            player = state.player_list[seat]
            if actor == "offline":
                player.tag_list.append("offline")
            else:
                player.user_id = actor
            state.bot_speed = "instant"
            state.game_status = status
            state.jiagang_tile = 15
            action = "cut" if status == "waiting_hand_action" else "pass"
            state.action_dict = {i: [action] if i == seat else [] for i in range(4)}
            if status == "waiting_hand_action":
                await state.broadcast_ask_hand_action()
            else:
                await state.broadcast_ask_other_action()
            await asyncio.wait_for(wait_action(state), 2)
            if action == "cut":
                assert len(player.discard_tiles) == 2
            else:
                assert state.action_dict[seat] == []
                assert state.game_status == ("deal_card_after_gang" if status.endswith("qianggang") else "deal_card")
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_reconnect_can_submit_before_wait_starts(mode):
    async def run():
        state, _ = make_state(mode)
        try:
            await state.broadcast_ask_other_action()
            tick = state.server_action_tick
            reply_on_ask(state, 1, "peng")
            await boardcast.reconnected_send_pending_ask(state, state.player_list[1].user_id)
            assert state.server_action_tick == tick
            await asyncio.wait_for(wait_action(state), 1)
            assert state.player_list[1].combination_tiles == ["k15"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", ["waiting_hand_action", "waiting_action_after_cut", "waiting_action_qianggang", "waiting_ready"])
def test_timeout_advances_without_consuming_stale_packets(mode, status):
    async def run():
        state, _ = make_state(mode)
        try:
            state.step_time = 0
            for player in state.player_list:
                player.remaining_time = 0
            state.game_status = status
            state.jiagang_tile = 15
            if status == "waiting_hand_action":
                state.action_dict = {0: ["cut"], 1: [], 2: [], 3: []}
                await state.broadcast_ask_hand_action()
            elif status == "waiting_ready":
                state.action_dict = {i: ["ready"] for i in range(4)}
                await boardcast.broadcast_ready_status(state)
            else:
                await state.broadcast_ask_other_action()
            await asyncio.wait_for(wait_action(state), 1)
            assert not state.player_list[1].combination_tiles
            if status == "waiting_hand_action":
                assert len(state.player_list[0].discard_tiles) == 2
            elif status == "waiting_ready":
                assert not any(state.action_dict.values())
            else:
                assert state.game_status == ("deal_card_after_gang" if status.endswith("qianggang") else "deal_card")
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", ["standard", "xueliu_exchange"])
def test_dingque_fast_response_remains_valid(mode):
    async def run():
        state, _ = make_state(mode)
        try:
            for seat in range(4):
                reply_on_ask(state, seat, "dingque", target_tile=2)
            await asyncio.wait_for(state._dingque_phase(), 1)
            assert [p.dingque_suit for p in state.player_list] == [2] * 4
            assert not state.waiting_players_list
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", ["xueliu", "xueliu_exchange"])
def test_opening_fast_response_preserves_tile_counts(mode):
    async def run():
        state, _ = make_state(mode)
        try:
            before = Counter(t for p in state.player_list for t in p.hand_tiles)
            for seat in range(4):
                reply_on_ask(state, seat, state.xueliu_opening_action, selected_tiles=[11, 12, 13])
            await asyncio.wait_for(state._xueliu_throw_three_phase(), 1)
            after = Counter(t for p in state.player_list for t in p.hand_tiles)
            if mode == "xueliu":
                after.update(t for p in state.player_list for t in p.xueliu_throw_tiles)
            assert before == after
            assert not state.waiting_players_list
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
