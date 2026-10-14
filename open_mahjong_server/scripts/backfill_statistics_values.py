"""One-time full scan of win/deal-in values. Dry-run by default; never runs at startup.

Run from open_mahjong_server: python scripts/backfill_statistics_values.py [--apply]
Connection: STATISTICS_DATABASE_URL, or the usual local_config/test_config.Config.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import psycopg2
from psycopg2 import sql
from psycopg2.extras import RealDictCursor, execute_values

from server.database.daily_aggregator import _stat_date_expr
from server.database.legacy_statistics import FAMILIES
from server.database.guobiao.record_analyzer import analyze_record_for_player
from server.database.riichi.record_stats import analyze_riichi_record
from server.database.shared_record_metrics import analyze_shared_record_for_player
from server.database.statistics_catalog import BY_SUB_RULE, resolve_record_statistics_identity

MIGRATION_KEY = "statistics_win_value_units_v1"
BACKUP_TABLE = "statistics_value_backfill_backup"


def _original_index(record, user_id, fallback, count):
    title = record.get("game_title") or {}
    matches = [i for i in range(count) if str(title.get(f"p{i}_uid")) == str(user_id)]
    if len(matches) == 1:
        return matches[0]
    # An explicitly recorded participant list is authoritative over an old index.
    if matches or any(title.get(f"p{i}_uid") is not None for i in range(count)):
        raise ValueError("participant identity is missing or ambiguous in the record")
    if type(fallback) is int and 0 <= fallback < count:
        return fallback
    raise ValueError("cannot resolve original player index")


def rebuild_values(row):
    record = row["record"]
    if not isinstance(record, dict) or not isinstance(record.get("game_round"), dict):
        raise ValueError("missing original record")
    rule, sub_rule = resolve_record_statistics_identity(record, row["rule"], row["sub_rule"], row["match_type"])
    definition = BY_SUB_RULE.get(sub_rule)
    if not definition or not definition["basic_stats"]:
        raise ValueError(f"unsupported statistics identity: {sub_rule}")
    if (rule, sub_rule) != (row["rule"], row["sub_rule"]):
        raise ValueError("record/index rule mismatch; repair the identity before this migration")
    index = _original_index(record, row["user_id"], row["original_player_index"], definition["player_count"])
    record = {**record, "game_title": {**record.get("game_title", {}), "rule": rule, "sub_rule": sub_rule}}
    # Use the same value readers as live statistics, without replaying complete
    # games to reconstruct unrelated turn counts or recollecting fan achievements.
    if rule == "guobiao" and sub_rule not in ("guobiao/sanma", "guobiao/blood_battle"):
        values = analyze_record_for_player(record, index)
        return values["win_score"], values["fangchong_score"]
    values = (analyze_riichi_record(record, index) if rule == "riichi"
              else analyze_shared_record_for_player(record, index))
    return values["total_fan_score"], values["total_fangchong_score"]


def _refresh_values(cursor, table, keys, aggregate_sql, params=(), *, backup_keys=None):
    """Back up and update just the two value columns; all other data stays intact."""
    identifier = sql.Identifier(table).as_string(cursor)
    join = " AND ".join(f't."{key}" IS NOT DISTINCT FROM a."{key}"' for key in keys)
    changed = "(t.total_fan_score,t.total_fangchong_score) IS DISTINCT FROM (a.total_fan_score,a.total_fangchong_score)"
    row_key = "jsonb_build_array(" + ",".join(f't."{key}"' for key in (backup_keys or keys)) + ")::text"
    cursor.execute(f"""WITH a AS ({aggregate_sql})
        INSERT INTO {BACKUP_TABLE}(migration_key,table_name,row_key,row_data)
        SELECT %s,%s,{row_key},to_jsonb(t) FROM {identifier} t JOIN a ON {join}
        WHERE {changed} ON CONFLICT DO NOTHING""", (*params, MIGRATION_KEY, table))
    cursor.execute(f"""WITH a AS ({aggregate_sql}) UPDATE {identifier} t
        SET total_fan_score=a.total_fan_score,total_fangchong_score=a.total_fangchong_score
        FROM a WHERE {join} AND {changed}""", params)
    return cursor.rowcount


def backfill(conn, *, apply=False, force=False, batch_size=500, progress=None):
    if batch_size < 1:
        raise ValueError("batch_size must be positive")
    report = {"migration": MIGRATION_KEY, "mode": "apply" if apply else "dry-run",
              "scanned": 0, "changed": 0, "by_sub_rule": {}, "errors": [], "error_count": 0}
    try:
        with conn.cursor() as cur:
            if not apply:
                cur.execute("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ, READ ONLY")
            else:
                cur.execute("SET LOCAL lock_timeout='10s'")
                cur.execute("SELECT pg_try_advisory_xact_lock(hashtext(%s))", (MIGRATION_KEY,))
                if not cur.fetchone()[0]:
                    raise RuntimeError("statistics value backfill is already running")
                # Coordinate with normal game saving and daily aggregation. A busy
                # system times out and rolls back instead of publishing partial data.
                cur.execute("LOCK TABLE game_player_metrics,scene_daily_stats IN SHARE ROW EXCLUSIVE MODE")
            cur.execute("SELECT meta_value FROM app_meta WHERE meta_key=%s", (MIGRATION_KEY,))
            if cur.fetchone() and apply and not force:
                conn.rollback()
                return {**report, "status": "already_complete"}
            if apply:
                cur.execute(f"""CREATE TABLE IF NOT EXISTS {BACKUP_TABLE} (
                    migration_key text NOT NULL,table_name text NOT NULL,row_key text NOT NULL,
                    row_data jsonb NOT NULL,backed_up_at timestamptz DEFAULT NOW(),
                    PRIMARY KEY(migration_key,table_name,row_key))""")
                cur.execute("""CREATE TEMP TABLE corrected_statistics_values(
                    game_id text,user_id bigint,total_fan_score int,total_fangchong_score int,
                    PRIMARY KEY(game_id,user_id)) ON COMMIT DROP""")
        with conn.cursor(name="statistics_value_scan", cursor_factory=RealDictCursor) as scan:
            scan.itersize = batch_size
            scan.execute("""SELECT m.*,g.record,p.original_player_index
                FROM game_player_metrics m LEFT JOIN game_records g USING(game_id)
                LEFT JOIN game_player_records p ON p.game_id=m.game_id AND p.user_id=m.user_id
                ORDER BY m.game_id,m.user_id""")
            while rows := scan.fetchmany(batch_size):
                changed = []
                for row in rows:
                    report["scanned"] += 1
                    try:
                        win, loss = rebuild_values(row)
                    except (ValueError, TypeError, KeyError, IndexError, ArithmeticError) as exc:
                        report["error_count"] += 1
                        if len(report["errors"]) < 20:
                            report["errors"].append({"game_id": row["game_id"], "user_id": row["user_id"], "error": str(exc)})
                        continue
                    if (win, loss) != (row["total_fan_score"], row["total_fangchong_score"]):
                        changed.append((row["game_id"], row["user_id"], win, loss))
                        report["changed"] += 1
                        key = row["sub_rule"]
                        report["by_sub_rule"][key] = report["by_sub_rule"].get(key, 0) + 1
                if apply and changed:
                    with conn.cursor() as cur:
                        execute_values(cur, "INSERT INTO corrected_statistics_values VALUES %s", changed)
                if progress:
                    progress(report)
        if report["error_count"]:
            conn.rollback()
            return {**report, "status": "blocked", "applied": False}
        if not apply:
            conn.rollback()
            return {**report, "status": "preview", "applied": False}
        with conn.cursor() as cur:
            report["metrics_updated"] = _refresh_values(cur, "game_player_metrics", ("game_id", "user_id"),
                                                       "SELECT * FROM corrected_statistics_values")
            report["legacy_updated"] = 0
            for family, rule in FAMILIES.items():
                table = f"{family}_history_stats"
                cur.execute("SELECT to_regclass(%s)", (table,))
                if cur.fetchone()[0] is None:
                    continue
                cur.execute(sql.SQL("LOCK TABLE {} IN SHARE ROW EXCLUSIVE MODE").format(sql.Identifier(table)))
                report["legacy_updated"] += _refresh_values(cur, table, ("user_id", "rule", "mode"),
                    """SELECT user_id,sub_rule AS rule,match_type AS mode,
                        SUM(total_fan_score) AS total_fan_score,SUM(total_fangchong_score) AS total_fangchong_score
                        FROM game_player_metrics WHERE rule=%s GROUP BY user_id,sub_rule,match_type""", (rule,))
            dims = "stat_date,room_type,match_tier,event_id,rule,sub_rule,game_type"
            report["daily_updated"] = _refresh_values(cur, "scene_daily_stats", tuple(dims.split(',')),
                f"""SELECT {_stat_date_expr('created_at')} AS stat_date,
                    room_type,match_tier,event_id,rule,sub_rule,game_type,
                    SUM(total_fan_score) AS total_fan_score,SUM(total_fangchong_score) AS total_fangchong_score
                    FROM game_player_metrics GROUP BY {dims}""", backup_keys=("id",))
            report.update(status="complete", applied=True)
            cur.execute("""INSERT INTO app_meta(meta_key,meta_value) VALUES(%s,%s)
                ON CONFLICT(meta_key) DO UPDATE SET meta_value=EXCLUDED.meta_value,updated_at=NOW()""",
                (MIGRATION_KEY, json.dumps(report, ensure_ascii=False)))
        conn.commit()
        return report
    except BaseException:
        conn.rollback()
        raise


def connect():
    dsn = os.environ.get("STATISTICS_DATABASE_URL")
    if dsn:
        return psycopg2.connect(dsn)
    try:
        from server.local_config import Config
    except ModuleNotFoundError as exc:
        if exc.name != "server.local_config":
            raise
        from server.test_config import Config
    return psycopg2.connect(**{key: getattr(Config, key) for key in ("host", "port", "user", "password", "database")})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--apply", action="store_true", help="commit the repair, backups and completion marker atomically")
    mode.add_argument("--dry-run", action="store_true", help="read-only preview (default)")
    parser.add_argument("--force", action="store_true", help="rescan after a completed run; retain the original backups")
    parser.add_argument("--batch-size", type=int, default=500)
    args = parser.parse_args()
    conn = connect()
    try:
        report = backfill(conn, apply=args.apply, force=args.force, batch_size=args.batch_size,
            progress=lambda r: print(f"Scanned {r['scanned']}; changed {r['changed']}; errors {r['error_count']}", file=sys.stderr))
    finally:
        conn.close()
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 2 if report.get("error_count") else 0


if __name__ == "__main__":
    raise SystemExit(main())
