"""
立直麻将玩家统计查询。表未建立时返回空结果。
"""
import logging

logger = logging.getLogger(__name__)


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
