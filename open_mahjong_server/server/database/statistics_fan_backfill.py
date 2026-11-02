"""首次更新补齐全部已启用的历史番种；备份、派生数据与完成标记原子提交。"""
import json
import logging
import time

from psycopg2.extras import Json, RealDictCursor, execute_values

from .daily_aggregator import _stat_date_expr, current_stat_date
from .statistics_catalog import RULES, BY_SUB_RULE, resolve_record_statistics_identity
from .statistics_repair import original_player_index
from .subrule_fans import collect_subrule_fans

BACKUP_TABLE = "statistics_fan_backfill_backup"
MIGRATION_KEY = "statistics_fans_catalog_v2"
# 旧版 v1 可能只回填了一个子规则，不能把它视为全量完成。
LOCK_KEY = "statistics_fans_catalog_v1"
logger = logging.getLogger(__name__)


def _is_complete(cursor, selected):
    cursor.execute("SELECT meta_value FROM app_meta WHERE meta_key=%s", (MIGRATION_KEY,))
    row = cursor.fetchone()
    try:
        saved = json.loads(row[0]) if row else {}
    except (ValueError, TypeError):
        return False
    return (isinstance(saved, dict) and saved.get("status") == "complete"
            and saved.get("scope") == "all" and saved.get("sub_rules") == selected)


def backfill(conn, *, apply=False, sub_rules=None, batch_size=200, force=False, progress=None):
    enabled = {r["sub_rule"] for r in RULES if r["fan_stats"]}
    selected = sorted(enabled if sub_rules is None else set(sub_rules))
    if not selected or not set(selected) <= enabled or batch_size < 1:
        raise ValueError("只能回填已启用番种统计的子规则，批量大小必须为正")
    full_scope = set(selected) == enabled
    report = dict(migration=MIGRATION_KEY, mode="apply" if apply else "dry-run",
                  scope="all" if full_scope else "subset", sub_rules=selected,
                  scanned=0, changed=0, by_sub_rule={}, errors=[], error_count=0)
    try:
        with conn.cursor() as cur:
            if not apply:
                cur.execute("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ, READ ONLY")
            else:
                if full_scope and not force and _is_complete(cur, selected):
                    conn.rollback()
                    return {**report, "status": "already_complete", "applied": False}
                # 并发启动先等待同一迁移事务，再检查一次完成标记。
                cur.execute("SET LOCAL lock_timeout='0'")
                cur.execute("SELECT pg_advisory_xact_lock(hashtext(%s))", (LOCK_KEY,))
                if full_scope and not force and _is_complete(cur, selected):
                    conn.rollback()
                    return {**report, "status": "already_complete", "applied": False}
                cur.execute("SET LOCAL lock_timeout='10s'")
                cur.execute("""LOCK TABLE game_records,game_player_records,game_player_metrics,
                    scene_tier_fan_daily IN SHARE ROW EXCLUSIVE MODE""")
                cur.execute(f"""CREATE TABLE IF NOT EXISTS {BACKUP_TABLE} (
                    migration_key text,table_name text,row_key text,row_data jsonb,
                    backed_up_at timestamptz DEFAULT NOW(),PRIMARY KEY(migration_key,table_name,row_key))""")
                cur.execute("""CREATE TEMP TABLE corrected_statistics_fans(
                    game_id text,user_id bigint,fan_stats jsonb,PRIMARY KEY(game_id,user_id)) ON COMMIT DROP""")
        with conn.cursor(name="fan_repair_scan", cursor_factory=RealDictCursor) as scan:
            scan.itersize = batch_size
            scan.execute("""SELECT m.game_id,m.user_id,m.rule,m.sub_rule,m.match_type,m.fan_stats,
                    g.record,p.original_player_index FROM game_player_metrics m
                LEFT JOIN game_records g USING(game_id)
                LEFT JOIN game_player_records p ON p.game_id=m.game_id AND p.user_id=m.user_id
                WHERE m.sub_rule=ANY(%s) ORDER BY m.game_id,m.user_id""", (selected,))
            while rows := scan.fetchmany(batch_size):
                changes = []
                for row in rows:
                    report["scanned"] += 1
                    try:
                        record = row["record"]
                        if not isinstance(record, dict) or not isinstance(record.get("game_round"), dict):
                            raise ValueError("缺少原始牌谱")
                        identity = resolve_record_statistics_identity(record, row["rule"], row["sub_rule"], row["match_type"])
                        if identity != (row["rule"], row["sub_rule"]):
                            raise ValueError("牌谱与索引子规则不一致，不能猜测归属")
                        index = original_player_index(record, row["user_id"], row["original_player_index"], BY_SUB_RULE[row["sub_rule"]]["player_count"])
                        fans = collect_subrule_fans(record, index, row["sub_rule"])
                    except (ValueError, TypeError, KeyError, IndexError, ArithmeticError) as exc:
                        report["error_count"] += 1
                        if len(report["errors"]) < 20:
                            report["errors"].append(dict(game_id=row["game_id"], user_id=row["user_id"], error=str(exc)))
                        continue
                    if fans != row["fan_stats"]:
                        changes.append((row["game_id"], row["user_id"], Json(fans)))
                        report["changed"] += 1
                        sub = row["sub_rule"]
                        report["by_sub_rule"][sub] = report["by_sub_rule"].get(sub, 0) + 1
                if apply and changes:
                    with conn.cursor() as cur:
                        execute_values(cur, "INSERT INTO corrected_statistics_fans VALUES %s", changes)
                if progress:
                    progress(report)
        if report["error_count"] or not apply:
            conn.rollback()
            return {**report, "status": "blocked" if report["error_count"] else "preview", "applied": False}
        with conn.cursor() as cur:
            cur.execute(f"""INSERT INTO {BACKUP_TABLE}(migration_key,table_name,row_key,row_data)
                SELECT %s,'game_player_metrics',jsonb_build_array(m.game_id,m.user_id)::text,to_jsonb(m)
                FROM game_player_metrics m JOIN corrected_statistics_fans c USING(game_id,user_id)
                ON CONFLICT DO NOTHING""", (MIGRATION_KEY,))
            cur.execute("""UPDATE game_player_metrics m SET fan_stats=c.fan_stats
                FROM corrected_statistics_fans c WHERE m.game_id=c.game_id AND m.user_id=c.user_id""")
            # 全部已结束统计日都覆盖，指标本身已正确但遗漏的日统计也要补齐。
            # 包含旧日统计中已无对应指标的条目，避免保留错误残余；不提前发布今日数据。
            day = _stat_date_expr("m.created_at")
            cur.execute(f"""CREATE TEMP TABLE corrected_fan_days ON COMMIT DROP AS
                SELECT DISTINCT stat_date,rule,sub_rule FROM (
                    SELECT {day} AS stat_date,m.rule,m.sub_rule FROM game_player_metrics m
                    WHERE m.sub_rule=ANY(%s) AND m.room_type IN ('match','events') AND m.match_tier IS NOT NULL
                    UNION SELECT stat_date,rule,sub_rule FROM scene_tier_fan_daily WHERE sub_rule=ANY(%s)
                ) days WHERE stat_date<%s""", (selected, selected, current_stat_date()))
            cur.execute("SELECT COUNT(*) FROM corrected_fan_days")
            report["daily_groups"] = cur.fetchone()[0]
            cur.execute(f"""INSERT INTO {BACKUP_TABLE}(migration_key,table_name,row_key,row_data)
                SELECT %s,'scene_tier_fan_daily',jsonb_build_array(d.stat_date,d.rule,d.sub_rule)::text,
                    jsonb_build_object('stat_date',d.stat_date,'rule',d.rule,'sub_rule',d.sub_rule,
                        'rows',COALESCE((SELECT jsonb_agg(to_jsonb(f)) FROM scene_tier_fan_daily f
                            WHERE (f.stat_date,f.rule,f.sub_rule)=(d.stat_date,d.rule,d.sub_rule)),'[]'::jsonb))
                FROM corrected_fan_days d ON CONFLICT DO NOTHING""", (MIGRATION_KEY,))
            cur.execute("""DELETE FROM scene_tier_fan_daily f USING corrected_fan_days d
                WHERE (f.stat_date,f.rule,f.sub_rule)=(d.stat_date,d.rule,d.sub_rule)""")
            cur.execute(f"""INSERT INTO scene_tier_fan_daily(stat_date,match_tier,rule,sub_rule,fan_field,fan_count)
                SELECT {day},m.match_tier,m.rule,m.sub_rule,fan.key,SUM(fan.value::int)
                FROM game_player_metrics m JOIN corrected_fan_days d
                    ON ({day},m.rule,m.sub_rule)=(d.stat_date,d.rule,d.sub_rule)
                CROSS JOIN LATERAL jsonb_each_text(COALESCE(m.fan_stats,'{{}}'::jsonb)) fan
                WHERE m.room_type IN ('match','events') AND m.match_tier IS NOT NULL
                GROUP BY {day},m.match_tier,m.rule,m.sub_rule,fan.key""")
            report.update(status="complete", applied=True)
            if full_scope:
                cur.execute("""INSERT INTO app_meta(meta_key,meta_value) VALUES(%s,%s)
                    ON CONFLICT(meta_key) DO UPDATE SET meta_value=EXCLUDED.meta_value,updated_at=NOW()""",
                    (MIGRATION_KEY, json.dumps(report, ensure_ascii=False)))
        conn.commit()
        return report
    except BaseException:
        conn.rollback()
        raise


def run_startup_fan_upgrade(db_manager):
    """由 lifespan 在开始接收请求前等待完成；后续启动只读取完成标记。"""
    conn = db_manager._get_connection()
    started = time.monotonic()
    last_logged = started

    def progress(report):
        nonlocal last_logged
        now = time.monotonic()
        if now - last_logged >= 15:
            logger.info("历史番种升级 %s: 已扫描 %s 条，待补齐 %s 条，错误 %s 条",
                        MIGRATION_KEY, report["scanned"], report["changed"], report["error_count"])
            last_logged = now

    try:
        logger.info("检查首次历史番种升级 %s", MIGRATION_KEY)
        report = backfill(conn, apply=True, progress=progress)
        if report["status"] == "blocked":
            logger.error("历史番种升级失败，已回滚且未写完成标记: %s", json.dumps(report, ensure_ascii=False))
            raise RuntimeError(f"历史番种升级失败：{report['error_count']} 条记录缺少原谱或身份冲突；修复后重启重试")
        logger.info("历史番种升级 %s: %s，扫描 %s 条、更新 %s 条，耗时 %.1f 秒",
                    MIGRATION_KEY, report["status"], report["scanned"], report["changed"], time.monotonic() - started)
        return report
    finally:
        db_manager._put_connection(conn)
