"""
从 game_records / game_player_records 回溯填充 guobiao_history_stats.total_round_score。
"""
import json
import logging
from collections import defaultdict
from typing import Any, Dict, Tuple

from .round_score_utils import sum_player_round_score

logger = logging.getLogger(__name__)

REGISTERED_USER_ID_MIN = 10000000


def backfill_guobiao_total_round_score(db_manager) -> None:
    """
    按 (user_id, mode) 汇总历史牌谱小局得分，写入 guobiao_history_stats.total_round_score。
    仅在迁移时调用；会覆盖该字段的现有值。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        cursor.execute("""
            SELECT gpr.user_id, gpr.match_type, gpr.original_player_index, gr.record
            FROM game_player_records gpr
            INNER JOIN game_records gr ON gr.game_id = gpr.game_id
            WHERE gpr.rule = 'guobiao'
              AND gpr.user_id > %s
              AND gpr.match_type IS NOT NULL
              AND gpr.original_player_index IS NOT NULL
        """, (REGISTERED_USER_ID_MIN,))

        totals: Dict[Tuple[int, str], int] = defaultdict(int)
        processed = 0
        for user_id, match_type, original_player_index, record_raw in cursor.fetchall():
            if record_raw is None:
                continue
            if isinstance(record_raw, str):
                try:
                    record = json.loads(record_raw)
                except json.JSONDecodeError:
                    logger.warning("跳过无效牌谱 JSON: user_id=%s match_type=%s", user_id, match_type)
                    continue
            elif isinstance(record_raw, dict):
                record = record_raw
            else:
                continue

            if not isinstance(record, dict):
                continue

            try:
                orig_idx = int(original_player_index)
            except (TypeError, ValueError):
                continue

            round_score = sum_player_round_score(record, orig_idx)
            totals[(int(user_id), str(match_type))] += round_score
            processed += 1

        cursor.execute("UPDATE guobiao_history_stats SET total_round_score = 0")

        updated_rows = 0
        for (user_id, mode), total_score in totals.items():
            cursor.execute("""
                INSERT INTO guobiao_history_stats (user_id, rule, mode, total_round_score)
                VALUES (%s, 'guobiao', %s, %s)
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    total_round_score = EXCLUDED.total_round_score,
                    updated_at = CURRENT_TIMESTAMP
            """, (user_id, mode, total_score))
            updated_rows += 1

        conn.commit()
        logger.info(
            "国标 total_round_score 回溯完成：处理 %s 条玩家对局记录，更新 %s 个 (user, mode) 统计行",
            processed,
            updated_rows,
        )
    except Exception as e:
        logger.error("回溯国标 total_round_score 失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
        raise
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
