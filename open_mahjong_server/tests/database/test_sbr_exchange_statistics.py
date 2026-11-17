import asyncio

from test_subrule_realtime import prepare
from test_subrule_statistics import db
from server.database.get_rule_stats import get_custom_rule_history
from server.database.sichuan.store_sichuan import store_sichuan_game_record
from server.database.subrule_fans import collect_subrule_fans
from server.gamestate.game_sichuan.test_sbr_exchange import make_state
from server.gamestate.game_sichuan.test_xueliu_doubles_full_match import play_match
from server.game_calculation.sichuan.xueliu_rules import SBR_EXCHANGE_SUB_RULE


def test_routed_exchange_match_persists_raw_fans_and_statistics_per_seat(db):
    prepare(db)
    state, _ = asyncio.run(play_match(51, make_state))
    record = state.game_record
    gid = store_sichuan_game_record(db, record, state.player_list, "custom", "1/4")
    assert gid
    rows = db.sql("SELECT user_id,sub_rule,total_rounds,win_count,total_fan_score,fan_stats "
                  "FROM game_player_metrics WHERE game_id=%s", (gid,))
    assert len(rows) == 4
    players = {p.user_id: p for p in state.player_list}
    for uid, sub, rounds, wins, fan, counts in rows:
        player = players[uid]
        assert sub == SBR_EXCHANGE_SUB_RULE and rounds == 4
        assert wins == player.record_counter.zimo_times + player.record_counter.dianhe_times
        assert fan == player.record_counter.win_score
        assert counts == collect_subrule_fans(record, player.original_player_index, sub)
        history = get_custom_rule_history(db, uid, "sichuan_xuezhan_exchange")
        assert len(history) == 1 and history[0]["total_games"] == 1 and "fan_stats" in history[0]
        assert get_custom_rule_history(db, uid, "sichuan") == []
