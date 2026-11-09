"""Committed rewards survive rate changes; client snapshots never authorize money."""
from copy import deepcopy

from psycopg2.extras import Json
import pytest

from server.database import commerce, game_rewards
from server.database.guobiao.store_guobiao import store_guobiao_game_record
from server.database.statistics_catalog import BY_SUB_RULE
from test_commerce import shop
from test_round_progress import record
from test_subrule_realtime import prepare, players, replay
from test_subrule_statistics import db


def test_v2_saved_replay_retries_under_v3_without_payload_conflict_or_recredit(db, monkeypatch):
    prepare(db)
    definition = BY_SUB_RULE['guobiao/standard']
    data = replay(definition)
    data['game_title'].update(room_type='match', end_time='2026-11-09T21:00:00+08:00',
        rating_result={str(10000101+i):dict(rating_system='grade', rating_pt=10) for i in range(4)})
    with monkeypatch.context() as old:
        old.setattr(game_rewards, 'REWARD_VERSION', 2)
        old.setattr(game_rewards, 'REWARD_MINOR_PER_HAND', 10)
        old.setattr(commerce, 'CALCULATION_VERSION', 2)
        game_id = store_guobiao_game_record(db, data, players(definition), 'match', '1/4_rank')
    assert game_id
    stored = deepcopy(db.sql('SELECT record FROM game_records WHERE game_id=%s', (game_id,))[0][0])
    assert stored['currency_reward']['version'] == 2
    assert {p['currency_delta_minor'] for p in stored['currency_reward']['players']} == {10}
    before = db.sql('SELECT user_id,balance_minor,earned_minor FROM user_wallets ORDER BY user_id')
    metrics_before = db.sql('SELECT COUNT(*) FROM game_player_metrics')

    forged = deepcopy(data)
    forged['currency_reward'] = dict(version=999, completed_hands=999, eligible=True,
        players=[dict(user_id=10000101+i, currency_delta_minor=999999, currency_reward_status='saved') for i in range(4)])
    assert store_guobiao_game_record(db, forged, players(definition), 'match', '1/4_rank') == game_id
    assert forged['currency_reward'] == stored['currency_reward']
    assert forged['match_result'] == stored['match_result']
    assert db.sql('SELECT record FROM game_records WHERE game_id=%s', (game_id,))[0][0] == stored
    assert db.sql('SELECT user_id,balance_minor,earned_minor FROM user_wallets ORDER BY user_id') == before
    assert db.sql('SELECT COUNT(*) FROM wallet_ledger') == [(4,)]
    assert db.sql('SELECT COUNT(*) FROM game_player_metrics') == metrics_before
    conn = db._get_connection()
    with conn.cursor() as cursor:
        outcome = commerce.settle_game_reward(cursor, game_id, forged)
    conn.commit()
    assert outcome == dict(credited_players=0, credited_minor=0)

    forged['game_round']['round_index_1']['action_ticks'].append(['changed replay content'])
    assert store_guobiao_game_record(db, forged, players(definition), 'match', '1/4_rank') is None
    assert db.sql('SELECT user_id,balance_minor,earned_minor FROM user_wallets ORDER BY user_id') == before


def legacy_record(hands=2):
    data = record(hands=hands)
    data['game_title']['room_type'] = 'match'
    for i in range(4):
        data['game_title'][f'p{i}_uid'] = 101+i
    return data


def settle(shop, game_id, data):
    conn = shop.pool.getconn()
    try:
        with conn.cursor() as cursor:
            result = commerce.settle_game_reward(cursor, game_id, data)
        conn.commit()
        return result
    finally:
        shop.pool.putconn(conn)


def test_persisted_v2_entitlement_with_missing_ledger_keeps_original_amount(shop):
    data = legacy_record()
    old = dict(version=2, completed_hands=2, eligible=True, players=[
        dict(user_id=uid, currency_delta_minor=0 if uid == 102 else 20,
             currency_reward_status='not_registered' if uid == 102 else 'saved') for uid in range(101, 105)])
    data['currency_reward'] = old
    shop.sql('INSERT INTO game_records VALUES(%s,%s)', ('v2-unpaid', Json(data)))
    shop.sql('INSERT INTO game_player_records SELECT %s,generate_series(101,104)', ('v2-unpaid',))
    forged = dict(data, currency_reward=dict(old, players=[]))
    assert settle(shop, 'v2-unpaid', forged) == dict(credited_players=3, credited_minor=60)
    assert commerce.get_wallet(shop.db, 101)['wallet_state']['balance_minor'] == 20
    assert shop.sql('SELECT DISTINCT calculation_version FROM wallet_ledger') == [(2,)]
    assert settle(shop, 'v2-unpaid', forged) == dict(credited_players=0, credited_minor=0)


@pytest.mark.parametrize('saved_replay', [False, True])
def test_legacy_ledger_without_snapshot_remains_idempotent_at_original_rate(shop, monkeypatch, saved_replay):
    data = legacy_record()
    game_id = 'ledger-only'
    if saved_replay:
        shop.sql('INSERT INTO game_records VALUES(%s,%s)', (game_id, Json(data)))
    shop.sql('INSERT INTO game_player_records SELECT %s,generate_series(101,104)', (game_id,))
    conn = shop.pool.getconn()
    try:
        with conn.cursor() as cursor, monkeypatch.context() as old:
            old.setattr(commerce, 'CALCULATION_VERSION', 2)
            for uid in (101, 103, 104):
                commerce._wallet_change(cursor, uid, 20, 'game_reward', game_id, hands=2)
        conn.commit()
        with conn.cursor() as cursor:
            snapshot = commerce.prepare_game_reward(cursor, data, [101,102,103,104], game_id=game_id)
        conn.rollback()
    finally:
        shop.pool.putconn(conn)
    assert snapshot['version'] == 2
    assert [p['currency_delta_minor'] for p in snapshot['players']] == [20, 0, 20, 20]
    assert settle(shop, game_id, data) == dict(credited_players=0, credited_minor=0)
    assert commerce.get_wallet(shop.db, 101)['wallet_state']['balance_minor'] == 20
    assert shop.sql('SELECT COUNT(*) FROM wallet_ledger') == [(3,)]


def test_unsaved_client_snapshot_cannot_forge_reward_version_or_amount(shop):
    data = legacy_record()
    data['currency_reward'] = dict(version=2, completed_hands=999, eligible=True,
        players=[dict(user_id=101, currency_delta_minor=999999, currency_reward_status='saved')])
    shop.sql('INSERT INTO game_player_records SELECT %s,generate_series(101,104)', ('new-game',))
    assert settle(shop, 'new-game', data) == dict(credited_players=3, credited_minor=30)
    assert commerce.get_wallet(shop.db, 101)['wallet_state']['balance_minor'] == 10
    assert shop.sql('SELECT DISTINCT calculation_version FROM wallet_ledger') == [(3,)]


def test_v2_three_player_snapshot_does_not_get_new_v3_retroactive_rewards(shop):
    data = legacy_record()
    del data['game_title']['p3_uid']
    data['game_title'].update(rule='riichi', sub_rule='riichi/sanma', player_count=3)
    data['currency_reward'] = dict(version=2, completed_hands=2, eligible=False, players=[
        dict(user_id=uid, currency_delta_minor=0, currency_reward_status='not_applicable') for uid in (101,102,103)])
    shop.sql('INSERT INTO game_records VALUES(%s,%s)', ('old-sanma', Json(data)))
    shop.sql('INSERT INTO game_player_records SELECT %s,generate_series(101,103)', ('old-sanma',))
    assert settle(shop, 'old-sanma', data) == dict(credited_players=0, credited_minor=0)
    assert shop.sql('SELECT COUNT(*) FROM wallet_ledger') == [(0,)]
