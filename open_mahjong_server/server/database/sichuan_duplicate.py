"""Formal wall assets and legacy replay migration. Live rooms stay in memory."""
import json
import secrets

from psycopg2.errors import LockNotAvailable

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
        CREATE INDEX IF NOT EXISTS idx_sichuan_duplicate_wall ON sichuan_duplicate_matches(wall_id);
        ALTER TABLE sichuan_duplicate_matches ALTER COLUMN event_id DROP NOT NULL;
        ALTER TABLE sichuan_duplicate_matches ALTER COLUMN wall_id DROP NOT NULL;
    """)


def _checkpoint_records(data, table):
    final = data.get("final_records", {}).get(table)
    if final:
        yield final
    for field in ("checkpoints", "table_checkpoints", "pending_checkpoints"):
        checkpoint = (data.get(field) or {}).get(table)
        if checkpoint and checkpoint.get("game_record"):
            yield checkpoint["game_record"]


def compact_completed_replays(cursor, match_id=None):
    """Retire duplicate timelines after a completed pair has two committed table IDs."""
    cursor.execute("""SELECT id FROM sichuan_duplicate_matches
        WHERE status='completed' AND data->'record_ids'->>'open' IS NOT NULL
          AND data->'record_ids'->>'closed' IS NOT NULL
          AND (%s::uuid IS NULL OR id=%s::uuid)
          AND NOT(data ? 'round_sources') FOR UPDATE""", (match_id, match_id))
    changed = 0
    for mid, in cursor.fetchall():
        cursor.execute("SELECT data FROM sichuan_duplicate_matches WHERE id=%s", (mid,))
        data = cursor.fetchone()[0]
        ids = data["record_ids"]
        from ..records.checkpoints import source_rounds
        rounds = source_rounds(cursor, mid, ids)
        if not all(ids[t] in rounds for t in ("open", "closed")):
            continue
        # Preserve every checkpoint field that differs from the saved result.
        # An unmatched or unfinished record must remain available for recovery.
        sources = {}
        for table in ("open", "closed"):
            for record in _checkpoint_records(data, table):
                if isinstance(record.get("game_round"), dict) and record["game_round"] == rounds[ids[table]]:
                    del record["game_round"]
                    record["round_source_game_id"] = ids[table]
                    sources[table] = ids[table]
        if sources:
            data["round_sources"] = sources
            cursor.execute("UPDATE sichuan_duplicate_matches SET data=%s WHERE id=%s",
                           (json.dumps(data, ensure_ascii=False), mid))
            changed += 1
    return changed


def attach_paired_wall(db, snapshot, *, event_id, owner_id, name):
    """Load or create the formal wall asset. Table identifiers exist only in memory until settlement."""
    conn = db._get_connection()
    try:
        with conn.cursor() as cursor:
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
                cursor.execute("SELECT id FROM duplicate_walls WHERE id=%s AND scope=%s AND event_id IS NOT DISTINCT FROM %s AND deleted_at IS NULL AND is_unlocked=FALSE FOR UPDATE NOWAIT",
                               (snapshot["source_wall_id"], scope, event_id))
                if not cursor.fetchone():
                    raise ValueError("所选牌山必须为当前范围内尚未解锁的密钥")
                wall_id, key = snapshot["source_wall_id"], snapshot["key"]
                # Several independent matches may share this locked source.
                # The wall row lock serializes attachment against unlocking.
                cursor.execute("UPDATE duplicate_walls SET rule=%s WHERE id=%s AND rule IS DISTINCT FROM %s",
                               (snapshot["profile"]["sub_rule"], wall_id, snapshot["profile"]["sub_rule"]))
            else:
                key = 'dup_' + secrets.token_hex(16)
                cursor.execute("""INSERT INTO duplicate_walls
                (key, owner_user_id, event_id, scope, wall_type, rule, name, tiles, seed, use_flowers, round_count, round_tiles)
                VALUES (%s,%s,%s,%s,'key',%s,%s,%s,%s,FALSE,%s,%s) RETURNING id""",
                (key, owner_id, event_id, scope, snapshot["profile"]["sub_rule"], name[:80],
                 json.dumps(rounds[0]), snapshot["seed"], len(rounds), json.dumps(rounds)))
                wall_id = cursor.fetchone()[0]
            snapshot.update(key=key, wall_id=wall_id, event_id=event_id, scope=scope, owner_user_id=owner_id)
        conn.commit()
        snapshot["table_record_ids"] = {side: secrets.token_hex(8) for side in ("open", "closed")}
    except LockNotAvailable as error:
        # Key management holds this row while it checks the in-memory matches.
        # Do not block the game loop that answers that check.
        conn.rollback()
        raise ValueError("密钥正在变更，请稍后重新创建房间") from error
    except BaseException:
        conn.rollback()
        raise
    finally:
        db._put_connection(conn)
