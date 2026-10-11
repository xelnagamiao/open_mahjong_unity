"""血流和后开杠保留全部实际和牌张；暗杠、加杠限本次摸牌。"""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .action_check import (check_action_after_cut, check_action_hand_action,
                           refresh_waiting_tiles, _xueliu_post_hu_kong_allowed)
from .boardcast import reconnected_send_pending_ask
from .test_post_peng_jiagang import FLOW_MODES, make_state, ReachedFollowup
from .test_action_window import submit
from .wait_action import wait_action
from .xueliu_bot import choose_hand, choose_claim
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer


@pytest.mark.parametrize("mode", FLOW_MODES)
def test_rule_snapshot_records_post_hu_kong_constraint(mode):
    state, _ = make_state(mode)
    state._record_rule_profile()
    profile = state.game_record["game_title"]["xueliu_rule_profile"]
    assert profile["allow_post_hu_ming_gang"] is True
    assert profile["post_hu_kong_wait_rule"] == "includes_all_won_tiles"
    assert profile["post_hu_hand_kong_requires_drawn_tile"] is True
    assert profile["version"] == ("xueliu-single-table-2v2-v2" if mode == "xueliu_doubles" else 4)
    if mode == "xueliu_doubles":
        assert state.game_record["game_title"]["doubles_rule_profile"] == profile


def locked_state(mode, kind, *, drawn=18, won=True, draw_slot=True):
    state, messages = make_state(mode)
    state.current_player_index = 0
    state.game_status = "waiting_hand_action"
    player = state.player_list[0]
    player.hand_tiles = ([15, 15, 15, 15, 16, 17, 21, 21, 21, 25]
                         if kind == "angang" else [15, 16, 17, 21, 21, 21, 25])
    player.combination_tiles = [] if kind == "angang" else ["k15"]
    if mode != "xueliu":
        player.combination_tiles.append("k29")
    player.combination_mask = [[1, int(m[1:]), 0, int(m[1:]), 0, int(m[1:])] for m in player.combination_tiles]
    player.dingque_suit = 3
    player.waiting_tiles = set(
        state.calculation_service.Sichuan_xueliu_tingpai_check(
            player.hand_tiles, player.combination_tiles, meld_count=state.xueliu_meld_count))
    assert player.waiting_tiles == {25}
    player.huapai_list = [25] if won else []
    player.hand_tiles.append(drawn)
    player.has_draw_slot = draw_slot
    player.post_hu_lock = player.has_won = won
    for other in state.player_list[1:]:
        other.waiting_tiles = set()
    return state, messages


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("kind", ["angang", "jiagang"])
@pytest.mark.parametrize("draw_slot", [True, False])
def test_555567_draw_8_cannot_kong_old_5_after_win(mode, kind, draw_slot):
    state, _ = locked_state(mode, kind, draw_slot=draw_slot)
    assert kind not in check_action_hand_action(state, 0)[0]


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_before_win_old_hand_kongs_remain_allowed(mode, kind):
    state, _ = locked_state(mode, kind, won=False)
    assert kind in check_action_hand_action(state, 0)[0]


def valid_drawn_kong(mode, kind):
    state, messages = locked_state(mode, kind)
    player = state.player_list[0]
    # 将旧手的一枚五换成八，摸到第四枚五；暗杠/加杠后仍听五筒。
    player.hand_tiles.remove(15)
    player.hand_tiles[-1] = 18
    player.hand_tiles.append(15)
    return state, messages


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_post_win_drawn_tile_kong_executes_and_keeps_won_tiles(mode, kind):
    async def run():
        state, messages = valid_drawn_kong(mode, kind)
        player = state.player_list[0]
        state.action_dict = check_action_hand_action(state, 0)
        assert kind in state.action_dict[0]
        await state.broadcast_ask_hand_action()
        await submit(state, 0, kind, target_tile=15)
        try:
            await asyncio.wait_for(wait_action(state), 2)
            assert ("G15" if kind == "angang" else "g15") in player.combination_tiles
            assert 15 not in player.hand_tiles
            assert player.waiting_tiles == {25}
            assert not player.has_draw_slot
            kong = next(m["message"]["do_action_info"] for m in messages
                        if m["message"].get("do_action_info", {}).get("action_list") == [kind])
            assert kong["is_mo_gang"] is True
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_ask_and_reconnect_only_advertise_drawn_kong_target(mode, kind):
    async def run():
        state, messages = valid_drawn_kong(mode, kind)
        state.action_dict = check_action_hand_action(state, 0)
        try:
            await state.broadcast_ask_hand_action()
            for seat in range(4):
                await drain_viewer(state, seat)
            uid = state.player_list[0].user_id
            await reconnected_send_pending_ask(state, uid)
            asks = [m for m in messages if m["type"].endswith("/broadcast_hand_action")]
            own = [m["message"]["ask_hand_action_info"] for m in asks if m["user_id"] == uid]
            assert len(own) == 2
            assert all(ask.get("kong_candidates") == {kind: [15]} for ask in own)
            assert all(not m["message"]["ask_hand_action_info"].get("kong_candidates")
                       for m in asks if m["user_id"] != uid)
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_forged_old_target_rejected_even_if_action_word_is_advertised(mode, kind):
    async def run():
        state, _ = locked_state(mode, kind)
        player = state.player_list[0]
        before_melds = list(player.combination_tiles)
        before_hand = list(player.hand_tiles)
        # 模拟旧询问/伪造候选：服务端消费操作时也必须重新检验目标。
        state.action_dict = {i: [kind, "cut"] if i == 0 else [] for i in range(4)}
        await state.broadcast_ask_hand_action()
        await submit(state, 0, kind, target_tile=15)
        await submit(state, 0, "cut", TileId=18, cutClass=True)
        try:
            await asyncio.wait_for(wait_action(state), 2)
            assert player.combination_tiles == before_melds
            assert player.hand_tiles == before_hand[:-1]
            assert not player.gang_score_records
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("kind", ["angang", "jiagang"])
def test_bot_does_not_choose_an_old_tile_while_locked(kind):
    hand = ([15]*4 + [16, 17, 21] + [25]*4 if kind == "angang"
            else [15, 16, 17, 21, 25])
    melds = [] if kind == "angang" else ["k15", "k25"]
    with patch(__package__ + ".xueliu_bot.evaluate", return_value=1):
        action, target, _ = choose_hand(hand, melds, [0]*34, [kind, "cut"], 3, 3, locked=True)
    assert (action, target) == (kind, 25)


def changed_wait_state(mode, kind="gang", won_tiles=(17,)):
    state, messages = make_state(mode)
    state.room_id = "910010"
    state.claim_protection = state.tactical_call = False
    seat = 1 if kind == "gang" else 0
    state.current_player_index = 0
    state.game_status = "waiting_action_after_cut" if kind == "gang" else "waiting_hand_action"
    player = state.player_list[seat]
    player.hand_tiles = [17, 18, 18, 18, 26, 26, 26, 27, 27, 27]
    player.combination_tiles = [] if mode == "xueliu" else ["k29"]
    player.combination_mask = [] if mode == "xueliu" else [[1, 29, 0, 29, 0, 29]]
    player.dingque_suit = 3
    player.has_won = player.post_hu_lock = True
    player.has_draw_slot = False
    player.huapai_list = list(won_tiles)
    player.win_count = len(won_tiles)
    refresh_waiting_tiles(state, seat)
    assert player.waiting_tiles == {16, 17, 19}
    if kind == "angang":
        player.hand_tiles.append(18)
        player.has_draw_slot = True
    else:
        state.player_list[0].discard_tiles = [18]
    for other in state.player_list:
        if other is not player:
            other.waiting_tiles = set()
    return state, messages, seat


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("kind", ["gang", "angang"])
@pytest.mark.parametrize("won_tiles,allowed", [
    ([17], True), ([17, 17], True), ([16], False), ([19], False),
    ([17, 19], False), ([17, 16, 19], False),
])
def test_changed_waits_only_need_to_include_all_actual_wins(mode, kind, won_tiles, allowed):
    state, _, seat = changed_wait_state(mode, kind, won_tiles)
    player = state.player_list[seat]
    hand, melds = list(player.hand_tiles), list(player.combination_tiles)
    assert _xueliu_post_hu_kong_allowed(state, player, kind, 18) is allowed
    actions = (check_action_after_cut(state, 18) if kind == "gang"
               else check_action_hand_action(state, seat))[seat]
    assert (kind in actions) is allowed
    assert "peng" not in actions
    assert player.hand_tiles == hand and player.combination_tiles == melds


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("kind", ["gang", "angang"])
def test_kong_recalculates_waits_instead_of_restoring_first_win_waits(mode, kind):
    async def run():
        state, messages, seat = changed_wait_state(mode, kind)
        player = state.player_list[seat]
        state.action_dict = (check_action_after_cut(state, 18) if kind == "gang"
                             else check_action_hand_action(state, seat))
        try:
            if kind == "gang":
                await state.broadcast_ask_other_action()
            else:
                await state.broadcast_ask_hand_action()
            await submit(state, seat, kind, target_tile=18)
            await asyncio.wait_for(wait_action(state), 2)
            assert player.waiting_tiles == {17}
            assert player.huapai_list == [17]
            assert state.game_status == "deal_card_after_gang"
            assert state.current_player_index == seat
            assert not player.has_draw_slot
            # 摸牌前、摸牌槽存在时、摸切后，均不应恢复已经失去的六万/九万听口。
            refresh_waiting_tiles(state, seat)
            player.hand_tiles.append(17)
            player.has_draw_slot = True
            refresh_waiting_tiles(state, seat)
            assert player.waiting_tiles == {17}
            assert "hu_self" in check_action_hand_action(state, seat, is_get_gang_tile=True)[seat]
            player.hand_tiles[-1] = 19
            refresh_waiting_tiles(state, seat)
            assert "hu_self" not in check_action_hand_action(state, seat)[seat]
            player.hand_tiles.pop()
            player.has_draw_slot = False
            refresh_waiting_tiles(state, seat)
            assert player.waiting_tiles == {17}
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", FLOW_MODES)
def test_stale_ming_kong_is_revalidated_using_discard_not_client_target(mode):
    async def run():
        state, _, seat = changed_wait_state(mode, won_tiles=(17, 19))
        player = state.player_list[seat]
        before = list(player.hand_tiles), list(player.combination_tiles)
        # 刚和过的九万也必须保留；旧询问中的杠按钮不能绕过消费时校验。
        state.action_dict = {i: ["gang", "pass"] if i == seat else [] for i in range(4)}
        try:
            await state.broadcast_ask_other_action()
            await submit(state, seat, "gang", target_tile=26)
            await submit(state, seat, "pass")
            await asyncio.wait_for(wait_action(state), 2)
            assert (player.hand_tiles, player.combination_tiles) == before
            assert state.player_list[0].discard_tiles == [18]
            assert not player.gang_score_records
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", FLOW_MODES)
def test_post_hu_ming_kong_still_requires_replacement_tile_and_non_dingque_suit(mode):
    state, _, seat = changed_wait_state(mode)
    state.tiles_list = []
    assert "gang" not in check_action_after_cut(state, 18)[seat]
    state.tiles_list = [29]*8
    state.player_list[seat].dingque_suit = 1
    assert "gang" not in check_action_after_cut(state, 18)[seat]


@pytest.mark.parametrize("mode", FLOW_MODES)
@pytest.mark.parametrize("kind", ["gang", "angang", "jiagang"])
@pytest.mark.parametrize("won_tiles,allowed", [([21], True), ([21, 24, 21], True), ([21, 29], False)])
def test_post_kong_waits_may_be_a_strict_superset_of_actual_wins(mode, kind, won_tiles, allowed):
    state, _ = make_state(mode)
    player = state.player_list[0]
    player.hand_tiles = list(range(21, 28))
    player.combination_tiles = [] if mode == "xueliu" else ["k19"]
    if kind == "jiagang":
        player.combination_tiles.append("k11")
    else:
        player.hand_tiles = [11]*3 + player.hand_tiles
    if kind != "gang":
        player.hand_tiles.append(11)
    player.has_draw_slot = kind != "gang"
    player.has_won = player.post_hu_lock = True
    player.dingque_suit = 3
    player.huapai_list = won_tiles
    # 杠后同时听一、四、七筒；只和过一筒也可以杠，无须集合相等。
    assert _xueliu_post_hu_kong_allowed(state, player, kind, 11) is allowed


@pytest.mark.parametrize("mode", FLOW_MODES)
def test_each_actual_win_extends_required_kong_wait_set(mode):
    async def run():
        state, _, seat = changed_wait_state(mode, won_tiles=())
        player = state.player_list[seat]
        player.has_won = player.post_hu_lock = False
        try:
            for tile, allowed in ((17, True), (19, False)):
                state.current_player_index = 0
                assert "hu" in check_action_after_cut(state, tile)[seat]
                state.pending_win = {"type": "ron", "discarder": 0, "hepai_tile": tile}
                with patch(__package__ + ".XueliuGameState.asyncio.sleep", new=AsyncMock()):
                    await state._settle_win()
                assert player.has_won and player.post_hu_lock
                assert _xueliu_post_hu_kong_allowed(state, player, "gang", 18) is allowed
            assert player.huapai_list == [17, 19]
            assert player.win_count == 2
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("teammate", [False, True])
def test_doubles_required_enemy_hu_still_takes_precedence_over_legal_kong(teammate):
    state, _, seat = changed_wait_state("xueliu_doubles", won_tiles=(18,))
    player = state.player_list[seat]
    player.hand_tiles = [13, 13, 13, 15, 15, 15, 16, 17, 26, 26]
    if teammate:
        state.current_player_index = 3  # 南/北同队；东/南异队。
    refresh_waiting_tiles(state, seat)
    assert _xueliu_post_hu_kong_allowed(state, player, "gang", 15)
    actions = check_action_after_cut(state, 15)[seat]
    assert actions == (["hu", "gang", "pass"] if teammate else ["hu"])


@pytest.mark.parametrize("mode", FLOW_MODES)
def test_ming_kong_ask_and_reconnect_restore_legal_discard_claim(mode):
    async def run():
        state, messages, seat = changed_wait_state(mode)
        player = state.player_list[seat]
        state.action_dict = check_action_after_cut(state, 18)
        try:
            await state.broadcast_ask_other_action()
            for index in range(4):
                await drain_viewer(state, index)
            await reconnected_send_pending_ask(state, player.user_id)
            asks = [m["message"]["ask_other_action_info"] for m in messages
                    if m["type"].endswith("/ask_other_action") and m["user_id"] == player.user_id]
            assert len(asks) == 2
            assert all(ask["action_list"] == ["gang", "pass"] and ask["cut_tile"] == 18 for ask in asks)
            assert asks[0]["action_tick"] == asks[1]["action_tick"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", FLOW_MODES)
def test_bot_can_choose_post_hu_ming_kong_from_server_actions(mode):
    state, _, seat = changed_wait_state(mode)
    player = state.player_list[seat]
    actions = check_action_after_cut(state, 18)[seat]
    # 只固定策略评分；手牌、听牌检查和服务器可选动作均使用真实计算。
    with patch(__package__ + ".xueliu_bot.evaluate", side_effect=[0, 1]):
        assert choose_claim(player.hand_tiles, player.combination_tiles, [0]*34,
                            actions, 18, state.xueliu_meld_count, 3) == "gang"


@pytest.mark.parametrize("mode", FLOW_MODES)
def test_real_loop_post_hu_ming_kong_draws_replacement_with_updated_waits(mode):
    async def run():
        state, messages, seat = changed_wait_state(mode)
        actor = state.player_list[seat]
        saved_hand, saved_melds, saved_masks = list(actor.hand_tiles), list(actor.combination_tiles), list(actor.combination_mask)
        state.room_id = "910010"
        state.Debug = True
        state._opening_phase = AsyncMock()
        visited = []

        def deal(current):
            for player in current.player_list:
                player.hand_tiles = [11, 12, 13, 14, 16, 17, 21, 22, 23, 25]
                player.combination_tiles = [] if mode == "xueliu" else ["k24"]
                player.combination_mask = [] if mode == "xueliu" else [[1, 24, 0, 24, 0, 24]]
                player.dingque_suit = 3
            current.player_list[0].hand_tiles.append(18)
            current.player_list[0].has_draw_slot = True
            actor.hand_tiles, actor.combination_tiles, actor.combination_mask = saved_hand, saved_melds, saved_masks
            actor.has_won = actor.post_hu_lock = True
            actor.huapai_list = [17]
            actor.win_count = 1
            refresh_waiting_tiles(current, seat)
            current.tiles_list = [17]*8

        for index, player in enumerate(state.player_list):
            socket = state.game_server.user_id_to_connection[player.user_id].websocket
            original_send = socket.send_json

            async def reply(payload, index=index, original_send=original_send):
                await original_send(payload)
                info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
                if not info or not info["action_list"]:
                    return
                if index == 0 and "cut" in info["action_list"]:
                    await submit(state, index, "cut", TileId=18, cutClass=True, tick=info["action_tick"])
                elif index == seat and "gang" in info["action_list"]:
                    visited.append("claim")
                    await submit(state, index, "gang", tick=info["action_tick"])
                elif "pass" in info["action_list"]:
                    await submit(state, index, "pass", tick=info["action_tick"])
            socket.send_json = reply

        original_ask = state.broadcast_ask_hand_action

        async def stop_after_replacement():
            if state.current_player_index == seat:
                visited.append("replacement")
                assert actor.waiting_tiles == {17}
                assert actor.hand_tiles[-1] == 17 and actor.has_draw_slot
                assert "hu_self" in state.action_dict[seat]
                raise ReachedFollowup()
            await original_ask()
        state.broadcast_ask_hand_action = stop_after_replacement
        try:
            with patch(__package__ + ".SichuanGameState.init_sichuan_tiles", side_effect=deal):
                with pytest.raises(ReachedFollowup):
                    await asyncio.wait_for(state.game_loop_sichuan(), 5)
            assert visited == ["claim", "replacement"]
            assert "g18" in actor.combination_tiles and 18 not in actor.hand_tiles
            kong = next(m["message"]["do_action_info"] for m in messages
                        if m["message"].get("do_action_info", {}).get("action_list") == ["gang"])
            assert kong["cut_from_player"] == 0 and kong["combination_target"] == "g18"
            assert len(actor.gang_score_records) == 1
            assert sum(p.score for p in state.player_list) == 0
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
