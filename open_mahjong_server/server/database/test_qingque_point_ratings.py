from concurrent.futures import ThreadPoolExecutor
from types import SimpleNamespace as NS

import pytest

from .test_rule_ratings import db, USERS, pytestmark
from .rule_ratings import settle_rated_game
from ..match.qingque_score_elo import ALGORITHM


def inputs():
    return [dict(user_id=uid,place=i+1,net_points=score) for i,(uid,score) in enumerate(zip(USERS,(800,200,-200,-800)))]


def test_points_atomic_retry_and_saved_evidence(db):
    def settle(_):
        return settle_rated_game(db,'points-game','qingque_elo_unlimited',inputs(),
                                 completed_hands=16,rating_algorithm=ALGORITHM)
    with ThreadPoolExecutor(max_workers=4) as pool:
        results = list(pool.map(settle,range(4)))
    assert all(result==results[0] for result in results)
    assert [results[0][str(uid)]['elo_delta'] for uid in USERS] == [16,4,-4,-16]
    assert db.sql("SELECT games FROM rule_ratings WHERE rule='qingque' ORDER BY user_id") == [(1,)]*4
    with pytest.raises(ValueError,match='不一致'):
        settle_rated_game(db,'points-game','qingque_elo_unlimited',inputs(),completed_hands=17)
    assert db.sql('SELECT count(*) FROM rating_settlements')[0][0] == 1


def test_fixed_and_unlimited_share_points_pool_without_rewriting_legacy(db):
    old = settle_rated_game(db,'old','qingque_elo_quanzhuang',inputs())
    assert [old[str(uid)]['elo_delta'] for uid in USERS] == [16,5.33,-5.33,-16]
    result = settle_rated_game(db,'new','qingque_elo_quanzhuang',inputs(),completed_hands=16,rating_algorithm=ALGORITHM)
    assert all(row['rating_algorithm']==ALGORITHM and row['rating_games']==2 for row in result.values())
    assert settle_rated_game(db,'old','qingque_elo_quanzhuang',inputs()) == old


def test_missing_or_invalid_evidence_never_falls_back_to_place(db):
    for n in (None,0,-1):
        with pytest.raises(ValueError):
            settle_rated_game(db,'bad','qingque_elo_unlimited',inputs(),completed_hands=n)
    bad = inputs();bad[0]['net_points'] += 1
    with pytest.raises(ValueError): settle_rated_game(db,'bad','qingque_elo_unlimited',bad,completed_hands=16)
    assert db.sql('SELECT count(*) FROM rating_settlements') == [(0,)]


@pytest.fixture
def replay_db(db):
    from .player_recent_records import ensure_schema
    from .riichi.record_stats import BASE_KEYS
    fields = [key for key in BASE_KEYS if key != 'total_games']
    db.sql("""ALTER TABLE game_player_records ADD COLUMN match_tier TEXT,
        ADD COLUMN event_id TEXT, ADD COLUMN title_used INT, ADD COLUMN character_used INT,
        ADD COLUMN profile_used INT, ADD COLUMN voice_used INT, ADD COLUMN avatar_frame_used INT;
        """ + f"""CREATE TABLE game_player_metrics(game_id TEXT,user_id BIGINT,username TEXT,
        rule TEXT,sub_rule TEXT,room_type TEXT,match_tier TEXT,event_id TEXT,game_type TEXT,
        match_type TEXT,score INT,rank INT,{','.join(key+' INT DEFAULT 0' for key in fields)},
        created_at TIMESTAMPTZ DEFAULT NOW(),fan_stats JSONB,PRIMARY KEY(game_id,user_id));""")
    conn = db._get_connection()
    with conn.cursor() as cur: ensure_schema(cur)
    conn.commit(); db._put_connection(conn)
    return db


def checkpoint_game(db):
    from ..match.qingque_session import ALGORITHM
    players = [NS(user_id=uid, username=f'p{i}', score=score, original_player_index=i)
               for i,(uid,score) in enumerate(zip(USERS,(300,-100,-100,-100)))]
    record = dict(game_title=dict(rule='qingque',sub_rule='qingque/standard',room_type='match',
        unlimited_rounds=True,**{f'p{i}_uid': uid for i,uid in enumerate(USERS)}),
        game_round={'round_index_1': dict(seats=[0,1,2,3],start_player_index=0,
            action_ticks=[['reset',0],['hu_self',0,100,['门前清'],[300,-100,-100,-100]],['end']])})
    return NS(room_rule='qingque',room_type='match',match_rating_algorithm=ALGORITHM,
        match_queue_type='qingque_elo_unlimited',gamestate_id='durable-game',
        player_list=players,game_record=record,master_seed=123,db_manager=db)


def test_restart_settles_last_complete_hand_publishes_replay_and_statistics_once(replay_db):
    from ..match.qingque_session import checkpoint_state, recover_checkpoints, finish_checkpoint
    state = checkpoint_game(replay_db)
    checkpoint_state(state)
    # Live play has started a second hand; these uncommitted points must not leak.
    state.player_list[0].score += 500
    state.player_list[1].score -= 500
    state.game_record['game_round']['round_index_2'] = dict(action_ticks=[['reset',1]])
    recover_checkpoints(replay_db, include_open=True)
    result, cloud_id, record = finish_checkpoint(replay_db, 'durable-game')
    assert result[str(USERS[0])]['rating_net_points'] == 300
    assert result[str(USERS[0])]['rating_completed_hands'] == 1
    assert list(record['game_round']) == ['round_index_1']
    assert record['game_title']['finish_reason'] == 'server_restart'
    assert replay_db.sql('SELECT match_type FROM game_player_records') == [('unlimited/4_rank',)]*4
    assert replay_db.sql('SELECT game_type,total_rounds FROM game_player_metrics') == [('unlimited',1)]*4
    recover_checkpoints(replay_db, include_open=True)
    assert finish_checkpoint(replay_db, 'durable-game')[1] == cloud_id
    assert replay_db.sql('SELECT count(*) FROM game_records') == [(1,)]
    assert replay_db.sql('SELECT games FROM rule_ratings') == [(1,)]*4


def test_replay_failure_retries_without_second_rating_and_frozen_scores_cannot_change(replay_db, monkeypatch):
    from ..match.qingque_session import checkpoint_state, finish_checkpoint
    from .qingque import store_qingque
    state = checkpoint_game(replay_db)
    checkpoint_state(state, final=True)
    writer = store_qingque.store_qingque_game_record
    monkeypatch.setattr(store_qingque, 'store_qingque_game_record', lambda *args: None)
    with pytest.raises(RuntimeError): finish_checkpoint(replay_db, 'durable-game')
    assert replay_db.sql('SELECT games FROM rule_ratings') == [(1,)]*4
    state.player_list[0].score += 1000; state.player_list[1].score -= 1000
    checkpoint_state(state, final=True)
    monkeypatch.setattr(store_qingque, 'store_qingque_game_record', writer)
    result, cloud_id, _ = finish_checkpoint(replay_db, 'durable-game')
    assert result[str(USERS[0])]['rating_net_points'] == 300
    assert cloud_id and replay_db.sql('SELECT games FROM rule_ratings') == [(1,)]*4


def test_zero_completed_hands_do_not_create_rating_checkpoint(replay_db):
    from ..match.qingque_session import checkpoint_state, finish_aborted_state
    state = checkpoint_game(replay_db)
    state.game_record['game_round']['round_index_1']['action_ticks'].pop()
    checkpoint_state(state)
    finish_aborted_state(state)
    assert replay_db.sql('SELECT count(*) FROM qingque_rating_sessions') == [(0,)]
    assert replay_db.sql('SELECT count(*) FROM rating_settlements') == [(0,)]
