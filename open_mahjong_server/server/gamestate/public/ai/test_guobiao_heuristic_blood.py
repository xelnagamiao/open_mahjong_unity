"""High-performance bot integration with Guobiao blood battle."""
import asyncio
from unittest.mock import AsyncMock, Mock, patch

import pytest

from . import guobiao_heuristic_ai as ai
from .guobiao_heuristic_logic import context_from_game, count_visible_from_game
from .pacing import BOT_SPEEDS
from ...game_guobiao import blood_battle as blood, boardcast
from ...game_guobiao.action_check import check_action_after_cut, check_action_hand_action
from ...game_guobiao.GuobiaoGameState import GuobiaoGameState
from ...game_guobiao.test_blood_battle import state_for_test
from ...test_room_lifecycle import make_room, make_server, parked_loop, USERS


@pytest.mark.parametrize("speed", BOT_SPEEDS)
@pytest.mark.parametrize("protection", [False, True])
def test_blood_room_adds_three_heuristic_bots_and_starts(speed, protection):
    async def run():
        server = make_server([USERS[0]])
        room = make_room([USERS[0]])
        room.update(sub_rule=blood.SUB_RULE, bot_speed=speed, claim_protection=protection)
        server.room_manager.rooms["1"] = room
        for seat in (3, 1, 2):
            response = await server.room_manager.add_guobiao_heuristic_bot_to_room(str(USERS[0]), "1", seat)
            assert response.success, response.message
            assert room["seat_list"][seat] == 3
        assert room["player_settings"][3]["username"] == "高性能罗伯特"
        with patch.object(GuobiaoGameState, "run_game_loop", parked_loop):
            try:
                assert await server.gamestate_manager.start_game(str(USERS[0]), "1") is None
                state = server.gamestate_manager.get_game_state_by_room_id("1")
                assert state.sub_rule == blood.SUB_RULE
                assert [p.user_id for p in state.player_list].count(3) == 3
                assert state.bot_speed == speed
                assert state.claim_protection is False
                assert room["claim_protection"] is protection
                assert state.hepai_limit == 8
                assert not state.tactical_call and not state.open_cuohe
            finally:
                for gid in list(server.gamestate_manager.gamestate_id_to_game_state):
                    await server.gamestate_manager.cleanup_game_state_complete(gamestate_id=gid)
    asyncio.run(run())


@pytest.mark.parametrize("case", ["non_host", "running", "occupied"])
def test_blood_room_still_enforces_seat_and_host_checks(case):
    async def run():
        server = make_server(USERS[:2])
        room = make_room(USERS[:2])
        room.update(sub_rule=blood.SUB_RULE, is_game_running=case == "running")
        server.room_manager.rooms["1"] = room
        requester = USERS[1] if case == "non_host" else USERS[0]
        seat = 1 if case == "occupied" else 3
        response = await server.room_manager.add_guobiao_heuristic_bot_to_room(str(requester), "1", seat)
        assert not response.success
        assert room["player_list"] == USERS[:2]
    asyncio.run(run())


@pytest.mark.parametrize("winners", [[1], [1, 2]])
@pytest.mark.parametrize("rob_kong", [False, True])
def test_public_win_tile_remains_visible_once_after_settlement(winners, rob_kong):
    async def run():
        state = state_for_test()
        state.player_list[3].hand_tiles = []
        state.hu_class = "hu_first"
        state.blood_pending_claims = {w: ("hu_first", "hu_second")[w - 1] for w in winners}
        state.result_dict = {action: (16, ["test"]) for action in state.blood_pending_claims.values()}
        if rob_kong:
            state.jiagang_tile = 45
            state.player_list[0].combination_tiles = ["g45"]
            state.player_list[0].combination_mask = [[3, 45, 1, 45, 0, 45, 0, 45]]
        else:
            state.player_list[0].discard_tiles = [45]
        before = count_visible_from_game(state, 3)
        with patch.object(blood, "append_action_tick", Mock()), \
                patch.object(blood, "player_action_record_hu", Mock()), \
                patch.object(blood, "hu_result_ready_pre_panel_seconds", return_value=0):
            await blood.settle_win(state)
        assert state.game_status == "deal_card"
        assert state.blood_public_win_tiles == [45]
        assert count_visible_from_game(state, 3) == before
        # Winner hands stay private even though the server retains them.
        state.player_list[1].hand_tiles = [19] * 14
        assert count_visible_from_game(state, 3) == before
    asyncio.run(run())


def test_retired_self_draw_hand_and_win_tile_remain_private():
    state = state_for_test()
    before = context_from_game(state, 3)
    winner = state.player_list[1]
    winner.is_hu = True
    winner.blood_win_is_zimo = True
    winner.blood_win_tile = 19
    winner.hand_tiles = [19] * 14
    assert context_from_game(state, 3).visible == before.visible


@pytest.mark.parametrize("retired", [[1], [1, 2]])
@pytest.mark.parametrize("phase", ["waiting_hand_action", "waiting_action_after_cut"])
def test_broadcast_only_dispatches_active_heuristic_bots(retired, phase):
    async def run():
        state = state_for_test()
        state.game_status = phase
        for index, player in enumerate(state.player_list):
            player.user_id = 3
            player.is_hu = index in retired
        if phase == "waiting_hand_action":
            state.player_list[0].hand_tiles.append(19)
            state.action_dict = check_action_hand_action(state, 0)
        else:
            state.player_list[0].discard_tiles = [45]
            state.action_dict = check_action_after_cut(state, 45)
        tasks = []
        def dispatch(_state, coro):
            tasks.append(asyncio.create_task(coro))
        with patch.object(boardcast, "guobiao_heuristic_action", AsyncMock()) as bot, \
                patch.object(boardcast, "start_owned_task", dispatch):
            if phase == "waiting_hand_action":
                await state.broadcast_ask_hand_action()
            else:
                await state.broadcast_ask_other_action()
            await asyncio.gather(*tasks)
        dispatched = {call.args[1] for call in bot.await_args_list}
        assert dispatched
        assert dispatched == {i for i, actions in state.action_dict.items() if actions}
        assert not dispatched.intersection(retired)
    asyncio.run(run())


@pytest.mark.parametrize("protection", [False, True])
@pytest.mark.parametrize("phase,action,seat", [
    ("waiting_hand_action", "hu_self", 0),
    ("waiting_action_after_cut", "hu_first", 1),
    ("waiting_action_after_cut", "hu_second", 2),
    ("waiting_action_after_cut", "hu_third", 3),
    ("waiting_action_qianggang", "hu_first", 1),
    ("waiting_action_qianggang", "hu_second", 2),
    ("waiting_action_qianggang", "hu_third", 3),
])
def test_heuristic_accepts_legal_blood_wins(protection, phase, action, seat):
    async def run():
        state = state_for_test()
        state.game_status = phase
        state.claim_protection = protection
        state.bot_speed = "instant"
        state.waiting_players_list = [seat]
        state.player_list[seat].user_id = 3
        state.result_dict = {action: (16, ["test"])}
        with patch.object(ai, "get_ai_action", AsyncMock()) as submit:
            await ai.guobiao_heuristic_action(state, seat, [action, "pass"], phase)
        submit.assert_awaited_once()
        assert submit.await_args.args[:3] == (state, seat, action)
    asyncio.run(run())
