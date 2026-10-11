"""
每日 04:00 聚合任务（统计日 = 北京时间当日 04:00 ~ 次日 04:00）：
- daily_stats：每日对局数 / 日活 / 活跃用户 / 最大在线
- scene_daily_stats：各场次（room_type/match_tier/event_id/rule/game_type）聚合
"""
import logging
from datetime import date, datetime, timedelta, timezone
from typing import Optional

from psycopg2 import Error

logger = logging.getLogger(__name__)

STAT_TZ = "Asia/Shanghai"
STAT_DAY_OFFSET_HOURS = 4  # 统计日边界：04:00 切日
SHANGHAI_TZ = timezone(timedelta(hours=8))
REGISTERED_USER_ID_MIN = 10000000
DEFAULT_RESTORE_SINCE = date(2026, 6, 6)
# 场次指标：天梯四档 + 比赛场（match_tier = event_id）
MATCH_TIERS = ("beginner", "intermediate", "advanced", "mcrpl", "elo")
MATCH_TIER_SQL = ", ".join(f"'{t}'" for t in MATCH_TIERS)
STATS_CATCHUP_THROUGH_META_KEY = "stats_catchup_through"

_SCENE_METRIC_COLUMNS = [
    "total_games", "total_rounds", "win_count", "self_draw_count", "deal_in_count",
    "total_fan_score", "total_win_turn", "total_fangchong_score",
    "first_place_count", "second_place_count", "third_place_count", "fourth_place_count",
    "fulu_round_count", "cuohe_count", "total_round_score",
]


def _stat_date_expr(column: str) -> str:
    """将 timestamptz 映射到统计日：减去 4 小时后取日期（04:00 切日）。"""
    return f"(({column} AT TIME ZONE '{STAT_TZ}') - interval '{STAT_DAY_OFFSET_HOURS} hours')::date"


def current_stat_date() -> date:
    """当前时刻所属的统计日（北京时间 04:00 切日）。"""
    now = datetime.now(SHANGHAI_TZ)
    return (now - timedelta(hours=STAT_DAY_OFFSET_HOURS)).date()


def aggregate_daily_stats(db_manager, stat_date: date, max_online: int = None) -> None:
    """聚合某统计日的 daily_stats（对局数/日活/活跃用户/最大在线），UPSERT。"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        date_expr_gr = _stat_date_expr("gr.created_at")
        cursor.execute(
            f"SELECT COUNT(*) FROM game_records WHERE {_stat_date_expr('created_at')} = %s",
            (stat_date,),
        )
        game_count = int(cursor.fetchone()[0] or 0)

        cursor.execute(
            """
            SELECT COUNT(*)::int
            FROM daily_login_users
            WHERE stat_date = %s AND user_id > %s
            """,
            (stat_date, REGISTERED_USER_ID_MIN),
        )
        dau = int(cursor.fetchone()[0] or 0)

        cursor.execute(f"""
            SELECT COUNT(DISTINCT gpr.user_id)
            FROM game_player_records gpr
            INNER JOIN game_records gr ON gr.game_id = gpr.game_id
            WHERE {date_expr_gr} = %s
              AND gpr.user_id > %s
        """, (stat_date, REGISTERED_USER_ID_MIN))
        active_users = int(cursor.fetchone()[0] or 0)

        if max_online is None:
            cursor.execute(
                "SELECT COALESCE(max_online, 0) FROM daily_online_cache WHERE stat_date = %s",
                (stat_date,),
            )
            row = cursor.fetchone()
            max_online = int(row[0]) if row else 0
        else:
            max_online = int(max_online)
        cursor.execute("""
            INSERT INTO daily_stats (stat_date, game_count, dau, active_users, max_online, updated_at)
            VALUES (%s, %s, %s, %s, %s, CURRENT_TIMESTAMP)
            ON CONFLICT (stat_date) DO UPDATE SET
                game_count = EXCLUDED.game_count,
                dau = EXCLUDED.dau,
                active_users = EXCLUDED.active_users,
                max_online = GREATEST(daily_stats.max_online, EXCLUDED.max_online),
                updated_at = CURRENT_TIMESTAMP
        """, (stat_date, game_count, dau, active_users, max_online))
        conn.commit()
        logger.info(
            "daily_stats 已聚合 %s: games=%s dau=%s active=%s max_online=%s",
            stat_date, game_count, dau, active_users, max_online,
        )
    except Error as e:
        logger.error("聚合 daily_stats 失败 %s: %s", stat_date, e, exc_info=True)
        if conn:
            conn.rollback()
        raise
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def _write_scene_statistics(cursor, stat_date: date):
    """仅刷新指定统计日的场次基础和番种快照，保留 sub_rule。"""
    if not isinstance(stat_date, date):
        raise ValueError("场次聚合必须指定统计日期")
    day = _stat_date_expr("created_at")
    predicate = f"{day} = %s"
    params = (stat_date,)
    for table in ("scene_daily_stats", "scene_tier_fan_daily"):
        cursor.execute(f"DELETE FROM {table} WHERE stat_date=%s", params)
    dims = "stat_date,room_type,match_tier,event_id,rule,sub_rule,game_type"
    metrics = [k for k in _SCENE_METRIC_COLUMNS if k not in ("total_games", "total_rounds")]
    per_game = ",".join(f"SUM({key}) AS {key}" for key in metrics)
    totals = ",".join(f"SUM({key})" for key in metrics)
    cursor.execute(f"""INSERT INTO scene_daily_stats ({dims},{','.join(_SCENE_METRIC_COLUMNS)})
        SELECT {dims},COUNT(*),SUM(total_rounds),{totals} FROM (
            SELECT {day} AS stat_date,game_id,room_type,match_tier,event_id,rule,sub_rule,game_type,
                MAX(total_rounds) AS total_rounds,{per_game}
            FROM game_player_metrics WHERE {predicate} AND sub_rule IS NOT NULL
            GROUP BY {day},game_id,room_type,match_tier,event_id,rule,sub_rule,game_type
        ) games GROUP BY {dims}""", params)
    cursor.execute(f"""INSERT INTO scene_tier_fan_daily(stat_date,match_tier,rule,sub_rule,fan_field,fan_count)
        SELECT {day},match_tier,rule,sub_rule,fan.key,SUM(fan.value::int)
        FROM game_player_metrics CROSS JOIN LATERAL jsonb_each_text(COALESCE(fan_stats,'{{}}'::jsonb)) fan
        WHERE {predicate} AND match_tier IS NOT NULL AND room_type IN ('match','events')
        GROUP BY {day},match_tier,rule,sub_rule,fan.key""", params)


def aggregate_scene_daily_stats(db_manager, stat_date: date) -> None:
    conn = db_manager._get_connection()
    try:
        with conn.cursor() as cursor:
            _write_scene_statistics(cursor, stat_date)
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        db_manager._put_connection(conn)


def run_daily_aggregation(db_manager, stat_date: date, max_online: int = None) -> None:
    """一次聚合某统计日的 daily_stats 与 scene_daily_stats（各单日，轻量）。"""
    aggregate_daily_stats(db_manager, stat_date, max_online)
    aggregate_scene_daily_stats(db_manager, stat_date)





def _get_meta_value(db_manager, meta_key: str) -> Optional[str]:
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            "SELECT meta_value FROM app_meta WHERE meta_key = %s",
            (meta_key,),
        )
        row = cursor.fetchone()
        return str(row[0]) if row and row[0] is not None else None
    except Exception:
        return None
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def _set_meta_value(db_manager, meta_key: str, meta_value: str) -> None:
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            """CREATE TABLE IF NOT EXISTS app_meta (
                   meta_key VARCHAR(100) PRIMARY KEY,
                   meta_value TEXT,
                   updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
               )"""
        )
        cursor.execute(
            """
            INSERT INTO app_meta (meta_key, meta_value)
            VALUES (%s, %s)
            ON CONFLICT (meta_key) DO UPDATE SET
                meta_value = EXCLUDED.meta_value,
                updated_at = CURRENT_TIMESTAMP
            """,
            (meta_key, meta_value),
        )
        conn.commit()
    except Exception as e:
        logger.error("写入 meta %s 失败: %s", meta_key, e, exc_info=True)
        if conn:
            conn.rollback()
        raise
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)





def run_startup_stats_restore(
    db_manager,
    since_date: date = DEFAULT_RESTORE_SINCE,
    date_to: Optional[date] = None,
) -> None:
    """按日补齐已写入的实时指标，不扫描历史牌谱或重建逐局指标。"""
    end = current_stat_date() - timedelta(days=1)
    if date_to is not None:
        end = min(end, date_to)
    last_raw = _get_meta_value(db_manager, STATS_CATCHUP_THROUGH_META_KEY)
    if last_raw:
        try:
            # 重算游标前一天，修正早期启动生成的不完整桶和部分遗漏的场次。
            since_date = max(since_date, date.fromisoformat(last_raw[:10]) - timedelta(days=1))
        except ValueError:
            pass
    if end < since_date:
        return
    run_catchup_aggregation(db_manager, since_date=since_date, date_to=end)
    _set_meta_value(db_manager, STATS_CATCHUP_THROUGH_META_KEY, end.isoformat())
    logger.info("统计维护完成（使用结算时实时写入的指标，完整统计日至 %s）", end)


def run_catchup_aggregation(
    db_manager,
    days: int = 7,
    since_date: Optional[date] = None,
    date_to: Optional[date] = None,
) -> None:
    """重聚合区间内有原始活动的已结束统计日；已有行不能代表整日已聚合。"""
    end = current_stat_date() - timedelta(days=1)
    if date_to is not None:
        end = min(end, date_to)
    start = since_date or (end - timedelta(days=days))
    if start > end:
        return
    conn = db_manager._get_connection()
    cursor = conn.cursor()
    try:
        cursor.execute(f"""
            SELECT stat_date FROM (
                SELECT {_stat_date_expr('created_at')} AS stat_date FROM game_records
                UNION SELECT stat_date FROM daily_login_users
                UNION SELECT stat_date FROM daily_online_cache
            ) source
            WHERE stat_date BETWEEN %s AND %s
            ORDER BY stat_date
        """, (start, end))
        dates = [row[0] for row in cursor.fetchall()]
    finally:
        cursor.close()
        db_manager._put_connection(conn)
    for stat_date in dates:
        run_daily_aggregation(db_manager, stat_date)
