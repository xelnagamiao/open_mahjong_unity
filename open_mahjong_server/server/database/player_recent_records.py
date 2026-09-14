"""玩家面板记录：九种规则各自的十场顺位，以及国标的历史最高番。"""
from datetime import datetime, timezone
import logging
from zoneinfo import ZoneInfo

from psycopg2.extras import Json

from ..gamestate.public.guobiao_win_snapshot import restore_guobiao_best_wins


logger = logging.getLogger(__name__)
RULES = ("guobiao", "riichi", "qingque", "classical", "jiandan", "sichuan", "changsha", "taiwan", "hongque")
CATEGORIES = ("match", "custom")
MIGRATION = "player_recent_records_v1"
MIGRATION_LOCK = 723419681025
RECENT_LIMIT = 10
# 老牌谱 end_time 与 CURRENT_TIMESTAMP 一样没有时区；与原服务器的本地时区对齐。
LEGACY_TIMEZONE = ZoneInfo("Asia/Shanghai")


def ensure_schema(cursor):
    cursor.execute("""
        CREATE TABLE IF NOT EXISTS player_recent_records (
            user_id BIGINT NOT NULL,
            rule VARCHAR(10) NOT NULL,
            room_type VARCHAR(16) NOT NULL CHECK (room_type IN ('match', 'custom')),
            placements JSONB NOT NULL DEFAULT '[]'::jsonb,
            big_win JSONB,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (user_id, rule, room_type)
        );
        CREATE TABLE IF NOT EXISTS player_recent_record_migrations (
            name TEXT PRIMARY KEY,
            last_game_id VARCHAR(16) NOT NULL DEFAULT '',
            completed_at TIMESTAMPTZ
        );
        CREATE TABLE IF NOT EXISTS player_recent_record_errors (
            game_id VARCHAR(16) PRIMARY KEY REFERENCES game_records(game_id) ON DELETE CASCADE,
            details JSONB NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
    """)


def ended_at(record, created_at):
    value = record.get("game_title", {}).get("end_time")
    try:
        value = value if isinstance(value, datetime) else datetime.fromisoformat(value)
    except (ValueError, TypeError):
        value = created_at
    if value.tzinfo is None:
        value = value.replace(tzinfo=LEGACY_TIMEZONE)
    return value.astimezone(timezone.utc).isoformat(timespec="microseconds")


def merge_placements(existing, incoming):
    # 同谱重放/重试不会增加点；时间相同以 game_id 确保稳定顺序。
    by_id = {entry["game_id"]: entry for entry in (existing or [])}
    by_id[incoming["game_id"]] = incoming
    return sorted(by_id.values(), key=lambda entry: (entry["ended_at"], entry["game_id"]))[-RECENT_LIMIT:]


def best_win_key(win):
    return (win["total_fan"], win["ended_at"], win["game_id"], win["round_index"], win["action_index"])


def update_player_recent_records(cursor, game_id, record=None, *, migration=False):
    """调用方的牌谱事务内更新；只读取实际写入的玩家行，不自行提交。"""
    if not migration:
        cursor.execute("SELECT pg_advisory_xact_lock_shared(%s)", (MIGRATION_LOCK,))
    cursor.execute("SELECT record, created_at FROM game_records WHERE game_id = %s", (game_id,))
    row = cursor.fetchone()
    if row is None:
        return
    stored_record, created_at = row
    record = record if record is not None else stored_record
    cursor.execute("""
        SELECT user_id, rule, room_type, rank, match_type
        FROM game_player_records WHERE game_id = %s ORDER BY user_id, rule, room_type
    """, (game_id,))
    players = cursor.fetchall()
    # 历史牌谱可能早于机器人保存限制：整场排除，与新保存入口一致。
    title = record.get("game_title") or {}
    title_users = [int(title[f"p{i}_uid"]) for i in range(4) if title.get(f"p{i}_uid") is not None]
    if any(user_id <= 10 for user_id in title_users) or any(row[0] <= 10 for row in players):
        cursor.execute("DELETE FROM player_recent_record_errors WHERE game_id = %s", (game_id,))
        return
    players = [row for row in players if row[1] in RULES and row[2] in CATEGORIES]
    if not players:
        cursor.execute("DELETE FROM player_recent_record_errors WHERE game_id = %s", (game_id,))
        return
    finished = ended_at(record, created_at)
    best, errors = restore_guobiao_best_wins(record) if any(row[1] == "guobiao" for row in players) else ({}, [])
    if errors:
        cursor.execute("""
            INSERT INTO player_recent_record_errors (game_id, details) VALUES (%s, %s)
            ON CONFLICT (game_id) DO UPDATE SET details = EXCLUDED.details, updated_at = CURRENT_TIMESTAMP
        """, (game_id, Json(errors)))
        logger.warning("国标最高番追溯未完成 game_id=%s: %s", game_id, "; ".join(errors))
    else:
        cursor.execute("DELETE FROM player_recent_record_errors WHERE game_id = %s", (game_id,))
    for user_id, rule, room_type, rank, match_type in players:
        key = (user_id, rule, room_type)
        cursor.execute("""
            INSERT INTO player_recent_records (user_id, rule, room_type) VALUES (%s, %s, %s)
            ON CONFLICT DO NOTHING
        """, key)
        # 固定顺序锁住玩家行；不同对局同时结束也不会相互覆盖十场数组或最高番。
        cursor.execute("""
            SELECT placements, big_win FROM player_recent_records
            WHERE user_id = %s AND rule = %s AND room_type = %s FOR UPDATE
        """, key)
        placements, big_win = cursor.fetchone()
        placements = merge_placements(placements, {
            "game_id": game_id, "ended_at": finished, "rank": rank, "match_type": match_type,
        })
        incoming = best.get(str(user_id)) if rule == "guobiao" else None
        if incoming:
            incoming = {**incoming, "game_id": game_id, "ended_at": finished}
            if big_win is None or best_win_key(incoming) > best_win_key(big_win):
                big_win = incoming
        cursor.execute("""
            UPDATE player_recent_records SET placements = %s, big_win = %s, updated_at = CURRENT_TIMESTAMP
            WHERE user_id = %s AND rule = %s AND room_type = %s
        """, (Json(placements), Json(big_win) if big_win else None, *key))


def backfill_player_recent_records(db_manager, batch_size=100):
    """首次启动分页回填全部历史。每页提交进度，中断后续跑；完成后不再全表扫描。"""
    if batch_size < 1:
        raise ValueError("batch_size 必须大于零")
    conn = db_manager._get_connection()
    locked = False
    processed = 0
    try:
        with conn.cursor() as cursor:
            cursor.execute("SELECT pg_advisory_lock(%s)", (MIGRATION_LOCK,))
            locked = True
            ensure_schema(cursor)
            cursor.execute("INSERT INTO player_recent_record_migrations (name) VALUES (%s) ON CONFLICT DO NOTHING", (MIGRATION,))
            cursor.execute("SELECT last_game_id, completed_at FROM player_recent_record_migrations WHERE name = %s", (MIGRATION,))
            last_id, completed = cursor.fetchone()
            conn.commit()
            if completed is None:
                logger.info("开始/继续回填玩家近期记录，checkpoint=%s", last_id or "起点")
                while True:
                    cursor.execute("SELECT game_id FROM game_records WHERE game_id > %s ORDER BY game_id LIMIT %s", (last_id, batch_size))
                    ids = [row[0] for row in cursor.fetchall()]
                    if not ids:
                        break
                    for game_id in ids:
                        update_player_recent_records(cursor, game_id, migration=True)
                    last_id = ids[-1]
                    cursor.execute("UPDATE player_recent_record_migrations SET last_game_id = %s WHERE name = %s", (last_id, MIGRATION))
                    conn.commit()
                    processed += len(ids)
                    logger.info("玩家近期记录已回填 %d 份牌谱，checkpoint=%s", processed, last_id)
            # 旧版不完整/损坏的牌谱单独登记，不伪造恢复成功；每次启动重新尝试。
            retry_after = ""
            while True:
                cursor.execute("SELECT game_id FROM player_recent_record_errors WHERE game_id > %s ORDER BY game_id LIMIT %s", (retry_after, batch_size))
                ids = [row[0] for row in cursor.fetchall()]
                if not ids:
                    break
                for game_id in ids:
                    update_player_recent_records(cursor, game_id, migration=True)
                retry_after = ids[-1]
                conn.commit()
            cursor.execute("SELECT COUNT(*) FROM player_recent_record_errors")
            errors = cursor.fetchone()[0]
            cursor.execute("""
                UPDATE player_recent_record_migrations SET completed_at =
                    CASE WHEN %s = 0 THEN COALESCE(completed_at, CURRENT_TIMESTAMP) ELSE NULL END
                WHERE name = %s
            """, (errors, MIGRATION))
            conn.commit()
            logger.info("玩家近期记录回填结束：本次扫描 %d，待修复牌谱 %d", processed, errors)
            return {"processed": processed, "errors": errors, "complete": errors == 0}
    except Exception:
        conn.rollback()
        raise
    finally:
        try:
            if locked and not conn.closed:
                with conn.cursor() as cursor:
                    cursor.execute("SELECT pg_advisory_unlock(%s)", (MIGRATION_LOCK,))
                conn.commit()
        finally:
            db_manager._put_connection(conn)


def get_player_recent_records(db_manager, user_id):
    result = {rule: {category: {"placements": [], "big_win": None} for category in CATEGORIES} for rule in RULES}
    conn = db_manager._get_connection()
    try:
        with conn.cursor() as cursor:
            cursor.execute("SELECT rule, room_type, placements, big_win FROM player_recent_records WHERE user_id = %s", (user_id,))
            for rule, category, placements, big_win in cursor.fetchall():
                if rule in result and category in CATEGORIES:
                    result[rule][category] = {"placements": placements, "big_win": big_win if rule == "guobiao" else None}
        conn.commit()
        return result
    except Exception:
        conn.rollback()
        raise
    finally:
        db_manager._put_connection(conn)
