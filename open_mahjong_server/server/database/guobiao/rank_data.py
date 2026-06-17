"""
段位数据 CRUD 操作（挂载到 DatabaseManager）
"""
import logging
from psycopg2 import Error

logger = logging.getLogger(__name__)


def get_rank_data(db_manager, user_id: int) -> dict:
    """
    获取用户段位数据
    Returns:
        {'guobiao_rank': str, 'guobiao_score': float} 或 None
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            "SELECT guobiao_rank, guobiao_score FROM rank_data WHERE user_id = %s",
            (user_id,)
        )
        row = cursor.fetchone()
        if row:
            return {"guobiao_rank": row[0], "guobiao_score": float(row[1])}
        return None
    except Error as e:
        logger.error(f"获取段位数据失败: {e}")
        if conn:
            conn.rollback()
        return None
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def update_rank_data(db_manager, user_id: int, guobiao_rank: str, guobiao_score: float):
    """更新用户段位数据（无记录时自动插入）"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO rank_data (user_id, guobiao_rank, guobiao_score)
            VALUES (%s, %s, %s)
            ON CONFLICT (user_id) DO UPDATE SET
                guobiao_rank = EXCLUDED.guobiao_rank,
                guobiao_score = EXCLUDED.guobiao_score,
                updated_at = CURRENT_TIMESTAMP
        """, (user_id, guobiao_rank, guobiao_score))
        conn.commit()
        logger.info(f"用户 {user_id} 段位更新: {guobiao_rank} / {guobiao_score}")
    except Error as e:
        logger.error(f"更新段位数据失败: {e}")
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def get_user_sponsor_mcrpl(db_manager, user_id: int) -> dict:
    """
    从 users 表获取赞助到期时间与 is_mcrpl_qualified 字段。
    is_sponsor 由 sponsor_expires_at > 当前时间 动态计算。
    Returns:
        {'is_sponsor': bool, 'sponsor_expires_at': datetime|None, 'is_mcrpl_qualified': bool} 或 None
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            """
            SELECT
                (sponsor_expires_at IS NOT NULL AND sponsor_expires_at > CURRENT_TIMESTAMP) AS is_sponsor,
                sponsor_expires_at,
                is_mcrpl_qualified
            FROM users WHERE user_id = %s
            """,
            (user_id,)
        )
        row = cursor.fetchone()
        if row:
            return {
                "is_sponsor": bool(row[0]),
                "sponsor_expires_at": row[1],
                "is_mcrpl_qualified": row[2],
            }
        return None
    except Error as e:
        logger.error(f"获取赞助者/MCRPL字段失败: {e}")
        if conn:
            conn.rollback()
        return None
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
