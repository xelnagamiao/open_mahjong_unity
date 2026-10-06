"""Real PostgreSQL tests use the existing disposable-schema fixture."""
from concurrent.futures import ThreadPoolExecutor
import pytest
from psycopg2.extras import Json
from .test_rule_ratings import db, USERS, pytestmark
from .rule_ratings import settle_rated_game, ensure_rating_schema, get_rule_leaderboard, get_ranked_history
from .guobiao.rank_data import get_rank_data
from ..match.sanma_rank_calculator import SANMA_PT_ALGORITHM


def inputs(places=(1,2,3)):
    return [dict(user_id=u, place=p) for u,p in zip(USERS,places)]


def test_three_pool_is_independent_persistent_and_repeat_is_idempotent(db):
    for uid in USERS[:3]:
        db.sql("INSERT INTO rule_ratings(user_id,rule,rank_name,rank_score,elo) VALUES(%s,'riichi','七段',1500,NULL),(%s,'riichi_sanma','初段',200,NULL)", (uid,uid))
    result=settle_rated_game(db,'sanma','riichi_sanma_beginner_banzhuang',inputs())
    assert [result[str(u)]['rating_pt'] for u in USERS[:3]] == [45,0,-45]
    assert all(r['rating_rule']=='riichi_sanma' and r['rating_algorithm']==SANMA_PT_ALGORITHM for r in result.values())
    assert settle_rated_game(db,'sanma','riichi_sanma_beginner_banzhuang',inputs())==result
    for uid in USERS[:3]:
        ratings=get_rank_data(db,uid)['ratings']
        assert ratings['riichi']['rank_score']==1500 and ratings['riichi']['games']==0
        assert ratings['riichi_sanma']['games']==1
        assert 'elo' not in ratings['riichi_sanma']
    board=get_rule_leaderboard(db,'riichi_sanma')
    assert [r['user_id'] for r in board]==USERS[:3]
    assert all(r['system']=='grade' and 'elo' not in r for r in board)
    assert get_rank_data(db,USERS[3])['ratings']['riichi_sanma']['games']==0
    assert db.sql("SELECT rule FROM rating_settlements WHERE game_id='sanma'")==[('riichi_sanma',)]


def test_concurrent_three_player_retry_applies_once(db):
    with ThreadPoolExecutor(max_workers=4) as executor:
        results=list(executor.map(lambda _:settle_rated_game(db,'once','riichi_sanma_beginner_dongfeng',inputs()),range(4)))
    assert all(r==results[0] for r in results)
    assert all(get_rank_data(db,u)['ratings']['riichi_sanma']['games']==1 for u in USERS[:3])


@pytest.mark.parametrize('players', [inputs()[:2],inputs()+[dict(user_id=USERS[3],place=4)],
    inputs((1,2,4)),inputs((1,1,2)),[dict(user_id=USERS[0],place=p) for p in (1,2,3)],
    [dict(user_id=1,place=1),*inputs()[1:]],inputs((True,2,3))])
def test_invalid_table_is_atomic(db,players):
    with pytest.raises(ValueError): settle_rated_game(db,'invalid','riichi_sanma_beginner_dongfeng',players)
    assert db.sql('SELECT COUNT(*) FROM rule_ratings')[0][0]==0
    assert db.sql('SELECT COUNT(*) FROM rating_settlements')[0][0]==0


@pytest.mark.parametrize('algorithm',['riichi_mleague_pt_v1','grade_place_pt_v1','bad'])
def test_wrong_algorithm_rejected(db,algorithm):
    with pytest.raises(ValueError): settle_rated_game(db,'bad','riichi_sanma_beginner_dongfeng',inputs(),rating_algorithm=algorithm)
    assert db.sql('SELECT COUNT(*) FROM rating_settlements')[0][0]==0


def test_failure_of_last_player_rolls_back_whole_table(db):
    db.sql("INSERT INTO rule_ratings(user_id,rule,rank_name,rank_score,elo) VALUES(%s,'riichi_sanma','bad',100,NULL)",(USERS[2],))
    with pytest.raises(ValueError): settle_rated_game(db,'fail','riichi_sanma_beginner_dongfeng',inputs())
    assert db.sql('SELECT user_id,rank_score,games FROM rule_ratings')==[(USERS[2],100,0)]
    assert db.sql('SELECT COUNT(*) FROM rating_settlements')[0][0]==0


def test_old_schema_constraint_migrates_without_touching_grades(db):
    db.sql("INSERT INTO rule_ratings(user_id,rule,rank_name,rank_score,elo) VALUES(%s,'riichi','七段',1234,NULL)",(USERS[0],))
    db.sql("ALTER TABLE rule_ratings DROP CONSTRAINT rule_ratings_rule_check; ALTER TABLE rule_ratings ADD CHECK(rule IN ('guobiao','riichi','qingque','sichuan'))")
    conn=db._get_connection()
    try:
        with conn.cursor() as c: ensure_rating_schema(c); ensure_rating_schema(c)
        conn.commit()
    finally: db._put_connection(conn)
    assert get_rank_data(db,USERS[0])['ratings']['riichi']['rank_score']==1234
    settle_rated_game(db,'new','riichi_sanma_beginner_dongfeng',inputs())


@pytest.mark.parametrize('rank,score,expected', [('初段',399,('二段',444)),('初段',0,('1级',1)),('十段',0,('十段',100))])
def test_promotion_demotion_and_permanent_title(db,rank,score,expected):
    db.sql("INSERT INTO rule_ratings(user_id,rule,rank_name,rank_score,elo) VALUES(%s,'riichi_sanma',%s,%s,NULL)",(USERS[0],rank,score))
    result=settle_rated_game(db,'grade','riichi_sanma_beginner_banzhuang',inputs((3,1,2)) if score==0 and rank=='初段' else inputs())
    assert (result[str(USERS[0])]['rank_after'],result[str(USERS[0])]['score_after'])==expected


def test_three_and_four_player_records_statistics_and_fans_are_separate(db):
    from .riichi.record_stats import ensure_schema, VERSION
    from .riichi.get_riichi_stats import get_riichi_fan_stats_total
    from .guobiao.get_rank_record_list import get_rank_record_list
    conn=db._get_connection()
    with conn.cursor() as c:ensure_schema(c)
    conn.commit();db._put_connection(conn)
    for gid,sub_rule,mode,room_type,games in (
        ('three','riichi/sanma','1/4_sanma_rank','match',1),
        ('four','riichi/mleague','1/4_rank','match',10),
        ('custom','riichi/sanma','1/4_sanma','custom',100)):
        db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)',(gid,Json({})))
        db.sql('INSERT INTO game_player_records VALUES(%s,%s,%s,0,1,0,%s,%s,%s,%s)',(gid,USERS[0],'test','riichi',sub_rule,mode,room_type))
        db.sql('INSERT INTO riichi_player_game_stats VALUES(%s,%s,%s,%s)',(gid,USERS[0],VERSION,Json({'total_games':games,'fan_stats':{'riichi':games}})))
    assert [r['game_id'] for r in get_rank_record_list(db,limit=1,rule='riichi_sanma')]==['three']
    assert [r['game_id'] for r in get_rank_record_list(db,limit=1,rule='riichi')]==['four']
    assert get_ranked_history(db,USERS[0],'riichi_sanma')[0]['total_games']==1
    assert get_ranked_history(db,USERS[0],'riichi')[0]['total_games']==10
    assert get_riichi_fan_stats_total(db,USERS[0],ranked=True,sanma=True)['riichi']==1
    assert get_riichi_fan_stats_total(db,USERS[0],ranked=True,sanma=False)['riichi']==10


def test_recent_placement_trends_filter_three_and_four_before_limit(db):
    from .player_recent_records import ensure_schema, get_player_recent_records
    conn=db._get_connection()
    with conn.cursor() as c:ensure_schema(c)
    conn.commit();db._put_connection(conn)
    # More recent four-player records must not evict the older Sanma trend.
    for i in range(12):
        gid=f'four-{i:02}'
        db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)',(gid,Json({})))
        db.sql('INSERT INTO game_player_records VALUES(%s,%s,%s,0,4,0,%s,%s,%s,%s)',
            (gid,USERS[0],'test','riichi','riichi/mleague','1/4_rank','match'))
    for gid,room in [('three','match'),('three-custom','custom')]:
        db.sql('INSERT INTO game_records(game_id,record) VALUES(%s,%s)',(gid,Json({})))
        db.sql('INSERT INTO game_player_records VALUES(%s,%s,%s,0,3,0,%s,%s,%s,%s)',
            (gid,USERS[0],'test','riichi','riichi/sanma','1/4_sanma_rank',room))
    db.sql("UPDATE game_records SET created_at='2026-01-01' WHERE game_id='three'")
    result=get_player_recent_records(db,USERS[0])
    assert [r['game_id'] for r in result['riichi_sanma']['match']['placements']]==['three']
    assert [r['rank'] for r in result['riichi']['match']['placements']]==[4]*10
    assert result['riichi_sanma']['custom']=={'placements':[], 'big_win':None}


def test_retries_cannot_change_pool_or_players(db):
    settle_rated_game(db,'identity','riichi_sanma_beginner_dongfeng',inputs())
    from .test_rule_ratings import players
    for queue,table in [('riichi_beginner_dongfeng',players()),
                        ('riichi_sanma_beginner_dongfeng',[dict(user_id=USERS[3],place=1),*inputs()[1:]])]:
        with pytest.raises(ValueError):settle_rated_game(db,'identity',queue,table)
    assert db.sql('SELECT COUNT(*) FROM rating_settlements')[0][0]==1
    assert db.sql("SELECT SUM(games) FROM rule_ratings WHERE rule='riichi_sanma'")[0][0]==3
