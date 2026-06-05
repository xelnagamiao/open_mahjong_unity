"""
国标段位排行榜查询（挂载到 DatabaseManager）
"""
import logging
from psycopg2 import Error
from ...match.rank_calculator import RANK_NAME_TO_INDEX

logger = logging.getLogger(__name__)

LEADERBOARD_LIMIT = 100
MIN_USER_ID = 10000000


def get_guobiao_leaderboard(db_manager, limit: int = LEADERBOARD_LIMIT) -> list:
    """
    获取国标段位 Top N 排行榜。
    条件：user_id > 10000000（非游客），guobiao_rank != '10级'。
    排序：段位索引降序，分数降序，user_id 升序。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute("""
            SELECT r.user_id, r.guobiao_rank, r.guobiao_score,
                   u.username, COALESCE(us.profile_image_id, 1) AS profile_image_id
            FROM rank_data r
            JOIN users u ON r.user_id = u.user_id
            LEFT JOIN user_settings us ON us.user_id = r.user_id
            WHERE r.user_id > %s AND r.guobiao_rank != '10级'
        """, (MIN_USER_ID,))
        rows = cursor.fetchall()
        if not rows:
            return []

        entries = []
        for row in rows:
            user_id, guobiao_rank, guobiao_score, username, profile_image_id = row
            rank_index = RANK_NAME_TO_INDEX.get(guobiao_rank, 0)
            entries.append({
                "user_id": user_id,
                "guobiao_rank": guobiao_rank,
                "guobiao_score": float(guobiao_score),
                "username": username or "",
                "profile_image_id": int(profile_image_id) if profile_image_id else 1,
                "_rank_index": rank_index,
            })

        entries.sort(
            key=lambda e: (-e["_rank_index"], -e["guobiao_score"], e["user_id"])
        )
        entries = entries[:limit]

        result = []
        for pos, e in enumerate(entries):
            result.append({
                "rank_position": pos,
                "user_id": e["user_id"],
                "username": e["username"],
                "profile_image_id": e["profile_image_id"],
                "guobiao_rank": e["guobiao_rank"],
                "guobiao_score": e["guobiao_score"],
            })
        return result
    except Error as e:
        logger.error(f"获取排行榜失败: {e}")
        if conn:
            conn.rollback()
        return []
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
