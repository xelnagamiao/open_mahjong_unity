"""One-time repair of Hongque v1.6 replay fan labels; never used by replay readers.

Database preview: python scripts/repair_hongque_record_fans.py --database
Apply with transactional backups: add --apply.
Exported JSON: --input old.json --output repaired.json (the source is retained).
Connection: HONGQUE_RECORD_DATABASE_URL, otherwise local_config/test_config.Config.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
import json
import os
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

# Frozen v1.6 values: migrating a historical record must not depend on future
# scoring changes. Counts were retained in the affected records; only values
# were omitted. No hands, payments, ranks or fan achievement counts are changed.
FAN_VALUES_V16 = {
    "清顺": 1, "清刻": 1, "一条龙": 3,
    "双同刻": 2, "三同刻": 6, "四同刻": 12,
    "双同顺": 2, "三同顺": 6, "四同顺": 12,
    "双同花": 2, "三同花": 6, "四同花": 12,
    "三连顺": 3, "四连顺": 6, "九归一": 6, "七归一": 3,
    "双虹会": 12, "彩虹": 6, "清一色": 18, "全彩": 12, "光谱": 6,
    "双色": 12, "三色": 6, "全纯色": 1, "全半色": 1,
    "清一数": 18, "二数": 12, "三数": 6, "四数": 3, "全带幺": 2,
    "天和": 18, "门清": 1, "海底": 2, "碰碰和": 3, "平和": 1,
    "金龙": 6, "二金": 3, "三金": 1,
}
HU_ACTIONS = {"hu_self", "hu_first", "hu_second", "hu_third"}
MIGRATION_KEY = "hongque_record_fan_values_v1"
BACKUP_TABLE = "hongque_record_fan_backups"


def rebuild_record(source):
    """Return a repaired copy and the number of changed labels; fail closed."""
    record = deepcopy(source)
    title = record.get("game_title") or {}
    if title.get("rule") != "hongque" and not str(title.get("sub_rule", "")).startswith("hongque/"):
        return record, 0
    if title.get("rule") not in (None, "", "hongque"):
        raise ValueError("record rule and sub_rule disagree")
    if title.get("sub_rule") not in (None, "", "hongque/v1.6"):
        raise ValueError("only Hongque v1.6 fan values are defined by this migration")
    rounds = record.get("game_round")
    if not isinstance(rounds, dict):
        raise ValueError("missing game_round")
    changed = 0
    for round_key, rd in rounds.items():
        if not isinstance(rd, dict) or not isinstance(rd.get("action_ticks"), list):
            raise ValueError(f"{round_key}: invalid action_ticks")
        for index, tick in enumerate(rd["action_ticks"]):
            if not isinstance(tick, list) or not tick or tick[0] not in HU_ACTIONS:
                continue
            context = f"{round_key} tick {index}"
            if len(tick) < 4 or not isinstance(tick[3], list):
                raise ValueError(f"{context}: invalid fan list")
            for fan_index, label in enumerate(tick[3]):
                if not isinstance(label, str) or not label.strip():
                    raise ValueError(f"{context}: invalid fan label")
                if "|" in label:
                    # Existing explicit values are authoritative; retain them.
                    if not re.fullmatch(r"[^|]+\|\d+", label):
                        raise ValueError(f"{context}: invalid explicit value {label!r}")
                    continue
                matched = re.fullmatch(r"([^*×|]+)(?:[*×]([1-9]\d*))?", label)
                if not matched or matched[1] not in FAN_VALUES_V16:
                    raise ValueError(f"{context}: unknown fan {label!r}")
                name, count = matched[1], int(matched[2] or 1)
                token = f"{name}*{count}" if count > 1 else name
                tick[3][fan_index] = f"{token}|{FAN_VALUES_V16[name] * count}"
                changed += 1
    return record, changed


def repair_database(conn, *, apply=False, batch_size=200):
    """Publish all repairs and their originals together, or roll back everything."""
    from psycopg2.extras import Json
    if batch_size < 1:
        raise ValueError("batch_size must be positive")
    report = {"mode": "apply" if apply else "dry-run", "scanned": 0,
              "changed_records": 0, "changed_labels": 0, "errors": [], "applied": False}
    try:
        with conn.cursor() as cur:
            if not apply:
                cur.execute("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ, READ ONLY")
            else:
                cur.execute("SET LOCAL lock_timeout='10s'")
                cur.execute("SELECT pg_try_advisory_xact_lock(hashtext(%s))", (MIGRATION_KEY,))
                if not cur.fetchone()[0]:
                    raise RuntimeError("Hongque record repair is already running")
                cur.execute(f"""CREATE TABLE IF NOT EXISTS {BACKUP_TABLE} (
                    game_id text PRIMARY KEY, record jsonb NOT NULL,
                    backed_up_at timestamptz NOT NULL DEFAULT NOW())""")
        with conn.cursor(name="hongque_record_fan_scan") as scan:
            scan.itersize = batch_size
            scan.execute("""SELECT game_id,record FROM game_records
                WHERE record->'game_title'->>'rule'='hongque'
                   OR record->'game_title'->>'sub_rule' LIKE 'hongque/%'
                ORDER BY game_id""" + (" FOR UPDATE" if apply else ""))
            while rows := scan.fetchmany(batch_size):
                for game_id, source in rows:
                    report["scanned"] += 1
                    try:
                        repaired, changed = rebuild_record(source)
                    except (TypeError, ValueError, AttributeError) as error:
                        report["errors"].append({"game_id": game_id, "error": str(error)})
                        continue
                    if not changed:
                        continue
                    report["changed_records"] += 1
                    report["changed_labels"] += changed
                    if apply:
                        with conn.cursor() as cur:
                            cur.execute(f"INSERT INTO {BACKUP_TABLE}(game_id,record) VALUES(%s,%s) ON CONFLICT DO NOTHING",
                                        (game_id, Json(source)))
                            cur.execute("UPDATE game_records SET record=%s WHERE game_id=%s", (Json(repaired), game_id))
        if report["errors"] or not apply:
            conn.rollback()
        else:
            conn.commit()
            report["applied"] = True
        return report
    except BaseException:
        conn.rollback()
        raise


def connect():
    import psycopg2
    dsn = os.environ.get("HONGQUE_RECORD_DATABASE_URL")
    if dsn:
        return psycopg2.connect(dsn)
    try:
        from server.local_config import Config
    except ModuleNotFoundError as error:
        if error.name != "server.local_config":
            raise
        from server.test_config import Config
    return psycopg2.connect(**{key: getattr(Config, key) for key in ("host", "port", "user", "password", "database")})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--database", action="store_true")
    source.add_argument("--input", type=Path)
    parser.add_argument("--apply", action="store_true", help="commit database repairs with backups")
    parser.add_argument("--output", type=Path, help="write a new JSON export; never overwrite the original")
    args = parser.parse_args()
    if args.database:
        if args.output:
            parser.error("--output is only valid with --input")
        conn = connect()
        try:
            report = repair_database(conn, apply=args.apply)
        finally:
            conn.close()
    else:
        if args.apply:
            parser.error("--apply is only valid with --database")
        repaired, changed = rebuild_record(json.loads(args.input.read_text(encoding="utf-8-sig")))
        if args.output:
            if args.input.resolve() == args.output.resolve():
                parser.error("--output must differ from the original")
            # Exclusive create also prevents overwriting an earlier repair.
            with args.output.open("x", encoding="utf-8") as target:
                json.dump(repaired, target, ensure_ascii=False, indent=2)
        report = {"changed_labels": changed, "output": str(args.output) if args.output else None}
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 2 if report.get("errors") else 0


if __name__ == "__main__":
    raise SystemExit(main())
