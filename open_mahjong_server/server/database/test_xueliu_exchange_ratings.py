"""在隔离 PostgreSQL schema 中验证血流评分和战绩。"""
from concurrent.futures import ThreadPoolExecutor

from psycopg2.extras import Json
import pytest

from .test_rule_ratings import db, USERS, players, pytestmark
from .rule_ratings import settle_rated_game, get_rule_leaderboard, get_ranked_history
from .guobiao.rank_data import get_rank_data
from .guobiao.get_rank_record_list import get_rank_record_list
from ..match.rating_rules import (XUELIU_EXCHANGE_QUEUE as QUEUE,
    XUELIU_EXCHANGE_LEGACY_QUEUE as LEGACY_QUEUE, XUELIU_EXCHANGE_RATING_RULE as POOL)


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


def test_new_matches_continue_legacy_elo_without_rewriting_the_old_settlement(db):
    old = settle_rated_game(db, 'old-full', LEGACY_QUEUE, players())
    new = settle_rated_game(db, 'new-half', QUEUE, players())
    assert new[str(USERS[0])]['elo_before'] == old[str(USERS[0])]['elo_after']
    assert new[str(USERS[0])]['rating_games'] == 2
    assert settle_rated_game(db, 'old-full', LEGACY_QUEUE, players()) == old
    rating = get_rank_data(db, USERS[0])['ratings'][POOL]
    assert rating['games'] == 2 and rating['elo'] == new[str(USERS[0])]['elo_after']
    assert db.sql('SELECT result FROM rating_settlements WHERE game_id=%s', ('old-full',))[0][0] == old


def test_exchange_record_list_and_metrics_preserve_both_lengths_and_separate_other_modes(db):
    fields = ('total_rounds', 'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
        'total_win_turn', 'total_fangchong_score', 'first_place_count', 'second_place_count',
        'third_place_count', 'fourth_place_count', 'fulu_round_count', 'cuohe_count', 'total_round_score')
    db.sql('CREATE TABLE game_player_metrics(game_id TEXT,user_id BIGINT,rule TEXT,sub_rule TEXT,room_type TEXT,match_type TEXT,'
           'rank INT DEFAULT 1,created_at TIMESTAMP DEFAULT NOW(),fan_stats JSONB,'
           + ','.join(f'{f} INT DEFAULT 0' for f in fields) + ')')
    db.sql('CREATE TABLE riichi_player_game_stats(game_id TEXT,user_id BIGINT,version INT,stats JSONB)')
    for gid, sub, room, rounds in [('standard', 'sichuan/standard', 'match', 16),
                                  ('legacy-null', None, 'match', 4),
                                  ('legacy-empty', '', 'match', 4),
                                  ('exchange-old', 'sichuan/xueliu_exchange', 'match', 16),
                                  ('exchange-new', 'sichuan/xueliu_exchange', 'match', 8),
                                  ('discard', 'sichuan/xueliu', 'custom', 4),
                                  ('custom', 'sichuan/xueliu_exchange', 'custom', 8)]:
        title = dict(match_queue_type=LEGACY_QUEUE if gid == 'exchange-old' else QUEUE if gid == 'exchange-new' else 'sichuan_elo_xuezhan',
                     max_round=rounds // 4)
        record = {'game_title': title, 'game_round': {f'round_index_{i}': {'action_ticks': [['end']]}
                  for i in range(1, rounds + 1)}}
        db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)', (gid, Json(record)))
        mode = f'{rounds // 4}/4' + ('_rank' if room == 'match' else '')
        db.sql("INSERT INTO game_player_records(game_id,user_id,rule,sub_rule,room_type,match_type,rank) VALUES(%s,%s,'sichuan',%s,%s,%s,1)",
               (gid, USERS[0], sub, room, mode))
        db.sql("INSERT INTO game_player_metrics(game_id,user_id,rule,sub_rule,room_type,match_type,total_rounds) VALUES(%s,%s,'sichuan',%s,%s,%s,%s)",
               (gid, USERS[0], sub or 'sichuan/standard', room, mode, rounds))
    old_record = db.sql("SELECT record FROM game_records WHERE game_id='exchange-old'")[0][0]
    records = {r['game_id']: r for r in get_rank_record_list(db, rule=POOL)}
    assert set(records) == {'exchange-old', 'exchange-new'}
    assert records['exchange-old']['match_type'] == '4/4_rank'
    assert records['exchange-old']['match_queue_type'] == LEGACY_QUEUE
    assert records['exchange-new']['match_type'] == '2/4_rank'
    assert {r['game_id'] for r in get_rank_record_list(db, rule='sichuan')} == {'standard', 'legacy-null', 'legacy-empty'}
    history = {r['mode']: r for r in get_ranked_history(db, USERS[0], POOL)}
    assert set(history) == {'2/4_rank', '4/4_rank'}
    assert history['2/4_rank']['total_rounds'] == 8 and history['2/4_rank']['total_games'] == 1
    assert history['4/4_rank']['total_rounds'] == 16 and history['4/4_rank']['total_games'] == 1
    assert sum(r['total_games'] for r in get_ranked_history(db, USERS[0], 'sichuan')) == 3
    from .player_recent_records import ensure_schema, get_player_recent_records
    conn = db._get_connection()
    with conn.cursor() as cursor:
        ensure_schema(cursor)
    conn.commit()
    db._put_connection(conn)
    recent = get_player_recent_records(db, USERS[0])
    assert {r['game_id'] for r in recent[POOL]['match']['placements']} == {'exchange-old', 'exchange-new'}
    assert {r['game_id'] for r in recent['sichuan']['match']['placements']} == {'standard', 'legacy-null', 'legacy-empty'}
    assert db.sql("SELECT record FROM game_records WHERE game_id='exchange-old'")[0][0] == old_record
    assert old_record['game_title']['max_round'] == 4 and len(old_record['game_round']) == 16
