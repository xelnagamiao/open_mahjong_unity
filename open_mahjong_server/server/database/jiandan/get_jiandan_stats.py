"""Read Jiandan history and fan statistics."""

from __future__ import annotations

import logging
from typing import Any, Dict, List

from psycopg2.extras import RealDictCursor

from .store_jiandan import FAN_FIELDS

logger = logging.getLogger(__name__)


def get_jiandan_history_stats(db_manager, user_id: int) -> List[Dict[str, Any]]:
    conn = None
    cursor = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor(cursor_factory=RealDictCursor)
        cursor.execute(
            """
            SELECT
                rule,
                mode,
                COALESCE(SUM(total_games), 0) AS total_games,
                COALESCE(SUM(total_rounds), 0) AS total_rounds,
                COALESCE(SUM(win_count), 0) AS win_count,
                COALESCE(SUM(self_draw_count), 0) AS self_draw_count,
                COALESCE(SUM(deal_in_count), 0) AS deal_in_count,
                COALESCE(SUM(total_fan_score), 0) AS total_fan_score,
                COALESCE(SUM(total_win_turn), 0) AS total_win_turn,
                COALESCE(SUM(total_fangchong_score), 0) AS total_fangchong_score,
                COALESCE(SUM(first_place_count), 0) AS first_place_count,
                COALESCE(SUM(second_place_count), 0) AS second_place_count,
                COALESCE(SUM(third_place_count), 0) AS third_place_count,
                COALESCE(SUM(fourth_place_count), 0) AS fourth_place_count,
                COALESCE(SUM(fulu_round_count), 0) AS fulu_round_count
            FROM jiandan_history_stats
            WHERE user_id = %s
            GROUP BY rule, mode
            ORDER BY rule, mode
            """,
            (user_id,),
        )
        return [dict(row) for row in cursor.fetchall()]
    except Exception as exc:
        logger.error("Failed to read Jiandan history statistics: %s", exc, exc_info=True)
        return []
    finally:
        if cursor is not None:
            cursor.close()
        if conn is not None:
            db_manager._put_connection(conn)


def get_jiandan_fan_stats_total(db_manager, user_id: int) -> Dict[str, int]:
    conn = None
    cursor = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor(cursor_factory=RealDictCursor)
        fan_columns = ", ".join(
            f"COALESCE(SUM({fan_id}), 0) AS {fan_id}" for fan_id in FAN_FIELDS
        )
        cursor.execute(
            f"SELECT {fan_columns} FROM jiandan_fan_stats WHERE user_id = %s",
            (user_id,),
        )
        row = cursor.fetchone()
        return {key: value for key, value in dict(row).items() if value is not None} if row else {}
    except Exception as exc:
        logger.error("Failed to read Jiandan fan statistics: %s", exc, exc_info=True)
        return {}
    finally:
        if cursor is not None:
            cursor.close()
        if conn is not None:
            db_manager._put_connection(conn)
