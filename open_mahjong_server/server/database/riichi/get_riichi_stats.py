"""
立直麻将玩家统计查询。表未建立时返回空结果。
"""
import logging
from typing import Any, Dict, List

from psycopg2.extras import RealDictCursor

logger = logging.getLogger(__name__)


def get_riichi_history_stats(db_manager, user_id: int) -> List[Dict[str, Any]]:
    """获取指定用户的立直历史统计数据（按 mode 分组）。"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor(cursor_factory=RealDictCursor)
        cursor.execute("""
            SELECT
                rule,
                mode,
                COALESCE(SUM(total_games), 0) as total_games,
                COALESCE(SUM(total_rounds), 0) as total_rounds,
                COALESCE(SUM(win_count), 0) as win_count,
                COALESCE(SUM(self_draw_count), 0) as self_draw_count,
                COALESCE(SUM(deal_in_count), 0) as deal_in_count,
                COALESCE(SUM(total_fan_score), 0) as total_fan_score,
                COALESCE(SUM(total_win_turn), 0) as total_win_turn,
                COALESCE(SUM(total_fangchong_score), 0) as total_fangchong_score,
                COALESCE(SUM(first_place_count), 0) as first_place_count,
                COALESCE(SUM(second_place_count), 0) as second_place_count,
                COALESCE(SUM(third_place_count), 0) as third_place_count,
                COALESCE(SUM(fourth_place_count), 0) as fourth_place_count,
                COALESCE(SUM(fulu_round_count), 0) as fulu_round_count
            FROM riichi_history_stats
            WHERE user_id = %s
            GROUP BY rule, mode
            ORDER BY rule, mode
        """, (user_id,))
        return [dict(row) for row in cursor.fetchall()]
    except Exception as e:
        logger.error("获取立直历史统计数据失败: %s", e, exc_info=True)
        return []
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def get_riichi_fan_stats_total(db_manager, user_id: int) -> dict:
    """获取指定用户的立直役种统计数据汇总（所有 mode 合计）。"""
    from .store_riichi import FAN_FIELDS

    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor(cursor_factory=RealDictCursor)
        fan_columns = ", ".join(f"COALESCE(SUM({field}), 0) AS {field}" for field in FAN_FIELDS)
        cursor.execute(
            f"""
            SELECT {fan_columns}
            FROM riichi_fan_stats
            WHERE user_id = %s
            """,
            (user_id,),
        )
        row = cursor.fetchone()
        if row:
            return {k: v for k, v in dict(row).items() if v is not None}
        return {}
    except Exception as e:
        logger.error("获取立直役种统计数据汇总失败: %s", e, exc_info=True)
        return {}
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def get_riichi_stats(db_manager, user_id: int) -> dict:
    """根据 user_id 获取立直麻将历史统计。"""
    conn = None
    result = {"total_games": 0, "first_count": 0, "second_count": 0, "third_count": 0, "fourth_count": 0, "avg_score": 0}
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT COUNT(*) AS total_games,
                   SUM(CASE WHEN rank = 1 THEN 1 ELSE 0 END) AS first_count,
                   SUM(CASE WHEN rank = 2 THEN 1 ELSE 0 END) AS second_count,
                   SUM(CASE WHEN rank = 3 THEN 1 ELSE 0 END) AS third_count,
                   SUM(CASE WHEN rank = 4 THEN 1 ELSE 0 END) AS fourth_count,
                   COALESCE(AVG(final_score), 0) AS avg_score
            FROM riichi_history_stats
            WHERE user_id = %s;
            """,
            (user_id,),
        )
        row = cursor.fetchone()
        if row:
            result = {
                "total_games": row[0] or 0,
                "first_count": row[1] or 0,
                "second_count": row[2] or 0,
                "third_count": row[3] or 0,
                "fourth_count": row[4] or 0,
                "avg_score": float(row[5] or 0),
            }
    except Exception as e:
        logger.warning(f"立直统计查询异常（可能表未建立，返回默认值）: {e}")
    finally:
        if conn:
            db_manager._release_connection(conn)
    return result
