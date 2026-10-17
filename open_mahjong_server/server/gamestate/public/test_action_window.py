"""Shared ask/receive contract: an advertised action is immediately actionable."""
import asyncio
import importlib

import pytest

from .ai.get_action import get_action
from .game_record_manager import init_game_record, init_game_round
from .lifecycle import cancel_auxiliary_tasks
from .outbound_pipe import drain_viewer, schedule_viewer_send
from .ask_timing import get_ask_elapsed
from ..verifier.host import build_db_manager, build_game_server, build_room_data


ADAPTERS = [
    ("guobiao", "game_guobiao", "GuobiaoGameState"),
    ("qingque", "game_mmcr", "QingqueGameState"),
    ("changsha", "game_changsha", "ChangshaGameState"),
    ("riichi", "game_riichi", "RiichiGameState"),
    ("classical", "game_classical", "ClassicalGameState"),
    ("sichuan", "game_sichuan", "SichuanGameState"),
    ("taiwan", "game_taiwan", "TaiwanGameState"),
]


def make_state(adapter):
    rule, module, name = adapter
    cls = getattr(importlib.import_module(f"server.gamestate.{module}.{name}"), name)
    db = build_db_manager()
    server = build_game_server(db, messages=[])
    room = build_room_data(seed=20261017, room_id="ask-window", hepai_limit=0)
    room.update(room_rule=rule, sub_rule=f"{rule}/standard", round_timer=20, step_timer=5,
                tips=False, claim_protection=False)
    state = cls(server, room, server.calculation_service, db, "ask-window")
    for seat, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = seat
        player.hand_tiles = [11, 12, 13, 13, 13, 14, 15, 21, 22, 23, 31, 32, 33]
    state.current_player_index = 0
    state.player_list[0].discard_tiles = [13]
    state.tiles_list = [19, 29, 39] * 6
    state.server_action_tick = 12
    state.game_status = "waiting_action_after_cut"
    init_game_record(state)
    init_game_round(state)
    broadcaster = importlib.import_module(f"server.gamestate.{module}.boardcast")
    waiter = importlib.import_module(f"server.gamestate.{module}.wait_action").wait_action
    return state, broadcaster, waiter


async def reply(state, seat, action, tick):
    player = state.player_list[seat]
    tile = player.hand_tiles[-1] if action == "cut" else None
    await get_action(state, f"conn-{player.user_id}", action, False, tile,
                     len(player.hand_tiles) - 1 if tile is not None else None,
                     None, action_tick=tick)


async def drain_all(state):
    await asyncio.gather(*(drain_viewer(state, seat) for seat in range(len(state.player_list))))


@pytest.mark.parametrize("adapter", ADAPTERS, ids=[x[0] for x in ADAPTERS])
@pytest.mark.parametrize("phase", ["hand", "claim"])
def test_ask_is_receivable_during_delivery(adapter, phase):
    async def run():
        state, broadcast, _ = make_state(adapter)
        seat, action = (0, "cut") if phase == "hand" else (3, "peng")
        state.game_status = "waiting_hand_action" if phase == "hand" else "waiting_action_after_cut"
        state.action_dict = {i: [action] if i == seat else [] for i in range(4)}

        async def send(payload):
            info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
            if info:
                await reply(state, seat, action, info["action_tick"] - 1)
                assert state.action_queues[seat].empty()
                await reply(state, seat, action, info["action_tick"])

        state.game_server.user_id_to_connection[state.player_list[seat].user_id].websocket.send_json = send
        try:
            await (broadcast.broadcast_ask_hand_action(state) if phase == "hand"
                   else broadcast.broadcast_ask_other_action(state))
            await drain_all(state)
            assert state.action_queues[seat].qsize() == 1
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("adapter", ADAPTERS, ids=[x[0] for x in ADAPTERS])
def test_wait_preserves_current_reply_and_wakeup(adapter):
    async def run():
        state, _, wait = make_state(adapter)
        state.action_dict = {i: ["pass"] if i == 1 else [] for i in range(4)}
        state.action_queues[1].put_nowait({"action_type": "peng", "_action_tick": 11})
        state.action_queues[1].put_nowait({"action_type": "pass", "_action_tick": 12})
        # Retained data must re-arm even a cleared Event.
        state.action_events[1].clear()
        try:
            await asyncio.wait_for(wait(state), .4)
            assert state.game_status == "deal_card"
            assert not state.player_list[1].combination_tiles
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("adapter", ADAPTERS, ids=[x[0] for x in ADAPTERS])
def test_one_viewer_send_does_not_delay_another_claim_prompt(adapter):
    async def run():
        state, broadcast, _ = make_state(adapter)
        state.action_dict = {i: ["peng", "pass"] if i in (1, 3) else [] for i in range(4)}
        gate, delivered = asyncio.Event(), asyncio.Event()
        socket1 = state.game_server.user_id_to_connection[state.player_list[1].user_id].websocket
        socket3 = state.game_server.user_id_to_connection[state.player_list[3].user_id].websocket
        original1, original3 = socket1.send_json, socket3.send_json

        async def slow_send(payload):
            await gate.wait()
            await original1(payload)

        async def fast_send(payload):
            await original3(payload)
            if "ask_other_action_info" in payload:
                delivered.set()

        socket1.send_json, socket3.send_json = slow_send, fast_send
        task = asyncio.create_task(broadcast.broadcast_ask_other_action(state))
        try:
            await asyncio.wait_for(delivered.wait(), .3)
        finally:
            gate.set()
            await task
            await drain_all(state)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("rule", ["zhongyong", "nanque", "jiandan"])
@pytest.mark.parametrize("phase", ["hand", "claim"])
def test_driver_wait_preserves_reply_during_flush(rule, phase):
    from ..game_zhongyong import ZhongyongGameState, NanqueGameState
    from ..game_jiandan.JiandanGameState import JiandanGameState

    async def run():
        state = {"zhongyong": ZhongyongGameState, "nanque": NanqueGameState, "jiandan": JiandanGameState}[rule]()
        state.initialize_round()
        state.step_time = 5
        state.game_server = build_game_server(build_db_manager(), messages=[])
        for player, uid in zip(state.player_list, state.game_server.user_id_to_connection):
            player.user_id = uid
            player.remaining_time = 20
        seat, action = (0, "cut") if phase == "hand" else (3, "peng")

        async def send(payload):
            info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
            if info and action in info["action_list"]:
                await state.submit_action(seat, action, TileId=state.player_list[seat].hand_tiles[-1])

        state.game_server.user_id_to_connection[state.player_list[seat].user_id].websocket.send_json = send
        try:
            state.open_action_window(dict(status="waiting_hand_action" if phase == "hand" else "waiting_action_after_cut",
                                          player=0, tile=13, actions={seat: [action]}))
            await state.flush_outbound_payloads()
            assert state.action_queues[seat].qsize() == 1
            results = await asyncio.wait_for(state.wait_action(), .4)
            assert results[seat]["action_type"] == action
            assert not results[seat].get("is_timeout_action")
        finally:
            await state.cleanup_game_state()
    asyncio.run(run())


@pytest.mark.parametrize("adapter", ADAPTERS, ids=[x[0] for x in ADAPTERS])
@pytest.mark.parametrize("action", ["chi_left", "chi_mid", "chi_right", "peng"])
def test_fast_repeated_claim_then_discard_does_not_stall(adapter, action):
    if adapter[0] == "sichuan" and action.startswith("chi"):
        pytest.skip("Sichuan does not allow chi")

    async def run():
        state, broadcast, wait = make_state(adapter)
        state.action_dict = {i: [action, "pass"] if i == 1 else [] for i in range(4)}

        async def send(payload):
            info = payload.get("ask_other_action_info")
            if info and action in info["action_list"]:
                for _ in range(3):
                    await reply(state, 1, action, info["action_tick"])
            info = payload.get("ask_hand_action_info")
            if info and "cut" in info["action_list"]:
                await reply(state, 1, "cut", info["action_tick"])

        state.game_server.user_id_to_connection[state.player_list[1].user_id].websocket.send_json = send
        try:
            await broadcast.broadcast_ask_other_action(state)
            await drain_all(state)
            await asyncio.wait_for(wait(state), .5)
            assert state.game_status == "onlycut_after_action"
            assert state.current_player_index == 1
            assert len(state.player_list[1].combination_tiles) == 1
            assert not state.player_list[0].discard_tiles
            tile = state.player_list[1].hand_tiles[-1]
            state.game_status = "waiting_hand_action"
            state.action_dict = {i: ["cut"] if i == 1 else [] for i in range(4)}
            await broadcast.broadcast_ask_hand_action(state)
            await asyncio.wait_for(wait(state), .5)
            assert state.player_list[1].discard_tiles == [tile]
            assert len(state.player_list[1].combination_tiles) == 1
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("adapter", [x for x in ADAPTERS if x[0] not in ("taiwan", "classical")],
                         ids=[x[0] for x in ADAPTERS if x[0] not in ("taiwan", "classical")])
def test_delayed_prompt_does_not_start_online_clock_in_waiter(adapter):
    async def run():
        state, broadcast, wait = make_state(adapter)
        state.action_dict = {i: ["pass"] if i == 1 else [] for i in range(4)}
        gate = asyncio.Event()
        schedule_viewer_send(state, 1, gate.wait)
        task = None
        try:
            await asyncio.wait_for(broadcast.broadcast_ask_other_action(state), .3)
            task = asyncio.create_task(wait(state))
            await asyncio.sleep(.02)
            assert 1 not in state._ask_delivered_at
            assert get_ask_elapsed(state, 1) == 0
            gate.set()
            await drain_all(state)
            await reply(state, 1, "pass", state.server_action_tick)
            await asyncio.wait_for(task, .5)
            assert state.player_list[1].remaining_time == 20
        finally:
            gate.set()
            if task is not None:
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("adapter", ADAPTERS, ids=[x[0] for x in ADAPTERS])
def test_ready_reply_during_first_broadcast_is_preserved(adapter):
    from .ready_phase import run_hu_result_ready_phase

    async def run():
        state, _, _ = make_state(adapter)
        state.waiting_players_list = []
        sent = False

        async def broadcast_ready(current):
            nonlocal sent
            if not sent:
                sent = True
                for seat in range(4):
                    await reply(current, seat, "ready", current.server_action_tick - 1)

        try:
            await asyncio.wait_for(run_hu_result_ready_phase(state, 0, broadcast_ready, pre_panel_delay_sec=0), .5)
            assert not any(state.action_dict.values())
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("adapter", ADAPTERS, ids=[x[0] for x in ADAPTERS])
def test_standalone_ready_broadcast_opens_ingress_and_resets_previous_clock(adapter):
    import time

    async def run():
        state, broadcast, wait = make_state(adapter)
        state._ask_delivered_at = {seat: time.time() - 100 for seat in range(4)}
        state.game_status = "waiting_ready"
        state.action_dict = {seat: ["ready"] for seat in range(4)}
        for seat, player in enumerate(state.player_list):
            async def send(payload, seat=seat):
                if payload.get("ready_status_info", {}).get("player_to_ready", {}).get(seat) is False:
                    await reply(state, seat, "ready", state.server_action_tick - 1)
            state.game_server.user_id_to_connection[player.user_id].websocket.send_json = send
        try:
            await broadcast.broadcast_ready_status(state)
            assert all(q.qsize() == 1 for q in state.action_queues.values())
            assert not state._ask_delivered_at
            while any(state.action_dict.values()):
                await asyncio.wait_for(wait(state), .4)
            assert all(p.remaining_time == 20 for p in state.player_list)
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


def test_driver_sends_claim_prompts_concurrently():
    from ..game_zhongyong import ZhongyongGameState

    async def run():
        state = ZhongyongGameState()
        state.initialize_round()
        state.game_server = build_game_server(build_db_manager(), messages=[])
        for player, uid in zip(state.player_list, state.game_server.user_id_to_connection):
            player.user_id = uid
        gate, delivered = asyncio.Event(), asyncio.Event()

        async def slow_send(payload):
            await gate.wait()

        async def fast_send(payload):
            if payload.get("ask_other_action_info"):
                delivered.set()

        state.game_server.user_id_to_connection[state.player_list[1].user_id].websocket.send_json = slow_send
        state.game_server.user_id_to_connection[state.player_list[3].user_id].websocket.send_json = fast_send
        state.open_action_window(dict(status="waiting_action_after_cut", player=0, tile=13,
                                      actions={1: ["peng", "pass"], 3: ["peng", "pass"]}))
        task = asyncio.create_task(state.flush_outbound_payloads())
        try:
            await asyncio.wait_for(delivered.wait(), .3)
        finally:
            gate.set()
            await task
            await state.cleanup_game_state()
    asyncio.run(run())
