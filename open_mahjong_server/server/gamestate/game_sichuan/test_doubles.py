"""SBR 2V2: real calculations, settlement, four viewer payloads and record metadata."""
import asyncio
import random
from unittest.mock import AsyncMock, patch

import pytest

from .SichuanGameState import SichuanGameState
from .action_check import check_hepai, refresh_waiting_tiles
from .boardcast import _base_game_info, _build_players_info
from .shunhe import apply_passed_win_shunhe, record_passed_self_draw_shunhe
from ..public.game_record_manager import init_game_record, init_game_round
from ..verifier.host import build_db_manager, build_game_server, build_room_data
from ...game_calculation.sichuan.doubles_rules import match_result, team_scores
from ...response import GameInfo


WIN = [11, 12, 13, 14, 15, 16, 21, 22, 23, 26, 27, 28, 25, 25]
THREE = [11, 12, 13, 14, 15, 16, 21, 22, 23, 31, 31, 31, 29, 29]


def make_state():
    messages = []
    db = build_db_manager()
    server = build_game_server(db, messages=messages)
    room = build_room_data(seed=20261009, room_id="900009", hepai_limit=64)
    room.update(room_rule="sichuan", sub_rule="sichuan/doubles", team_by_seat=[0, 1, 0, 1], blood_battle=False)
    state = SichuanGameState(server, room, server.calculation_service, db, "doubles")
    for i, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = i
    state._reset_round_state()
    for player in state.player_list:
        player.hand_tiles = WIN[:-1]
    state.tiles_list = [19, 29, 39] * 3
    init_game_record(state)
    state._record_rule_profile()
    init_game_round(state)
    server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    return state, messages


def settle(state, winners, *, zimo=False, discarder=1):
    state.current_player_index = winners[0] if zimo else discarder
    state.pending_win = {"type": "zimo" if zimo else "ron", "discarder": discarder, "hepai_tile": 25}
    state.sichuan_hu_results = {i: {"fan": 0, "fan_list": ["平和"], "hepai_tile": 25} for i in winners}
    with patch(__package__ + ".SichuanGameState.asyncio.sleep", new_callable=AsyncMock):
        asyncio.run(state._settle_win())


def test_profile_skips_opening_and_forces_blood_battle_zero_fan():
    state, _ = make_state()
    state._dingque_phase = AsyncMock(side_effect=AssertionError("double must not ask dingque"))
    asyncio.run(state._opening_phase())
    assert state.blood_battle and state.hepai_limit == 0
    assert all(p.dingque_suit == 0 for p in state.player_list)
    assert state.game_record["game_title"]["doubles_rule_profile"]["version"] == "sbr2025-doubles-v1"


@pytest.mark.parametrize("seed", range(12))
def test_random_seating_keeps_partners_opposite_and_bot_identity_separate(seed):
    state, _ = make_state()
    for p in state.player_list:
        p.user_id = 0
    state._shuffle_players(random.Random(seed))
    teams = [p.team_id for p in state.player_list]
    assert teams[0] == teams[2] != teams[1] == teams[3]


@pytest.mark.parametrize("kind", ["handgot", "dianhe", "qianggang"])
@pytest.mark.parametrize("hand,melds,valid", [(WIN, [], True), (THREE, [], False), (WIN[:6] + [31,31], ["k21", "G22"], False)])
def test_winning_requires_missing_suit_including_melds(kind, hand, melds, valid):
    state, _ = make_state()
    player = state.player_list[0]
    player.hand_tiles = hand if kind == "handgot" else hand[:-1]
    player.combination_tiles = melds
    player.shunhe_passed_max_fan = 64
    actions = {i: [] for i in range(4)}
    check_hepai(state, actions, hand[-1], 0, kind)
    assert bool(actions[0]) == valid


def test_three_suits_cannot_tenpai_and_pass_creates_no_shunhe():
    state, _ = make_state()
    state.player_list[0].hand_tiles = THREE[:-1]
    refresh_waiting_tiles(state, 0)
    assert state.player_list[0].waiting_tiles == set()
    assert state._evaluate_liuju_ting(THREE[:-1], [], 0, [11, 21]) == (False, set(), 0, "active")
    state.sichuan_hu_results = {0: {"fan": 8, "is_zimo": True}}
    assert not apply_passed_win_shunhe(state, [0])
    assert not record_passed_self_draw_shunhe(state, 0)
    assert state.player_list[0].shunhe_passed_max_fan is None


@pytest.mark.parametrize("discards,expected", [([], "passive"), ([11, 12], "passive"), ([11, 21], "active")])
def test_flower_pig_uses_original_discards_and_melds(discards, expected):
    state, _ = make_state()
    assert state._classify_hua_zhu([11,21], ["G31"], 0, discards) == expected


def test_partners_first_two_end_settle_all_debts_without_chajiao_or_tax_refund():
    state, messages = make_state()
    for i in (1, 3):
        state.player_list[i].hand_tiles = THREE[:-1]
        state.player_list[i].discard_tiles = [11, 21]
    state._refund_gang_record = lambda record: pytest.fail("no no-ting refund on team early end")
    state.player_list[1].gang_score_records = [{"amount": 9}]
    settle(state, [0])
    assert state.game_status == "deal_card"
    settle(state, [2])
    assert state.game_status == "END" and state.doubles_end_reason == "team_first_two"
    assert [p.score for p in state.player_list] == [1, -2, 1, 0]
    assert state._liuju_final_panel_shown
    results = [m["message"]["show_result_info"] for m in messages if m["type"].endswith("show_result")]
    assert not any(r.get("liuju_step") == "chajiao" for r in results)
    state._record_team_round()
    assert state.team_round_history[0]["score_changes"] == {0: 2, 1: -2}


def test_multi_ron_keeps_third_winner_after_two_same_team():
    state, _ = make_state()
    settle(state, [0, 2, 3], discarder=1)
    assert sum(p.is_hu for p in state.player_list) == 3
    assert [p.score for p in state.player_list] == [1, -3, 1, 1]
    assert len(state.deferred_hu_settlements) == 3


def test_opposing_first_two_continue_and_team_internal_payment_cancels():
    state, _ = make_state()
    settle(state, [0], discarder=2)
    settle(state, [1], discarder=3)
    assert state.game_status == "deal_card"
    state._apply_hu_score_changes(0, 8, False, 2)
    assert team_scores(state.player_list) == {0: 0, 1: 0}


def test_mid_hu_has_no_hand_or_fan_for_teammate_and_snapshot_is_private():
    state, messages = make_state()
    settle(state, [0], zimo=True)
    for entry in messages:
        result = entry["message"].get("show_result_info")
        if result:
            assert "hu_fan" not in result and "hu_score" not in result
            assert "hepai_player_hand" not in result
            if entry["user_id"] != 101:
                assert result["hepai_tile"] == 0
    info = GameInfo(**{**_base_game_info(state), "players_info": _build_players_info(state, 101)})
    assert info.players_info[2].hand_tiles is None
    assert info.players_info[2].team_id == 0
    assert info.sichuan_teams["team_by_player_index"] == [0,1,0,1]


@pytest.mark.parametrize("scores,history,winner,reason", [
    ({0:8,1:-8}, [], 0, "total_score"),
    ({0:0,1:0}, [{"score_changes":{0:-8,1:8}}, {"score_changes":{0:0,1:0}}], 1, "last_positive_board"),
    ({0:0,1:0}, [], None, "draw"),
    ({0:-24,1:-24}, [{"score_changes":{0:-24,1:0}}], None, "draw"),
])
def test_match_result_and_tie_policy(scores, history, winner, reason):
    result = match_result(scores, history)
    assert result["winner_team_id"] == winner and result["reason"] == reason
    assert sum(result["match_points"].values()) == 1


def test_team_match_ranks_ignore_individual_order_and_record_result():
    state, _ = make_state()
    for p, score in zip(state.player_list, [-10, 4, 20, -14]):
        p.score = score
    state._finish_team_match()
    assert [p.record_counter.rank_result for p in state.player_list] == [1,2,1,2]
    assert state.game_record["doubles_result"]["scores"] == {0:10,1:-10}


def test_reconnect_restores_team_and_retired_status_without_partner_hand():
    state, messages = make_state()
    settle(state, [0], zimo=True)
    state.player_list[2].tag_list.append('offline')
    state.reconnected_send_pending_ask = AsyncMock()
    asyncio.run(state.player_reconnect(103))
    snapshot = next(m['message']['game_info'] for m in reversed(messages)
                    if m['user_id'] == 103 and 'game_info' in m['message'])
    assert snapshot['sichuan_teams']['team_by_player_index'] == [0,1,0,1]
    assert snapshot['players_info'][0]['is_hu']
    assert 'hand_tiles' not in snapshot['players_info'][0]
    assert snapshot['players_info'][2]['hand_tiles'] == WIN[:-1]


def test_wall_end_active_penalty_and_passive_no_penalty_are_separate_from_transfers():
    state, _ = make_state()
    state.player_list[0].hand_tiles = THREE[:-1]
    state.player_list[0].discard_origin_tiles = [11]
    state.player_list[0].discard_tiles = [21]
    state.player_list[1].hand_tiles = THREE[:-1]
    state.player_list[1].discard_tiles = [11,12]
    with patch(__package__ + '.SichuanGameState.asyncio.sleep', new_callable=AsyncMock):
        asyncio.run(state._settle_liuju())
    # Two ready hands collect one point each from each flower pig; only active loses 24 extra.
    assert [p.score for p in state.player_list] == [-26,-2,2,2]
    assert sum(p.score for p in state.player_list) == -24
