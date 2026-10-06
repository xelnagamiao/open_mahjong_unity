"""Server-only access to persisted duplicate walls and their record lock."""
import json
import re
import secrets
import string
from collections import Counter
from contextvars import ContextVar

duplicate_room_context = ContextVar("duplicate_room_context", default=None)
SUPPORTED_RULES = {"guobiao"}


def load_duplicate_wall(db, key):
    key = str(key or "").strip()
    if not re.fullmatch(r"[A-Za-z0-9_-]{8,40}", key):
        raise ValueError("复式密钥格式无效")
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute("SELECT id, key, rule, wall_type, tiles, event_id, deleted_at, seed, use_flowers, round_count, round_tiles FROM duplicate_walls WHERE key = %s", (key,))
            row = cursor.fetchone()
        if not row or row[6] is not None:
            raise ValueError("复式密钥不存在或已删除")
        tiles = json.loads(row[4]) if isinstance(row[4], str) else row[4]
        return dict(id=row[0], key=row[1], rule=row[2], wall_type=row[3], tiles=tiles,
                    event_id=row[5], seed=row[7], use_flowers=row[8], round_count=row[9],
                    round_tiles=json.loads(row[10]) if isinstance(row[10], str) else row[10])
    finally:
        db._put_connection(conn)


def validate_duplicate_wall(wall, room):
    rule = room.get("room_rule", "")
    sub_rule = room.get("sub_rule", "")
    if sub_rule == "guobiao/sanma":
        raise ValueError("三人国标不支持四人复式牌墙")
    if rule not in SUPPORTED_RULES:
        raise ValueError("复式牌墙仅支持国标麻将（含蓝十改）")
    if wall["rule"] not in (rule, sub_rule):
        raise ValueError("复式牌墙的玩法与房间不一致")
    if wall.get("event_id") and wall["event_id"] != room.get("event_id"):
        raise ValueError("比赛复式牌墙只能用于所属比赛")
    numbers = list(range(11, 20)) + list(range(21, 30)) + list(range(31, 40)) + list(range(41, 48))
    expected = Counter({tile: 4 for tile in numbers})
    use_flowers = wall.get("use_flowers")
    if use_flowers is None:
        # Older in-memory callers infer the flag from the persisted wall, never from a room default.
        use_flowers = any(tile in range(51, 59) for tile in wall.get("tiles", []))
    if type(use_flowers) is not bool:
        raise ValueError("复式牌墙花牌设置必须为布尔值")
    if (sub_rule == "guobiao/lanshi" or wall["rule"] == "guobiao/lanshi") and use_flowers:
        raise ValueError("蓝十改复式牌墙不能包含花牌")
    if use_flowers:
        expected.update(range(51, 59))
    count = wall.get("round_count", 1)
    if type(count) is not int or count not in (1, 4, 8, 12, 16):
        raise ValueError("复式局数必须为 1、4、8、12 或 16 局")
    rounds = wall.get("round_tiles")
    if rounds is None and count == 1:
        rounds = [wall.get("tiles")]
    if not isinstance(rounds, list) or len(rounds) != count:
        raise ValueError("复式牌山局数不完整")
    if rounds[0] != wall.get("tiles"):
        raise ValueError("复式首局牌山数据不一致")
    for index, tiles in enumerate(rounds, 1):
        if not isinstance(tiles, list) or any(type(tile) is not int for tile in tiles) or Counter(tiles) != expected:
            raise ValueError(f"第 {index} 局复式牌墙牌组与国标子规则不一致（请核对花牌与蓝十改设置）")
    wall.update(round_count=count, round_tiles=rounds, use_flowers=use_flowers)


def apply_duplicate_room_config(room, wall=None):
    wall = wall if wall is not None else duplicate_room_context.get()
    if not wall:
        return
    validate_duplicate_wall(wall, room)
    if room.get("random_seed"):
        raise ValueError("复式密钥与场景复现种子不能同时使用")
    room.update(is_duplicate=True, duplicate_key=wall["key"], duplicate_wall_type=wall["wall_type"],
                allow_spectator=False, random_seed=0, is_player_set_random_seed=False,
                duplicate_round_count=wall["round_count"], game_round=max(1, wall["round_count"] // 4),
                use_flowers=wall.get("use_flowers", any(tile in range(51, 59) for tile in wall["tiles"])))


def link_duplicate_record(cursor, game_id, game_record):
    key = (game_record.get("game_title") or {}).get("duplicate_key")
    if not key:
        return
    cursor.execute("""INSERT INTO duplicate_games (game_id, wall_id, ended_at)
        SELECT %s, id, NOW() FROM duplicate_walls WHERE key = %s
        ON CONFLICT (game_id) DO UPDATE SET ended_at = EXCLUDED.ended_at
        WHERE duplicate_games.wall_id = EXCLUDED.wall_id""", (game_id, key))
    if cursor.rowcount != 1:
        raise ValueError("复式牌谱无法关联原始牌墙")
    cursor.execute("UPDATE duplicate_walls SET ended_at = NOW() WHERE key = %s", (key,))


def reserve_duplicate_game(db, wall_id):
    game_id = ''.join(secrets.choice(string.ascii_letters + string.digits) for _ in range(16))
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute("SELECT id, rule FROM duplicate_walls WHERE id = %s AND deleted_at IS NULL FOR UPDATE", (wall_id,))
            row = cursor.fetchone()
            if not row:
                raise ValueError("复式牌墙已删除，无法开始对局")
            if row[1].split("/", 1)[0] != "guobiao":
                raise ValueError("复式牌墙仅支持国标麻将（含蓝十改）")
            cursor.execute("INSERT INTO duplicate_games (game_id, wall_id) VALUES (%s, %s)", (game_id, wall_id))
        conn.commit()
        return game_id
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def duplicate_record_is_visible(db, game_record, game_id=None):
    title = game_record.get("game_title") or {}
    key = title.get("duplicate_key") or None
    marked_duplicate = title.get("is_duplicate") is True or bool(title.get("duplicate_seed")) or isinstance(title.get("duplicate_tiles"), list)
    if not key and not game_id and not marked_duplicate:
        return True
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute("""SELECT
                (%s IS NULL OR EXISTS (SELECT 1 FROM duplicate_walls WHERE key = %s AND is_unlocked))
                AND NOT EXISTS (SELECT 1 FROM duplicate_games g
                    LEFT JOIN duplicate_walls w ON w.id = g.wall_id
                    WHERE g.game_id = %s AND w.is_unlocked IS DISTINCT FROM TRUE)
                AND (%s IS NOT NULL OR NOT %s OR EXISTS (
                    SELECT 1 FROM duplicate_games g JOIN duplicate_walls w ON w.id = g.wall_id
                    WHERE g.game_id = %s AND w.is_unlocked))""", (key, key, game_id, key, marked_duplicate, game_id))
            row = cursor.fetchone()
        return bool(row and row[0] is True)
    finally:
        db._put_connection(conn)


def mark_duplicate_game_ended(db, game_id):
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute("""UPDATE duplicate_games SET ended_at = NOW()
                WHERE game_id = %s AND ended_at IS NULL RETURNING wall_id""", (game_id,))
            row = cursor.fetchone()
            if row:
                cursor.execute("UPDATE duplicate_walls SET ended_at = NOW() WHERE id = %s", (row[0],))
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def ensure_duplicate_tables(cursor):
    cursor.execute("""
CREATE TABLE IF NOT EXISTS duplicate_walls (
  id BIGSERIAL PRIMARY KEY,
  key VARCHAR(40) NOT NULL UNIQUE,
  owner_user_id BIGINT NOT NULL,
  event_id VARCHAR(32) NULL,
  scope VARCHAR(16) NOT NULL CHECK (scope IN ('personal', 'event')),
  wall_type VARCHAR(16) NOT NULL CHECK (wall_type IN ('manual', 'seed', 'key')),
  rule VARCHAR(32) NOT NULL,
  name VARCHAR(80) NOT NULL DEFAULT '',
  tiles JSONB NOT NULL CHECK (jsonb_typeof(tiles) = 'array'),
  seed TEXT NULL,
  use_flowers BOOLEAN NOT NULL DEFAULT TRUE,
  is_unlocked BOOLEAN NOT NULL DEFAULT FALSE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
  unlocked_at TIMESTAMPTZ NULL,
  ended_at TIMESTAMPTZ NULL,
  deleted_at TIMESTAMPTZ NULL,
  CHECK ((scope = 'personal' AND event_id IS NULL) OR (scope = 'event' AND event_id IS NOT NULL))
);
ALTER TABLE duplicate_walls ADD COLUMN IF NOT EXISTS use_flowers BOOLEAN;
UPDATE duplicate_walls SET use_flowers = EXISTS (
  SELECT 1 FROM jsonb_array_elements(tiles) tile WHERE tile IN ('51'::jsonb, '52'::jsonb, '53'::jsonb, '54'::jsonb, '55'::jsonb, '56'::jsonb, '57'::jsonb, '58'::jsonb)
) WHERE use_flowers IS NULL;
ALTER TABLE duplicate_walls ALTER COLUMN use_flowers SET DEFAULT TRUE;
ALTER TABLE duplicate_walls ALTER COLUMN use_flowers SET NOT NULL;
ALTER TABLE duplicate_walls ADD COLUMN IF NOT EXISTS round_count INTEGER NOT NULL DEFAULT 1
  CHECK (round_count IN (1, 4, 8, 12, 16));
ALTER TABLE duplicate_walls ADD COLUMN IF NOT EXISTS round_tiles JSONB
  CHECK (round_tiles IS NULL OR (jsonb_typeof(round_tiles) = 'array' AND jsonb_array_length(round_tiles) = round_count));
CREATE INDEX IF NOT EXISTS idx_duplicate_walls_owner ON duplicate_walls(owner_user_id, scope, created_at);
CREATE INDEX IF NOT EXISTS idx_duplicate_walls_event ON duplicate_walls(event_id, created_at);
CREATE TABLE IF NOT EXISTS duplicate_games (
  game_id VARCHAR(16) PRIMARY KEY,
  wall_id BIGINT NOT NULL REFERENCES duplicate_walls(id),
  ended_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS idx_duplicate_games_wall ON duplicate_games(wall_id);
""")
