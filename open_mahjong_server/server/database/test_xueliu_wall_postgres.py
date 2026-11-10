"""Real persistence checks in a uniquely owned schema of an explicit test database."""
import json
import os
import uuid
from copy import deepcopy
from types import SimpleNamespace

import psycopg2
from psycopg2 import sql
from psycopg2.pool import ThreadedConnectionPool
import pytest

from .duplicate_walls import load_duplicate_wall, validate_duplicate_wall, reserve_duplicate_game, duplicate_record_is_visible
from .sichuan_duplicate import PairedMatchStore, ensure_tables
from ..game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE, make_board, flatten_board
from ..gamestate.public.sichuan_duplicate import new_match


@pytest.fixture
def isolated_db():
    dsn = os.environ.get('DUPLICATE_TEST_DATABASE_URL')
    if not dsn:
        pytest.skip('DUPLICATE_TEST_DATABASE_URL not configured')
    schema = 'xueliu_wall_test_' + uuid.uuid4().hex
    admin = psycopg2.connect(dsn)
    admin.autocommit = True
    with admin.cursor() as cursor:
        cursor.execute(sql.SQL('CREATE SCHEMA {}').format(sql.Identifier(schema)))
    pool = ThreadedConnectionPool(1, 4, dsn, options=f'-c search_path={schema}')
    db = SimpleNamespace(_get_connection=pool.getconn, _put_connection=pool.putconn)
    try:
        conn = db._get_connection()
        with conn.cursor() as cursor:
            ensure_tables(cursor)
        conn.commit()
        db._put_connection(conn)
        yield db
    finally:
        pool.closeall()
        with admin.cursor() as cursor:
            cursor.execute(sql.SQL('DROP SCHEMA {} CASCADE').format(sql.Identifier(schema)))
        admin.close()


@pytest.mark.parametrize('kind', ['manual', 'seed', 'key'])
@pytest.mark.parametrize('wall_rule,sub_rule,layout', [
    ('sichuan/sbr', SBR_DUPLICATE, 'standard'),
    ('sichuan/xueliu', XUELIU_DUPLICATE, 'xueliu'),
])
def test_sichuan_saved_wall_supports_independent_groups_and_immutable_source(isolated_db, kind, wall_rule, sub_rule, layout):
    db = isolated_db
    boards = [make_board('db-sichuan', n, layout=layout) for n in range(1, 5)]
    rounds = [flatten_board(b) for b in boards]
    key = 'dup_' + uuid.uuid4().hex
    conn = db._get_connection()
    with conn.cursor() as cursor:
        cursor.execute('''INSERT INTO duplicate_walls
            (key, owner_user_id, event_id, scope, wall_type, rule, tiles, seed, use_flowers, round_count, round_tiles)
            VALUES (%s,101,'test-event','event',%s,%s,%s,%s,FALSE,4,%s) RETURNING id''',
            (key, kind, wall_rule, json.dumps(rounds[0]), None if kind == 'manual' else 'db-sichuan', json.dumps(rounds)))
        wall_id = cursor.fetchone()[0]
    conn.commit()
    db._put_connection(conn)
    saved = load_duplicate_wall(db, key)
    validate_duplicate_wall(saved, dict(room_rule='sichuan', sub_rule=sub_rule, event_id='test-event'))
    for sub in ['sichuan/xueliu', 'sichuan/xueliu_exchange', 'sichuan/standard', 'sichuan/doubles', 'sichuan/xueliu_doubles']:
        with pytest.raises(ValueError, match='双打复式'):
            validate_duplicate_wall(saved, dict(room_rule='sichuan', sub_rule=sub, event_id='test-event'))
    with pytest.raises(ValueError, match='必须先绑定'):
        reserve_duplicate_game(db, wall_id)
    with pytest.raises(ValueError, match='不一致'):
        validate_duplicate_wall(saved, dict(room_rule='sichuan', sub_rule=SBR_DUPLICATE if layout == 'xueliu' else XUELIU_DUPLICATE, event_id='test-event'))
    snapshot = new_match(str(uuid.uuid4()), sub_rule, {'open':'1','closed':'2'}, boards, 'db-sichuan')
    snapshot.update(source_wall_id=wall_id, key=key, wall_type=kind)
    store = PairedMatchStore(db)
    store.create(snapshot, event_id='test-event', owner_id=101, name='saved wall')
    restored = store.unfinished()[0]
    assert restored['boards'] == boards
    assert restored['wall_id'] == wall_id and restored['key'] == key
    assert restored['profile']['wall_layout'] == layout
    duplicate = deepcopy(snapshot)
    duplicate['id'] = str(uuid.uuid4())
    duplicate['room_ids'] = {'open': '3', 'closed': '4'}
    saved_again = load_duplicate_wall(db, key)
    validate_duplicate_wall(saved_again, dict(room_rule='sichuan', sub_rule=sub_rule, event_id='test-event'))
    assert saved_again['wall_type'] == kind and saved_again['round_tiles'] == rounds
    store.create(duplicate, event_id='test-event', owner_id=101, name='duplicate')
    games = [reserve_duplicate_game(db, wall_id) for _ in range(4)]
    assert len(set(games)) == 4
    for game in games:
        assert not duplicate_record_is_visible(db, {'game_title': {'duplicate_key':key}}, game)
    count = 5 if layout == 'xueliu' else 8
    snapshot['boards'].extend(make_board('db-sichuan', n, layout=layout) for n in range(5, count + 1))
    snapshot['scores'] = [100, 0]
    snapshot['revision'] = 1
    store.save(snapshot, 0)
    restored = {item['id']: item for item in store.unfinished()}
    assert len(restored[snapshot['id']]['boards']) == count
    assert restored[snapshot['id']]['scores'] == [100, 0]
    assert restored[duplicate['id']]['scores'] == [0, 0]
    assert restored[duplicate['id']]['boards'] == boards
    conn = db._get_connection()
    with conn.cursor() as cursor:
        cursor.execute('SELECT count(*), max(round_count) FROM duplicate_walls')
        assert cursor.fetchone() == (1, 4)
        cursor.execute('SELECT wall_type,round_tiles FROM duplicate_walls WHERE id=%s', (wall_id,))
        assert cursor.fetchone() == (kind, rounds)
        cursor.execute('SELECT COUNT(*) FROM sichuan_duplicate_matches WHERE wall_id=%s', (wall_id,))
        assert cursor.fetchone() == (2,)
        cursor.execute('UPDATE duplicate_walls SET is_unlocked=TRUE WHERE id=%s', (wall_id,))
    conn.commit()
    db._put_connection(conn)
    blocked = deepcopy(duplicate); blocked['id'] = str(uuid.uuid4())
    with pytest.raises(ValueError, match='尚未解锁'):
        store.create(blocked, event_id='test-event', owner_id=101, name='late')


def test_old_xueliu_tiebreak_wall_reuse_restores_planned_source_only(isolated_db):
    db = isolated_db
    boards = [make_board('legacy', n, layout='xueliu') for n in range(1, 5)]
    first = new_match(str(uuid.uuid4()), XUELIU_DUPLICATE, {'open': '1', 'closed': '2'}, boards, 'legacy')
    store = PairedMatchStore(db)
    store.create(first, event_id='test-event', owner_id=101, name='legacy')
    extra = flatten_board(make_board('legacy', 5, layout='xueliu'))
    conn = db._get_connection()
    with conn.cursor() as cursor:
        cursor.execute('UPDATE duplicate_walls SET round_count=5,round_tiles=round_tiles || %s::jsonb WHERE id=%s',
                       (json.dumps([extra]), first['wall_id']))
    conn.commit(); db._put_connection(conn)
    saved = load_duplicate_wall(db, first['key'])
    assert saved['round_count'] == 4 and saved['legacy_source_round_count'] == 5
    validate_duplicate_wall(saved, dict(room_rule='sichuan', sub_rule=XUELIU_DUPLICATE, event_id='test-event'))
    second = new_match(str(uuid.uuid4()), XUELIU_DUPLICATE, {'open': '3', 'closed': '4'}, boards, 'legacy')
    second.update(source_wall_id=first['wall_id'], key=first['key'], legacy_source_round_count=5)
    store.create(second, event_id='test-event', owner_id=101, name='reuse')
    saved = load_duplicate_wall(db, first['key'])
    assert saved['round_count'] == 4 and 'legacy_source_round_count' not in saved


def test_concurrent_groups_share_one_key_without_extra_quota_or_snapshot_collisions(isolated_db):
    from concurrent.futures import ThreadPoolExecutor
    db = isolated_db
    store = PairedMatchStore(db)
    board = make_board('parallel', 1)
    first = new_match(str(uuid.uuid4()), SBR_DUPLICATE, {'open': '1', 'closed': '2'}, [board], 'parallel')
    store.create(first, event_id='test-event', owner_id=101, name='first')
    def create_group(number):
        match = new_match(str(uuid.uuid4()), SBR_DUPLICATE,
                          {'open': str(number * 2 + 1), 'closed': str(number * 2 + 2)}, [board], 'parallel')
        match.update(source_wall_id=first['wall_id'], key=first['key'], wall_type='key')
        store.create(match, event_id='test-event', owner_id=101, name=f'group {number}')
        return match
    with ThreadPoolExecutor(max_workers=3) as executor:
        matches = [first, *executor.map(create_group, [1, 2, 3])]
    restored = store.unfinished()
    assert len(restored) == 4
    assert {m['id'] for m in restored} == {m['id'] for m in matches}
    assert all(m['wall_id']==first['wall_id'] and m['boards']==[board] for m in restored)
    conn = db._get_connection()
    with conn.cursor() as cursor:
        cursor.execute('SELECT COUNT(*) FROM duplicate_walls')
        assert cursor.fetchone() == (1,)
    db._put_connection(conn)
