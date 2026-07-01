"""
每日 04:00 聚合任务：
- daily_stats：每日对局数 / 用户量 / 最大在线
- scene_daily_stats：各场次（room_type/match_tier/event_id/rule/game_type）同 history_stats 字段聚合
数据源为 game_player_metrics（store 时实时写入），避免重解牌谱 JSON。可重跑自愈。
"""
import logging
from datetime import date
from psycopg2 import Error

logger = logging.getLogger(__name__)

# scene_daily_stats 指标列（与 guobiao_history_stats 一致）
_SCENE_METRIC_COLUMNS = [
    "total_games", "total_rounds", "win_count", "self_draw_count", "deal_in_count",
    "total_fan_score", "total_win_turn", "total_fangchong_score",
    "first_place_count", "second_place_count", "third_place_count", "fourth_place_count",
    "fulu_round_count", "cuohe_count", "total_round_score",
]


def aggregate_daily_stats(db_manager, stat_date: date, max_online: int = None) -> None:
    """聚合某日的 daily_stats（对局数/用户量/最大在线），UPSERT。

    max_online 优先取传入值；为 None 时从 daily_online_cache 读取该日缓存峰值
    （保证凌晨停机重启后仍能恢复当日最大在线）。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            "SELECT COUNT(*) FROM game_records WHERE created_at::date = %s",
            (stat_date,),
        )
        game_count = int(cursor.fetchone()[0] or 0)
        cursor.execute(
            "SELECT COUNT(DISTINCT user_id) FROM game_player_metrics WHERE created_at::date = %s",
            (stat_date,),
        )
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
            INSERT INTO daily_stats (stat_date, game_count, active_users, max_online, updated_at)
            VALUES (%s, %s, %s, %s, CURRENT_TIMESTAMP)
            ON CONFLICT (stat_date) DO UPDATE SET
                game_count = EXCLUDED.game_count,
                active_users = EXCLUDED.active_users,
                max_online = GREATEST(daily_stats.max_online, EXCLUDED.max_online),
                updated_at = CURRENT_TIMESTAMP
        """, (stat_date, game_count, active_users, max_online))
        conn.commit()
        logger.info(f"daily_stats 已聚合 {stat_date}: games={game_count} users={active_users} max_online={max_online}")
    except Error as e:
        logger.error(f"聚合 daily_stats 失败 {stat_date}: {e}", exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def aggregate_scene_daily_stats(db_manager, stat_date: date) -> None:
    """聚合某日的 scene_daily_stats（先删后插，可重跑）。"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute("DELETE FROM scene_daily_stats WHERE stat_date = %s", (stat_date,))

        # 先按 game_id 聚合（修正 total_rounds 为每局一份），再按场次聚合
        inner = """
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
                WHERE created_at::date = %s
                GROUP BY game_id, room_type, match_tier, event_id, rule, game_type
            ) g
            GROUP BY room_type, match_tier, event_id, rule, game_type
        """
        cols = ("stat_date, room_type, match_tier, event_id, rule, game_type, "
                + ", ".join(_SCENE_METRIC_COLUMNS))
        # 直接用 INSERT...SELECT，列顺序与 inner 输出一致
        cursor.execute(
            f"INSERT INTO scene_daily_stats ({cols}) {inner}",
            (stat_date, stat_date),
        )
        conn.commit()
        logger.info(f"scene_daily_stats 已聚合 {stat_date}")
    except Error as e:
        logger.error(f"聚合 scene_daily_stats 失败 {stat_date}: {e}", exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def run_daily_aggregation(db_manager, stat_date: date, max_online: int = None) -> None:
    """一次聚合某日的 daily_stats 与 scene_daily_stats。"""
    aggregate_daily_stats(db_manager, stat_date, max_online)
    aggregate_scene_daily_stats(db_manager, stat_date)


def run_catchup_aggregation(db_manager, days: int = 7) -> None:
    """启动时自愈：补齐最近 N 天中缺失 daily_stats 的日期。

    凌晨 3-4 点停机、5 点重启后，4 点聚合任务未执行；本函数在启动时补跑昨日
    及近 N 天任何缺失的日期。daily_stats/scene_daily_stats 均为可重跑（先删后插/UPSERT）。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT d::date AS stat_date
            FROM generate_series(CURRENT_DATE - (%s::int), CURRENT_DATE - 1, interval '1 day') d
            WHERE d::date NOT IN (SELECT stat_date FROM daily_stats)
            ORDER BY d
            """,
            (days,),
        )
        missing = [r[0] for r in cursor.fetchall()]
    except Error as e:
        logger.error(f"查询缺失日期失败: {e}", exc_info=True)
        missing = []
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)

    if not missing:
        logger.info("每日统计无需补齐")
        return
    for stat_date in missing:
        logger.info(f"补齐每日统计: {stat_date}")
        run_daily_aggregation(db_manager, stat_date)
