"""在隔离 PostgreSQL schema 中验证血流评分和战绩。"""
from concurrent.futures import ThreadPoolExecutor

from psycopg2.extras import Json
import pytest

from .test_rule_ratings import db, USERS, players, pytestmark
from .rule_ratings import settle_rated_game, get_rule_leaderboard, get_ranked_history
from .guobiao.rank_data import get_rank_data
from .guobiao.get_rank_record_list import get_rank_record_list
from ..match.rating_rules import XUELIU_EXCHANGE_QUEUE as QUEUE, XUELIU_EXCHANGE_RATING_RULE as POOL


def test_exchange_elo_is_persistent_independent_and_concurrent_retry_applies_once(db):
    db.sql("INSERT INTO rule_ratings(user_id,rule,elo,games) VALUES(%s,'sichuan',1777,8)", (USERS[0],))
    with ThreadPoolExecutor(max_workers=4) as executor:
        results = list(executor.map(lambda _: settle_rated_game(db, 'flow-once', QUEUE, players()), range(4)))
    assert all(r == results[0] for r in results)
    assert [results[0][str(u)]['elo_delta'] for u in USERS] == [16, 5.33, -5.33, -16]
    ratings = get_rank_data(db, USERS[0])['ratings']
    assert ratings[POOL]['elo'] == 1516 and ratings[POOL]['games'] == 1
    assert ratings['sichuan']['elo'] == 1777 and ratings['sichuan']['games'] == 8
    assert all(r['rule'] == POOL for r in get_rule_leaderboard(db, POOL))
    with pytest.raises(ValueError):
        settle_rated_game(db, 'flow-once', 'sichuan_elo_xuezhan', players())


def test_exchange_record_list_and_metrics_are_separate_from_standard_and_custom(db):
    from .rule_ratings import get_ranked_history
    fields = ('total_rounds', 'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
        'total_win_turn', 'total_fangchong_score', 'first_place_count', 'second_place_count',
        'third_place_count', 'fourth_place_count', 'fulu_round_count', 'cuohe_count', 'total_round_score')
    db.sql('CREATE TABLE game_player_metrics(user_id BIGINT,rule TEXT,sub_rule TEXT,room_type TEXT,match_type TEXT,'
           + ','.join(f'{f} INT DEFAULT 0' for f in fields) + ')')
    for gid, sub, room, rounds in [('standard', 'sichuan/standard', 'match', 16),
                                  ('legacy-null', None, 'match', 4),
                                  ('legacy-empty', '', 'match', 4),
                                  ('exchange', 'sichuan/xueliu_exchange', 'match', 16),
                                  ('discard', 'sichuan/xueliu', 'custom', 4),
                                  ('custom', 'sichuan/xueliu_exchange', 'custom', 8)]:
        title = dict(match_queue_type=QUEUE if gid == 'exchange' else 'sichuan_elo_xuezhan')
        db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)', (gid, Json({'game_title': title})))
        db.sql("INSERT INTO game_player_records(game_id,user_id,rule,sub_rule,room_type,match_type,rank) VALUES(%s,%s,'sichuan',%s,%s,'4/4_rank',1)",
               (gid, USERS[0], sub, room))
        db.sql("INSERT INTO game_player_metrics(user_id,rule,sub_rule,room_type,match_type,total_rounds) VALUES(%s,'sichuan',%s,%s,'4/4_rank',%s)",
               (USERS[0], sub, room, rounds))
    assert [r['game_id'] for r in get_rank_record_list(db, rule=POOL)] == ['exchange']
    assert {r['game_id'] for r in get_rank_record_list(db, rule='sichuan')} == {'standard', 'legacy-null', 'legacy-empty'}
    assert get_ranked_history(db, USERS[0], POOL)[0]['rule'] == POOL
    assert get_ranked_history(db, USERS[0], POOL)[0]['total_games'] == 1
    assert get_ranked_history(db, USERS[0], 'sichuan')[0]['total_games'] == 3
    from .player_recent_records import ensure_schema, get_player_recent_records
    conn = db._get_connection()
    with conn.cursor() as cursor:
        ensure_schema(cursor)
    conn.commit()
    db._put_connection(conn)
    recent = get_player_recent_records(db, USERS[0])
    assert [r['game_id'] for r in recent[POOL]['match']['placements']] == ['exchange']
    assert {r['game_id'] for r in recent['sichuan']['match']['placements']} == {'standard', 'legacy-null', 'legacy-empty'}
