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
from ..match.rating_rules import RULES,QUEUES,GRADE_RULES

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
    return [dict(user_id=u,place=p,match_points=points) for u,p,points in zip(USERS,places,(60,10,-20,-50))]

def test_additive_migration_preserves_national_data(db):
    data=get_rank_data(db,USERS[0]);assert data['guobiao_rank']=='四段' and data['guobiao_score']==812.34
    assert set(data['ratings'])==set(RULES)
    assert all(r['games']==0 for r in data['ratings'].values())
    assert all(('elo' not in r if rule in GRADE_RULES else r['elo']==1500) for rule,r in data['ratings'].items())
    assert data['ratings']['riichi']['rank_name']=='10级'

@pytest.mark.parametrize('queue',list(QUEUES))
def test_independent_atomic_settlement_and_retry(db,queue):
    spec=QUEUES[queue];inputs=players()[:spec.player_count]
    result=settle_rated_game(db,'game',queue,inputs)
    assert settle_rated_game(db,'game',queue,inputs)==result
    for uid in USERS[:spec.player_count]:
        data=get_rank_data(db,uid)
        assert data['ratings'][spec.rating_rule]['games']==1
        assert all(r['games']==0 for rule,r in data['ratings'].items() if rule!=spec.rating_rule)
        if spec.graded:
            assert 'elo' not in data['ratings'][spec.rating_rule]
            assert 'elo_after' not in result[str(uid)]
        else:
            assert data['ratings'][spec.rating_rule]['elo']==result[str(uid)]['elo_after']
    if spec.rule!='guobiao':assert get_rank_data(db,USERS[0])['guobiao_score']==812.34
    assert len(get_rule_leaderboard(db,spec.rating_rule))==spec.player_count
    if spec.graded:
        assert db.sql('SELECT COUNT(*) FROM rule_ratings WHERE elo IS NOT NULL')[0][0]==0
        assert all('elo' not in row for row in get_rule_leaderboard(db,spec.rating_rule))

def test_same_game_concurrent_retry_applies_once(db):
    with ThreadPoolExecutor(max_workers=4) as executor:
        results=list(executor.map(lambda _:settle_rated_game(db,'same','qingque_elo_quanzhuang',players()),range(4)))
    assert all(r==results[0] for r in results)
    assert get_rank_data(db,USERS[0])['ratings']['qingque']['games']==1


def test_riichi_points_cost_and_algorithm_are_saved_and_retry_does_not_recalculate(db):
    from ..match.riichi_rank_calculator import RIICHI_PT_ALGORITHM
    for uid in USERS:
        db.sql("INSERT INTO rule_ratings(user_id,rule,rank_name,rank_score,elo) VALUES(%s,'riichi','七段',1500,NULL)", (uid,))
    result = settle_rated_game(db,'riichi-points','riichi_advanced_banzhuang',players())
    assert [result[str(u)]['rating_pt'] for u in USERS] == [54.75,4.75,-25.25,-55.25]
    assert all(p['rating_algorithm'] == RIICHI_PT_ALGORITHM and p['rating_rank_cost'] == 5.25 for p in result.values())
    saved_result, saved_inputs = db.sql("SELECT result,players FROM rating_settlements WHERE game_id='riichi-points'")[0]
    assert saved_result == result and [p['match_points'] for p in saved_inputs] == [60,10,-20,-50]
    # A retry uses the committed result even after a grade adjustment or a version change.
    db.sql("UPDATE rule_ratings SET rank_name='八段',rank_score=1700 WHERE rule='riichi'")
    retry = [dict(user_id=p['user_id'],place=p['place']) for p in players()]
    assert settle_rated_game(db,'riichi-points','riichi_advanced_banzhuang',retry,rating_algorithm='grade_place_pt_v1') == result
    assert all(get_rank_data(db,u)['ratings']['riichi']['rank_score'] == 1700 for u in USERS)
    assert all(get_rank_data(db,u)['ratings']['riichi']['games'] == 1 for u in USERS)


@pytest.mark.parametrize('points', [None, True, float('nan'), float('inf')])
def test_new_riichi_invalid_game_points_roll_back_without_touching_existing_grades(db,points):
    data = players(); data[1]['match_points'] = points
    with pytest.raises(ValueError): settle_rated_game(db,'invalid','riichi_beginner_banzhuang',data)
    assert db.sql('SELECT COUNT(*) FROM rating_settlements')[0][0] == 0
    assert db.sql('SELECT COUNT(*) FROM rule_ratings')[0][0] == 0
    assert get_rank_data(db,USERS[0])['guobiao_score'] == 812.34


def test_in_flight_legacy_riichi_keeps_place_pt_and_cached_old_games_remain_unchanged(db):
    from ..match.rank_calculator import calculate_pt
    from ..match.riichi_rank_calculator import LEGACY_PT_ALGORITHM
    legacy_inputs = [dict(user_id=p['user_id'],place=p['place']) for p in players()]
    result = settle_rated_game(db,'legacy','riichi_beginner_banzhuang',legacy_inputs,rating_algorithm=LEGACY_PT_ALGORITHM)
    assert [result[str(u)]['rating_pt'] for u in USERS] == [round(calculate_pt('beginner','banzhuang',i,'10级'),2) for i in range(1,5)]
    assert all(p['rating_algorithm'] == LEGACY_PT_ALGORITHM and 'rating_match_points' not in p for p in result.values())
    assert settle_rated_game(db,'legacy','riichi_beginner_banzhuang',players()) == result
    assert all(get_rank_data(db,u)['ratings']['riichi']['games'] == 1 for u in USERS)

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
        queue=next(q for q,s in QUEUES.items() if s.rating_rule==rule);spec=QUEUES[queue]
        gid=str(i);db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)',(gid,Json({'game_title':{'match_queue_type':queue}})))
        for index,uid in enumerate(USERS[:spec.player_count]):
            sub_rule=spec.sub_rule or ('riichi/sanma' if spec.player_count == 3 else spec.rule+'/standard')
            db.sql('INSERT INTO game_player_records VALUES(%s,%s,%s,0,%s,%s,%s,%s,%s,%s)',(gid,uid,str(uid),index+1,index,spec.rule,sub_rule,'1/4_sanma_rank' if spec.player_count==3 else '1/4_rank','match'))
    for rule in RULES:
        records=get_rank_record_list(db,limit=1,rule=rule)
        game_rule='riichi' if rule=='riichi_sanma' else 'sichuan' if rule=='sichuan_xueliu_exchange' else rule
        assert len(records)==1 and records[0]['rule']==game_rule and len(records[0]['players'])==(3 if rule=='riichi_sanma' else 4)
    db.sql("UPDATE game_player_records SET room_type='custom' WHERE rule='sichuan'")
    assert not get_rank_record_list(db,rule='sichuan')


def create_metric_fixture(db):
    fields = ('total_rounds','win_count','self_draw_count','deal_in_count','total_fan_score',
              'total_win_turn','total_fangchong_score','first_place_count','second_place_count',
              'third_place_count','fourth_place_count','fulu_round_count','cuohe_count','total_round_score')
    columns=','.join(f'{field} INT DEFAULT 0' for field in fields)
    db.sql(f'CREATE TABLE game_player_metrics(user_id BIGINT,rule TEXT,sub_rule TEXT,match_type TEXT,room_type TEXT,{columns})')


@pytest.mark.parametrize('rule',RULES)
def test_ranked_history_counts_only_requested_player_rule_and_matching_games(db,rule):
    create_metric_fixture(db)
    if rule in ('riichi','riichi_sanma'):
        from .riichi.record_stats import ensure_schema
        conn = db._get_connection()
        with conn.cursor() as cursor:
            ensure_schema(cursor)
        conn.commit()
        db._put_connection(conn)
    fixture_index = 0
    for uid,r,mode,room_type in ((USERS[0],rule,'1/4_rank','match'),
                                 (USERS[0],rule,'1/4_rank','match'),
                                 (USERS[0],rule,'2/4_rank','match'),
                                 (USERS[0],rule,'1/4','custom'),
                                 (USERS[1],rule,'1/4_rank','match'),
                                 (USERS[0],'other','1/4_rank','match')):
        from ..match.rating_rules import ranked_record_scope
        game_rule, sub_rule = ranked_record_scope(r) if r in RULES else (r, None)
        db.sql('INSERT INTO game_player_metrics(user_id,rule,sub_rule,match_type,room_type,total_rounds,win_count,first_place_count,total_round_score) VALUES(%s,%s,%s,%s,%s,4,2,1,30)',(uid,game_rule,sub_rule,mode,room_type))
        if rule in ('riichi','riichi_sanma'):
            gid = 'stats' + str(fixture_index)
            fixture_index += 1
            db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)', (gid, Json({})))
            game_rule='riichi' if r=='riichi_sanma' else r
            sub_rule='riichi/sanma' if r=='riichi_sanma' else r
            if rule=='riichi_sanma': mode=mode.replace('/4','/4_sanma')
            db.sql('INSERT INTO game_player_records VALUES(%s,%s,%s,0,1,0,%s,%s,%s,%s)', (gid,uid,str(uid),game_rule,sub_rule,mode,room_type))
            from .riichi.record_stats import VERSION
            db.sql('INSERT INTO riichi_player_game_stats VALUES(%s,%s,%s,%s)', (gid,uid,VERSION,Json({
                'total_games':1,'total_rounds':4,'win_count':2,'first_place_count':1,'total_round_score':30})))
    data={r['mode']:r for r in get_ranked_history(db,USERS[0],rule)}
    if rule=='riichi_sanma':data={mode.replace('_sanma',''):row for mode,row in data.items()}
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
    if rule == 'riichi':
        from .riichi.record_stats import ensure_schema
        conn = db._get_connection()
        with conn.cursor() as cursor:
            ensure_schema(cursor)
        conn.commit()
        db._put_connection(conn)
        for gid, mode, room, count in (('rank','1/4_rank','match',3), ('custom','1/4','custom',7)):
            db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)', (gid,Json({})))
            db.sql('INSERT INTO game_player_records VALUES(%s,%s,%s,0,1,0,%s,%s,%s,%s)',
                (gid,USERS[0],'player',rule,rule,mode,room))
            from .riichi.record_stats import VERSION
            db.sql('INSERT INTO riichi_player_game_stats VALUES(%s,%s,%s,%s)',
                (gid,USERS[0],VERSION,Json({'fan_stats':{fan:count}})))
    assert read(db,USERS[0],ranked=True)[fan]==3
    assert read(db,USERS[0],ranked=False)[fan]==7
    assert read(db,USERS[0])[fan]==10
    assert read(db,USERS[1],ranked=True)[fan]==0
