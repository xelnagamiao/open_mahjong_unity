"""虹雀实际局数与实时保存隔离，不在共享库中操作。"""
from datetime import date
import pytest

from test_subrule_statistics import db
from test_subrule_realtime import players, replay, prepare
from server.database.statistics_catalog import BY_SUB_RULE
from server.database.scene_stats import derive_game_type
from server.database.get_rule_stats import get_rule_history


@pytest.mark.parametrize('mode,expected', [('4/4','hands4'),('8/4','hands8'),
    ('12/4','hands12'),('16/4','hands16'),('16/4_rank','hands16')])
def test_hongque_classifies_actual_hands(mode, expected):
    assert derive_game_type(mode, 'hongque') == expected
    assert derive_game_type('4/4_rank', 'guobiao') == 'quanzhuang'


def test_hongque_live_modes_keep_four_hand_custom_separate(db):
    prepare(db)
    from server.database.hongque.store_hongque import store_hongque_game_record
    from server.database.daily_aggregator import aggregate_scene_daily_stats
    definition = BY_SUB_RULE['hongque/v1.6']
    for mode in ('4/4','8/4','12/4','16/4','16/4_rank'):
        category = 'match' if mode.endswith('_rank') else 'custom'
        record = replay(definition)
        record['game_title'].update(room_type=category, match_tier='elo' if category == 'match' else None)
        gid = store_hongque_game_record(db, record, players(definition), category, mode)
        assert gid
    expected = [('custom','12/4','hands12',1),('custom','16/4','hands16',1),
                ('custom','4/4','hands4',1),('custom','8/4','hands8',1),('match','16/4_rank','hands16',1)]
    assert db.sql('SELECT room_type,match_type,game_type,COUNT(DISTINCT game_id) FROM game_player_metrics GROUP BY 1,2,3 ORDER BY 1,2') == expected
    db.sql("UPDATE game_player_metrics SET created_at='2026-01-01 12:00:00+08'")
    aggregate_scene_daily_stats(db, date(2026,1,1))
    aggregate_scene_daily_stats(db, date(2026,1,1))
    assert db.sql("SELECT game_type,SUM(total_games) FROM scene_daily_stats WHERE room_type='match' GROUP BY game_type") == [('hands16',1)]
    recent = db.sql("SELECT placements FROM player_recent_records WHERE user_id=10000101 AND rule='hongque' AND room_type='match'")[0][0]
    assert len(recent) == 1 and recent[0]['match_type'] == '16/4_rank'
    ranked = get_rule_history(db,10000101,'hongque','match')
    assert len(ranked) == 1 and ranked[0]['mode'] == '16/4_rank' and ranked[0]['total_games'] == 1
    assert {row['mode'] for row in get_rule_history(db,10000101,'hongque')} == {'4/4','8/4','12/4','16/4'}


def test_new_hongque_ranked_games_write_canonical_mode(db):
    prepare(db)
    from server.database.hongque.store_hongque import store_hongque_game_record
    definition = BY_SUB_RULE['hongque/v1.6']
    record = replay(definition)
    record['game_title'].update(room_type='match', match_tier='elo')
    gid = store_hongque_game_record(db, record, players(definition), 'match', '16/4_rank')
    assert gid
    assert db.sql('SELECT DISTINCT match_type FROM game_player_records WHERE game_id=%s',(gid,)) == [('16/4_rank',)]
    assert db.sql('SELECT DISTINCT match_type,game_type FROM game_player_metrics WHERE game_id=%s',(gid,)) == [('16/4_rank','hands16')]
    stats = get_rule_history(db,10000101,'hongque','match')
    assert len(stats) == 1 and stats[0]['mode'] == '16/4_rank' and stats[0]['total_games'] == 1
