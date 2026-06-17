"""
为缺少 user_settings / user_config 行的存量用户补默认值。

与 create_user 注册时写入的初始值一致：
- user_settings: title/profile/character/voice = 1
- user_config: volume = 100

本脚本仅执行一次（由 data_migrations 标记）。
"""
from __future__ import annotations

import logging

from psycopg2 import Error

logger = logging.getLogger(__name__)

MIGRATION_ID = "backfill_missing_user_settings_config_v1"


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


def backfill_user_settings_config(db_manager) -> None:
    """
    为 users 中尚无 user_settings / user_config 的用户插入默认行。
    仅执行一次（由 data_migrations 标记）。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        if _migration_applied(cursor, MIGRATION_ID):
            logger.info("user_settings/user_config 缺失补全迁移已执行过，跳过")
            return

        cursor.execute("""
            INSERT INTO user_settings (user_id, title_id, profile_image_id, character_id, voice_id)
            SELECT u.user_id, 1, 1, 1, 1
            FROM users u
            LEFT JOIN user_settings us ON us.user_id = u.user_id
            WHERE us.user_id IS NULL
        """)
        settings_inserted = cursor.rowcount

        cursor.execute("""
            INSERT INTO user_config (user_id, volume)
            SELECT u.user_id, 100
            FROM users u
            LEFT JOIN user_config uc ON uc.user_id = u.user_id
            WHERE uc.user_id IS NULL
        """)
        config_inserted = cursor.rowcount

        _mark_migration_applied(cursor, MIGRATION_ID)
        conn.commit()
        logger.info(
            "user_settings/user_config 缺失补全迁移完成: settings 新增 %s 条, config 新增 %s 条",
            settings_inserted, config_inserted,
        )
    except Error as e:
        logger.error("user_settings/user_config 缺失补全迁移失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
