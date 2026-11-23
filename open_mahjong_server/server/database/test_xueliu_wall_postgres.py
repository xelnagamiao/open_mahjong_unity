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
from .sichuan_duplicate import attach_paired_wall, ensure_tables
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
    attach_paired_wall(db, snapshot, event_id='test-event', owner_id=101, name='saved wall')
    duplicate = deepcopy(snapshot)
    duplicate['id'] = str(uuid.uuid4())
    duplicate['room_ids'] = {'open': '3', 'closed': '4'}
    saved_again = load_duplicate_wall(db, key)
    validate_duplicate_wall(saved_again, dict(room_rule='sichuan', sub_rule=sub_rule, event_id='test-event'))
    assert saved_again['wall_type'] == kind and saved_again['round_tiles'] == rounds
    attach_paired_wall(db, duplicate, event_id='test-event', owner_id=101, name='duplicate')
    games = [gid for match in (snapshot, duplicate) for gid in match["table_record_ids"].values()]
    assert len(set(games)) == 4
    for game in games:
        assert not duplicate_record_is_visible(db, {'game_title': {'duplicate_key':key}}, game)
    count = 5 if layout == 'xueliu' else 8
    snapshot['boards'].extend(make_board('db-sichuan', n, layout=layout) for n in range(5, count + 1))
    snapshot['scores'] = [100, 0]
    snapshot['revision'] = 1
    assert duplicate['boards'] == boards and duplicate['scores'] == [0, 0]
    conn = db._get_connection()
    with conn.cursor() as cursor:
        cursor.execute('SELECT count(*), max(round_count) FROM duplicate_walls')
        assert cursor.fetchone() == (1, 4)
        cursor.execute('SELECT wall_type,round_tiles FROM duplicate_walls WHERE id=%s', (wall_id,))
        assert cursor.fetchone() == (kind, rounds)
        cursor.execute("SELECT count(*) FROM duplicate_games")
        assert cursor.fetchone() == (0,)
        cursor.execute('SELECT COUNT(*) FROM sichuan_duplicate_matches WHERE wall_id=%s', (wall_id,))
        assert cursor.fetchone() == (0,)
        cursor.execute('UPDATE duplicate_walls SET is_unlocked=TRUE WHERE id=%s', (wall_id,))
    conn.commit()
    db._put_connection(conn)
    blocked = deepcopy(duplicate); blocked['id'] = str(uuid.uuid4())
    with pytest.raises(ValueError, match='尚未解锁'):
        attach_paired_wall(db, blocked, event_id='test-event', owner_id=101, name='late')


def test_group_creation_yields_to_concurrent_key_management(isolated_db):
    db = isolated_db
    board = make_board('key-management', 1)
    asset = new_match(str(uuid.uuid4()), SBR_DUPLICATE, {'open': '1', 'closed': '2'}, [board], 'key-management')
    attach_paired_wall(db, asset, event_id='test-event', owner_id=101, name='asset')
    other = new_match(str(uuid.uuid4()), SBR_DUPLICATE, {'open': '3', 'closed': '4'}, [board], 'key-management')
    other.update(source_wall_id=asset['wall_id'], key=asset['key'])
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute('SELECT id FROM duplicate_walls WHERE id=%s FOR UPDATE', (asset['wall_id'],))
            with pytest.raises(ValueError, match='密钥正在变更'):
                attach_paired_wall(db, other, event_id='test-event', owner_id=101, name='other')
    finally:
        conn.rollback()
        db._put_connection(conn)
    attach_paired_wall(db, other, event_id='test-event', owner_id=101, name='other')
    assert set(asset['table_record_ids'].values()).isdisjoint(other['table_record_ids'].values())


def test_legacy_tiebreak_asset_keeps_its_planned_length_without_live_snapshot_access(isolated_db):
    db = isolated_db
    boards = [make_board('legacy', n, layout='xueliu') for n in range(1, 5)]
    first = new_match(str(uuid.uuid4()), XUELIU_DUPLICATE, {'open': '1', 'closed': '2'}, boards, 'legacy')
    attach_paired_wall(db, first, event_id='test-event', owner_id=101, name='legacy')
    extra = flatten_board(make_board('legacy', 5, layout='xueliu'))
    conn = db._get_connection()
    with conn.cursor() as cursor:
        cursor.execute("INSERT INTO sichuan_duplicate_matches(id,wall_id,event_id,status,data) VALUES(%s,%s,%s,'completed',%s)",
                       (first['id'], first['wall_id'], 'test-event', json.dumps(first)))
        cursor.execute('UPDATE duplicate_walls SET round_count=5,round_tiles=round_tiles || %s::jsonb WHERE id=%s',
                       (json.dumps([extra]), first['wall_id']))
        # Simulate an upgrade from the previous schema, then remove access to
        # the snapshot relation to exercise the normal room creation path.
        cursor.execute('ALTER TABLE duplicate_walls DROP COLUMN legacy_planned_boards')
        ensure_tables(cursor)
        cursor.execute('ALTER TABLE sichuan_duplicate_matches RENAME TO legacy_match_archive')
    conn.commit()
    db._put_connection(conn)
    saved = load_duplicate_wall(db, first['key'])
    assert saved['round_count'] == 4 and len(saved['round_tiles']) == 4
    validate_duplicate_wall(saved, dict(room_rule='sichuan', sub_rule=XUELIU_DUPLICATE, event_id='test-event'))
    second = new_match(str(uuid.uuid4()), XUELIU_DUPLICATE, {'open': '3', 'closed': '4'}, boards, 'legacy')
    second.update(source_wall_id=first['wall_id'], key=first['key'])
    attach_paired_wall(db, second, event_id='test-event', owner_id=101, name='reuse')
    saved = load_duplicate_wall(db, first['key'])
    assert saved['round_count'] == 4 and saved['round_tiles'] == [flatten_board(b) for b in boards]
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute('SELECT round_count, legacy_planned_boards, round_tiles->4 FROM duplicate_walls WHERE id=%s',
                           (first['wall_id'],))
            assert cursor.fetchone() == (5, 4, extra)
    finally:
        db._put_connection(conn)


def test_concurrent_groups_share_one_key_without_extra_quota_or_snapshot_collisions(isolated_db):
    from concurrent.futures import ThreadPoolExecutor
    db = isolated_db
    board = make_board('parallel', 1)
    first = new_match(str(uuid.uuid4()), SBR_DUPLICATE, {'open': '1', 'closed': '2'}, [board], 'parallel')
    attach_paired_wall(db, first, event_id='test-event', owner_id=101, name='first')
    def create_group(number):
        match = new_match(str(uuid.uuid4()), SBR_DUPLICATE,
                          {'open': str(number * 2 + 1), 'closed': str(number * 2 + 2)}, [board], 'parallel')
        match.update(source_wall_id=first['wall_id'], key=first['key'], wall_type='key')
        attach_paired_wall(db, match, event_id='test-event', owner_id=101, name=f'group {number}')
        return match
    with ThreadPoolExecutor(max_workers=3) as executor:
        matches = [first, *executor.map(create_group, [1, 2, 3])]
    assert len({gid for m in matches for gid in m['table_record_ids'].values()}) == 8
    assert all(m['wall_id'] == first['wall_id'] and m['boards'] == [board] for m in matches)
    conn = db._get_connection()
    with conn.cursor() as cursor:
        cursor.execute('SELECT COUNT(*) FROM duplicate_walls')
        assert cursor.fetchone() == (1,)
    db._put_connection(conn)
