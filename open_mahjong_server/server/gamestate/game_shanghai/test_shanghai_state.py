import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .ShanghaiGameState import ShanghaiGameState
from ...game_calculation.shanghai import qiaoma
from ...room.room_validators import ShanghaiRoomValidator
from ..public.game_record_manager import init_game_record, init_game_round


def make_state(**room_overrides):
    server = SimpleNamespace(user_id_to_connection={}, gamestate_manager=SimpleNamespace(), room_manager=SimpleNamespace())
    state = ShanghaiGameState(server, {
        "room_id": "123456", "player_list": [101, 102, 103, 104],
        "round_timer": 0, "step_timer": 0, "game_round": 1,
        "tips": True, "room_type": "custom", "allow_spectator": False,
        **room_overrides,
    }, None, None, "qiaoma-test-state")
    for index, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = index
    state.tiles_list = [11, 12, 13, 21, 22, 23]
    init_game_record(state)
    init_game_round(state)
    return state


def lock(player, hand, melds=()):
    player.hand_tiles = list(hand)
    player.combination_tiles = list(melds)
    player.declared_ready = player.ready_locked = True
    player.locked_waits = qiaoma.waits(hand, melds)


def test_wall_has_144_tiles_including_twenty_flowers():
    state = make_state()
    state.master_seed = 1729
    state.init_tiles()
    wall = state.tiles_list + [t for p in state.player_list for t in p.hand_tiles]
    assert len(wall) == 144
    assert len(state.player_list[0].hand_tiles) == 14
    assert all(len(p.hand_tiles) == 13 for p in state.player_list[1:])
    assert sum(t in qiaoma.FLOWERS for t in wall) == 20
    assert Counter(wall)[45] == 4


@pytest.mark.parametrize("flower", qiaoma.FLOWERS)
def test_every_flower_forces_replacement(flower):
    state = make_state()
    state.player_list[0].hand_tiles = [11, 12, flower]
    assert state.check_hand_actions(0)[0] == ["buhua"]


def test_can_declare_with_insufficient_flowers_after_chow():
    state = make_state()
    player = state.player_list[0]
    player.combination_tiles = ["s12"]
    player.hand_tiles = [21, 22, 23, 31, 32, 33, 17, 18, 19, 44, 11]
    player.huapai_list = [51]
    player.kuikae_forbidden_tiles = {13}
    assert state.ready_candidate_cuts(0)[11] == [44]
    assert "riichi_cut" in state.after_claim_actions()[0]
    assert "angang" not in state.after_claim_actions()[0]


def test_forced_win_has_no_pass_or_discard():
    state = make_state()
    lock(state.player_list[0], [11, 12, 13, 21, 22, 23, 31, 32, 33, 17, 18, 19, 44])
    state.player_list[0].hand_tiles.append(44)
    state.player_list[0].last_drawn_tile = 44
    assert state.check_hand_actions(0)[0] == ["hu_self"]


def test_no_chow_or_pung_after_declaration():
    state = make_state()
    lock(state.player_list[1], [11, 11, 12, 13, 14, 21, 22, 23, 31, 32, 33, 44, 44])
    assert not {"peng", "chi_left", "chi_mid", "chi_right"}.intersection(state.check_discard_actions(11)[1])


def test_locked_hand_cannot_change_discard():
    state = make_state()
    player = state.player_list[0]
    lock(player, [11, 12, 13, 21, 22, 23, 31, 32, 33, 17, 18, 19, 44])
    player.hand_tiles.append(22)
    player.last_drawn_tile = 22
    player.has_draw_slot = True
    before = list(player.hand_tiles)
    asyncio.run(state.execute_cut(0, {"TileId": 11}))
    assert player.hand_tiles == before


def test_added_kong_required_only_with_flowers():
    state = make_state()
    player = state.player_list[0]
    lock(player, [21, 22, 23, 31, 32, 33, 17, 18, 19, 44], ["k11"])
    player.hand_tiles.append(11)
    player.last_drawn_tile = 11
    player.has_draw_slot = True
    assert state._declared_ready_auto_jiagang_tile(0) is None
    player.huapai_list = [45]
    assert state._declared_ready_auto_jiagang_tile(0) == 11


def test_locked_direct_kong_preserves_waits():
    state = make_state()
    player = state.player_list[1]
    lock(player, [11, 11, 11, 21, 22, 23, 31, 32, 33, 17, 18, 19, 44])
    assert state._kong_preserves_waits(1, 11, "g")
    assert "gang" in state.check_discard_actions(11)[1]
    player.locked_waits = {21}
    assert "gang" not in state.check_discard_actions(11)[1]


def test_contracts_symmetric_and_fourth_meld_can_release():
    state = make_state()
    player = state.player_list[1]
    player.combination_tiles = ["s12", "s15", "s18"]
    player.claim_sources = [0, 0, 0]
    state._refresh_contracts()
    assert state.contract_partners(1) == {0}
    assert state.contract_partners(0) == {1}
    player.combination_tiles.append("s22")
    player.claim_sources.append(2)
    state._refresh_contracts()
    assert state.contracts == set()
    player.claim_sources[-1] = 0
    state._refresh_contracts()
    assert state.contracts == {(0, 1)}


def test_declared_giver_is_excluded_from_contract_counts():
    state = make_state()
    state.player_list[0].declared_ready = True
    receiver = state.player_list[1]
    receiver.combination_tiles = ["k11", "k21", "k31"]
    receiver.claim_sources = [0, 0]
    state._remember_claim_liability(1, 0, 31)
    assert receiver.claim_sources == [0, 0, None]
    assert not state.contracts


def test_winner_dealer_and_multiple_ron_discarder_dealer():
    state = make_state()
    assert state.next_dealer() == 0
    state.pending_winners = [{"index": 2, "payer": 1}]
    assert state.next_dealer() == 2
    state.pending_winners.append({"index": 3, "payer": 1})
    assert state.next_dealer() == 1


def test_room_rejects_unimplemented_subrule():
    args = dict(room_name="上海敲麻", game_round=1, round_timer=30, step_timer=3)
    assert ShanghaiRoomValidator(**args).sub_rule == qiaoma.SUB_RULE
    with pytest.raises(ValueError):
        ShanghaiRoomValidator(**args, sub_rule="shanghai/unknown")


def test_opening_replacement_chain_preserves_concealed_hand_count():
    async def run():
        state = make_state()
        state.player_list[0].hand_tiles = [11] * 12 + [45, 51]
        state.tiles_list = [21, 22, 47, 46]
        await state._opening_flower_replacement()
        assert len(state.player_list[0].hand_tiles) == 14
        assert sorted(state.player_list[0].huapai_list) == [45, 46, 47, 51]
        assert state.player_list[0].opening_flowers_done
    with patch('server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action', new=AsyncMock()), \
         patch('server.gamestate.game_shanghai.ShanghaiGameState.broadcast_do_action', new=AsyncMock()):
        asyncio.run(run())


def test_multiple_ron_is_forced_even_when_clients_pass():
    async def run():
        state = make_state()
        state.player_list[0].discard_tiles = [44]
        lock(state.player_list[1], [11, 12, 13, 21, 22, 23, 31, 32, 33, 17, 18, 19, 44])
        lock(state.player_list[2], [14, 15, 16, 24, 25, 26, 34, 35, 36, 41, 41, 41, 44])
        allowed = state.check_discard_actions(44)
        assert allowed[1] == ["hu_first"] and allowed[2] == ["hu_second"]
        await state.resolve_discard_responses({1: {"action_type": "pass"}}, allowed)
        assert [item["index"] for item in state.pending_winners] == [1, 2]
        assert state.next_dealer() == 0
        assert state.game_status == "END"
    asyncio.run(run())


def test_robbed_added_kong_restores_pung_and_consumes_fourth_tile():
    async def run():
        state = make_state()
        source = state.player_list[0]
        source.hand_tiles = [14, 15, 16, 24, 25, 26, 34, 35, 36, 41, 13]
        source.combination_tiles = ["k13"]
        source.combination_mask = [[1, 13, 0, 13, 0, 13]]
        source.last_drawn_tile = 13
        source.has_draw_slot = True
        lock(state.player_list[1], [11, 12, 21, 22, 23, 31, 32, 33, 17, 18, 19, 44, 44])
        await state.execute_jiagang(0, 13)
        assert state.game_status == "waiting_action_qianggang"
        assert state.action_dict[1] == ["hu_first"]
        await state.resolve_rob_kong_responses({}, state.action_dict)
        assert source.combination_tiles == ["k13"]
        assert 13 not in source.hand_tiles
        assert state.pending_winners[0]["detail"]["base_score"] == 44
        assert "抢杠" in state.pending_winners[0]["detail"]["fan_names"]
    with patch('server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action', new=AsyncMock()):
        asyncio.run(run())


def test_flower_on_last_wall_tile_is_draw_not_unreplaced_discard():
    async def run():
        state = make_state()
        state.tiles_list = [45]
        state.player_list[1].hand_tiles = [11, 12, 13, 21, 22, 23, 31, 32, 33, 17, 18, 19, 44]
        await state._deal_normal()
        assert state.game_status == "END"
        assert state.player_list[1].huapai_list == [45]
        assert 45 not in state.player_list[1].hand_tiles
    with patch('server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action', new=AsyncMock()):
        asyncio.run(run())


def test_pung_pass_restriction_clears_on_own_draw():
    async def run():
        state = make_state()
        player = state.player_list[1]
        player.hand_tiles = [11, 11, 12, 13, 14, 21, 22, 23, 31, 32, 33, 44, 44]
        player.passed_pungs = {11}
        assert "peng" not in state.check_discard_actions(11)[1]
        await state._deal_normal()
        assert not player.passed_pungs
    with patch('server.gamestate.game_taiwan.TaiwanGameState.broadcast_do_action', new=AsyncMock()):
        asyncio.run(run())


def test_concealed_kong_shows_middle_two_tiles():
    assert make_state().build_concealed_kong_mask([11] * 4) == [2, 11, 0, 11, 0, 11, 2, 11]


def test_claim_deadline_uses_entire_room_thinking_bank_and_step():
    from ..game_taiwan.wait_action import _build_ask_deadlines
    state = make_state()
    state.game_status = "waiting_action_after_cut"
    state.player_list[1].remaining_time = 30
    assert _build_ask_deadlines(state, {1: ["peng"]}, 5, 100)[1] == 135


def test_first_and_later_flower_claims_share_room_budget_and_reconnect_origin():
    from ..game_taiwan.wait_action import _build_ask_deadlines
    state = make_state()
    state.game_status = "waiting_action_after_cut"
    state.step_time = 5
    player = state.player_list[1]
    player.remaining_time = 30
    state.opening_claim_exempt.add(1)
    assert _build_ask_deadlines(state, {1: ["peng"]}, 5, 100)[1] == 135
    assert state.claim_clock(player) == (30, 5)
    state.opening_claim_exempt.clear()
    assert state.claim_clock(player) == (30, 5)
    state._ask_delivered_at = {1: 100}
    with patch('server.gamestate.game_shanghai.action_timing.time.time', return_value=101.2):
        assert state.claim_clock(player, reconnecting=True) == (30, 4)


def test_expired_full_budget_consumes_thinking_bank():
    async def run():
        state = make_state()
        state.game_status = "waiting_action_after_cut"
        state.step_time = 5
        state.action_dict = {1: ["peng", "pass"]}
        state.player_list[1].remaining_time = 30
        # The received claim is already expired, so this test needs no real sleep.
        state._ask_delivered_at = {1: 0}
        responses, _ = await state.collect_action_responses()
        assert responses == {}
        assert state.player_list[1].remaining_time == 0
    asyncio.run(run())


def test_chow_forbidden_tiles_are_in_private_protocol():
    state = make_state()
    state.player_list[0].kuikae_forbidden_tiles = {13, 16}
    assert state.build_private_hand_action_info(0)["forbidden_cut_tiles"] == [13, 16]
