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


def aggregate_scene_daily_stats(db_manager, stat_date: date) -> None:
    """聚合某统计日的 scene_daily_stats（天梯四档 + 比赛场）。

    单日 DELETE + INSERT，04:00 定时任务调用，开销可控。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            f"""
            DELETE FROM scene_daily_stats
            WHERE stat_date = %s
              AND (
                (room_type = 'match' AND match_tier IN ({MATCH_TIER_SQL}))
                OR room_type = 'events'
              )
            """,
            (stat_date,),
        )

        date_expr = _stat_date_expr("created_at")
        inner = f"""
            SELECT
                %s::date AS stat_date,
                room_type, match_tier, event_id, rule, game_type,
                COUNT(DISTINCT game_id) AS total_games,
                SUM(per_game_rounds) AS total_rounds,
                SUM(win_count) AS win_count,
                SUM(self_draw_count) AS self_draw_count,
                SUM(deal_in_count) AS deal_in_count,
                SUM(total_fan_score) AS total_fan_score,
                SUM(total_win_turn) AS total_win_turn,
                SUM(total_fangchong_score) AS total_fangchong_score,
                SUM(first_place_count) AS first_place_count,
                SUM(second_place_count) AS second_place_count,
                SUM(third_place_count) AS third_place_count,
                SUM(fourth_place_count) AS fourth_place_count,
                SUM(fulu_round_count) AS fulu_round_count,
                SUM(cuohe_count) AS cuohe_count,
                SUM(total_round_score) AS total_round_score
            FROM (
                SELECT game_id, room_type, match_tier, event_id, rule, game_type,
                       MAX(total_rounds) AS per_game_rounds,
                       SUM(win_count) AS win_count,
                       SUM(self_draw_count) AS self_draw_count,
                       SUM(deal_in_count) AS deal_in_count,
                       SUM(total_fan_score) AS total_fan_score,
                       SUM(total_win_turn) AS total_win_turn,
                       SUM(total_fangchong_score) AS total_fangchong_score,
                       SUM(first_place_count) AS first_place_count,
                       SUM(second_place_count) AS second_place_count,
                       SUM(third_place_count) AS third_place_count,
                       SUM(fourth_place_count) AS fourth_place_count,
                       SUM(fulu_round_count) AS fulu_round_count,
                       SUM(cuohe_count) AS cuohe_count,
                       SUM(total_round_score) AS total_round_score
                FROM game_player_metrics
                WHERE {date_expr} = %s
                  AND (
                    (room_type = 'match' AND match_tier IN ({MATCH_TIER_SQL}))
                    OR (room_type = 'events' AND event_id IS NOT NULL)
                  )
                GROUP BY game_id, room_type, match_tier, event_id, rule, game_type
            ) g
            GROUP BY room_type, match_tier, event_id, rule, game_type
        """
        cols = ("stat_date, room_type, match_tier, event_id, rule, game_type, "
                + ", ".join(_SCENE_METRIC_COLUMNS))
        cursor.execute(
            f"INSERT INTO scene_daily_stats ({cols}) {inner}",
            (stat_date, stat_date),
        )
        conn.commit()
        logger.info("scene_daily_stats 已聚合 %s（天梯四档 + 比赛场）", stat_date)
        from .tier_fan_aggregator import increment_scene_tier_fan_for_date
        increment_scene_tier_fan_for_date(db_manager, stat_date)
    except Error as e:
        logger.error("聚合 scene_daily_stats 失败 %s: %s", stat_date, e, exc_info=True)
        if conn:
            conn.rollback()
        raise
    finally:
        if conn:
            cursor.close()
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
    """增量恢复遗漏指标，并重聚合已结束的统计日；不生成当天半成品快照。"""
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
    from .backfill_game_player_metrics import (
        backfill_missing_game_player_metrics, fill_event_match_tier_on_records,
    )
    from .riichi.record_stats import backfill_stats as backfill_riichi_stats
    fill_event_match_tier_on_records(db_manager)
    backfill_riichi_stats(db_manager)
    rows = backfill_missing_game_player_metrics(db_manager, date_from=since_date, date_to=end)
    run_catchup_aggregation(db_manager, since_date=since_date, date_to=end)
    _set_meta_value(db_manager, STATS_CATCHUP_THROUGH_META_KEY, end.isoformat())
    logger.info("统计维护完成（metrics 增量 %d 行，完整统计日至 %s）", rows, end)


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
