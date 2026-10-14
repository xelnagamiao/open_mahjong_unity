"""SBR 轮庄及双打机器人只和对手的真实操作路径回归。"""
import asyncio
from unittest.mock import patch

import pytest

from . import xueliu_bot
from .action_check import check_action_after_cut, check_action_jiagang
from .boardcast import _base_game_info
from .init_tiles import init_sichuan_tiles
from .test_doubles import WIN
from .test_post_peng_jiagang import make_state
from .wait_action import wait_action
from ..public.game_record_manager import init_game_round
from ..public.lifecycle import cancel_auxiliary_tasks


@pytest.mark.parametrize("dealer", range(4))
@pytest.mark.parametrize("event", [None, {"winners": [0], "discarder": None},
                                  {"winners": [3], "discarder": 2},
                                  {"winners": [1, 2], "discarder": 3}])
def test_sbr_doubles_rotates_dealer_after_every_board(dealer, event):
    state, _ = make_state("doubles")
    state.dealer_index = dealer
    state.first_win_event = event
    assert state._decide_next_dealer() == (dealer + 1) % 4


@pytest.mark.parametrize("mode", ["standard", "xueliu", "xueliu_exchange", "xueliu_doubles"])
def test_other_modes_keep_existing_first_win_dealer_rule(mode):
    state, _ = make_state(mode)
    state.dealer_index = 2
    state.first_win_event = None
    assert state._decide_next_dealer() == 2
    state.first_win_event = {"winners": [3], "discarder": 1}
    assert state._decide_next_dealer() == 3
    state.first_win_event = {"winners": [0, 3], "discarder": 1}
    assert state._decide_next_dealer() == 1


def test_two_orbits_deal_and_record_at_the_rotating_dealer_without_reseating():
    state, _ = make_state("doubles")
    original = [(p.user_id, p.player_index, p.original_player_index, p.team_id) for p in state.player_list]
    for board in range(1, 9):
        state.current_round = state.round_index = board
        state._reset_round_state()
        init_sichuan_tiles(state)
        state.current_player_index = state.dealer_index
        init_game_round(state)
        expected = (board - 1) % 4
        assert state.dealer_index == expected
        assert [len(p.hand_tiles) for p in state.player_list] == [14 if i == expected else 13 for i in range(4)]
        snapshot = _base_game_info(state)
        assert snapshot["dealer_index"] == snapshot["current_player_index"] == expected
        record = state.game_record["game_round"][f"round_index_{board}"]
        assert record["dealer_index"] == record["start_player_index"] == expected
        assert [(p.user_id, p.player_index, p.original_player_index, p.team_id) for p in state.player_list] == original
        state.first_win_event = {"winners": [3], "discarder": 0}
        state.dealer_index = state._decide_next_dealer()


async def inline_cpu(_state, fn, *args, **kwargs):
    return fn(*args, **kwargs)


def claim_state(mode, actor_index, source_offset, rob, won):
    state, messages = make_state(mode)
    state.bot_speed = "instant"
    state.claim_protection = state.tactical_call = False
    source = (actor_index + source_offset) % 4
    state.current_player_index = source
    state.game_status = "waiting_action_qianggang" if rob else "waiting_action_after_cut"
    for p in state.player_list:
        p.waiting_tiles = set()
        p.remaining_time = 120
    actor = state.player_list[actor_index]
    actor.user_id = 2
    actor.has_won = actor.post_hu_lock = won
    actor.huapai_list = [25] if won else []
    actor.waiting_tiles = {25}
    state.player_list[source].discard_tiles = [25]
    if rob:
        state.jiagang_tile = 25
        state.player_list[source].combination_tiles = ["g25"]
        state.player_list[source].combination_mask = [[1, 25, 3, 25, 0, 25, 0, 25]]
    state.action_dict = check_action_jiagang(state, 25) if rob else check_action_after_cut(state, 25)
    return state, messages


@pytest.mark.parametrize("mode,won", [("doubles", False), ("xueliu_doubles", False), ("xueliu_doubles", True)])
@pytest.mark.parametrize("actor", range(4))
@pytest.mark.parametrize("source_offset", [1, 2, 3])
@pytest.mark.parametrize("rob", [False, True])
def test_live_bot_claims_only_enemy_wins(mode, won, actor, source_offset, rob):
    async def run():
        state, _ = claim_state(mode, actor, source_offset, rob, won)
        offered = state.action_dict[actor].copy()
        assert "hu" in offered
        try:
            with patch.object(xueliu_bot, "run_room_bot_cpu", inline_cpu):
                await state.broadcast_ask_other_action()
                await asyncio.wait_for(wait_action(state), 2)
            if source_offset == 2:
                assert state.game_status == ("deal_card_after_gang" if rob else "deal_card")
                assert not state.pending_win
            else:
                assert state.game_status == "settle_win"
                assert list(state.sichuan_hu_results) == [actor]
                assert state.pending_win["discarder"] == (actor + source_offset) % 4
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", ["doubles", "xueliu_doubles"])
def test_bot_still_self_draws_and_does_not_mutate_authoritative_choices(mode):
    async def run():
        state, _ = make_state(mode)
        state.bot_speed = "instant"
        state.current_player_index = 0
        state.game_status = "waiting_hand_action"
        state.player_list[0].user_id = 2
        state.player_list[0].hand_tiles = WIN.copy()
        state.waiting_players_list = [0]
        state.action_dict = {0: ["hu_self", "cut"], 1: [], 2: [], 3: []}
        await xueliu_bot.xueliu_smart_bot_action(state, 0, state.action_dict[0], state.game_status)
        assert (await state.action_queues[0].get())["action_type"] == "hu_self"
        assert state.action_dict[0] == ["hu_self", "cut"]
    asyncio.run(run())
