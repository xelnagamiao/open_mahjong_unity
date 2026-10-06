"""Optional PostgreSQL duplicate lifecycle tests, each in an isolated schema."""
import os
from types import SimpleNamespace
from uuid import uuid4

import psycopg2
from psycopg2.extras import Json
from psycopg2.pool import ThreadedConnectionPool
import pytest

from server.database.db_manager import DatabaseManager
from server.database.duplicate_walls import (
    ensure_duplicate_tables, load_duplicate_wall, reserve_duplicate_game,
    mark_duplicate_game_ended, validate_duplicate_wall, apply_duplicate_room_config, duplicate_record_is_visible,
)


@pytest.fixture
def duplicate_db(monkeypatch):
    dsn = os.environ.get("OM_RECENT_TEST_DSN")
    if not dsn:
        pytest.skip("OM_RECENT_TEST_DSN is required for isolated PostgreSQL tests")
    schema = "duplicate_python_test_" + uuid4().hex
    admin = psycopg2.connect(dsn)
    admin.autocommit = True
    with admin.cursor() as cursor:
        cursor.execute(f'CREATE SCHEMA "{schema}"')
    pool = ThreadedConnectionPool(1, 4, dsn=dsn, options=f"-c search_path={schema}")
    db = SimpleNamespace(_get_connection=pool.getconn, _put_connection=pool.putconn)
    conn = pool.getconn()
    with conn:
        with conn.cursor() as cursor:
            ensure_duplicate_tables(cursor)
            cursor.execute("""
                CREATE TABLE game_records (game_id VARCHAR(16) PRIMARY KEY, record JSONB NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP);
                CREATE TABLE game_player_records (
                    game_id VARCHAR(16) REFERENCES game_records(game_id), user_id BIGINT,
                    username TEXT, score INT, rank INT CHECK (rank BETWEEN 1 AND 4),
                    original_player_index INT, rule VARCHAR(10), sub_rule TEXT, match_type TEXT,
                    room_type TEXT, match_tier TEXT, event_id TEXT, title_used INT,
                    character_used INT, profile_used INT, voice_used INT, avatar_frame_used INT,
                    pt_change NUMERIC(12, 2),
                    PRIMARY KEY (game_id, user_id));
            """)
    pool.putconn(conn)
    from server.database import scene_stats
    monkeypatch.setattr(scene_stats, "record_game_metrics", lambda *args: None)
    try:
        yield db
    finally:
        pool.closeall()
        with admin.cursor() as cursor:
            cursor.execute(f'DROP SCHEMA "{schema}" CASCADE')
        admin.close()


def execute(db, sql, args=(), *, fetch=False):
    conn = db._get_connection()
    try:
        with conn:
            with conn.cursor() as cursor:
                cursor.execute(sql, args)
                return cursor.fetchone() if fetch else None
    finally:
        db._put_connection(conn)


def insert_wall(db, rule="guobiao"):
    return execute(db, """INSERT INTO duplicate_walls
        (key,owner_user_id,scope,wall_type,rule,tiles,seed)
        VALUES ('DUP_test01234567',10000001,'personal','key',%s,%s,'private-seed') RETURNING id""",
        (rule, Json([11] * 4)), fetch=True)[0]


@pytest.mark.parametrize("count", [1, 4, 8, 12, 16])
def test_database_series_roundtrip_and_legacy_single_round(duplicate_db, count):
    db = duplicate_db
    wall_id = insert_wall(db)
    tiles = [tile for tile in list(range(11, 20)) + list(range(21, 30)) + list(range(31, 40)) + list(range(41, 48)) for _ in range(4)]
    rounds = [tiles[index:] + tiles[:index] for index in range(count)]
    execute(db, "UPDATE duplicate_walls SET tiles=%s,use_flowers=FALSE,round_count=%s,round_tiles=%s WHERE id=%s",
            (Json(tiles), count, Json(rounds), wall_id))
    wall = load_duplicate_wall(db, "DUP_test01234567")
    room = {"room_rule": "guobiao", "sub_rule": "guobiao/standard", "game_round": 4}
    apply_duplicate_room_config(room, wall)
    assert wall["round_tiles"] == rounds
    assert room["duplicate_round_count"] == count
    assert room["game_round"] == max(1, count // 4)
    if count == 1:
        execute(db, "UPDATE duplicate_walls SET round_tiles=NULL WHERE id=%s", (wall_id,))
        legacy = load_duplicate_wall(db, wall["key"])
        apply_duplicate_room_config(room, legacy)
        assert legacy["round_tiles"] == [tiles]


@pytest.mark.parametrize("wall_rule", ["guobiao", "guobiao/lanshi"])
def test_store_links_same_reserved_game_and_rechecks_record_lock(duplicate_db, wall_rule):
    db = duplicate_db
    rule = "guobiao"
    wall_id = insert_wall(db, wall_rule)
    wall = load_duplicate_wall(db, "DUP_test01234567")
    assert wall["id"] == wall_id
    assert wall["seed"] == "private-seed"  # Internal service only; never a live payload.
    assert isinstance(wall["use_flowers"], bool)
    game_id = reserve_duplicate_game(db, wall_id)
    assert execute(db, "SELECT ended_at FROM duplicate_games WHERE game_id=%s", (game_id,), fetch=True) == (None,)
    record = {"game_title": {"rule": rule, "sub_rule": "guobiao/lanshi" if wall_rule.endswith("lanshi") else "guobiao/standard", "room_type": "custom",
                             "duplicate_key": wall["key"], "duplicate_game_id": game_id, "max_round": 1},
              "game_round": {}}
    players = [SimpleNamespace(user_id=10000001+i, username=f"player{i}", score=0, original_player_index=i,
                               record_counter=SimpleNamespace(rank_result=i+1)) for i in range(4)]
    store = getattr(DatabaseManager, f"store_{rule}_game_record")
    assert store(db, record, players, "custom", "duplicate") == game_id
    assert execute(db, "SELECT COUNT(*), COUNT(pt_change) FROM game_player_records WHERE game_id=%s",
                   (game_id,), fetch=True) == (4, 0)
    ended = execute(db, """SELECT g.ended_at, w.ended_at FROM duplicate_games g
        JOIN duplicate_walls w ON w.id=g.wall_id WHERE game_id=%s""", (game_id,), fetch=True)
    assert all(ended)
    assert DatabaseManager.get_record_by_id(db, game_id) is None
    execute(db, "UPDATE duplicate_walls SET is_unlocked=TRUE WHERE id=%s", (wall_id,))
    assert DatabaseManager.get_record_by_id(db, game_id) is not None
    execute(db, """UPDATE game_records SET record=jsonb_set(record,
        '{game_title,duplicate_key}', '"DUP_unknown00000"') WHERE game_id=%s""", (game_id,))
    assert DatabaseManager.get_record_by_id(db, game_id) is None
    execute(db, "UPDATE game_records SET record=%s WHERE game_id=%s", (Json(record), game_id))
    execute(db, "UPDATE duplicate_walls SET is_unlocked=FALSE WHERE id=%s", (wall_id,))
    assert DatabaseManager.get_record_by_id(db, game_id) is None
    # Association still protects records whose old header lacks the key.
    execute(db, "UPDATE game_records SET record=record #- '{game_title,duplicate_key}' WHERE game_id=%s", (game_id,))
    assert DatabaseManager.get_record_by_id(db, game_id) is None


@pytest.mark.parametrize("rule", ["qingque", "classical", "changsha", "sichuan", "riichi", "jiandan", "taiwan"])
def test_historical_non_guobiao_records_keep_locks_but_cannot_start_new_games(duplicate_db, rule):
    db = duplicate_db
    wall_id = insert_wall(db, rule)
    game_id = "historicdupgame1"
    record = {"game_title": {"rule": rule, "sub_rule": f"{rule}/standard", "room_type": "custom",
                             "duplicate_key": "DUP_test01234567"}, "game_round": {}}
    execute(db, "INSERT INTO game_records (game_id, record) VALUES (%s, %s)", (game_id, Json(record)))
    execute(db, "INSERT INTO duplicate_games (game_id, wall_id, ended_at) VALUES (%s, %s, NOW())", (game_id, wall_id))
    assert DatabaseManager.get_record_by_id(db, game_id) is None
    execute(db, "UPDATE duplicate_walls SET is_unlocked=TRUE WHERE id=%s", (wall_id,))
    assert DatabaseManager.get_record_by_id(db, game_id) is not None
    execute(db, "UPDATE duplicate_walls SET is_unlocked=FALSE WHERE id=%s", (wall_id,))
    execute(db, "UPDATE game_records SET record=record #- '{game_title,duplicate_key}' WHERE game_id=%s", (game_id,))
    assert DatabaseManager.get_record_by_id(db, game_id) is None
    with pytest.raises(ValueError, match="仅支持国标"):
        reserve_duplicate_game(db, wall_id)
    assert execute(db, "SELECT COUNT(*) FROM duplicate_games", fetch=True) == (1,)


def test_deleted_wall_cannot_race_game_reservation(duplicate_db):
    wall_id = insert_wall(duplicate_db)
    load_duplicate_wall(duplicate_db, "DUP_test01234567")
    execute(duplicate_db, "UPDATE duplicate_walls SET deleted_at=NOW() WHERE id=%s", (wall_id,))
    with pytest.raises(ValueError, match="已删除"):
        reserve_duplicate_game(duplicate_db, wall_id)
    assert execute(duplicate_db, "SELECT COUNT(*) FROM duplicate_games", fetch=True) == (0,)


def test_cancelled_match_gets_end_time_without_unlock(duplicate_db):
    wall_id = insert_wall(duplicate_db)
    game_id = reserve_duplicate_game(duplicate_db, wall_id)
    mark_duplicate_game_ended(duplicate_db, game_id)
    ended, unlocked = execute(duplicate_db, """SELECT g.ended_at, w.is_unlocked FROM duplicate_games g
        JOIN duplicate_walls w ON w.id=g.wall_id WHERE game_id=%s""", (game_id,), fetch=True)
    assert ended is not None and unlocked is False


@pytest.mark.parametrize("rule,use_flowers", [("guobiao", True), ("guobiao", False), ("guobiao/lanshi", False)])
def test_load_and_apply_flowers_use_the_stored_wall_not_the_room_default(duplicate_db, rule, use_flowers):
    tiles = [tile for tile in list(range(11, 20)) + list(range(21, 30)) + list(range(31, 40)) + list(range(41, 48)) for _ in range(4)]
    if use_flowers:
        tiles += list(range(51, 59))
    wall_id = insert_wall(duplicate_db, rule)
    execute(duplicate_db, "UPDATE duplicate_walls SET tiles=%s, use_flowers=%s WHERE id=%s", (Json(tiles), use_flowers, wall_id))
    wall = load_duplicate_wall(duplicate_db, "DUP_test01234567")
    assert wall["seed"] == "private-seed" and wall["use_flowers"] is use_flowers
    room = {"room_rule": "guobiao", "sub_rule": "guobiao/lanshi" if rule.endswith("lanshi") else "guobiao/standard", "use_flowers": not use_flowers}
    apply_duplicate_room_config(room, wall)
    assert room["use_flowers"] is use_flowers
    assert "seed" not in room and "tiles" not in room
    with pytest.raises(ValueError, match="牌组|花牌"):
        validate_duplicate_wall({**wall, "use_flowers": not use_flowers}, room)


def test_legacy_schema_migration_infers_flowers_and_preserves_locked_seeds(duplicate_db):
    db = duplicate_db
    first_id = insert_wall(db)
    execute(db, "UPDATE duplicate_walls SET tiles=%s WHERE id=%s", (Json([11, 51]), first_id))
    execute(db, """INSERT INTO duplicate_walls (key, owner_user_id, scope, wall_type, rule, tiles, seed)
        VALUES ('DUP_legacy_no_flower',10000001,'personal','seed','guobiao/lanshi',%s,'known-seed')""", (Json([11, 12]),))
    execute(db, "ALTER TABLE duplicate_walls DROP COLUMN use_flowers")
    conn = db._get_connection()
    try:
        with conn:
            with conn.cursor() as cursor:
                ensure_duplicate_tables(cursor)
                ensure_duplicate_tables(cursor)
    finally:
        db._put_connection(conn)
    assert execute(db, "SELECT use_flowers,seed,is_unlocked,unlocked_at FROM duplicate_walls WHERE id=%s", (first_id,), fetch=True) == (True, "private-seed", False, None)
    assert execute(db, "SELECT use_flowers,seed,is_unlocked FROM duplicate_walls WHERE key='DUP_legacy_no_flower'", fetch=True) == (False, "known-seed", False)


def test_seed_and_full_wall_stay_locked_then_survive_public_record_read_after_delete(duplicate_db):
    db = duplicate_db
    wall_id = insert_wall(db)
    game_id = reserve_duplicate_game(db, wall_id)
    record = {"game_title": {"rule": "guobiao", "duplicate_key": "DUP_test01234567", "is_duplicate": True,
                             "duplicate_seed": "private-seed", "duplicate_tiles": [11, 12, 13]}}
    execute(db, "INSERT INTO game_records (game_id,record) VALUES (%s,%s)", (game_id, Json(record)))
    assert DatabaseManager.get_record_by_id(db, game_id) is None
    execute(db, "UPDATE duplicate_walls SET is_unlocked=TRUE,unlocked_at=NOW(),deleted_at=NOW() WHERE id=%s", (wall_id,))
    assert duplicate_record_is_visible(db, record, game_id) is True
    public = DatabaseManager.get_record_by_id(db, game_id)
    assert public is not None
    assert "private-seed" in str(public) and "duplicate_tiles" in str(public)
    with pytest.raises(ValueError, match="已删除"):
        load_duplicate_wall(db, "DUP_test01234567")


@pytest.mark.parametrize("marker", [{"is_duplicate": True}, {"duplicate_seed": "secret"}, {"duplicate_tiles": [11, 12]}])
def test_duplicate_marker_without_key_fails_closed_until_association_is_unlocked(duplicate_db, marker):
    db = duplicate_db
    record = {"game_title": marker}
    assert duplicate_record_is_visible(db, record) is False
    assert duplicate_record_is_visible(db, record, "missinggame") is False
    wall_id = insert_wall(db)
    game_id = reserve_duplicate_game(db, wall_id)
    assert duplicate_record_is_visible(db, record, game_id) is False
    execute(db, "UPDATE duplicate_walls SET is_unlocked=TRUE WHERE id=%s", (wall_id,))
    assert duplicate_record_is_visible(db, record, game_id) is True
