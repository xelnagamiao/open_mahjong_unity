"""结算准备回归：模拟 Unity 回传最后一次操作询问的 tick，走真实入站路由。"""
import asyncio

import pytest

from . import boardcast
from .test_action_window import MODES, make_state, submit
from ..gamestate_router import handle_gamestate_message
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer


async def present_result(state, messages, final_panel):
    state.game_status = "waiting_hand_action"
    state.action_dict = {0: ["cut"], 1: [], 2: [], 3: []}
    await state.broadcast_ask_hand_action()
    await asyncio.gather(*(drain_viewer(state, i) for i in range(4)))
    # Unity SendAction uses LastAskActionTick; show_result/ready_status do not update it.
    client_ticks = {}
    for entry in messages:
        info = entry["message"].get("ask_hand_action_info")
        if info:
            client_ticks[entry["user_id"]] = info["action_tick"]
    await boardcast.broadcast_result(
        state, hu_class="liuju", liuju_step="chajiao", liuju_status_final=final_panel,
        next_status="round_end_by_ready", player_to_score={i: p.score for i, p in enumerate(state.player_list)},
    )
    state._liuju_final_panel_shown = final_panel
    return client_ticks


async def send_ready(state, seat, tick_kind, client_ticks):
    if tick_kind == "missing":
        player = state.player_list[seat]
        conn = state.game_server.user_id_to_connection[player.user_id]
        await handle_gamestate_message(state.game_server, f"conn-{player.user_id}",
            dict(type="gamestate/GB/send_action", gamestate_id=state.gamestate_id, action="ready"), conn.websocket)
    else:
        tick = {"unity": client_ticks[state.player_list[seat].user_id],
                "current": state.server_action_tick, "zero": 0}[tick_kind]
        await submit(state, seat, "ready", tick=tick)


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("final_panel", [False, True])
@pytest.mark.parametrize("tick_kind", ["unity", "zero", "current", "missing"])
def test_four_ready_players_skip_remaining_settlement_wait(mode, final_panel, tick_kind):
    async def run():
        state, messages = make_state(mode)
        try:
            client_ticks = await present_result(state, messages, final_panel)
            assert all(tick != state.server_action_tick for tick in client_ticks.values())
            sent = set()
            for seat, player in enumerate(state.player_list):
                socket = state.game_server.user_id_to_connection[player.user_id].websocket
                original = socket.send_json

                async def send(payload, seat=seat, original=original):
                    await original(payload)
                    if payload.get("ready_status_info") and seat not in sent:
                        sent.add(seat)
                        await send_ready(state, seat, tick_kind, client_ticks)

                socket.send_json = send
            await asyncio.wait_for(state._ready_phase(liuju=final_panel), .3)
            assert sent == {0, 1, 2, 3}
            assert not any(state.action_dict.values())
            assert all(p.remaining_time > 0 for p in state.player_list), "must finish by ready, not timeout"
            status = messages[-1]["message"]["ready_status_info"]["player_to_ready"]
            assert all(status.values())
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("last_seat", range(4))
def test_only_the_fourth_ready_player_unblocks_settlement(mode, last_seat):
    async def run():
        state, messages = make_state(mode)
        task = None
        try:
            client_ticks = await present_result(state, messages, True)
            task = asyncio.create_task(state._ready_phase(liuju=True))
            await asyncio.sleep(0)
            for seat in range(4):
                if seat != last_seat:
                    await send_ready(state, seat, "unity", client_ticks)
                    await asyncio.sleep(.01)
            assert not task.done()
            assert state.action_dict[last_seat] == ["ready"]
            assert all(not state.action_dict[i] for i in range(4) if i != last_seat)
            await send_ready(state, last_seat, "unity", client_ticks)
            await asyncio.wait_for(task, .3)
            assert not any(state.action_dict.values())
        finally:
            if task is not None and not task.done():
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_ready_from_previous_tick_is_bound_to_current_ready_window(mode):
    async def run():
        state, messages = make_state(mode)
        try:
            client_ticks = await present_result(state, messages, True)
            state.game_status = "waiting_ready"
            state.action_dict = {i: ["ready"] for i in range(4)}
            await boardcast.broadcast_ready_status(state)
            await send_ready(state, 0, "unity", client_ticks)
            packet = state.action_queues[0].get_nowait()
            assert packet["_action_tick"] == state.server_action_tick
            assert state.action_events[0].is_set()
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("human_count", range(5))
def test_bots_are_ready_automatically_and_all_humans_can_skip(mode, human_count):
    async def run():
        state, messages = make_state(mode)
        try:
            client_ticks = await present_result(state, messages, True)
            sent = set()
            for seat, player in enumerate(state.player_list):
                if seat >= human_count:
                    player.user_id = 0
                    continue
                socket = state.game_server.user_id_to_connection[player.user_id].websocket
                original = socket.send_json

                async def send(payload, seat=seat, original=original):
                    await original(payload)
                    if payload.get("ready_status_info") and seat not in sent:
                        sent.add(seat)
                        await send_ready(state, seat, "unity", client_ticks)

                socket.send_json = send
            await asyncio.wait_for(state._ready_phase(liuju=True), .3)
            assert sent == set(range(human_count))
            assert not any(state.action_dict.values())
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_duplicate_ready_cannot_replace_another_players_confirmation(mode):
    async def run():
        state, messages = make_state(mode)
        task = None
        try:
            client_ticks = await present_result(state, messages, True)
            task = asyncio.create_task(state._ready_phase(liuju=True))
            await asyncio.sleep(0)
            for _ in range(5):
                await send_ready(state, 0, "unity", client_ticks)
            for seat in (1, 2):
                await send_ready(state, seat, "unity", client_ticks)
            await asyncio.sleep(.02)
            assert not task.done()
            assert state.action_dict[3] == ["ready"]
            await send_ready(state, 3, "unity", client_ticks)
            await asyncio.wait_for(task, .3)
            assert not any(state.action_dict.values())
        finally:
            if task is not None and not task.done():
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("status", ["waiting_hand_action", "waiting_action_after_cut", "END"])
def test_ready_outside_settlement_cannot_be_queued(mode, status):
    async def run():
        state, _ = make_state(mode)
        try:
            state.game_status = status
            # Even if a stale caller left the ready option/seat gate behind,
            # the explicit phase check must still reject the confirmation.
            state.action_dict = {0: ["ready"], 1: [], 2: [], 3: []}
            state.waiting_players_list = [0]
            await submit(state, 0, "ready")
            assert state.action_queues[0].empty()
            assert not state.action_events[0].is_set()
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_missing_fourth_ready_still_uses_timeout_fallback(mode, monkeypatch):
    from ..public import ready_phase

    async def run():
        state, messages = make_state(mode)
        task = None
        try:
            client_ticks = await present_result(state, messages, True)
            monkeypatch.setattr(ready_phase, "sichuan_liuju_final_ready_wait_seconds", lambda: .05)
            task = asyncio.create_task(state._ready_phase(liuju=True))
            await asyncio.sleep(0)
            for seat in (0, 1, 2):
                await send_ready(state, seat, "unity", client_ticks)
            await asyncio.sleep(.02)
            assert not task.done()
            assert not any(state.action_dict[i] for i in (0, 1, 2))
            # ready 预算会向上取整，wait_action 每秒轮询一次；为整套测试中的
            # 调度延迟保留余量。四人都准备的快速退出由上面的独立用例约束。
            await asyncio.wait_for(task, 3)
            assert not any(state.action_dict.values())
            assert state.player_list[3].remaining_time == 0
            assert all(state.player_list[i].remaining_time > 0 for i in (0, 1, 2))
        finally:
            if task is not None and not task.done():
                task.cancel()
                await asyncio.gather(task, return_exceptions=True)
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
