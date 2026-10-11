"""血流同局逐步累计；覆盖真实广播、连续和牌、查叫、重连与轮转。"""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .boardcast import _build_players_info, broadcast_do_action, broadcast_result
from .test_xueliu_integration import full_state, WIN_HAND
from .test_xueliu_doubles import make_state as doubles_state, WIN


@pytest.fixture(params=["sichuan/xueliu", "sichuan/xueliu_exchange", "sichuan/xueliu_doubles"])
def game(request):
    state, messages = doubles_state() if request.param.endswith("doubles") else full_state()
    if request.param == "sichuan/xueliu_exchange":
        state.sub_rule = request.param
        state._configure_rule()
    return state, messages


def assert_rows(state, expected_rows):
    for player in state.player_list:
        assert list(map(int, player.score_history)) == [row[player.original_player_index] for row in expected_rows]
        assert player.round_number_history == list(range(1, len(expected_rows) + 1))
        assert sum(map(int, player.score_history)) == player.score


def test_repeated_win_broadcasts_update_one_round_and_continue_snapshot(game):
    state, messages = game
    _build_players_info(state, state.player_list[0].user_id)
    assert_rows(state, [[0, 0, 0, 0]])
    for _ in range(2):
        player = state.player_list[0]
        player.hand_tiles = list(WIN if state.xueliu_exchange else WIN_HAND)
        player.has_draw_slot = True
        player.waiting_tiles = {25}
        state.current_player_index = 0
        state.sichuan_hu_results = {0: {"fan": 2, "fan_list": ["基本胡"], "hepai_tile": 25}}
        state.pending_win = {"type": "zimo"}
        with patch(__package__ + ".XueliuGameState.asyncio.sleep", new_callable=AsyncMock):
            asyncio.run(state._settle_win())
        scores = [p.score for p in state.player_list]
        assert_rows(state, [scores])
        for msg in messages:
            if msg["type"].endswith("/xueliu_continue"):
                for p in msg["message"]["game_info"]["players_info"]:
                    assert p["round_number_history"] == [1]
                    assert list(map(int, p["score_history"])) == [p["score"]]
    assert scores[0] > 0
    first_round = scores.copy()
    state.current_round = 2
    _build_players_info(state, None)
    assert_rows(state, [first_round, [0, 0, 0, 0]])
    for i, player in enumerate(state.player_list):
        player.score += [6, -2, -2, -2][i]
    _build_players_info(state, None)
    assert_rows(state, [first_round, [6, -2, -2, -2]])


def test_kong_refund_round_rotation_and_reconnect_preserve_totals(game):
    state, _ = game
    # 弃三张血流的杠不即时计分，使用真实规则产生的分变（可为 0）。
    changes = state._record_gang_score(0, 11, "xiayu2")
    asyncio.run(broadcast_do_action(state, ["angang"], 0, gang_score_changes=changes))
    first = [p.score for p in state.player_list]
    assert_rows(state, [first])
    if state.player_list[0].gang_score_records:
        state._refund_gang_record(state.player_list[0].gang_score_records[-1])
    asyncio.run(broadcast_result(state, "hu_first", player_to_score={p.player_index: p.score for p in state.player_list}))
    first = [p.score for p in state.player_list]
    assert_rows(state, [first])
    # 重复下发快照不增加一行；前局封存后，按玩家身份保存分差。
    for _ in range(2):
        _build_players_info(state, state.player_list[0].user_id)
        assert_rows(state, [first])
    state.current_round = 2
    state.player_list = state.player_list[1:] + state.player_list[:1]
    for seat, player in enumerate(state.player_list):
        player.player_index = seat
        player.score += 6 if player.original_player_index == 0 else -2
    for _ in range(2):
        snapshot = _build_players_info(state, state.player_list[0].user_id)
        assert all(p["round_number_history"] == [1, 2] for p in snapshot)
        assert_rows(state, [first, [6, -2, -2, -2]])


def test_wall_end_merges_chajiao_into_existing_live_round(game):
    state, messages = game
    for i, player in enumerate(state.player_list):
        player.score = [6, -2, -2, -2][i]
        player.hand_tiles = list((WIN if state.xueliu_exchange else WIN_HAND)[:-1])
        player.dingque_suit = 3
    # 一家未听，其余听牌；调用真实查叫流程，前面的和牌分仍留在同一行。
    state.player_list[3].hand_tiles = [11, 13, 15, 17, 19, 21, 23, 25, 27, 29]
    with patch(__package__ + ".XueliuGameState.asyncio.sleep", new_callable=AsyncMock):
        asyncio.run(state._settle_liuju())
    final_scores = [p.score for p in state.player_list]
    assert_rows(state, [final_scores])
    assert final_scores != [6, -2, -2, -2]
    results = [m["message"]["show_result_info"] for m in messages if m["type"].endswith("/show_result")]
    assert results[-1]["liuju_status_final"]
    _build_players_info(state, None)
    assert_rows(state, [final_scores])


def test_blood_battle_history_path_is_unchanged():
    from .test_action_window import make_state
    state, _ = make_state("standard")
    _build_players_info(state, None)
    assert all(p.score_history == [] and p.round_number_history == [] for p in state.player_list)
