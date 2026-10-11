"""实时统计、结构初始化和子规则隔离：仅操作独立 PostgreSQL 测试 schema。"""
import os
import uuid
from types import SimpleNamespace

import psycopg2
import pytest
from psycopg2.extras import Json

from server.database.statistics_catalog import RULES, resolve_statistics_identity, resolve_record_statistics_identity
from server.database.statistics_schema import ensure_statistics_schema
from server.database.riichi.record_stats import BASE_KEYS
from server.database.scene_stats import record_game_metrics


def test_catalog_has_one_entry_per_subrule_and_no_free_statistics():
    assert len(RULES) == len({r['key'] for r in RULES}) == len({r['sub_rule'] for r in RULES})
    assert all(r['basic_stats'] == (r['rule'] != 'free') for r in RULES)
    assert sum(r['fan_stats'] for r in RULES) == 21
    assert sum(r['matchmaking'] for r in RULES) == 7
    assert resolve_statistics_identity('hongkong', 'hongkong/new13', {'detailed_config': {'new13_version':'lianhuise'}}) == ('hongkong','hongkong/new13_lianhuise')
    assert resolve_statistics_identity('riichi', None, {}, '1/3') == ('riichi','riichi/sanma')


@pytest.fixture
def db():
    dsn = os.environ.get('SUBRULE_TEST_DATABASE_URL')
    if not dsn:
        pytest.skip('requires local PostgreSQL test connection')
    schema = 'subrule_test_' + uuid.uuid4().hex
    admin = psycopg2.connect(dsn)
    admin.autocommit = True
    with admin.cursor() as cur:
        cur.execute('CREATE SCHEMA ' + schema)
    conn = psycopg2.connect(dsn, options='-c search_path=' + schema)
    model = SimpleNamespace(_get_connection=lambda: conn, _put_connection=lambda _: None)
    def execute(query, args=None):
        with conn.cursor() as cur:
            cur.execute(query, args)
            rows = cur.fetchall() if cur.description else None
        conn.commit()
        return rows
    model.sql = execute
    metric_fields = [k for k in BASE_KEYS if k != 'total_games']
    execute(f"""CREATE TABLE game_records(game_id varchar(16) PRIMARY KEY,record jsonb,created_at timestamptz DEFAULT NOW());
        CREATE TABLE game_player_records(game_id varchar(16),user_id bigint,username text,rule varchar(10),sub_rule varchar(96),
            room_type text,match_tier text,event_id text,match_type text,score int,rank int,original_player_index int,
            title_used int,character_used int,profile_used int,voice_used int,avatar_frame_used int,pt_change int,
            PRIMARY KEY(game_id,user_id));
        CREATE TABLE game_player_metrics(id bigserial PRIMARY KEY,game_id varchar(16),user_id bigint,username text,
            rule varchar(10),sub_rule varchar(32),room_type text,match_tier text,event_id text,game_type text,
            match_type text,score int,rank int,{','.join(k+' int DEFAULT 0' for k in metric_fields)},created_at timestamptz DEFAULT NOW());
        CREATE TABLE scene_daily_stats(id bigserial PRIMARY KEY,stat_date date,room_type text,match_tier text,event_id text,
            rule text,game_type text,{','.join(k+' int DEFAULT 0' for k in BASE_KEYS)},
            UNIQUE(stat_date,room_type,match_tier,event_id,rule,game_type));
        CREATE TABLE scene_tier_fan_daily(stat_date date,match_tier text,rule text,fan_field text,fan_count int,
            PRIMARY KEY(stat_date,match_tier,rule,fan_field));
        CREATE TABLE riichi_player_game_stats(game_id varchar(16),user_id bigint,version int,stats jsonb,PRIMARY KEY(game_id,user_id));
        CREATE TABLE app_meta(meta_key varchar(100) PRIMARY KEY,meta_value text,updated_at timestamp DEFAULT NOW());
        INSERT INTO app_meta(meta_key,meta_value) VALUES('stats_catchup_through','2099-12-31');
        CREATE TABLE guobiao_history_stats(user_id bigint,rule varchar(10),mode text,{','.join(k+' int DEFAULT 0' for k in BASE_KEYS)},PRIMARY KEY(user_id,rule,mode));
        CREATE TABLE guobiao_fan_stats(user_id bigint,rule varchar(10),mode text,pinghe int DEFAULT 0,PRIMARY KEY(user_id,rule,mode));
    """)
    try:
        with conn.cursor() as cur:
            ensure_statistics_schema(cur)
        conn.commit()
        yield model
    finally:
        conn.close()
        with admin.cursor() as cur:
            cur.execute('DROP SCHEMA ' + schema + ' CASCADE')
        admin.close()


def seed(db, sub, suffix='', created='2020-01-01', wins=0):
    rule = sub.split('/')[0]
    gid = 'g' + str(RULES.index(next(r for r in RULES if r['sub_rule'] == sub))) + suffix
    record = {'game_title': {'rule':rule,'sub_rule':sub,'room_type':'custom','p0_uid':10000101}, 'game_round': {'round_index_1': {'seats':[0,1,2] if sub.endswith('/sanma') else [0,1,2,3], 'action_ticks':[['end']]}}}
    if wins:
        record['game_round'] = {'round_index_1': {'seats':[0,1,2,3], 'start_player_index':0,
            'action_ticks': [['reset',0], *([['hu_self',0,8,['平和'],[24,-8,-8,-8]]] * wins), ['end']]}}
    db.sql('INSERT INTO game_records VALUES(%s,%s,%s)', (gid, Json(record), created))
    db.sql("INSERT INTO game_player_records(game_id,user_id,username,rule,sub_rule,room_type,match_tier,event_id,match_type,score,rank,original_player_index) VALUES(%s,10000101,'test',%s,%s,'custom',NULL,NULL,'1/4',24,1,0)", (gid,rule,sub))
    return gid, record


def test_live_writes_and_retries_do_not_double_count(db):
    _, record = seed(db,'hongque/v1.6',wins=1)
    player = SimpleNamespace(user_id=10000101,username='test',score=24,original_player_index=0,
                             record_counter=SimpleNamespace(rank_result=1))
    scene = dict(rule='hongque',sub_rule='hongque/v1.6',room_type='custom',match_type='1/4')
    record_game_metrics(db,'live',record,[player],scene)
    record_game_metrics(db,'live',record,[player],scene)
    assert db.sql("SELECT COUNT(*),SUM(win_count) FROM game_player_metrics WHERE game_id='live'") == [(1,1)]
    assert db.sql("SELECT fan_stats FROM game_player_metrics WHERE game_id='live'") == [({'平和': 1},)]


def test_legacy_accumulators_keep_same_mode_variants_separate(db):
    from server.database.legacy_statistics import refresh_legacy_statistics
    player = SimpleNamespace(user_id=10000101, username='test', score=24, original_player_index=0,
                             record_counter=SimpleNamespace(rank_result=1))
    for sub in ('guobiao/standard','guobiao/transparent','guobiao/xiaolin'):
        gid, record = seed(db,sub,wins=1)
        scene = dict(rule='guobiao',sub_rule=sub,room_type='custom',match_type='1/4')
        record_game_metrics(db,gid,record,[player],scene)
        refresh_legacy_statistics(db,gid,'guobiao')
    assert db.sql('SELECT rule,total_games FROM guobiao_history_stats ORDER BY rule') == [
        ('guobiao/standard',1),('guobiao/transparent',1),('guobiao/xiaolin',1)]
    assert db.sql('SELECT rule,pinghe FROM guobiao_fan_stats ORDER BY rule') == [
        ('guobiao/standard',1),('guobiao/transparent',1)]
    refresh_legacy_statistics(db,'g1','guobiao')
    refresh_legacy_statistics(db,'g1','guobiao')
    assert db.sql('SELECT SUM(total_games) FROM guobiao_history_stats') == [(3,)]


@pytest.mark.parametrize('title,index,expected', [
    ({'rule': 'guobiao', 'sub_rule': 'guobiao/transparent'}, ('guobiao', 'guobiao/standard', '1/4'), ('guobiao', 'guobiao/transparent')),
    ({'sub_rule': 'hongque/v1.6'}, ('guobiao', 'guobiao/standard', '1/4'), ('hongque', 'hongque/v1.6')),
    ({'rule': 'hongkong', 'sub_rule': 'hongkong/new13', 'detailed_config': {'new13_version': 'lianhuise'}}, ('hongkong', 'hongkong/standard', '1/4'), ('hongkong', 'hongkong/new13_lianhuise')),
    ({'rule': 'jiandan', 'sub_rule': 'jiandan/standard'}, ('guobiao', 'guobiao/standard', '1/4'), ('zhongyong', 'zhongyong/nanque')),
    ({'rule': 'guobiao', 'sub_rule': 'guobiao/unknown'}, ('guobiao', 'guobiao/standard', '1/4'), ('guobiao', 'guobiao/unknown')),
    ({'rule': 'riichi', 'match_type': '1/3'}, ('guobiao', 'guobiao/standard', '1/4'), ('riichi', 'riichi/sanma')),
    ({'rule': 'riichi', 'match_type': '1/3'}, ('riichi', 'riichi/standard', '1/4'), ('riichi', 'riichi/sanma')),
    ({'rule': 'guobiao'}, ('guobiao', 'riichi/standard', '1/4'), ('guobiao', 'guobiao/standard')),
    ({'rule': 'guobiao'}, ('guobiao', 'guobiao/transparent', '1/4'), ('guobiao', 'guobiao/transparent')),
    ({}, ('riichi', None, '1/3'), ('riichi', 'riichi/sanma')),
])
def test_replay_identity_preserves_explicit_subrules_and_legacy_evidence(title, index, expected):
    assert resolve_record_statistics_identity({'game_title': title}, *index) == expected


def test_schema_initialization_is_idempotent_and_preserves_data(db):
    gid, record = seed(db, 'guobiao/transparent', wins=1)
    player = SimpleNamespace(user_id=10000101, username='test', score=24, original_player_index=0,
                             record_counter=SimpleNamespace(rank_result=1))
    record_game_metrics(db, gid, record, [player],
                        dict(rule='guobiao', sub_rule='guobiao/transparent', room_type='custom', match_type='1/4'))
    db.sql("INSERT INTO guobiao_history_stats(user_id,rule,mode,total_games) VALUES(10000999,'guobiao','1/4',99)")
    db.sql("INSERT INTO scene_daily_stats(stat_date,rule,total_games) VALUES('2020-01-01','guobiao',99)")
    db.sql("INSERT INTO scene_tier_fan_daily VALUES('2020-01-01','beginner','guobiao','pinghe',99,'guobiao/standard')")
    tables = ('game_records', 'game_player_records', 'game_player_metrics', 'scene_daily_stats',
              'scene_tier_fan_daily', 'guobiao_history_stats', 'guobiao_fan_stats', 'app_meta')
    before = {table: db.sql(f'SELECT * FROM {table}') for table in tables}
    for _ in range(2):
        with db._get_connection().cursor() as cur:
            ensure_statistics_schema(cur)
        db._get_connection().commit()
    assert {table: db.sql(f'SELECT * FROM {table}') for table in tables} == before
    assert db.sql("""SELECT table_name,character_maximum_length FROM information_schema.columns
        WHERE table_schema=current_schema() AND (table_name,column_name) IN
        (('game_player_metrics','sub_rule'),('game_player_records','sub_rule'),
         ('guobiao_history_stats','rule'),('guobiao_fan_stats','rule')) ORDER BY table_name""") == [
            ('game_player_metrics',96), ('game_player_records',96),
            ('guobiao_fan_stats',96), ('guobiao_history_stats',96)]
    indexes = {r[0] for r in db.sql("SELECT indexname FROM pg_indexes WHERE schemaname=current_schema()")}
    assert {'game_player_metrics_game_user_key', 'game_player_metrics_subrule_user',
            'scene_daily_subrule_key', 'scene_fan_subrule_key'} <= indexes


def test_legacy_refresh_only_touches_current_players(db):
    from server.database.legacy_statistics import refresh_legacy_statistics
    test_live_writes_and_retries_do_not_double_count(db)
    db.sql("INSERT INTO guobiao_history_stats(user_id,rule,mode,total_games) VALUES(10000999,'guobiao','1/4',99)")
    refresh_legacy_statistics(db, 'live', 'guobiao')
    assert db.sql("SELECT total_games FROM guobiao_history_stats WHERE user_id=10000999") == [(99,)]


@pytest.mark.parametrize('family,game_id', [('guobiao',None), ('guobiao',''), (None,'game'), ('unknown','game')])
def test_legacy_refresh_requires_explicit_scope(family, game_id):
    from unittest.mock import Mock
    from server.database.legacy_statistics import _refresh_legacy_statistics
    cursor = Mock()
    with pytest.raises(ValueError):
        _refresh_legacy_statistics(cursor, family, game_id)
    cursor.execute.assert_not_called()
