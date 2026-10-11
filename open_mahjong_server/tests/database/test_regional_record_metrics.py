import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from test_subrule_statistics import db
from test_subrule_realtime import prepare
from server.database.get_rule_stats import get_custom_rule_history
from server.database.taiwan.store_taiwan import store_taiwan_game_record


def identify_players(state, rotation):
    for player in state.player_list:
        player.original_player_index = (player.player_index + rotation) % 4
        player.user_id = 10000101 + player.original_player_index
        player.username = f"regional-{player.original_player_index}"
    return {player.original_player_index: player.score for player in state.player_list}


def nanchang_record(rotation, win):
    from server.gamestate.game_nanchang.test_state import state, NORMAL
    s = state()
    before = identify_players(s, rotation)
    s.start_game_recording()
    s.start_round_recording()
    if win:
        player = s.player_list[2]
        player.hand_tiles = NORMAL.copy()
        player.has_draw_slot = True
        s.current_player_index = 2
        s.settle_win(2, "self_draw", 41)
    else:
        player = s.player_list[1]
        player.hand_tiles = [21] * 4 + [11, 12, 13, 31, 32, 33, 41, 41, 45, 45]
        player.has_draw_slot = True
        s.commit_kong(1, 21, "angang")
        s.tiles_list = []
        s.draw_for(1)
    s.finalize_round_recording()
    return s, before, {2} if win else set(), {2} if win else set(), {}


def nanjing_record(rotation, win):
    from server.gamestate.game_nanjing.test_state import turn, act, PLAIN
    s = turn(PLAIN[:-1] + [45])
    before = identify_players(s, rotation)
    s.player_list[0].huapai_list = [45] * 3
    if win:
        s.tiles_list[-1] = 22
        for index in (1, 2):
            s.player_list[index].hand_tiles = PLAIN[:-1]
    s.start_game_recording()
    s.start_round_recording()
    act(s, action_type="buhua")
    if win:
        act(s, action_type="cut", TileId=22, cutClass=True)
        act(s, {1: "hu", 2: "hu"})
    else:
        s.discard_log = [(1, 18), (2, 18), (3, 18)]
        s.player_list[0].hand_tiles[-1] = 18
        s.player_list[0].discard_origin_tiles = [18] * 3
        s.open_action_window(s.begin_turn(0))
        act(s, action_type="cut", TileId=18, cutClass=True)
        act(s)
        s.end_draw()
    s.finalize_round_recording()
    s.finalize_game_recording()
    return s, before, {1, 2} if win else set(), set(), {0: 2} if win else {}


def wuhan_record(rotation, win):
    from server.gamestate.game_wuhan.test_flow import make_state, indicator, hand, draw, PURE
    from server.gamestate.public.game_record_manager import init_game_record, init_game_round
    s = make_state()
    indicator(s)
    s.current_round = 4
    before = identify_players(s, rotation)
    player = hand(s, 0, PURE, ["s12"])
    player.huapai_list = [37] * 3 + [38] * 3 + [45]
    for other in s.player_list[1:]:
        other.huapai_list = [45]
    init_game_record(s)
    init_game_round(s)
    draw(player, 29)
    asyncio.run(s.execute_cut(0, dict(TileId=29, IsMoCut=True)))
    assert s.xunmu == 2
    asyncio.run(s._check_bonus(0))
    if win:
        draw(player, 15)
        s.result_dict["hu_self"] = s.score_candidate(0, "self_draw")
        s.accept_self_draw(0)
    with patch("server.gamestate.game_wuhan.WuhanGameState.asyncio.sleep", new=AsyncMock()):
        asyncio.run(s._settle_hand(before))
    return s, before, {0} if win else set(), {0} if win else set(), {}


@pytest.mark.parametrize("builder", [nanchang_record, nanjing_record, wuhan_record],
                         ids=["nanchang", "nanjing", "wuhan"])
@pytest.mark.parametrize("rotation", range(4))
@pytest.mark.parametrize("win", [True, False], ids=["win", "draw"])
def test_generated_regional_record_publishes_exact_metrics(db, builder, rotation, win):
    prepare(db)
    state, before, winners, self_draws, deal_ins = builder(rotation, win)
    rule = state.game_record["game_title"]["rule"]
    sub_rule = state.game_record["game_title"]["sub_rule"]
    game_id = store_taiwan_game_record(db, state.game_record, state.player_list, "custom", "1/4")
    assert game_id
    for player in state.player_list:
        expected = (player.score - before[player.original_player_index],
                    int(player.player_index in winners), int(player.player_index in self_draws),
                    deal_ins.get(player.player_index, 0), None, player.record_counter.win_turn,
                    player.record_counter.win_score, player.record_counter.fangchong_score)
        rows = db.sql("SELECT total_round_score,win_count,self_draw_count,deal_in_count,fan_stats,"
                      "total_win_turn,total_fan_score,total_fangchong_score "
                      "FROM game_player_metrics WHERE game_id=%s AND user_id=%s", (game_id, player.user_id))
        assert rows == [expected]
        history = get_custom_rule_history(db, player.user_id, rule)
        assert len(history) == 1
        assert history[0]["sub_rule"] == sub_rule
        assert history[0]["total_round_score"] == expected[0]
        assert "fan_stats" not in history[0]
    assert sum(player.score for player in state.player_list) == sum(before.values())
