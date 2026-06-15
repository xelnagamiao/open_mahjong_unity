"""
从国标牌谱回填「七对」番种统计。

历史版本中 store_guobiao_fan_stats 使用「七对子」作为映射 key，而和牌结算输出「七对」，
导致 qiduizi 字段长期未累加。本脚本扫描 game_records 中的和牌番种，统计「七对」「七对子」
（及 kshen 变体「七对/七小对」），按牌谱统计结果覆盖写入 guobiao_fan_stats.qiduizi（非叠加增量）。
"""
from __future__ import annotations

import json
import logging
from collections import defaultdict
from typing import Any, Dict, Iterable, Optional, Tuple

from psycopg2 import Error

logger = logging.getLogger(__name__)

MIGRATION_ID = "guobiao_backfill_qiduizi_from_replays_v2"

QIDUIZI_FAN_NAMES = frozenset({"七对", "七对子", "七对/七小对"})
HU_CLASSES = frozenset({"hu_self", "hu_first", "hu_second", "hu_third"})


def _is_qiduizi_fan(fan_name: Any) -> bool:
    if not isinstance(fan_name, str):
        return False
    base_name = fan_name.partition("*")[0].strip()
    return base_name in QIDUIZI_FAN_NAMES


def _should_count_game(game_title: dict, user_ids: Iterable[Optional[int]]) -> bool:
    if game_title.get("rule") != "guobiao":
        return False
    if game_title.get("sub_rule") in ("guobiao/xiaolin", "guobiao/kshen"):
        return False
    if game_title.get("hepai_limit", 8) != 8:
        return False
    if any((uid or 0) <= 10 for uid in user_ids):
        return False
    return True


def _uid_for_player_index(game_title: dict, seats: list, player_index: int) -> Optional[int]:
    if not seats or len(seats) != 4:
        return None
    for original_i, seat_player_index in enumerate(seats):
        if seat_player_index == player_index:
            return game_title.get(f"p{original_i}_uid")
    return None


def count_qiduizi_from_record(record: dict) -> Dict[int, int]:
    """从单份牌谱 JSON 统计各玩家的七对和牌次数（仅注册用户）。"""
    counts: Dict[int, int] = defaultdict(int)
    game_title = record.get("game_title") or {}
    user_ids = [game_title.get(f"p{i}_uid") for i in range(4)]
    if not _should_count_game(game_title, user_ids):
        return counts

    for round_data in (record.get("game_round") or {}).values():
        if not isinstance(round_data, dict):
            continue
        seats = round_data.get("seats") or [0, 1, 2, 3]
        for tick in round_data.get("action_ticks") or []:
            if not isinstance(tick, list) or len(tick) < 4:
                continue
            if tick[0] not in HU_CLASSES:
                continue
            hu_fan = tick[3]
            if not isinstance(hu_fan, list):
                continue
            if "错和" in hu_fan:
                continue
            if not any(_is_qiduizi_fan(fan_name) for fan_name in hu_fan):
                continue

            hepai_player_index = tick[1]
            if not isinstance(hepai_player_index, int):
                continue
            user_id = _uid_for_player_index(game_title, seats, hepai_player_index)
            if user_id and user_id > 10_000_000:
                counts[user_id] += 1
    return counts


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


def backfill_qiduizi_stats(db_manager) -> None:
    """
    扫描国标牌谱，以牌谱为准覆盖 guobiao_fan_stats.qiduizi（跳过错和、小林规等不计统计的对局）。
    仅执行一次（由 data_migrations 标记）。
    """
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        if _migration_applied(cursor, MIGRATION_ID):
            logger.info("七对番种牌谱回填已执行过，跳过")
            return

        record_totals: Dict[Tuple[int, str, str], int] = defaultdict(int)
        cursor.execute("""
            SELECT gr.game_id, gr.record, MIN(gpr.match_type) AS match_type
            FROM game_records gr
            INNER JOIN game_player_records gpr ON gpr.game_id = gr.game_id
            WHERE gpr.rule = 'guobiao'
            GROUP BY gr.game_id, gr.record
        """)

        scanned_games = 0
        matched_games = 0
        for _game_id, record_raw, match_type in cursor.fetchall():
            scanned_games += 1
            if isinstance(record_raw, str):
                record = json.loads(record_raw)
            else:
                record = record_raw

            game_counts = count_qiduizi_from_record(record)
            if not game_counts:
                continue

            matched_games += 1
            mode = match_type
            if not mode:
                max_round = (record.get("game_title") or {}).get("max_round", 4)
                mode = f"{max_round}/4"

            rule = "guobiao"
            for user_id, count in game_counts.items():
                record_totals[(user_id, rule, mode)] += count

        updated_rows = 0
        total_qiduizi = 0
        for (user_id, rule, mode), expected in record_totals.items():
            cursor.execute(
                """
                INSERT INTO guobiao_fan_stats (user_id, rule, mode, qiduizi)
                VALUES (%s, %s, %s, %s)
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    qiduizi = EXCLUDED.qiduizi,
                    updated_at = CURRENT_TIMESTAMP
                """,
                (user_id, rule, mode, expected),
            )
            updated_rows += 1
            total_qiduizi += expected

        _mark_migration_applied(cursor, MIGRATION_ID)
        conn.commit()
        logger.info(
            "七对番种牌谱回填完成: 扫描 %s 局, 命中 %s 局, 覆盖更新 %s 条统计, 牌谱合计七对 %s 次",
            scanned_games,
            matched_games,
            updated_rows,
            total_qiduizi,
        )
    except Error as e:
        logger.error("七对番种牌谱回填失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
