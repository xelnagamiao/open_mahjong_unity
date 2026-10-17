"""碰后加杠：真实主循环、操作路由、补牌及抢杠回归。"""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from . import action_check
from .test_action_window import make_state as make_solo_state, reply_on_ask, submit
from .wait_action import wait_action
from .test_doubles import make_state as make_battle_doubles
from .test_xueliu_doubles import make_state as make_flow_doubles
from ..public.hand_slot_utils import has_draw_slot
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer


FLOW_MODES = ("xueliu", "xueliu_exchange", "xueliu_doubles")
BATTLE_MODES = ("standard", "doubles")


def make_state(mode):
    if mode == "doubles":
        return make_battle_doubles()
    if mode == "xueliu_doubles":
        return make_flow_doubles()
    return make_solo_state(mode, claim_protection=False, tactical_call=False)


class ReachedFollowup(Exception):
    pass


@pytest.mark.parametrize("mode", FLOW_MODES + BATTLE_MODES)
@pytest.mark.parametrize("target", [15, 21])
def test_real_loop_offers_kong_after_peng_and_draws_replacement(mode, target):
    """既可加刚碰的刻子，也可加先前的刻子；碰后不能误认摸杠/自摸。"""
    async def run():
        state, messages = make_state(mode)
        state.room_id = "910010"
        state.Debug = True
        state._opening_phase = AsyncMock()
        state.claim_protection = False
        state.tactical_call = False
        expected_actions = ["jiagang", "cut"] if mode in FLOW_MODES else ["cut"]
        visited = []

        def deal(current):
            for player in current.player_list:
                player.hand_tiles = [11, 12, 13, 14, 16, 18, 22, 24, 26, 28, 29, 19, 27]
                if mode == "xueliu":
                    player.hand_tiles = player.hand_tiles[:10]
                player.dingque_suit = 3 if mode != "doubles" else 0
            current.player_list[0].hand_tiles.append(15)
            current.player_list[0].has_draw_slot = True
            actor = current.player_list[1]
            actor.hand_tiles = [15, 15, 15, 21, 22, 23, 24, 25, 26, 29]
            if mode == "xueliu":
                actor.hand_tiles = actor.hand_tiles[:7]
            actor.combination_tiles = ["k21"]
            actor.combination_mask = [[1, 21, 0, 21, 0, 21]]
            current.tiles_list = [19, 29, 39] * 3

        for seat, player in enumerate(state.player_list):
            socket = state.game_server.user_id_to_connection[player.user_id].websocket
            original_send = socket.send_json

            async def reply(payload, seat=seat, original_send=original_send):
                await original_send(payload)
                info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
                if not info:
                    return
                actions = info["action_list"]
                if not actions:
                    return
                if seat == 0 and state.game_status == "waiting_hand_action":
                    await submit(state, seat, "cut", TileId=15, cutClass=True, tick=info["action_tick"])
                elif seat == 1 and "peng" in actions:
                    await submit(state, seat, "peng", tick=info["action_tick"])
                elif seat == 1 and state.game_status == "waiting_hand_action":
                    await submit(state, seat, "jiagang", target_tile=target, tick=info["action_tick"])
                elif "pass" in actions:
                    await submit(state, seat, "pass", tick=info["action_tick"])
            socket.send_json = reply

        original_ask = state.broadcast_ask_hand_action

        async def check_post_peng_ask():
            if state.current_player_index == 1:
                visited.append("post_peng")
                assert state.player_list[1].combination_tiles == ["k21", "k15"]
                assert state.action_dict[1] == expected_actions
                assert not has_draw_slot(state.player_list[1])
            await original_ask()
            if state.current_player_index == 1 and mode in BATTLE_MODES:
                assert state.action_queues[1].empty(), "blood battle must reject forged post-peng kong"
                raise ReachedFollowup()

        state.broadcast_ask_hand_action = check_post_peng_ask
        original_broadcast = state.broadcast_do_action

        async def stop_at_replacement(**fields):
            await original_broadcast(**fields)
            if fields.get("action_list") == ["deal_gang_tile"]:
                visited.append("replacement")
                assert fields["action_player"] == 1
                assert has_draw_slot(state.player_list[1])
                raise ReachedFollowup()

        state.broadcast_do_action = stop_at_replacement
        try:
            with patch(__package__ + ".SichuanGameState.init_sichuan_tiles", side_effect=deal):
                with pytest.raises(ReachedFollowup):
                    await asyncio.wait_for(state.game_loop_sichuan(), 5)
            assert visited == (["post_peng", "replacement"] if mode in FLOW_MODES else ["post_peng"])
            if mode in FLOW_MODES:
                actor = state.player_list[1]
                assert f"g{target}" in actor.combination_tiles
                assert actor.hand_tiles.count(target) == 0
                kong = next(m["message"]["do_action_info"] for m in messages
                            if m["message"].get("do_action_info", {}).get("action_list") == ["jiagang"])
                assert kong["is_mo_gang"] is False
                assert len(actor.gang_score_records) == 1
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("blocked", [None, "wall", "no_fourth_tile", "not_peng", "dingque"])
def test_post_peng_candidates_respect_kong_boundaries(mode, blocked):
    state, _ = make_state(mode)
    player = state.player_list[1]
    player.hand_tiles = [11, 11, 11, 11, 21, 22, 23, 29]
    player.combination_tiles = ["k21"]
    player.dingque_suit = 3
    if blocked == "wall":
        state.tiles_list = state.tiles_list[:state.dead_wall_count]
    elif blocked == "no_fourth_tile":
        player.hand_tiles.remove(21)
    elif blocked == "not_peng":
        player.combination_tiles = ["s21"]
    elif blocked == "dingque":
        player.dingque_suit = 2
    actions = action_check.check_action_after_peng(state, 1)
    assert actions == {0: [], 1: ["cut"] if blocked else ["jiagang", "cut"], 2: [], 3: []}


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("response", ["hu", "pass"])
def test_post_peng_jiagang_can_be_robbed_or_continue_to_draw(mode, response):
    async def run():
        state, messages = make_state(mode)
        actor = state.player_list[1]
        actor.hand_tiles = [21, 22, 23, 24, 25, 26, 27, 28]
        if mode == "xueliu":
            actor.hand_tiles = actor.hand_tiles[:5]
        actor.combination_tiles = ["k21", "k15"]
        actor.combination_mask = [[1, 21, 0, 21, 0, 21], [1, 15, 0, 15, 0, 15]]
        actor.has_draw_slot = False
        for player in state.player_list:
            player.waiting_tiles = set()
            player.dingque_suit = 3
        robber = state.player_list[2]
        robber.hand_tiles = [22, 23, 11, 12, 13, 17, 18, 19, 27, 28, 29, 26, 26]
        if mode == "xueliu":
            robber.hand_tiles = robber.hand_tiles[:5] + robber.hand_tiles[8:]
        action_check.refresh_waiting_tiles(state, 2)
        assert 21 in robber.waiting_tiles
        state.current_player_index = 1
        state.game_status = "waiting_hand_action"
        state.action_dict = action_check.check_action_after_peng(state, 1)
        scores = [p.score for p in state.player_list]
        reply_on_ask(state, 1, "jiagang", target_tile=21)
        try:
            await state.broadcast_ask_hand_action()
            await asyncio.wait_for(wait_action(state), 2)
            assert state.game_status == "waiting_action_qianggang"
            assert actor.combination_tiles == ["g21", "k15"]
            assert len(actor.hand_tiles) == (4 if mode == "xueliu" else 7)
            assert not has_draw_slot(actor)
            assert len(actor.gang_score_records) == 1
            reply_on_ask(state, 2, response)
            await state.broadcast_ask_other_action()
            await drain_viewer(state, 2)
            asks = [m["message"]["ask_other_action_info"] for m in messages
                    if m["user_id"] == robber.user_id and m["message"].get("ask_other_action_info")]
            assert asks[-1]["cut_tile"] == 21, "抢杠提示必须显示加杠牌"
            await asyncio.wait_for(wait_action(state), 2)
            if response == "hu":
                assert state.game_status == "settle_win"
                assert state.pending_win["type"] == "qianggang"
                assert actor.combination_tiles == ["k21", "k15"]
                assert len(actor.combination_mask[0]) == 6
                assert not actor.gang_score_records
                assert [p.score for p in state.player_list] == scores
            else:
                assert state.game_status == "deal_card_after_gang"
                assert actor.combination_tiles == ["g21", "k15"]
                assert len(actor.gang_score_records) == 1
            assert sum(p.score for p in state.player_list) == sum(scores)
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
