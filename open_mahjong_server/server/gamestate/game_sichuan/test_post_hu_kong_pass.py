"""JJ 血流保杠过和：四种真实状态机、权威询问与连续行牌回归。"""
import asyncio
from copy import deepcopy

import pytest

from .action_check import (
    _xueliu_post_hu_kong_allowed, check_action_after_cut, check_action_hand_action,
    check_action_jiagang, is_xueliu_required_hu, refresh_waiting_tiles,
    xueliu_ron_preserves_kong,
)
from .boardcast import reconnected_send_pending_ask
from .DuplicateGameState import XueliuDuplicateGameState
from .duplicate_wall import SichuanDuplicateWall, init_duplicate_board
from .shunhe import record_passed_self_draw_shunhe
from .test_action_window import submit
from .test_post_peng_jiagang import make_state as make_single_state
from .wait_action import wait_action
from ..public.game_record_manager import init_game_record, init_game_round
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer
from ..public.sichuan_duplicate import PairedMatch, new_match
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from ...game_calculation.sichuan.duplicate_rules import XUELIU_DUPLICATE, make_board, team_by_seat


MODES = ("xueliu", "xueliu_exchange", "xueliu_doubles", "xueliu_doubles_duplicate")


def make_state(mode):
    if mode != "xueliu_doubles_duplicate":
        return make_single_state(mode)
    messages = []
    db = build_db_manager()
    server = build_game_server(db, messages=messages)
    data = new_match("kong-pass", XUELIU_DUPLICATE, {"open": "910010", "closed": "910011"},
                     [make_board(20261012, 1)], "20261012")
    server.gamestate_manager.sichuan_duplicate_matches = {"kong-pass": PairedMatch(data, lambda *_: None)}
    room = build_room_data(seed=0, room_id="910010")
    room.update(room_rule="sichuan", sub_rule=XUELIU_DUPLICATE, room_type="events",
                team_by_seat=team_by_seat("open"), duplicate_match_id="kong-pass",
                duplicate_table="open", duplicate_key="kong-pass", duplicate_game_id="kong-pass-open")
    state = XueliuDuplicateGameState(server, room, server.calculation_service, db, "kong-pass")
    for index, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = index
    init_duplicate_board(state, data["boards"][0])
    init_game_record(state)
    init_game_round(state)
    server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    return state, messages


def claim_state(mode, *, won=(14,), incoming=12, source=1, rob=False, extra_triplet=False):
    state, messages = make_state(mode)
    state.room_id = "910010"
    state.claim_protection = state.tactical_call = False
    state.tiles_list = [19, 29, 39] * 3
    state.current_player_index = source
    state.game_status = "waiting_action_qianggang" if rob else "waiting_action_after_cut"
    for p in state.player_list:
        p.hand_tiles = []
        p.combination_tiles = []
        p.waiting_tiles = set()
        p.remaining_time = 120
    actor = state.player_list[0]
    actor.hand_tiles = [13, 13, 13, 14, 21, 21 if extra_triplet else 22, 21 if extra_triplet else 23, 27, 28, 29]
    actor.combination_tiles = [] if mode == "xueliu" else ["k25"]
    actor.combination_mask = [] if mode == "xueliu" else [[1, 25, 0, 25, 0, 25]]
    actor.dingque_suit = 3
    actor.huapai_list = list(won)
    actor.has_won = actor.post_hu_lock = bool(won)
    actor.has_draw_slot = False
    actor.win_count = len(won)
    refresh_waiting_tiles(state, 0)
    assert actor.waiting_tiles == {12, 14, 15}
    state.player_list[source].discard_tiles = [incoming]
    state.jiagang_tile = incoming
    if rob:
        state.player_list[source].combination_tiles = [f"g{incoming}"]
        state.player_list[source].combination_mask = [[1, incoming, 3, incoming, 0, incoming, 0, incoming]]
    state.action_dict = check_action_jiagang(state, incoming) if rob else check_action_after_cut(state, incoming)
    return state, messages


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("rob", [False, True])
@pytest.mark.parametrize("won", [(14,), (14, 14), (12,), (15,), (14, 12), (12, 15), ()])
@pytest.mark.parametrize("incoming", [12, 14, 15])
def test_3334_win_history_decides_optional_or_required(mode, rob, won, incoming):
    state, _ = claim_state(mode, won=won, incoming=incoming, rob=rob)
    optional = not won or (set(won) == {14} and incoming in (12, 15))
    assert state.action_dict[0] == (["hu", "pass"] if optional else ["hu"])
    assert is_xueliu_required_hu(state, 0) is (not optional)
    assert _xueliu_post_hu_kong_allowed(state, state.player_list[0], "gang", 13) is (not won or set(won) == {14})


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("suit", [1, 2, 3])
@pytest.mark.parametrize("rank", [3, 4, 6])
def test_exception_is_hand_shape_based_not_special_cased_to_3334(mode, suit, rank):
    state, _ = make_state(mode)
    actor = state.player_list[0]
    triple = suit * 10 + rank
    other = (suit % 3 + 1) * 10
    actor.hand_tiles = [triple] * 3 + [triple + 1, other + 1, other + 2, other + 3, other + 7, other + 8, other + 9]
    actor.combination_tiles = [] if mode == "xueliu" else [f"k{other + 5}"]
    actor.dingque_suit = (suit + 1) % 3 + 1
    actor.has_won = actor.post_hu_lock = True
    actor.has_draw_slot = False
    actor.huapai_list = [triple + 1]
    state.tiles_list = [19, 29, 39]
    for incoming, expected in [(triple - 1, True), (triple + 1, False), (triple + 2, True)]:
        assert xueliu_ron_preserves_kong(state, 0, incoming) is expected


@pytest.mark.parametrize("mode", MODES)
def test_losing_one_of_multiple_legal_kongs_is_enough_and_does_not_mutate(mode):
    state, _ = claim_state(mode, extra_triplet=True)
    actor = state.player_list[0]
    before = deepcopy((actor.hand_tiles, actor.combination_tiles, actor.huapai_list, actor.waiting_tiles))
    assert _xueliu_post_hu_kong_allowed(state, actor, "gang", 13)
    assert _xueliu_post_hu_kong_allowed(state, actor, "gang", 21)
    assert xueliu_ron_preserves_kong(state, 0, 12)
    assert (actor.hand_tiles, actor.combination_tiles, actor.huapai_list, actor.waiting_tiles) == before
    actor.huapai_list.append(12)
    assert not _xueliu_post_hu_kong_allowed(state, actor, "gang", 13)
    assert _xueliu_post_hu_kong_allowed(state, actor, "gang", 21)
    assert not xueliu_ron_preserves_kong(state, 0, 15), "已经失去的杠不能再次作为过和理由"


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("source", [1, 2, 3])
def test_opponent_and_teammate_relationship(mode, source):
    state, _ = claim_state(mode, won=(12,), incoming=14, source=source)
    teammate = source == 2 and "doubles" in mode
    assert state.action_dict[0] == (["hu", "pass"] if teammate else ["hu"])


@pytest.mark.parametrize("mode", MODES)
def test_no_replacement_tile_means_no_kong_exception(mode):
    state, _ = claim_state(mode)
    state.tiles_list = []
    assert check_action_after_cut(state, 12)[0] == ["hu"]
    # 不用牌墙内容预测第四枚能否摸到，相同剩余张数不会影响选择权限。
    for wall in ([13], [39]):
        state.tiles_list = wall
        assert check_action_after_cut(state, 12)[0] == ["hu", "pass"]


def test_duplicate_own_wall_exhausted_does_not_borrow_opponent_wall():
    state, _ = claim_state("xueliu_doubles_duplicate")
    state.tiles_list = SichuanDuplicateWall(state, make_board(20261012, 1))
    wall = state.tiles_list
    while wall.can_draw(state.player_list[0]):
        wall.pop_for_player(state.player_list[0])
    assert wall and not wall.can_draw(state.player_list[0])
    assert check_action_after_cut(state, 12)[0] == ["hu"]


@pytest.mark.parametrize("mode", MODES)
def test_missing_suit_and_old_four_tiles_do_not_create_fake_kong_exception(mode):
    state, _ = claim_state(mode)
    actor = state.player_list[0]
    actor.dingque_suit = 1
    assert not xueliu_ron_preserves_kong(state, 0, 12)
    actor.dingque_suit = 3
    actor.hand_tiles = [15, 15, 15, 15, 16, 17, 21, 22, 23, 25]
    actor.huapai_list = [25]
    assert not xueliu_ron_preserves_kong(state, 0, 15)
    actor.huapai_list = []
    assert not xueliu_ron_preserves_kong(state, 0, 25)


@pytest.mark.parametrize("mode", MODES)
def test_future_added_kong_does_not_create_an_exception_if_waits_survive(mode):
    state, _ = claim_state(mode)
    actor = state.player_list[0]
    actor.hand_tiles = [21, 22, 23, 24, 25, 26, 27]
    actor.combination_tiles = ["k13"] + ([] if mode == "xueliu" else ["k29"])
    actor.huapai_list = [21]
    refresh_waiting_tiles(state, 0)
    assert actor.waiting_tiles == {21, 24, 27}
    assert not xueliu_ron_preserves_kong(state, 0, 24)
    actor.hand_tiles.append(13)
    actor.has_draw_slot = True
    assert _xueliu_post_hu_kong_allowed(state, actor, "jiagang", 13)
    assert not xueliu_ron_preserves_kong(state, 0, 24)


@pytest.mark.parametrize("mode", MODES)
def test_completed_kong_does_not_hide_a_different_future_kong(mode):
    state, _ = claim_state(mode)
    actor = state.player_list[0]
    actor.hand_tiles = [13, 13, 13, 14, 27, 28, 29]
    actor.combination_tiles = ["g25"] + ([] if mode == "xueliu" else ["G19"])
    refresh_waiting_tiles(state, 0)
    assert actor.waiting_tiles == {12, 14, 15}
    assert check_action_after_cut(state, 12)[0] == ["hu", "pass"]


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("rob", [False, True])
@pytest.mark.parametrize("choice", ["pass", "hu", "timeout"])
def test_optional_ask_reconnect_and_choice_are_authoritative(mode, rob, choice):
    async def run():
        state, messages = claim_state(mode, rob=rob)
        actor = state.player_list[0]
        before = list(actor.huapai_list), list(actor.hand_tiles), list(actor.combination_tiles)
        if choice == "timeout":
            actor.remaining_time = state.step_time = 0
        try:
            await state.broadcast_ask_other_action()
            await drain_viewer(state, 0)
            await reconnected_send_pending_ask(state, actor.user_id)
            own = [m["message"]["ask_other_action_info"] for m in messages
                   if m["user_id"] == actor.user_id and "ask_other_action_info" in m["message"]]
            assert len(own) == 2
            assert all(info["action_list"] == ["hu", "pass"] for info in own)
            if choice != "timeout":
                await submit(state, 0, choice)
            await asyncio.wait_for(wait_action(state), 1)
            if choice == "hu":
                assert state.game_status == "settle_win"
                actor.shunhe_passed_max_fan = 1
                actor.tag_list.append("shunhe_1")
                await state._settle_win()
                assert actor.huapai_list == [14, 12]
                assert actor.shunhe_passed_max_fan is None
                assert not any(t.startswith("shunhe_") for t in actor.tag_list)
                assert not _xueliu_post_hu_kong_allowed(state, actor, "gang", 13)
            else:
                assert state.game_status == ("deal_card_after_gang" if rob else "deal_card")
                assert (actor.huapai_list, actor.hand_tiles, actor.combination_tiles) == before
                assert actor.shunhe_passed_max_fan is None
                assert not any(t.startswith("shunhe_") for t in actor.tag_list)
                assert _xueliu_post_hu_kong_allowed(state, actor, "gang", 13)
                # 随后对手打此前和过的四万，仍立即必和。
                state.current_player_index = 1
                state.game_status = "waiting_action_after_cut"
                state.player_list[1].discard_tiles = [14]
                state.action_dict = check_action_after_cut(state, 14)
                assert state.action_dict[0] == ["hu"]
                await state.broadcast_ask_other_action()
                await asyncio.wait_for(wait_action(state), .5)
                assert state.game_status == "settle_win"
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("connection", ["online", "offline", "bot"])
@pytest.mark.parametrize("rob", [False, True])
def test_mandatory_win_cannot_be_bypassed_by_forged_pass(mode, connection, rob):
    async def run():
        state, messages = claim_state(mode, won=(12,), incoming=14, rob=rob)
        actor = state.player_list[0]
        if connection == "offline":
            actor.tag_list.append("offline")
        elif connection == "bot":
            actor.user_id = 2
        try:
            before = actor.remaining_time
            await state.broadcast_ask_other_action()
            if connection == "online":
                await drain_viewer(state, 0)
                ask = next(m["message"]["ask_other_action_info"] for m in messages
                           if m["user_id"] == actor.user_id and "ask_other_action_info" in m["message"])
                assert ask["action_list"] == []
                await reconnected_send_pending_ask(state, actor.user_id)
                assert messages[-1]["message"]["ask_other_action_info"]["action_list"] == []
                await submit(state, 0, "pass")
            await asyncio.wait_for(wait_action(state), .5)
            assert state.game_status == "settle_win"
            assert set(state.sichuan_hu_results) == {0}
            assert actor.remaining_time == before
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("choice", ["pass", "hu"])
def test_optional_and_mandatory_multi_ron_resolve_independently(mode, choice):
    async def run():
        state, _ = claim_state(mode)
        other = state.player_list[2]
        other.hand_tiles = list(state.player_list[0].hand_tiles)
        other.combination_tiles = list(state.player_list[0].combination_tiles)
        other.huapai_list = [12]
        other.has_won = other.post_hu_lock = True
        other.waiting_tiles = {12, 14, 15}
        other.dingque_suit = 3
        state.action_dict = check_action_after_cut(state, 12)
        assert state.action_dict[0] == ["hu", "pass"]
        assert state.action_dict[2] == ["hu"]
        try:
            await state.broadcast_ask_other_action()
            pending = asyncio.create_task(wait_action(state))
            await asyncio.sleep(.01)
            assert not pending.done()
            await submit(state, 0, choice)
            await asyncio.wait_for(pending, 1)
            assert set(state.sichuan_hu_results) == ({0, 2} if choice == "hu" else {2})
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
def test_self_draw_stays_optional_and_old_shunhe_cannot_block_post_hu_ron(mode):
    state, _ = claim_state(mode)
    actor = state.player_list[0]
    actor.shunhe_passed_max_fan = 999
    actor.tag_list.append("shunhe_999")
    assert check_action_after_cut(state, 12)[0] == ["hu", "pass"]
    assert check_action_after_cut(state, 14)[0] == ["hu"]
    state.current_player_index = 0
    state.game_status = "waiting_hand_action"
    actor.hand_tiles.append(12)
    actor.has_draw_slot = True
    assert set(check_action_hand_action(state, 0)[0]) == {"cut", "hu_self"}
    assert not record_passed_self_draw_shunhe(state, 0)
    actor.has_won = actor.post_hu_lock = False
    actor.hand_tiles.pop()
    actor.has_draw_slot = False
    state.current_player_index = 1
    assert "hu" not in check_action_after_cut(state, 14)[0], "首次和牌前维持顺和"


@pytest.mark.parametrize("mode", MODES)
def test_rule_record_snapshot_includes_exception(mode):
    state, _ = make_state(mode)
    state._record_rule_profile()
    profile = state.game_record["game_title"]["xueliu_rule_profile"]
    assert profile["post_hu_opponent_ron_required"] is True
    assert profile["post_hu_ron_kong_exception"] == "loses_legal_kong"
    assert profile["version"] == ("xueliu-single-table-2v2-v3" if "doubles" in mode else 5)
    if "doubles" in mode:
        assert state.game_record["game_title"]["doubles_rule_profile"]["post_hu_ron_kong_exception"] == "loses_legal_kong"


@pytest.mark.parametrize("mode", ["standard", "doubles"])
def test_sbr_is_not_subject_to_bloodflow_mandatory_rule(mode):
    state, _ = make_single_state(mode)
    actor = state.player_list[0]
    actor.has_won = True
    actor.huapai_list = [14]
    assert not xueliu_ron_preserves_kong(state, 0, 12)
    state.current_player_index = 1
    state.game_status = "waiting_action_after_cut"
    state.action_dict = {0: ["hu"], 1: [], 2: [], 3: []}
    assert not is_xueliu_required_hu(state, 0)


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("connection", ["offline", "smart_bot", "draw_bot"])
@pytest.mark.parametrize("rob", [False, True])
def test_optional_hu_uses_existing_bot_or_offline_policy(mode, connection, rob):
    async def run():
        state, _ = claim_state(mode, rob=rob)
        state.bot_speed = "instant"
        actor = state.player_list[0]
        if connection == "offline":
            actor.tag_list.append("offline")
        else:
            actor.user_id = 2 if connection == "smart_bot" else 0
        assert state.action_dict[0] == ["hu", "pass"]
        try:
            await state.broadcast_ask_other_action()
            await asyncio.wait_for(wait_action(state), 3)
            # 规则允许选择；牌效机器人沿用和对手的策略，掉线托管保留手牌。
            if connection != "smart_bot":
                assert state.game_status == ("deal_card_after_gang" if rob else "deal_card")
                assert actor.huapai_list == [14]
                assert actor.shunhe_passed_max_fan is None
            else:
                assert state.game_status == "settle_win"
                assert set(state.sichuan_hu_results) == {0}
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("kind", ["gang", "angang"])
def test_pass_twice_then_kong_and_win_replacement(mode, kind):
    async def run():
        state, messages = claim_state(mode)
        actor = state.player_list[0]
        try:
            for incoming in (12, 15):
                state.current_player_index = 1
                state.game_status = "waiting_action_after_cut"
                state.player_list[1].discard_tiles = [incoming]
                state.action_dict = check_action_after_cut(state, incoming)
                await state.broadcast_ask_other_action()
                await submit(state, 0, "pass")
                await asyncio.wait_for(wait_action(state), 1)
                assert state.game_status == "deal_card"
                assert actor.huapai_list == [14]
            if kind == "gang":
                state.game_status = "waiting_action_after_cut"
                state.player_list[1].discard_tiles = [13]
                state.action_dict = check_action_after_cut(state, 13)
                await state.broadcast_ask_other_action()
            else:
                state.current_player_index = 0
                state.game_status = "waiting_hand_action"
                actor.hand_tiles.append(13)
                actor.has_draw_slot = True
                state.action_dict = check_action_hand_action(state, 0)
                await state.broadcast_ask_hand_action()
            assert kind in state.action_dict[0]
            await submit(state, 0, kind, target_tile=13)
            await asyncio.wait_for(wait_action(state), 1)
            assert state.game_status == "deal_card_after_gang"
            assert actor.waiting_tiles == {14}
            assert ("g13" if kind == "gang" else "G13") in actor.combination_tiles
            state.tiles_list = [14, 29]
            actor.get_tile(state.tiles_list)
            state.game_status = "waiting_hand_action"
            state.action_dict = check_action_hand_action(state, 0, is_get_gang_tile=True)
            assert "hu_self" in state.action_dict[0]
            await state.broadcast_ask_hand_action()
            await submit(state, 0, "hu_self")
            await asyncio.wait_for(wait_action(state), 1)
            assert state.game_status == "settle_win"
            await state._settle_win()
            assert actor.huapai_list == [14, 14]
            assert actor.has_won and actor.post_hu_lock and not actor.is_hu
            assert actor.waiting_tiles == {14}
            assert sum(p.score for p in state.player_list) == 0
            assert any(m["message"].get("do_action_info", {}).get("action_list") == [kind] for m in messages)
            assert state.game_record["game_round"][f"round_index_{state.round_index}"]["action_ticks"]
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())


@pytest.mark.parametrize("mode", MODES)
@pytest.mark.parametrize("won,optional", [((14,), True), ((12,), False)])
def test_tactical_force_pass_only_available_for_optional_hu(mode, won, optional):
    async def run():
        state, _ = claim_state(mode, won=won)
        state.tactical_call = True
        state.action_dict = check_action_after_cut(state, 12)
        assert state.action_dict[0] == (["hu", "pass", "force_pass"] if optional else ["hu"])
        try:
            await state.broadcast_ask_other_action()
            await submit(state, 0, "force_pass")
            await asyncio.wait_for(wait_action(state), 1)
            assert state.game_status == ("deal_card" if optional else "settle_win")
            assert state.player_list[0].shunhe_passed_max_fan is None
        finally:
            await cancel_auxiliary_tasks(state)
    asyncio.run(run())
