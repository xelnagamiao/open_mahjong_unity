"""Exercise the one-off rebuild against disposable PostgreSQL schemas."""
from datetime import datetime, timezone
import os

import psycopg2
from psycopg2.extras import Json
import pytest

from .test_rule_ratings import db, USERS, players
from .rule_ratings import settle_rated_game
from ..match.rating_rules import ELO_ALGORITHM_VERSION, HONGQUE_ELO_ALGORITHM_VERSION, elo_deltas
from scripts.rebuild_hongque_elo import BACKUP_TABLE, rebuild_database

pytestmark = pytest.mark.skipif(not os.environ.get('RATING_TEST_DATABASE_URL'),
                                reason='requires PostgreSQL test connection')


def seed_old_game(db, game_id, rounds, day=1, places=(1, 2, 3, 4)):
    inputs = players(places)
    old = dict((uid, (float(elo), games)) for uid, elo, games in
               db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque'"))
    values = [old.get(uid, (1500, 0))[0] for uid in USERS]
    deltas = elo_deltas(values, places, k=32)
    result = {}
    for uid, before, delta in zip(USERS, values, deltas):
        games = old.get(uid, (1500, 0))[1] + 1
        after = round(before + delta, 2)
        result[str(uid)] = dict(rating_rule='hongque', rating_system='elo', elo_before=before,
            elo_after=after, elo_delta=delta, rating_games=games, rating_algorithm=ELO_ALGORITHM_VERSION)
        db.sql("""INSERT INTO rule_ratings(user_id,rule,elo,games) VALUES(%s,'hongque',%s,%s)
            ON CONFLICT(user_id,rule) DO UPDATE SET elo=EXCLUDED.elo,games=EXCLUDED.games""", (uid,after,games))
    ended_at = datetime(2026, 10, day, 3, tzinfo=timezone.utc)
    title = dict(rule='hongque', sub_rule='hongque/v1.6', room_type='match', max_round=rounds,
                 end_time=ended_at.isoformat(), rating_rule='hongque', rating_result=result,
                 rating_game_id='runtime-' + game_id,
                 match_queue_type='hongque_elo_banzhuang' if rounds == 8 else 'hongque_elo_quanzhuang')
    title.update({f'p{i}_uid': uid for i,uid in enumerate(USERS)})
    record = dict(game_title=title, game_round={'round_index_1': {'action_ticks': [['end']]}})
    db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)', (game_id, Json(record)))
    for index, (uid, place) in enumerate(zip(USERS, places)):
        db.sql("""INSERT INTO game_player_records(game_id,user_id,rank,original_player_index,rule,sub_rule,match_type,room_type)
            VALUES(%s,%s,%s,%s,'hongque','hongque/v1.6',%s,'match')""", (game_id,uid,place,index,f'{rounds}/4_rank'))
    db.sql("INSERT INTO rating_settlements(game_id,rule,result,players,ended_at) VALUES(%s,'hongque',%s,%s,%s)",
           ('runtime-' + game_id, Json(result), Json(inputs), ended_at))
    return record


def rebuild(db, **kwargs):
    conn = db._get_connection()
    try:
        return rebuild_database(conn, **kwargs)
    finally:
        db._put_connection(conn)


def assert_no_backup(db):
    assert db.sql('SELECT to_regclass(%s)', (BACKUP_TABLE,))[0][0] is None


def test_mixed_history_preview_apply_backups_retry_cache_and_idempotence(db):
    # Reverse lexical IDs: chronological end time must determine the replay order.
    first = seed_old_game(db, 'z-half', 8)
    second = seed_old_game(db, 'a-full', 16, day=2)
    unrelated = settle_rated_game(db, 'sichuan-game', 'sichuan_elo_xuezhan', players())
    custom = {'game_title': {'rule':'hongque','room_type':'custom','max_round':8}}
    db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)', ('custom',Json(custom)))
    before = db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id")
    preview = rebuild(db)
    assert not preview['applied'] and preview['half_games'] == preview['full_games'] == 1
    assert [p['elo_after'] for p in preview['changes']] == [1523.92,1507.98,1492.02,1476.08]
    assert db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id") == before
    assert_no_backup(db)
    applied = rebuild(db, apply=True)
    assert applied['applied'] and applied['updated_users'] == 4
    assert db.sql("SELECT elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id") == [
        (1523.92,2),(1507.98,2),(1492.02,2),(1476.08,2)]
    new_half = db.sql("SELECT record FROM game_records WHERE game_id='z-half'")[0][0]
    new_full = db.sql("SELECT record FROM game_records WHERE game_id='a-full'")[0][0]
    for original, updated, k in ((first,new_half,16),(second,new_full,32)):
        assert updated['game_round'] == original['game_round']
        assert all(row['rating_k'] == k and row['rating_algorithm'] == HONGQUE_ELO_ALGORITHM_VERSION
                   for row in updated['game_title']['rating_result'].values())
    assert [new_half['game_title']['rating_result'][str(uid)]['elo_delta'] for uid in USERS] == [8,2.67,-2.67,-8]
    assert [new_full['game_title']['rating_result'][str(uid)]['elo_before'] for uid in USERS] == [1508,1502.67,1497.33,1492]
    assert settle_rated_game(db,'runtime-a-full','hongque_elo_quanzhuang',players()) == new_full['game_title']['rating_result']
    assert db.sql("SELECT result FROM rating_settlements WHERE game_id='sichuan-game'")[0][0] == unrelated
    assert db.sql("SELECT record FROM game_records WHERE game_id='custom'")[0][0] == custom
    backup = db.sql(f'SELECT backup FROM {BACKUP_TABLE}')[0][0]
    assert {row['game_id']:row['record'] for row in backup['records']} == {'z-half':first,'a-full':second}
    # A repeated invocation must not undo new games played after migration.
    settle_rated_game(db, 'new-half', 'hongque_elo_banzhuang', players((4,3,2,1)))
    after_new_game = db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id")
    assert rebuild(db, apply=True)['already_applied']
    assert db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id") == after_new_game
    assert db.sql(f'SELECT backup FROM {BACKUP_TABLE}')[0][0] == backup


def test_old_missing_link_and_inputs_are_rebuilt_from_exact_result_and_recorded_places(db):
    source = seed_old_game(db, 'legacy', 8, places=(1,1,3,4))
    title = source['game_title']
    del title['rating_game_id']
    del title['match_queue_type']
    # Naive historical times are Beijing time, identical to the original UTC time.
    title['end_time'] = '2026-10-01 11:00:00'
    db.sql("UPDATE game_records SET record=%s WHERE game_id='legacy'", (Json(source),))
    db.sql("UPDATE rating_settlements SET players=NULL WHERE game_id='runtime-legacy'")
    assert rebuild(db, apply=True)['ranked_games'] == 1
    cache, inputs = db.sql("SELECT result,players FROM rating_settlements WHERE game_id='runtime-legacy'")[0]
    assert [cache[str(uid)]['elo_delta'] for uid in USERS] == [5.33,5.33,-2.67,-7.99]
    assert inputs == [dict(user_id=uid,place=place) for uid,place in zip(USERS,(1,1,3,4))]


def test_deleted_players_participate_in_history_without_recreating_accounts(db):
    seed_old_game(db, 'deleted', 8)
    uid = USERS[-1]
    db.sql('DELETE FROM rule_ratings WHERE user_id=%s', (uid,))
    db.sql('DELETE FROM rank_data WHERE user_id=%s', (uid,))
    db.sql('DELETE FROM users WHERE user_id=%s', (uid,))
    report = rebuild(db, apply=True)
    assert report['deleted_accounts'] == 1 and report['updated_users'] == 3
    assert db.sql('SELECT user_id FROM users WHERE user_id=%s',(uid,)) == []
    result = db.sql("SELECT result FROM rating_settlements WHERE game_id='runtime-deleted'")[0][0]
    assert result[str(uid)]['elo_after'] == 1492


@pytest.mark.parametrize('damage,match', [
    ("DELETE FROM game_records", '缺少牌谱'),
    ("DELETE FROM game_records; DELETE FROM game_player_records", '缺少对应牌谱'),
    ("UPDATE game_records SET record=jsonb_set(record,'{game_title,max_round}','16')", '局数互相冲突'),
    ("UPDATE rating_settlements SET players=NULL; UPDATE game_player_records SET rank=0", '历史名次'),
    ("UPDATE rule_ratings SET games=20 WHERE rule='hongque'", '历史不足'),
    ("UPDATE game_player_records SET rank=4 WHERE original_player_index=0", '名次与参与记录冲突'),
])
def test_invalid_history_aborts_without_partial_updates(db, damage, match):
    seed_old_game(db, 'damaged', 8)
    db.sql(damage)
    before = db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id")
    with pytest.raises(ValueError, match=match):
        rebuild(db, apply=True)
    assert db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id") == before
    assert_no_backup(db)


def test_write_failure_rolls_back_ratings_records_caches_and_backup(db):
    first = seed_old_game(db, 'half', 8)
    second = seed_old_game(db, 'full', 16, day=2)
    before = db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id")
    db.sql('ALTER TABLE rule_ratings ADD CONSTRAINT reject_new_rating CHECK (elo <> 1523.92)')
    with pytest.raises(psycopg2.Error):
        rebuild(db, apply=True)
    assert db.sql("SELECT user_id,elo,games FROM rule_ratings WHERE rule='hongque' ORDER BY user_id") == before
    assert dict(db.sql('SELECT game_id,record FROM game_records')) == {'half':first,'full':second}
    assert db.sql("SELECT result FROM rating_settlements WHERE game_id='runtime-half'")[0][0] == first['game_title']['rating_result']
    assert_no_backup(db)


def test_missing_retry_cache_is_rebuilt_from_ranked_record(db):
    original = seed_old_game(db, 'cache-missing', 8)
    db.sql("DELETE FROM rating_settlements WHERE game_id='runtime-cache-missing'")
    report = rebuild(db, apply=True)
    assert report['ranked_games'] == 1
    assert db.sql("SELECT result FROM rating_settlements WHERE game_id='runtime-cache-missing'")[0][0][str(USERS[0])]['elo_delta'] == 8
    backup = db.sql(f'SELECT backup FROM {BACKUP_TABLE}')[0][0]
    assert backup['inserted_settlement_ids'] == ['runtime-cache-missing']
    assert backup['records'][0]['record'] == original


def test_empty_or_custom_only_database_is_unchanged_and_does_not_consume_marker(db):
    for with_custom in (False, True):
        if with_custom:
            custom = {'game_title': {'rule':'hongque','room_type':'custom','max_round':8}}
            db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)', ('custom',Json(custom)))
        assert rebuild(db, apply=True)['ranked_games'] == 0
        assert_no_backup(db)
        assert db.sql("SELECT * FROM rule_ratings WHERE rule='hongque'") == []
    assert db.sql("SELECT record FROM game_records WHERE game_id='custom'")[0][0] == custom
