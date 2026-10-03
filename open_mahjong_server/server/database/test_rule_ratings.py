"""Real PostgreSQL migrations and settlements, exclusively in disposable schemas."""
import os
import uuid
from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace
import pytest
import psycopg2
from psycopg2.extras import Json
from psycopg2.pool import ThreadedConnectionPool
from .rule_ratings import ensure_rating_schema,settle_rated_game,get_rule_leaderboard,get_ranked_history
from .guobiao.rank_data import get_rank_data
from .guobiao.get_rank_record_list import get_rank_record_list
from ..match.rating_rules import RULES,QUEUES

pytestmark=pytest.mark.skipif(not os.environ.get('RATING_TEST_DATABASE_URL'),reason='requires PostgreSQL test connection')
USERS=list(range(11000001,11000005))

@pytest.fixture
def db():
    schema='rating_test_'+uuid.uuid4().hex
    admin=psycopg2.connect(os.environ['RATING_TEST_DATABASE_URL']);admin.autocommit=True
    with admin.cursor() as c:c.execute('CREATE SCHEMA '+schema)
    pool=ThreadedConnectionPool(1,8,os.environ['RATING_TEST_DATABASE_URL'],options='-c search_path='+schema)
    db=SimpleNamespace(_get_connection=pool.getconn,_put_connection=pool.putconn)
    def sql(query,args=None):
        conn=pool.getconn()
        try:
            with conn.cursor() as c:
                c.execute(query,args);rows=c.fetchall() if c.description else None
            conn.commit();return rows
        except Exception:conn.rollback();raise
        finally:pool.putconn(conn)
    db.sql=sql
    sql("""CREATE TABLE users(user_id BIGINT PRIMARY KEY,username TEXT);
        CREATE TABLE user_settings(user_id BIGINT PRIMARY KEY,profile_image_id INT DEFAULT 1);
        CREATE TABLE rank_data(user_id BIGINT PRIMARY KEY REFERENCES users,guobiao_rank TEXT DEFAULT '10级',guobiao_score DOUBLE PRECISION DEFAULT 0,updated_at TIMESTAMP DEFAULT NOW());
        CREATE TABLE game_records(game_id TEXT PRIMARY KEY,record JSONB,created_at TIMESTAMP DEFAULT NOW());
        CREATE TABLE game_player_records(game_id TEXT,user_id BIGINT,username TEXT,score INT,rank INT,original_player_index INT,rule TEXT,sub_rule TEXT,match_type TEXT,room_type TEXT);
        INSERT INTO users SELECT i,'player '||i FROM generate_series(11000001,11000004)i;
        INSERT INTO rank_data(user_id,guobiao_rank,guobiao_score) VALUES(11000001,'四段',812.34);
    """)
    conn=pool.getconn()
    with conn.cursor() as c:ensure_rating_schema(c);ensure_rating_schema(c)
    conn.commit();pool.putconn(conn)
    try:yield db
    finally:
        pool.closeall()
        with admin.cursor() as c:c.execute('DROP SCHEMA '+schema+' CASCADE')
        admin.close()

def players(places=(1,2,3,4)):
    return [dict(user_id=u,place=p) for u,p in zip(USERS,places)]

def test_additive_migration_preserves_national_data(db):
    data=get_rank_data(db,USERS[0]);assert data['guobiao_rank']=='四段' and data['guobiao_score']==812.34
    assert set(data['ratings'])==set(RULES)
    assert all(r['elo']==1500 and r['games']==0 for r in data['ratings'].values())
    assert data['ratings']['riichi']['rank_name']=='10级'

@pytest.mark.parametrize('queue',list(QUEUES))
def test_independent_atomic_settlement_and_retry(db,queue):
    spec=QUEUES[queue];result=settle_rated_game(db,'game',queue,players())
    assert settle_rated_game(db,'game',queue,players())==result
    for uid in USERS:
        data=get_rank_data(db,uid)
        assert data['ratings'][spec.rule]['games']==1
        assert all(r['games']==0 for rule,r in data['ratings'].items() if rule!=spec.rule)
        assert data['ratings'][spec.rule]['elo']==result[str(uid)]['elo_after']
    if spec.rule!='guobiao':assert get_rank_data(db,USERS[0])['guobiao_score']==812.34
    assert len(get_rule_leaderboard(db,spec.rule))==4

def test_same_game_concurrent_retry_applies_once(db):
    with ThreadPoolExecutor(max_workers=4) as executor:
        results=list(executor.map(lambda _:settle_rated_game(db,'same','qingque_elo_quanzhuang',players()),range(4)))
    assert all(r==results[0] for r in results)
    assert get_rank_data(db,USERS[0])['ratings']['qingque']['games']==1

def test_failure_rolls_back_all_four_and_retry_works(db):
    db.sql("ALTER TABLE rule_ratings ADD CONSTRAINT force_failure CHECK (elo>=1490)")
    with pytest.raises(psycopg2.Error):settle_rated_game(db,'fail','sichuan_elo_xuezhan',players())
    assert db.sql('SELECT COUNT(*) FROM rating_settlements')[0][0]==0
    assert db.sql('SELECT COUNT(*) FROM rule_ratings')[0][0]==0
    db.sql('ALTER TABLE rule_ratings DROP CONSTRAINT force_failure')
    assert settle_rated_game(db,'fail','sichuan_elo_xuezhan',players())

def test_tied_national_pt_and_elo_only_no_grade(db):
    r=settle_rated_game(db,'tie','beginner_dongfeng',players((1,1,3,4)))
    assert r[str(USERS[0])]['pt']==pytest.approx(7.35)
    r=settle_rated_game(db,'elo','qingque_elo_quanzhuang',players())
    assert all(p['pt']==0 and p['rank_after']=='' for p in r.values())

def test_grade_and_elo_leaderboard_sort_independently(db):
    settle_rated_game(db,'q','qingque_elo_quanzhuang',players((4,3,2,1)))
    assert get_rule_leaderboard(db,'qingque')[0]['user_id']==USERS[3]
    assert get_rule_leaderboard(db,'guobiao')[0]['user_id']==USERS[0]
    assert not get_rule_leaderboard(db,'riichi')
    with pytest.raises(ValueError):get_rule_leaderboard(db,"riichi'; DROP TABLE users;")

def test_record_filter_before_limit_and_custom_exclusion(db):
    for i,rule in enumerate(RULES):
        gid=str(i);db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)',(gid,Json({'game_title':{'match_queue_type':next(q for q,s in QUEUES.items() if s.rule==rule)}})))
        for index,uid in enumerate(USERS):
            db.sql('INSERT INTO game_player_records VALUES(%s,%s,%s,0,%s,%s,%s,%s,%s,%s)',(gid,uid,str(uid),index+1,index,rule,rule+'/standard','1/4_rank','match'))
    for rule in RULES:
        records=get_rank_record_list(db,limit=1,rule=rule)
        assert len(records)==1 and records[0]['rule']==rule and len(records[0]['players'])==4
    db.sql("UPDATE game_player_records SET room_type='custom' WHERE rule='sichuan'")
    assert not get_rank_record_list(db,rule='sichuan')


def create_metric_fixture(db):
    fields = ('total_rounds','win_count','self_draw_count','deal_in_count','total_fan_score',
              'total_win_turn','total_fangchong_score','first_place_count','second_place_count',
              'third_place_count','fourth_place_count','fulu_round_count','cuohe_count','total_round_score')
    columns=','.join(f'{field} INT DEFAULT 0' for field in fields)
    db.sql(f'CREATE TABLE game_player_metrics(user_id BIGINT,rule TEXT,match_type TEXT,room_type TEXT,{columns})')


@pytest.mark.parametrize('rule',RULES)
def test_ranked_history_counts_only_requested_player_rule_and_matching_games(db,rule):
    create_metric_fixture(db)
    for uid,r,mode,room_type in ((USERS[0],rule,'1/4_rank','match'),
                                 (USERS[0],rule,'1/4_rank','match'),
                                 (USERS[0],rule,'2/4_rank','match'),
                                 (USERS[0],rule,'1/4','custom'),
                                 (USERS[1],rule,'1/4_rank','match'),
                                 (USERS[0],'other','1/4_rank','match')):
        db.sql('INSERT INTO game_player_metrics(user_id,rule,match_type,room_type,total_rounds,win_count,first_place_count,total_round_score) VALUES(%s,%s,%s,%s,4,2,1,30)',(uid,r,mode,room_type))
    data={r['mode']:r for r in get_ranked_history(db,USERS[0],rule)}
    assert set(data)=={'1/4_rank','2/4_rank'}
    assert data['1/4_rank']['total_games']==2
    assert data['1/4_rank']['total_rounds']==8
    assert data['1/4_rank']['win_count']==4
    assert data['1/4_rank']['first_place_count']==2
    assert data['1/4_rank']['total_round_score']==60
    assert data['2/4_rank']['total_games']==1
    assert not get_ranked_history(db,999999999,rule)
    with pytest.raises(ValueError):get_ranked_history(db,USERS[0],'unknown')


def create_fan_fixture(db,rule):
    if rule=='riichi':
        from .riichi.store_riichi import FAN_FIELDS
        from .riichi.get_riichi_stats import get_riichi_fan_stats_total as read
    else:
        from .qingque.store_qingque import FAN_FIELDS
        from .qingque.get_qingque_stats import get_qingque_fan_stats_total as read
    columns=','.join(f'{field} INT DEFAULT 0' for field in FAN_FIELDS)
    db.sql(f'CREATE TABLE {rule}_fan_stats(user_id BIGINT,mode TEXT,{columns})')
    return read,FAN_FIELDS[0]


@pytest.mark.parametrize('rule',('riichi','qingque'))
def test_fan_statistics_keep_ranked_and_custom_games_separate(db,rule):
    read,fan=create_fan_fixture(db,rule)
    db.sql(f'INSERT INTO {rule}_fan_stats(user_id,mode,{fan}) VALUES(%s,%s,3),(%s,%s,7)',(USERS[0],'1/4_rank',USERS[0],'1/4'))
    assert read(db,USERS[0],ranked=True)[fan]==3
    assert read(db,USERS[0],ranked=False)[fan]==7
    assert read(db,USERS[0])[fan]==10
    assert read(db,USERS[1],ranked=True)[fan]==0
