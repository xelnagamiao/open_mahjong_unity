"""
天梯场次番种统计：按 (统计日, match_tier) 写入 scene_tier_fan_daily，查询时 SUM 得全站总计。
"""
import logging
from datetime import date
from typing import Dict

from psycopg2 import Error

from .daily_aggregator import MATCH_TIERS, MATCH_TIER_SQL, _stat_date_expr
from .guobiao.record_analyzer import collect_fans_for_player
from .guobiao.store_guobiao import FAN_FIELDS
from .backfill_game_player_metrics import (
    REGISTERED_USER_ID_MIN,
    _parse_record,
    _resolve_scene_fields,
)

logger = logging.getLogger(__name__)


def _accumulate_tier_fans(cursor, game_ids: list, fan_agg: Dict[tuple, Dict[str, int]]) -> None:
    if not game_ids:
        return
    cursor.execute("""
        SELECT gpr.game_id, gpr.rule, gpr.room_type, gpr.match_tier, gpr.event_id,
               gpr.original_player_index, gr.record
        FROM game_player_records gpr
        INNER JOIN game_records gr ON gr.game_id = gpr.game_id
        WHERE gpr.game_id = ANY(%s::varchar[])
          AND gpr.user_id > %s
          AND gpr.original_player_index IS NOT NULL
    """, (game_ids, REGISTERED_USER_ID_MIN))
    for _game_id, rule, room_type, match_tier, event_id, opi, record_raw in cursor.fetchall():
        record = _parse_record(record_raw)
        if record is None:
            continue
        room_type, match_tier, event_id = _resolve_scene_fields(
            record, room_type, match_tier, event_id,
        )
        if room_type == "match" and match_tier in MATCH_TIERS:
            key_tier = match_tier
        elif room_type == "events" and event_id:
            key_tier = match_tier or event_id
        else:
            continue
        if rule != "guobiao":
            continue
        try:
            orig_idx = int(opi)
        except (TypeError, ValueError):
            continue
        fans = collect_fans_for_player(record, orig_idx)
        if not fans:
            continue
        key = (key_tier, rule)
        bucket = fan_agg.setdefault(key, {f: 0 for f in FAN_FIELDS})
        for field, cnt in fans.items():
            bucket[field] = bucket.get(field, 0) + cnt


def _write_fan_daily(cursor, stat_date: date, fan_agg: Dict[tuple, Dict[str, int]]) -> int:
    cursor.execute(
        "DELETE FROM scene_tier_fan_daily WHERE stat_date = %s",
        (stat_date,),
    )
    inserted = 0
    for (match_tier, rule), fans in fan_agg.items():
        for field, cnt in fans.items():
            if not cnt:
                continue
            cursor.execute("""
                INSERT INTO scene_tier_fan_daily (stat_date, match_tier, rule, fan_field, fan_count)
                VALUES (%s, %s, %s, %s, %s)
            """, (stat_date, match_tier, rule, field, cnt))
            inserted += 1
    return inserted


def rebuild_scene_tier_fan_daily_range(
    db_manager,
    date_from: date,
    date_to: date,
) -> None:
    """按统计日逐日重建 scene_tier_fan_daily（首次回填）。"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute(
            "DELETE FROM scene_tier_fan_daily WHERE stat_date >= %s AND stat_date <= %s",
            (date_from, date_to),
        )
        current = date_from
        total_rows = 0
        while current <= date_to:
            date_expr = _stat_date_expr("created_at")
            cursor.execute(f"""
                SELECT DISTINCT game_id FROM game_player_metrics
                WHERE {date_expr} = %s
                  AND (
                    (room_type = 'match' AND match_tier IN ({MATCH_TIER_SQL}))
                    OR (room_type = 'events' AND event_id IS NOT NULL)
                  )
                  AND rule = 'guobiao'
            """, (current,))
            game_ids = [r[0] for r in cursor.fetchall()]
            fan_agg: Dict[tuple, Dict[str, int]] = {}
            _accumulate_tier_fans(cursor, game_ids, fan_agg)
            total_rows += _write_fan_daily(cursor, current, fan_agg)
            current = date.fromordinal(current.toordinal() + 1)
        conn.commit()
        logger.info(
            "scene_tier_fan_daily 重建完成：%s ~ %s，共 %d 行",
            date_from, date_to, total_rows,
        )
    except Error as e:
        logger.error("重建 scene_tier_fan_daily 失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
        raise
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)


def increment_scene_tier_fan_for_date(db_manager, stat_date: date) -> None:
    """04:00 增量：DELETE+INSERT 指定统计日，与 scene_daily_stats 同口径可重跑。"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        date_expr = _stat_date_expr("created_at")
        cursor.execute(f"""
            SELECT DISTINCT game_id FROM game_player_metrics
            WHERE {date_expr} = %s
              AND (
                (room_type = 'match' AND match_tier IN ({MATCH_TIER_SQL}))
                OR (room_type = 'events' AND event_id IS NOT NULL)
              )
              AND rule = 'guobiao'
        """, (stat_date,))
        game_ids = [r[0] for r in cursor.fetchall()]
        fan_agg: Dict[tuple, Dict[str, int]] = {}
        _accumulate_tier_fans(cursor, game_ids, fan_agg)
        rows = _write_fan_daily(cursor, stat_date, fan_agg)
        conn.commit()
        logger.info("scene_tier_fan_daily 已聚合 %s：%d 局，%d 番种行", stat_date, len(game_ids), rows)
    except Error as e:
        logger.error("聚合 scene_tier_fan_daily 失败 %s: %s", stat_date, e, exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
