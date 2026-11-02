"""Real persistence checks in a uniquely owned schema of an explicit test database."""
import json
import os
import uuid
from copy import deepcopy
from types import SimpleNamespace

import psycopg2
from psycopg2 import sql
from psycopg2.pool import SimpleConnectionPool
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
    pool = SimpleConnectionPool(1, 4, dsn, options=f'-c search_path={schema}')
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
def test_sichuan_saved_wall_attaches_once_and_restores_exact_boards(isolated_db, kind, wall_rule, sub_rule, layout):
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
    with pytest.raises(ValueError, match='已绑定'):
        store.create(duplicate, event_id='test-event', owner_id=101, name='duplicate')
    games = [reserve_duplicate_game(db, wall_id) for _ in range(2)]
    assert len(set(games)) == 2
    for game in games:
        assert not duplicate_record_is_visible(db, {'game_title': {'duplicate_key':key}}, game)
    count = 5 if layout == 'xueliu' else 8
    snapshot['boards'].extend(make_board('db-sichuan', n, layout=layout) for n in range(5, count + 1))
    snapshot['revision'] = 1
    store.save(snapshot, 0)
    assert len(store.unfinished()[0]['boards']) == count
    conn = db._get_connection()
    with conn.cursor() as cursor:
        cursor.execute('SELECT count(*), max(round_count) FROM duplicate_walls')
        assert cursor.fetchone() == (1, count)
        cursor.execute('SELECT wall_type FROM duplicate_walls WHERE id=%s', (wall_id,))
        assert cursor.fetchone()[0] == kind
    db._put_connection(conn)
