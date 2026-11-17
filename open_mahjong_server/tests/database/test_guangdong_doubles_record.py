from functools import partial

from test_subrule_realtime import prepare
from test_subrule_statistics import db

from server.database.taiwan.store_taiwan import store_taiwan_game_record
from server.gamestate.game_guangdong.test_doubles import PROFILE, presentation
from server.gamestate.game_guangdong.test_doubles_match import play_match


def test_eight_board_record_persists_scores_and_fans_once(db, monkeypatch):
    prepare(db)
    db.store_guangdong_game_record = partial(store_taiwan_game_record, db)
    gs, _ = play_match(monkeypatch, db)
    gid = gs.game_record["game_title"]["cloud_game_id"]
    assert db.sql("SELECT count(*) FROM game_records WHERE game_id=%s", (gid,)) == [(1,)]
    rows = db.sql("""SELECT user_id,sub_rule,match_type,score,win_count,fan_stats,total_fan_score,total_rounds
                     FROM game_player_metrics WHERE game_id=%s ORDER BY user_id""", (gid,))
    assert len(rows) == 4 and all(r[1:3] == (PROFILE, "2/4") for r in rows)
    assert [r[3] for r in rows] == [p.score for p in sorted(gs.player_list, key=lambda p:p.user_id)]
    assert sum(r[4] for r in rows) == 16
    assert sum(r[5].get("鸡和",0) for r in rows)==8
    assert sum(r[5].get("碰碰和",0) for r in rows)>0
    assert all(r[7] == 8 for r in rows)
    base_points = fan_total = 0
    for board in gs.game_record["game_round"].values():
        for tick_index, raw_fan in board["win_fan_by_tick"].items():
            tick = board["action_ticks"][int(tick_index)]
            base_points += tick[2]
            fan_total += raw_fan
            assert raw_fan == sum(int(f.split("|")[2]) for f in tick[3] if f.startswith("GD|") and f.split("|")[2].isdigit())
    assert sum(r[6] for r in rows) == base_points and 0 < fan_total < base_points
    assert not db.sql("SELECT game_id FROM game_player_metrics WHERE sub_rule='guangdong/mil2023'")
    assert db.store_guangdong_game_record(gs.game_record,gs.player_list,"custom","2/4")==gid
    assert db.sql("SELECT count(*),sum(win_count) FROM game_player_metrics WHERE game_id=%s",(gid,))==[(4,16)]
