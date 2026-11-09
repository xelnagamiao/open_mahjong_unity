"""Persist paired matches alongside the existing duplicate-wall record lock."""
import json
import secrets

from .duplicate_walls import ensure_duplicate_tables
from ..game_calculation.sichuan.duplicate_rules import flatten_board


def ensure_tables(cursor):
    ensure_duplicate_tables(cursor)
    cursor.execute("""CREATE TABLE IF NOT EXISTS sichuan_duplicate_matches (
        id UUID PRIMARY KEY, wall_id BIGINT NOT NULL REFERENCES duplicate_walls(id),
        event_id VARCHAR(32) NULL, status VARCHAR(20) NOT NULL,
        revision INTEGER NOT NULL DEFAULT 0, data JSONB NOT NULL,
        updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
        CREATE INDEX IF NOT EXISTS idx_sichuan_duplicate_event ON sichuan_duplicate_matches(event_id);
        ALTER TABLE sichuan_duplicate_matches ALTER COLUMN event_id DROP NOT NULL;
    """)


class PairedMatchStore:
    def __init__(self, db):
        self.db = db

    def create(self, snapshot, *, event_id, owner_id, name):
        conn = self.db._get_connection()
        try:
            with conn.cursor() as cursor:
                ensure_tables(cursor)
                rounds = [flatten_board(b) for b in snapshot["boards"]]
                scope = "event" if event_id else "personal"
                quota_owner = event_id if event_id else owner_id
                if scope == "personal" and (type(owner_id) is not int or owner_id <= 10):
                    raise ValueError("个人复式须由已登录玩家创建")
                cursor.execute("SELECT pg_advisory_xact_lock(hashtextextended(%s, 0))", (f"duplicate:{scope}:{quota_owner}",))
                cursor.execute("""SELECT COUNT(*) FILTER (WHERE (created_at AT TIME ZONE 'Asia/Shanghai')::date =
                    (CURRENT_TIMESTAMP AT TIME ZONE 'Asia/Shanghai')::date), COUNT(*) FILTER (WHERE deleted_at IS NULL)
                    FROM duplicate_walls WHERE scope=%s AND """ + ("event_id=%s" if event_id else "owner_user_id=%s"), (scope, quota_owner))
                today, stored = cursor.fetchone()
                daily, storage = (25, 100) if event_id else (5, 25)
                if not snapshot.get("source_wall_id") and (today >= daily or stored >= storage):
                    raise ValueError(f"复式牌墙达到每日 {daily} 个或同时保存 {storage} 个的限额")
                if snapshot.get("source_wall_id"):
                    cursor.execute("SELECT id FROM duplicate_walls WHERE id=%s AND scope=%s AND event_id IS NOT DISTINCT FROM %s AND deleted_at IS NULL AND is_unlocked=FALSE FOR UPDATE",
                                   (snapshot["source_wall_id"], scope, event_id))
                    if not cursor.fetchone():
                        raise ValueError("所选牌山必须为当前范围内尚未解锁的密钥")
                    cursor.execute("SELECT id FROM sichuan_duplicate_matches WHERE wall_id=%s", (snapshot["source_wall_id"],))
                    if cursor.fetchone():
                        raise ValueError("该牌山已绑定一组开闭室，请创建新的比赛牌山")
                    wall_id, key = snapshot["source_wall_id"], snapshot["key"]
                    # Mark the attached wall as paired so unlock checks cover both rooms.
                    cursor.execute("UPDATE duplicate_walls SET rule=%s WHERE id=%s", (snapshot["profile"]["sub_rule"], wall_id))
                else:
                    key = 'dup_' + secrets.token_hex(16)
                    cursor.execute("""INSERT INTO duplicate_walls
                    (key, owner_user_id, event_id, scope, wall_type, rule, name, tiles, seed, use_flowers, round_count, round_tiles)
                    VALUES (%s,%s,%s,%s,'key',%s,%s,%s,%s,FALSE,%s,%s) RETURNING id""",
                    (key, owner_id, event_id, scope, snapshot["profile"]["sub_rule"], name[:80],
                     json.dumps(rounds[0]), snapshot["seed"], len(rounds), json.dumps(rounds)))
                    wall_id = cursor.fetchone()[0]
                snapshot.update(key=key, wall_id=wall_id, event_id=event_id, scope=scope, owner_user_id=owner_id)
                cursor.execute("""INSERT INTO sichuan_duplicate_matches (id,wall_id,event_id,status,revision,data)
                    VALUES (%s,%s,%s,%s,%s,%s)""", (snapshot["id"], wall_id, event_id,
                    snapshot["status"], snapshot["revision"], json.dumps(snapshot, default=str)))
            conn.commit()
        except BaseException:
            conn.rollback()
            raise
        finally:
            self.db._put_connection(conn)

    def save(self, snapshot, expected_revision):
        conn = self.db._get_connection()
        try:
            with conn.cursor() as cursor:
                cursor.execute("""UPDATE sichuan_duplicate_matches SET status=%s,revision=%s,data=%s,updated_at=NOW()
                    WHERE id=%s AND revision=%s""", (snapshot["status"], snapshot["revision"],
                    json.dumps(snapshot, default=str), snapshot["id"], expected_revision))
                if cursor.rowcount != 1:
                    raise ValueError("复式比赛状态版本冲突，未覆盖已保存结果")
                rounds = [flatten_board(b) for b in snapshot["boards"]]
                cursor.execute("UPDATE duplicate_walls SET tiles=%s,round_count=%s,round_tiles=%s WHERE id=%s AND is_unlocked=FALSE",
                               (json.dumps(rounds[0]), len(rounds), json.dumps(rounds), snapshot["wall_id"]))
            conn.commit()
        except BaseException:
            conn.rollback()
            raise
        finally:
            self.db._put_connection(conn)

    def unfinished(self):
        conn = self.db._get_connection()
        try:
            with conn.cursor() as cursor:
                ensure_tables(cursor)
                cursor.execute("SELECT data FROM sichuan_duplicate_matches WHERE status <> 'cancelled' AND COALESCE((data->>'rooms_deleted')::boolean,FALSE)=FALSE ORDER BY updated_at")
                rows = cursor.fetchall()
            conn.commit()
            return [json.loads(r[0]) if isinstance(r[0], str) else r[0] for r in rows]
        except BaseException:
            conn.rollback()
            raise
        finally:
            self.db._put_connection(conn)
