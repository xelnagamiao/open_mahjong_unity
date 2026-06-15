"""
段位升段分数下调后的存量数据自动升段。

初级场升段分数被调低（5级 60->40, 4级 80->60, 3级 100->60, 2级 100->80）后，
部分玩家的当前分数已超过所在段位的新升段线，需要把他们自动升到正确段位。

本脚本扫描 rank_data，对每个分数已达到/超过升段线的玩家做连续升段
（溢出分带到下一段起始分，与 rank_calculator.apply_pt 的升段规则一致），
并写回数据库。仅执行一次（由 data_migrations 标记）。
"""
from __future__ import annotations

import logging
from typing import Tuple

from psycopg2 import Error

from ...match.rank_calculator import RANK_TABLE, get_rank_index

logger = logging.getLogger(__name__)

MIGRATION_ID = "guobiao_auto_promote_after_promote_score_lowered_v1"


def promote_until_stable(rank_idx: int, score: float) -> Tuple[int, float]:
    """连续升段直到分数低于当前段升段线（溢出分带入下一段起始分）。"""
    while rank_idx < len(RANK_TABLE) - 1:
        _, _, promote_score, _ = RANK_TABLE[rank_idx]
        if score >= promote_score:
            overflow = score - promote_score
            rank_idx += 1
            next_start = RANK_TABLE[rank_idx][1]
            score = next_start + overflow
        else:
            break
    return rank_idx, score


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


def backfill_auto_promote(db_manager) -> None:
    """
    扫描 rank_data，把分数已超过新升段线的玩家自动升段。
    仅执行一次（由 data_migrations 标记）。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        if _migration_applied(cursor, MIGRATION_ID):
            logger.info("段位自动升段迁移已执行过，跳过")
            return

        cursor.execute("SELECT user_id, guobiao_rank, guobiao_score FROM rank_data")
        rows = cursor.fetchall()

        scanned = 0
        promoted = 0
        for user_id, rank_name, score in rows:
            scanned += 1
            if rank_name is None or score is None:
                continue

            old_idx = get_rank_index(rank_name)
            score = float(score)
            new_idx, new_score = promote_until_stable(old_idx, score)

            if new_idx == old_idx:
                continue

            new_rank = RANK_TABLE[new_idx][0]
            new_score = round(new_score, 2)
            cursor.execute(
                """
                UPDATE rank_data
                SET guobiao_rank = %s, guobiao_score = %s, updated_at = CURRENT_TIMESTAMP
                WHERE user_id = %s
                """,
                (new_rank, new_score, user_id),
            )
            promoted += 1
            logger.info(
                "用户 %s 自动升段: %s/%s -> %s/%s",
                user_id, rank_name, score, new_rank, new_score,
            )

        _mark_migration_applied(cursor, MIGRATION_ID)
        conn.commit()
        logger.info(
            "段位自动升段迁移完成: 扫描 %s 名玩家, 升段 %s 名",
            scanned, promoted,
        )
    except Error as e:
        logger.error("段位自动升段迁移失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
