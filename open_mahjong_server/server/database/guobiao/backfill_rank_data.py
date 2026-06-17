"""
为缺少 rank_data 行的存量用户补默认段位（10级 / 0）。

rank_data 表上线前注册的老账号可能没有对应记录，导致排位结算 UPDATE 影响 0 行、
重登后段位丢失。本脚本仅执行一次（由 data_migrations 标记）。
"""
from __future__ import annotations

import logging

from psycopg2 import Error

logger = logging.getLogger(__name__)

MIGRATION_ID = "guobiao_backfill_missing_rank_data_v1"


def _migration_applied(cursor, migration_id: str) -> bool:
    cursor.execute(
        "SELECT 1 FROM data_migrations WHERE migration_id = %s",
        (migration_id,),
    )
    return cursor.fetchone() is not None


def _mark_migration_applied(cursor, migration_id: str) -> None:
    cursor.execute(
        "INSERT INTO data_migrations (migration_id) VALUES (%s) ON CONFLICT DO NOTHING",
        (migration_id,),
    )


def backfill_rank_data(db_manager) -> None:
    """
    为 users 中尚无 rank_data 的用户插入默认 10级 / 0。
    仅执行一次（由 data_migrations 标记）。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        if _migration_applied(cursor, MIGRATION_ID):
            logger.info("rank_data 缺失补全迁移已执行过，跳过")
            return

        cursor.execute("""
            INSERT INTO rank_data (user_id, guobiao_rank, guobiao_score)
            SELECT u.user_id, '10级', 0
            FROM users u
            LEFT JOIN rank_data r ON r.user_id = u.user_id
            WHERE r.user_id IS NULL
        """)
        inserted = cursor.rowcount

        _mark_migration_applied(cursor, MIGRATION_ID)
        conn.commit()
        logger.info("rank_data 缺失补全迁移完成: 新增 %s 条记录", inserted)
    except Error as e:
        logger.error("rank_data 缺失补全迁移失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
