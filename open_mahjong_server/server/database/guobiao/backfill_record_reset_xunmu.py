"""
一次性：给历史国标牌谱补上开局 ['reset', start_player_index]，
并按 player_index_go_to 周巡目重算 total_win_turn。

- 改 game_records.record JSON
- 更新 game_player_metrics.total_win_turn
- 重建 guobiao_history_stats
- 重聚合含国标 metrics 的 scene_daily_stats 统计日

手动运行：python -m server.database.guobiao.backfill_record_reset_xunmu
生产由 run_startup_stats_restore 按 app_meta 只跑一次。
"""
import json
import logging
from typing import Any, Dict, Optional

from psycopg2.extras import Json

from .backfill_history_stats import backfill_guobiao_history_stats
from .record_analyzer import analyze_record_for_player, patch_guobiao_record_resets

logger = logging.getLogger(__name__)

STAT_TZ = "Asia/Shanghai"
STAT_DAY_OFFSET_HOURS = 4


def _parse_record(raw) -> Optional[Dict[str, Any]]:
    if raw is None:
        return None
    if isinstance(raw, str):
        try:
            return json.loads(raw)
        except json.JSONDecodeError:
            return None
    if isinstance(raw, dict):
        return raw
    return None


def _is_guobiao_record(record: Dict[str, Any]) -> bool:
    title = record.get("game_title") or {}
    return isinstance(title, dict) and str(title.get("rule") or "").lower() == "guobiao"


def _stat_date_expr(column: str) -> str:
    return f"(({column} AT TIME ZONE '{STAT_TZ}') - interval '{STAT_DAY_OFFSET_HOURS} hours')::date"


def backfill_guobiao_record_reset_xunmu(db_manager) -> None:
    conn = None
    cursor = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        cursor.execute("SELECT game_id, record FROM game_records")
        rows = cursor.fetchall()
        patched_games = 0
        inserted_resets = 0
        record_map: Dict[str, Dict[str, Any]] = {}
        for game_id, raw in rows:
            record = _parse_record(raw)
            if record is None or not _is_guobiao_record(record):
                continue
            inserted = patch_guobiao_record_resets(record)
            record_map[game_id] = record
            if inserted:
                cursor.execute(
                    "UPDATE game_records SET record = %s WHERE game_id = %s",
                    (Json(record), game_id),
                )
                patched_games += 1
                inserted_resets += inserted
        conn.commit()
        logger.info(
            "国标牌谱 reset 补齐：扫描 %d 份，改写 %d 份，插入 %d 条 reset",
            len(record_map), patched_games, inserted_resets,
        )

        cursor.execute("""
            SELECT m.game_id, m.user_id, p.original_player_index, m.total_win_turn
            FROM game_player_metrics m
            JOIN game_player_records p ON p.game_id = m.game_id AND p.user_id = m.user_id
            WHERE m.rule = 'guobiao'
        """)
        metric_rows = cursor.fetchall()
        updated_metrics = 0
        for game_id, user_id, original_index, old_win_turn in metric_rows:
            record = record_map.get(game_id)
            if record is None or original_index is None:
                continue
            try:
                orig = int(original_index)
            except (TypeError, ValueError):
                continue
            cnt = analyze_record_for_player(record, orig)
            if not cnt:
                continue
            new_win_turn = int(cnt["win_turn"])
            if new_win_turn == (old_win_turn or 0):
                continue
            cursor.execute(
                """
                UPDATE game_player_metrics
                SET total_win_turn = %s
                WHERE game_id = %s AND user_id = %s
                """,
                (new_win_turn, game_id, user_id),
            )
            updated_metrics += 1
        conn.commit()
        logger.info("game_player_metrics.total_win_turn 更新 %d 行", updated_metrics)

        cursor.execute(f"""
            SELECT DISTINCT {_stat_date_expr("created_at")} AS stat_date
            FROM game_player_metrics
            WHERE rule = 'guobiao'
        """)
        scene_dates = [row[0] for row in cursor.fetchall() if row[0] is not None]
        cursor.close()
        db_manager._put_connection(conn)
        conn = None

        backfill_guobiao_history_stats(db_manager)

        if scene_dates:
            from ..daily_aggregator import aggregate_scene_daily_stats
            for stat_date in scene_dates:
                aggregate_scene_daily_stats(db_manager, stat_date)
            logger.info("scene_daily_stats 已重聚合 %d 个统计日", len(scene_dates))
    except Exception:
        if conn:
            conn.rollback()
        raise
    finally:
        if conn:
            if cursor is not None:
                cursor.close()
            db_manager._put_connection(conn)


if __name__ == "__main__":
    import sys
    from pathlib import Path

    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(message)s")
    root = Path(__file__).resolve().parents[3]
    if str(root) not in sys.path:
        sys.path.insert(0, str(root))
    from server.local_config import Config
    from server.database.db_manager import DatabaseManager

    db = DatabaseManager(Config.host, Config.user, Config.password, Config.database, Config.port)
    db.init_database()
    backfill_guobiao_record_reset_xunmu(db)
    print("国标牌谱 reset / 和巡回填完成")
