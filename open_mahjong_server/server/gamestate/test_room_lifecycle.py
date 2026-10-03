"""Room/game lifetime regressions using real managers and memory-only IO."""
import asyncio
from contextlib import asynccontextmanager
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from server.gamestate import gamestate_manager as module
from server.gamestate.gamestate_manager import GameStateManager
from server.gamestate.gamestate_router import handle_gamestate_message
from server.gamestate.public.lifecycle import start_owned_task
from server.gamestate.public.vote_manager import VoteManager
from server.room.room_manager import RoomManager

USERS = [910001, 910002, 910003, 910004]
RULES = {
    "guobiao": module.GuobiaoGameState, "qingque": module.QingqueGameState,
    "changsha": module.ChangshaGameState, "classical": module.ClassicalGameState,
    "riichi": module.RiichiGameState, "sichuan": module.SichuanGameState,
    "xueliu": module.XueliuGameState, "jiandan": module.JiandanGameState,
    "taiwan": module.TaiwanGameState, "shanghai": module.ShanghaiGameState,
    "hongque": module.HongqueGameState, "free": module.FreeGameState,
}


async def parked_loop(self):
    await asyncio.Event().wait()


def make_server(users):
    db = MagicMock()
    db.get_rank_data.return_value = {}
    db.get_user_settings.return_value = {}
    server = SimpleNamespace(db_manager=db, calculation_service=MagicMock(),
                             players={}, user_id_to_connection={}, friend_manager=None, match_manager=None)
    server.room_manager = RoomManager(server)
    server.gamestate_manager = GameStateManager(server)
    for uid in users:
        if uid <= 10:
            continue
        conn = SimpleNamespace(Connect_id=str(uid), user_id=uid, username=f"test-{uid}",
                               current_room_id="1", is_tourist=False,
                               websocket=SimpleNamespace(send_json=AsyncMock()))
        server.players[str(uid)] = conn
        server.user_id_to_connection[uid] = conn
    return server


def make_room(users, rule="guobiao", room_type="custom"):
    room = {"room_id": "1", "room_type": room_type, "room_rule": rule,
            "host_user_id": users[0], "host_name": "test", "room_name": "lifecycle",
            "player_list": list(users), "player_settings": {u: {"username": f"test-{u}"} for u in users},
            "ready_list": list(users), "is_game_running": False, "max_player": 4,
            "has_password": False, "tips": False, "game_round": 1,
            "round_timer": 20, "step_timer": 5}
    if rule == "xueliu":
        room.update(room_rule="sichuan", sub_rule="sichuan/xueliu")
    return room


@asynccontextmanager
async def game(users=USERS, rule="guobiao", room_type="custom"):
    server = make_server(users)
    room = make_room(users, rule, room_type)
    server.room_manager.rooms["1"] = room
    with patch.object(RULES[rule], "run_game_loop", parked_loop):
        result = await server.gamestate_manager.start_game(str(users[0]), "1")
        assert result is None, result
        state = server.gamestate_manager.get_game_state_by_room_id("1")
        if rule != "hongque":
            for index, player in enumerate(state.player_list):
                player.player_index = index
                player.original_player_index = index
        await asyncio.sleep(0)
        try:
            yield server, room, state
        finally:
            for gid in list(server.gamestate_manager.gamestate_id_to_game_state):
                await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=gid)


def test_online_owner_can_leave_bot_lobby_without_ending_game():
    async def run():
        async with game([USERS[0], 0, 2, 3]) as (server, room, state):
            response = await server.room_manager.leave_room(str(USERS[0]), "1")
            assert response.success
            assert "1" not in server.room_manager.rooms
            assert server.gamestate_manager.get_game_state_by_user_id(USERS[0]) is state
            assert not state.game_task.done()
            assert [p.user_id for p in state.player_list] == [USERS[0], 0, 2, 3]
            assert "offline" not in state.player_list[0].tag_list
            assert server.room_manager._generate_room_id() != "1"
            assert server.room_manager.allocate_match_room_id() != "1"
            assert await server.gamestate_manager.player_reconnect(USERS[0])
            assert server.players[str(USERS[0])].current_room_id is None
    asyncio.run(run())


def test_four_players_leave_lobby_and_reconnect_in_turn():
    async def run():
        async with game() as (server, room, state):
            for uid in USERS:
                assert (await server.room_manager.leave_room(str(uid), "1")).success
                await server.gamestate_manager.player_disconnect(uid)
                assert await server.gamestate_manager.player_reconnect(uid)
                assert server.gamestate_manager.get_game_state_by_gamestate_id(state.gamestate_id) is state
            assert not server.room_manager.rooms
            assert all(conn.current_room_id is None for conn in server.players.values())
            assert not state.game_task.done()
    asyncio.run(run())


@pytest.mark.parametrize("rule", RULES)
def test_all_rules_close_immediately_when_all_humans_are_offline(rule):
    async def run():
        async with game(rule=rule, room_type="events") as (server, room, state):
            for uid in USERS[:-1]:
                await server.gamestate_manager.player_disconnect(uid)
                assert server.gamestate_manager.is_user_in_active_game(uid)
            await server.gamestate_manager.player_disconnect(USERS[-1])
            assert state.lifecycle_state == "closed"
            assert state.close_reason == "all_humans_offline"
            assert state.game_task.done()
            assert not server.gamestate_manager.gamestate_id_to_game_state
            assert not server.gamestate_manager.user_id_to_game_state
            assert not server.room_manager.active_game_room_ids
            assert room["is_game_running"] is False
            assert room["ready_list"] == []
            assert "active_gamestate_id" not in room
    asyncio.run(run())


def test_bot_uid_ten_does_not_keep_game_alive():
    async def run():
        async with game([USERS[0], 0, 2, 10]) as (server, room, state):
            assert set(server.gamestate_manager.user_id_to_game_state) == {USERS[0]}
            await server.gamestate_manager.player_disconnect(USERS[0])
            assert state.lifecycle_state == "closed"
    asyncio.run(run())


def test_old_cleanup_preserves_replaced_indexes_and_new_room():
    async def run():
        async with game() as (server, room, old):
            gm = server.gamestate_manager
            new = SimpleNamespace(gamestate_id="new-game", room_id="1")
            gm.user_id_to_game_state[USERS[0]] = new
            gm.room_id_to_GuobiaoGameState["1"] = new
            replacement = {**room, "instance_id": "new-room", "active_gamestate_id": "new-game"}
            server.room_manager.rooms["1"] = replacement
            await gm.cleanup_game_state_complete(gamestate_id=old.gamestate_id)
            assert gm.user_id_to_game_state[USERS[0]] is new
            assert gm.room_id_to_GuobiaoGameState["1"] is new
            assert replacement["is_game_running"] is True
            assert replacement["ready_list"] == USERS
    asyncio.run(run())


def test_close_without_lobby_releases_number_and_is_idempotent():
    async def run():
        async with game() as (server, room, state):
            await server.room_manager.destroy_room("1")
            assert server.room_manager._generate_room_id() == "2"
            cleanup = AsyncMock(wraps=state.cleanup_game_state)
            state.cleanup_game_state = cleanup
            await asyncio.gather(*[server.gamestate_manager.cleanup_game_state_complete(
                gamestate_id=state.gamestate_id) for _ in range(3)])
            assert cleanup.await_count == 1
            assert server.room_manager._generate_room_id() == "1"
            assert not server.gamestate_manager.user_id_to_game_state
    asyncio.run(run())


def test_reconnect_waiting_during_close_cannot_restore_closed_game():
    async def run():
        async with game() as (server, room, state):
            entered, release = asyncio.Event(), asyncio.Event()
            original = state.cleanup_game_state
            async def cleanup():
                entered.set()
                await release.wait()
                await original()
            state.cleanup_game_state = cleanup
            closing = asyncio.create_task(server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id))
            await entered.wait()
            assert not await server.gamestate_manager.player_reconnect(USERS[0])
            assert server.gamestate_manager.get_spectator_list() == []
            assert server.gamestate_manager.is_user_in_active_game(USERS[0])
            release.set()
            await closing
    asyncio.run(run())


def test_stale_connection_cannot_disconnect_new_session():
    async def run():
        async with game() as (server, room, state):
            old = server.user_id_to_connection[USERS[0]]
            server.user_id_to_connection[USERS[0]] = SimpleNamespace(websocket=SimpleNamespace(send_json=AsyncMock()))
            await server.gamestate_manager.player_disconnect(USERS[0], connection=old)
            assert "offline" not in state.player_list[0].tag_list
    asyncio.run(run())


def test_abandon_retains_occupancy_but_rejects_reconnect_and_actions():
    async def run():
        async with game() as (server, room, state):
            gm = server.gamestate_manager
            await gm.abandon_player_game(str(USERS[0]), USERS[0])
            assert gm.is_user_in_active_game(USERS[0])
            assert not await gm.player_reconnect(USERS[0])
            socket = server.players[str(USERS[0])].websocket
            await handle_gamestate_message(server, str(USERS[0]),
                {"type": "gamestate/GB/cut_tile", "gamestate_id": state.gamestate_id}, socket)
            assert socket.send_json.call_args.args[0]["message"] == "您已放弃当前对局"
            for uid in USERS[1:]:
                await gm.abandon_player_game(str(uid), uid)
            assert not gm.user_id_to_game_state
            assert state.close_reason == "all_humans_offline"
    asyncio.run(run())


def test_failed_start_rolls_back_lobby_and_every_index():
    async def run():
        server = make_server(USERS)
        room = make_room(USERS)
        server.room_manager.rooms["1"] = room
        with patch.object(module, "configure_duplicate_state", side_effect=ValueError("injected start failure")):
            result = await server.gamestate_manager.start_game(str(USERS[0]), "1")
        assert not result.success
        assert not server.gamestate_manager.gamestate_id_to_game_state
        assert not server.gamestate_manager.user_id_to_game_state
        assert not server.room_manager.active_game_room_ids
        assert not room["is_game_running"]
    asyncio.run(run())


def test_notification_failure_does_not_prevent_core_cleanup():
    async def run():
        async with game() as (server, room, state):
            server.friend_manager = SimpleNamespace(on_game_end=AsyncMock(side_effect=RuntimeError("notification failed")))
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id, reason="runtime_error")
            assert not server.gamestate_manager.gamestate_id_to_game_state
            assert not server.room_manager.active_game_room_ids
            assert not room["is_game_running"]
    asyncio.run(run())


def test_cleanup_failure_still_stops_game_and_owned_waits():
    async def run():
        async with game() as (server, room, state):
            child = start_owned_task(state, asyncio.Event().wait())
            state.cleanup_game_state = AsyncMock(side_effect=RuntimeError("rule cleanup failed"))
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
            assert state.game_task.done()
            assert child.cancelled()
            assert not server.gamestate_manager.user_id_to_game_state
            assert not server.room_manager.active_game_room_ids
    asyncio.run(run())


def test_old_event_rollback_preserves_replacement_room_and_game():
    async def run():
        async with game(room_type="events") as (server, room, state):
            old_room = {**room, "instance_id": "old-room", "active_gamestate_id": "old-game"}
            await server.room_manager._rollback_event_seating(
                "1", [], automatic=True, original_room=old_room)
            assert server.room_manager.rooms["1"] is room
            assert room["is_game_running"]
            assert server.gamestate_manager.get_game_state_by_user_id(USERS[0]) is state
            assert all(conn.current_room_id == "1" for conn in server.players.values())
    asyncio.run(run())


def test_closed_game_rejects_late_owned_work():
    async def run():
        async with game() as (server, room, state):
            await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id)
            action = AsyncMock()
            child = start_owned_task(state, action())
            await asyncio.gather(child, return_exceptions=True)
            assert child.cancelled()
            action.assert_not_awaited()
    asyncio.run(run())


def test_vote_timer_can_close_its_own_game_without_self_cancellation():
    async def run():
        async with game() as (server, room, state):
            vote = state.vote_manager = VoteManager(state)
            vote.phase = "end_countdown"
            with patch("server.gamestate.public.vote_manager.END_COUNTDOWN", 0):
                vote._timer_task = asyncio.create_task(vote._end_resolve_task())
                await asyncio.wait_for(vote._timer_task, timeout=2)
            assert state.lifecycle_state == "closed"
            assert state.close_reason == "vote_end"
            assert not room["is_game_running"]
            assert not server.gamestate_manager.user_id_to_game_state
            for conn in server.players.values():
                ended = [call.args[0] for call in conn.websocket.send_json.call_args_list
                         if call.args[0].get("type") == "gamestate/vote_end"]
                assert ended[0]["gamestate_id"] == state.gamestate_id
    asyncio.run(run())


@pytest.mark.parametrize("phase_name", ["sichuan", "xueliu", "tactical"])
def test_cancelled_opening_or_tactical_phase_drains_child_waits(phase_name):
    """Closing during an opening choice or claim must not orphan Event.wait tasks."""
    async def run():
        rule = "guobiao" if phase_name == "tactical" else phase_name
        async with game(rule=rule) as (_, _, state):
            started = set()

            class ObservedEvent(asyncio.Event):
                async def wait(self):
                    started.add(asyncio.current_task())
                    return await super().wait()

            state.action_events = {i: ObservedEvent() for i in range(4)}
            if phase_name == "sichuan":
                broadcaster = "server.gamestate.game_sichuan.SichuanGameState.broadcast_dingque_ask"
                coroutine = state._dingque_phase()
            elif phase_name == "xueliu":
                broadcaster = "server.gamestate.game_sichuan.boardcast.broadcast_xueliu_throw_three_ask"
                coroutine = state._xueliu_throw_three_phase()
            else:
                from server.gamestate.public.tactical_claim import tactical_grace_phase, init_tactical_round_state
                init_tactical_round_state(state)
                state.tactical_grace_seconds = 60
                state._tactical_action_snapshot = {0: ["chi_left"], 1: ["peng"], 2: [], 3: []}
                state.action_dict = dict(state._tactical_action_snapshot)
                broadcaster = "server.gamestate.game_guobiao.boardcast.broadcast_ask_other_action"
                coroutine = tactical_grace_phase(
                    state, "chi_left", 0, {"action_type": "chi_left"}, 33,
                    broadcast_do_action=AsyncMock(), broadcast_ask_other_action=AsyncMock(),
                    initial_claim_broadcasted=True,
                )
            before = asyncio.all_tasks()
            children = set()
            with patch(broadcaster, new=AsyncMock()):
                task = asyncio.create_task(coroutine)
                try:
                    for _ in range(30):
                        await asyncio.sleep(0)
                        if len(started) >= (1 if phase_name == "tactical" else 4):
                            break
                    assert started, "Phase never reached its event wait"
                    children = asyncio.all_tasks() - before - {task}
                    task.cancel()
                    await asyncio.gather(task, return_exceptions=True)
                    assert task.cancelled()
                    assert all(child.done() for child in children), "Closing left child waits alive"
                finally:
                    task.cancel()
                    for child in children:
                        child.cancel()
                    await asyncio.gather(task, *children, return_exceptions=True)
    asyncio.run(run())


def test_hongque_closed_recipient_does_not_abort_disconnect_cleanup():
    async def run():
        async with game(rule="hongque") as (server, _, state):
            server.user_id_to_connection[USERS[1]].websocket.send_json.side_effect = RuntimeError(
                'Cannot call "send" once a close message has been sent.')
            await server.gamestate_manager.player_disconnect(USERS[0])
            assert state.lifecycle_state == "running"
            server.user_id_to_connection[USERS[2]].websocket.send_json.assert_awaited()
            for uid in USERS[1:]:
                await server.gamestate_manager.player_disconnect(uid)
            assert state.lifecycle_state == "closed"
            assert not server.gamestate_manager.user_id_to_game_state
            assert not server.room_manager.active_game_room_ids
    asyncio.run(run())
