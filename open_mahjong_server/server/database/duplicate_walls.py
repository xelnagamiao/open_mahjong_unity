"""Server-only access to persisted duplicate walls and their record lock."""
import json
import re
import secrets
import string
from collections import Counter
from contextvars import ContextVar

duplicate_room_context = ContextVar("duplicate_room_context", default=None)
SUPPORTED_RULES = {"guobiao"}
XUELIU_WALL_RULE = "sichuan/xueliu"
SBR_WALL_RULE = "sichuan/sbr"
SBR_WALL_SUBRULE = "sichuan/doubles_duplicate"
XUELIU_WALL_SUBRULE = "sichuan/xueliu_doubles_duplicate"


class DuplicateRecordLockedError(PermissionError):
    """The record exists, but its duplicate key has not been released."""

    def __init__(self):
        super().__init__("该复式密钥未解禁，牌谱暂不可查看或下载")


def supports_duplicate_room(room):
    return room.get("room_rule") == "guobiao" or (
        room.get("room_rule") == "sichuan" and (room.get("sub_rule") == XUELIU_WALL_SUBRULE
                                                or room.get("sub_rule") == SBR_WALL_SUBRULE))


def load_duplicate_wall(db, key):
    key = str(key or "").strip()
    if not re.fullmatch(r"[A-Za-z0-9_-]{8,40}", key):
        raise ValueError("复式密钥格式无效")
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute("SELECT id, key, rule, wall_type, tiles, event_id, deleted_at, seed, use_flowers, round_count, round_tiles, scope, owner_user_id FROM duplicate_walls WHERE key = %s", (key,))
            row = cursor.fetchone()
            planned_count = None
            if row and row[2] in (SBR_WALL_SUBRULE, XUELIU_WALL_SUBRULE):
                cursor.execute("SELECT MIN((data->>'planned_boards')::integer) FROM sichuan_duplicate_matches WHERE wall_id=%s", (row[0],))
                planned_count = cursor.fetchone()[0]
        if not row or row[6] is not None:
            raise ValueError("复式密钥不存在或已删除")
        tiles = json.loads(row[4]) if isinstance(row[4], str) else row[4]
        wall = dict(id=row[0], key=row[1], rule=row[2], wall_type=row[3], tiles=tiles,
                    event_id=row[5], seed=row[7], use_flowers=row[8], round_count=row[9], scope=row[11], owner_user_id=row[12],
                    round_tiles=json.loads(row[10]) if isinstance(row[10], str) else row[10])
        if (planned_count in (1, 4, 8, 12, 16) and isinstance(wall["round_tiles"], list)
                and planned_count < wall["round_count"] and len(wall["round_tiles"]) == wall["round_count"]):
            wall["legacy_source_round_count"] = wall["round_count"]
            wall.update(round_count=planned_count, round_tiles=wall["round_tiles"][:planned_count])
        return wall
    finally:
        db._put_connection(conn)


def validate_duplicate_wall(wall, room):
    rule = room.get("room_rule", "")
    sub_rule = room.get("sub_rule", "")
    if sub_rule in ("guobiao/transparent", "guobiao/transparent_realtime", "guobiao/four_jokers", "guobiao/huahun", "guobiao/sixteen"):
        raise ValueError("此子规则不支持普通复式牌墙，请使用随机种子复现牌局")
    if sub_rule == "guobiao/sanma":
        raise ValueError("三人国标不支持四人复式牌墙")
    bloodflow = rule == "sichuan" and sub_rule == XUELIU_WALL_SUBRULE
    bloodbattle = rule == "sichuan" and sub_rule == SBR_WALL_SUBRULE
    sichuan = bloodflow or bloodbattle
    if not supports_duplicate_room(room):
        raise ValueError("复式牌墙仅支持国标麻将（含蓝十改）和川麻双打复式")
    accepted_rules = (XUELIU_WALL_RULE, XUELIU_WALL_SUBRULE) if bloodflow else (SBR_WALL_RULE, SBR_WALL_SUBRULE) if bloodbattle else (rule, sub_rule)
    if wall["rule"] not in accepted_rules:
        raise ValueError("复式牌墙的玩法与房间不一致")
    if sichuan and wall.get("event_id") != room.get("event_id"):
        raise ValueError("川麻双打复式须使用当前个人、赛事或基地范围内的密钥")
    if wall.get("event_id") and wall["event_id"] != room.get("event_id"):
        raise ValueError("比赛复式牌墙只能用于所属比赛")
    numbers = list(range(11, 20)) + list(range(21, 30)) + list(range(31, 40))
    if not sichuan:
        numbers += list(range(41, 48))
    expected = Counter({tile: 4 for tile in numbers})
    use_flowers = wall.get("use_flowers")
    if use_flowers is None:
        # Older in-memory callers infer the flag from the persisted wall, never from a room default.
        use_flowers = any(tile in range(51, 59) for tile in wall.get("tiles", []))
    if type(use_flowers) is not bool:
        raise ValueError("复式牌墙花牌设置必须为布尔值")
    if sichuan and use_flowers:
        raise ValueError("川麻复式牌墙不能包含花牌")
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
            raise ValueError(f"第 {index} 局复式牌墙牌组与{'川麻子规则（108 张数牌）' if sichuan else '国标子规则（请核对花牌与蓝十改设置）'}不一致")
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
            if row[1].split("/", 1)[0] != "guobiao" and row[1] not in (SBR_WALL_SUBRULE, XUELIU_WALL_SUBRULE):
                raise ValueError("川麻复式密钥必须先绑定一组双打开闭室")
            cursor.execute("INSERT INTO duplicate_games (game_id, wall_id) VALUES (%s, %s)", (game_id, wall_id))
        conn.commit()
        return game_id
    except Exception:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)


def is_training_duplicate_record(title):
    """Identify the server's keyless paired-training format, not access authority."""
    return (title.get("wall_source") == "training" and title.get("duplicate_wall_type") == "training"
            and title.get("is_duplicate") is True and not title.get("duplicate_key")
            and title.get("sub_rule") in (SBR_WALL_SUBRULE, XUELIU_WALL_SUBRULE))


def duplicate_record_is_visible(db, game_record, game_id=None):
    title = game_record.get("game_title") or {}
    key = title.get("duplicate_key") or None
    marked_duplicate = title.get("is_duplicate") is True or bool(title.get("duplicate_seed")) or isinstance(title.get("duplicate_tiles"), list)
    if not key and not game_id and not marked_duplicate:
        return True
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
            if is_training_duplicate_record(title):
                # A caller-supplied training marker must not unlock a formal game.
                # Require the persisted record and its keyless paired snapshot.
                cursor.execute("""SELECT EXISTS (
                    SELECT 1 FROM game_records r JOIN sichuan_duplicate_matches m
                      ON m.id::text = r.record->'game_title'->>'duplicate_match_id'
                    WHERE r.game_id=%s AND m.wall_id IS NULL
                      AND m.data->>'wall_source'='training'
                      AND m.data->'record_ids'->>(r.record->'game_title'->>'duplicate_table')=r.game_id
                      AND r.record->'game_title'->>'wall_source'='training'
                      AND r.record->'game_title'->>'duplicate_wall_type'='training'
                      AND r.record->'game_title'->>'is_duplicate'='true'
                      AND r.record->'game_title'->>'sub_rule' IN (%s,%s)
                      AND COALESCE(r.record->'game_title'->>'duplicate_key','')=''
                      AND NOT EXISTS (SELECT 1 FROM duplicate_games g WHERE g.game_id=r.game_id))""",
                    (game_id, SBR_WALL_SUBRULE, XUELIU_WALL_SUBRULE))
                row = cursor.fetchone()
                return bool(row and row[0] is True)
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
ALTER TABLE duplicate_walls DROP CONSTRAINT IF EXISTS duplicate_walls_round_count_check;
ALTER TABLE duplicate_walls ADD CONSTRAINT duplicate_walls_round_count_check CHECK (
  round_count IN (1, 4, 8, 12, 16) OR (rule = 'sichuan/xueliu_doubles_duplicate' AND round_count > 0));
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
