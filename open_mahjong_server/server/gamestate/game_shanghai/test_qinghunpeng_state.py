import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock
from unittest.mock import patch

import pytest

from .QinghunpengGameState import QinghunpengGameState
from .ShanghaiGameState import ShanghaiGameState
from ...game_calculation.shanghai import qinghunpeng as rule
from ...room.room_validators import ShanghaiRoomValidator
from ..public.game_record_manager import init_game_record, init_game_round


def make_state():
    server = SimpleNamespace(user_id_to_connection={}, gamestate_manager=SimpleNamespace(), room_manager=SimpleNamespace())
    state = QinghunpengGameState(server, {
        "room_id": "123456", "player_list": [101,102,103,104], "sub_rule": rule.SUB_RULE,
        "round_timer": 0, "step_timer": 0, "game_round": 1,
        "tips": True, "room_type": "custom", "allow_spectator": False,
    }, None, None, "qinghunpeng-test-state")
    for index, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = index
    state.tiles_list = [21,22,23,24,25,26]
    init_game_record(state)
    init_game_round(state)
    return state


def test_independent_class_and_144_tile_wall():
    state = make_state()
    assert not isinstance(state, ShanghaiGameState)
    state.master_seed = 144
    state.init_tiles()
    tiles = state.tiles_list + [t for p in state.player_list for t in p.hand_tiles]
    assert len(tiles) == 144 and sum(t in state.flower_tiles for t in tiles) == 8
    assert Counter(tiles)[45] == 4
    assert state.build_concealed_kong_mask([45]*4) == [0,45]*4
    config = ShanghaiRoomValidator(room_name="清混碰", game_round=1, round_timer=30, step_timer=5, sub_rule=rule.SUB_RULE)
    assert config.sub_rule == rule.SUB_RULE


def test_dragons_are_playable_and_win_does_not_require_declaration():
    state = make_state()
    player = state.player_list[0]
    player.hand_tiles = [11,12,13,14,15,16,17,18,19,45,45,45,41,41]
    assert state.check_hand_actions(0)[0] == ["hu_self", "cut"]
    assert not player.declared_ready
    player.hand_tiles[-1] = 51
    assert state.check_hand_actions(0)[0] == ["buhua"]


def test_contracts_are_symmetric_at_three_and_do_not_stack():
    state = make_state()
    for _ in range(3): state._remember_claim_liability(0,1,11)
    for _ in range(3): state._remember_claim_liability(1,0,11)
    for _ in range(3): state._remember_claim_liability(0,2,11)
    assert state.contracts == {(0,1),(0,2)}
    assert state.contract_partners(1) == {0}
    assert set(state.player_list[0].tag_list) >= {"chengbao_1","chengbao_2"}


@pytest.mark.parametrize("partner,expected", [
    (2, [60,-30,-30,0]),
    (1, [60,-60,0,0]),
])
@pytest.mark.parametrize("draw_debt,multiplier", [(0,1),(2,2)])
def test_rob_kong_contract_settlement_broadcast_and_record(partner, expected, draw_debt, multiplier):
    async def run():
        state = make_state()
        state.huangfan_count = draw_debt
        state.run_hu_result_ready_phase = AsyncMock()
        for _ in range(3):
            state._remember_claim_liability(0,partner,11)
        state.player_list[0].hand_tiles = [11,12,14,15,16,17,18,19,45,45,45,41,41]
        detail = state.score_candidate(0,"robbing_kong",13)
        assert detail["base_score"] == 10
        state.pending_winners = [{"index":0,"payer":1,"source":"robbing_kong",
            "hu_class":"hu_third","tile":13,"detail":detail}]
        before = {p.original_player_index:p.score for p in state.player_list}
        changes = [value * multiplier for value in expected]
        with patch('server.gamestate.game_shanghai.QinghunpengGameState.broadcast_result',new=AsyncMock()) as broadcast:
            assert not await state._settle_hand(before)
        result = broadcast.await_args.kwargs
        assert result["is_qianggang"] is True and result["ron_discarder_index"] == 1
        assert result["hu_score"] == 10 * multiplier
        assert result["score_changes"] == dict(enumerate(changes))
        assert [p.score - before[p.original_player_index] for p in state.player_list] == changes
        ticks = state.game_record["game_round"][f"round_index_{state.round_index}"]["action_ticks"]
        win = next(tick for tick in ticks if tick[0] == "hu_third")
        assert win[2] == 10 * multiplier and win[4] == changes
        assert "抢杠" in win[3] and "承包结算" in win[3]
        assert state.huangfan_count == max(0,draw_debt-1)
        assert sum(changes) == 0
    asyncio.run(run())


def test_illegal_cross_suit_claim_and_kong_are_rejected():
    async def run():
        state = make_state()
        player = state.player_list[1]
        player.hand_tiles = [21,21,21,21,11,12,13,14]
        player.combination_tiles = ["s12"]
        player.has_draw_slot = True
        state.player_list[0].discard_tiles = [21]
        assert "peng" not in state.check_discard_actions(21)[1]
        assert "angang" not in state.check_hand_actions(1)[1]
        await state.execute_claim(1,"peng")
        await state.execute_angang(1,21)
        assert player.combination_tiles == ["s12"]
    asyncio.run(run())


@pytest.mark.parametrize("target,outcomes,expected_scores", [
    (4, [False,False,True,True], [20,20]),
    (16, [False]*8+[True]*8, [20]*8),
    (16, [True]*8+[False]*8+[True]*8, [10]*8+[20]*8),
    (4, [True,True,True,False,False,True,True], [10,10,10,20,20]),
    (4, [True,False,False,False,True,False,True,True,True], [10,20,20,20,20]),
])
def test_scheduled_hands_then_clear_all_draw_debt(target, outcomes, expected_scores):
    async def run():
        state = make_state()
        state.game_server.gamestate_manager.cleanup_game_state_complete = AsyncMock()
        state.run_hu_result_ready_phase = AsyncMock()
        state.max_round = target // 4
        results = iter(outcomes)
        async def hand():
            if next(results):
                state.pending_winners = [{"index":0,"payer":None,"source":"self_draw", "hu_class":"hu_self",
                    "tile":11,"detail":{"base_score":10,"fan_names":["清一色"]}}]
        state._run_hand = hand
        with patch('server.gamestate.game_shanghai.QinghunpengGameState.liuju_ready_wait_seconds',return_value=0), \
             patch('server.gamestate.game_shanghai.QinghunpengGameState.broadcast_result',new=AsyncMock()) as broadcast:
            await state.game_loop_chinese()
        calls = [c.kwargs for c in broadcast.await_args_list]
        wins = [c for c in calls if c["hu_class"] != "liuju"]
        assert [c["hu_score"] for c in wins] == expected_scores
        assert [c["next_status"] for c in calls] == ["round_end_by_ready"]*(len(outcomes)-1) + ["match_end"]
        assert state.completed_win_hands == sum(outcomes) and state.huangfan_count == 0
        rounds = list(state.game_record["game_round"].values())
        assert [r["current_round"] for r in rounds] == list(range(1,len(outcomes)+1))
        debt = 0
        for won, data in zip(outcomes,rounds):
            assert data["detailed_config"]["huangfan_count"] == debt
            debt = max(0,debt-1) if won else debt+1
        assert len(rounds) == len(outcomes) and sum(p.score for p in state.player_list) == 0
    asyncio.run(run())


def test_multi_ron_consumes_one_huangfan_and_one_match_hand():
    async def run():
        state = make_state()
        state.huangfan_count = 2
        state.run_hu_result_ready_phase = AsyncMock()
        state.pending_winners = [{"index":i,"payer":0,"source":"discard","hu_class":a,"tile":41,
                                  "detail":{"base_score":10,"fan_names":["混碰"]}}
                                 for i,a in [(1,"hu_first"),(2,"hu_second")]]
        before = {p.original_player_index:p.score for p in state.player_list}
        assert not await state._settle_hand(before)
        assert [p.score for p in state.player_list] == [-40,20,20,0]
        assert state.huangfan_count == 1 and state.completed_win_hands == 1
        assert state.next_dealer() == 0
    asyncio.run(run())


def test_fourth_chow_allows_same_tile_discard_only_at_fourth_exposed_meld():
    async def run():
        state = make_state()
        player = state.player_list[1]
        player.hand_tiles = [11,11,12,13]
        player.combination_tiles = ["s15","s18","k41"]
        state.player_list[0].discard_tiles = [11]
        assert "chi_right" in state.check_discard_actions(11)[1]
        await state.execute_claim(1,"chi_right")
        assert not player.kuikae_forbidden_tiles
        await state.execute_cut(1,{"TileId":11,"TileIndex":0,"CutClass":False})
        assert player.discard_tiles == [11]
        assert 11 not in player.passed_win_tiles
        state.current_player_index = 2
        assert "hu_third" in state.check_discard_actions(11)[1]
    asyncio.run(run())


def test_pass_ron_blocks_only_same_tile_until_own_draw():
    async def run():
        state = make_state()
        player = state.player_list[1]
        player.hand_tiles = [41,41,42,42,43,43,44,44,45,45,46,46,47]
        state.player_list[0].discard_tiles = [47]
        state.game_status = "waiting_action_after_cut"
        allowed = state.check_discard_actions(47)
        await state.resolve_discard_responses({1:{"action_type":"pass"}}, allowed)
        assert state.score_candidate(1,"discard",47) is None
        assert state.score_candidate(1,"discard",46)["base_score"] == 20
        await state._deal_normal()
        assert not player.passed_win_tiles
    asyncio.run(run())


def test_declined_self_draw_marks_discard_but_does_not_lock_hand():
    async def run():
        state = make_state()
        player = state.player_list[0]
        player.hand_tiles = [11,12,13,14,15,16,17,18,19,45,45,45,41,41]
        await state.execute_cut(0,{"TileId":41,"TileIndex":13,"CutClass":False})
        assert player.discard_tiles == [41] and 41 in player.passed_win_tiles
        assert not player.ready_locked
    asyncio.run(run())


def test_first_turn_flower_replacement_can_then_ron_upper_discard():
    async def run():
        state = make_state()
        player = state.player_list[1]
        player.hand_tiles = [11,12,13,14,15,16,17,18,19,45,45,41,51]
        state.player_list[0].discard_tiles = [41]
        state.tiles_list = [22,45]
        await state._opening_flower_replacement()
        assert 51 in player.hand_tiles
        await state._deal_normal()
        assert state.game_status == "waiting_hand_action" and state.action_dict[1] == ["buhua"]
        assert state.tiles_list == [22,45]
        await state.execute_buhua(1)
        assert state.game_status == "waiting_action_after_cut"
        assert state.action_dict[1] == ["hu_first","pass"]
        assert all(not state.action_dict[i] for i in (0,2,3))
        assert state.tiles_list == [22] and player.normal_draw_count == 0
        await state.resolve_discard_responses({1:{"action_type":"hu_first"}}, state.action_dict)
        assert state.pending_winners[0]["detail"]["base_score"] == 4
    asyncio.run(run())


def test_pung_before_first_draw_forces_flowers_then_discard():
    async def run():
        state = make_state()
        player = state.player_list[2]
        player.hand_tiles = [11,11,12,13,14,15,16,17,41,41,45,51,52]
        state.player_list[0].discard_tiles = [11]
        await state.execute_claim(2,"peng")
        assert state.after_claim_actions()[2] == ["buhua"]
        await state.execute_buhua(2)
        assert state.action_dict[2] == ["buhua"]
        await state.execute_buhua(2)
        assert "cut" in state.action_dict[2] and "buhua" not in state.action_dict[2]
        assert player.huapai_list == [52,51]
        assert player.normal_draw_count == 0
    asyncio.run(run())


def test_opening_direct_kong_replaces_flowers_before_kong_draw():
    async def run():
        state = make_state()
        player = state.player_list[2]
        player.hand_tiles = [11,11,11,12,13,14,15,16,17,41,41,45,51]
        state.player_list[0].discard_tiles = [11]
        state.tiles_list = [22,45,46]
        await state.execute_claim(2,"gang")
        await state._deal_supplement()
        assert state.action_dict[2] == ["buhua"] and state.tiles_list == [22,45,46]
        await state.execute_buhua(2)
        assert state.game_status == "deal_card_after_gang" and state.tiles_list == [22,45]
        await state._deal_supplement()
        assert state.tiles_list == [22] and player.last_drawn_tile == 45
        assert len(player.hand_tiles) == 11
    asyncio.run(run())


def test_last_discard_is_lezi_and_rob_kong_can_be_declined():
    state = make_state()
    state.player_list[1].hand_tiles = [11,12,13,14,15,16,17,18,19,11,12,13,41]
    assert state.score_candidate(1,"discard",41) is None
    state.tiles_list = []
    assert state.score_candidate(1,"discard",41)["base_score"] == 10
    state.tiles_list = [22]
    assert state.check_added_kong_actions(41)[1] == ["hu_first","pass"]


def test_reconnect_preserves_subrule_contract_and_forced_flower_action():
    from ..game_taiwan.boardcast import send_reconnect_game_state, reconnected_send_pending_ask_for_viewer
    async def run():
        state = make_state()
        socket = AsyncMock()
        state.game_server.user_id_to_connection[101] = SimpleNamespace(websocket=socket)
        state.player_list[0].hand_tiles = [11,12,51]
        state.player_list[0].tag_list = ["chengbao_2"]
        state.game_status = "waiting_hand_action"
        state.action_dict = state.check_hand_actions(0)
        await send_reconnect_game_state(state,state.player_list[0])
        payload = next(call.args[0] for call in socket.send_json.await_args_list if "game_info" in call.args[0])
        assert payload["game_info"]["sub_rule"] == rule.SUB_RULE
        assert payload["game_info"]["players_info"][0]["tag_list"] == ["chengbao_2"]
        await reconnected_send_pending_ask_for_viewer(state,101,0)
        assert socket.send_json.await_args.args[0]["ask_hand_action_info"]["action_list"] == ["buhua"]
    asyncio.run(run())
