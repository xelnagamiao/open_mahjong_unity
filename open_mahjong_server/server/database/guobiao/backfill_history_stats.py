"""
分析工具：从历史牌谱 JSON 重建 guobiao_history_stats，按 (user_id, match_type) 分开
排位(_rank)与自定义房间数据，并正确赋值 total_round_score（局均点分子）。

- 数据源：game_records.record + game_player_records（match_type / original_player_index / rank）
- 复用 round_score_utils 解析每小局净得分；和牌/放铳/错和/副露从 action_ticks 重建
- 可重跑：先 DELETE guobiao_history_stats 再 INSERT
- 手动运行：python -m server.database.guobiao.backfill_history_stats
"""
import json
import logging
from collections import defaultdict
from typing import Any, Dict, Tuple

from .record_analyzer import analyze_record_for_player, collect_fans_for_player
from .round_score_utils import sum_player_round_score
from .store_guobiao import FAN_FIELDS

logger = logging.getLogger(__name__)

REGISTERED_USER_ID_MIN = 10000000
_SKIP_SUB_RULES = ("guobiao/xiaolin", "guobiao/kshen", "guobiao/lanshi")


def backfill_guobiao_history_stats(db_manager) -> None:
    """重建 guobiao_history_stats：按 (user_id, match_type) 分开聚合，局均点分子正确赋值。"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        cursor.execute("""
            SELECT DISTINCT game_id FROM game_player_records
            WHERE rule='guobiao' AND user_id <= %s
        """, (REGISTERED_USER_ID_MIN,))
        exclude_games = {r[0] for r in cursor.fetchall()}

        cursor.execute("""
            SELECT DISTINCT game_id FROM game_player_records
            WHERE rule='guobiao'
              AND user_id > %s
              AND match_type IS NOT NULL
              AND (sub_rule IS NULL OR sub_rule NOT IN %s)
        """, (REGISTERED_USER_ID_MIN, _SKIP_SUB_RULES))
        eligible_games = [r[0] for r in cursor.fetchall() if r[0] not in exclude_games]
        logger.info("backfill history_stats: eligible games=%d, excluded(AI)=%d", len(eligible_games), len(exclude_games))

        cursor.execute("""
            SELECT game_id, user_id, match_type, original_player_index, rank
            FROM game_player_records
            WHERE rule='guobiao' AND user_id > %s AND match_type IS NOT NULL
              AND game_id = ANY(%s::varchar[])
        """, (REGISTERED_USER_ID_MIN, eligible_games))
        gpr_rows = cursor.fetchall()

        cursor.execute("""
            SELECT game_id, record FROM game_records
            WHERE game_id = ANY(%s::varchar[])
        """, (eligible_games,))
        record_map: Dict[str, Any] = {}
        for gid, raw in cursor.fetchall():
            if raw is None:
                continue
            record_map[gid] = json.loads(raw) if isinstance(raw, str) else raw

        agg: Dict[Tuple[int, str], dict] = defaultdict(lambda: {
            "total_games": 0, "total_rounds": 0, "win_count": 0, "self_draw_count": 0,
            "deal_in_count": 0, "total_fan_score": 0, "total_fangchong_score": 0,
            "first_place_count": 0, "second_place_count": 0, "third_place_count": 0,
            "fourth_place_count": 0, "fulu_round_count": 0, "cuohe_count": 0,
            "total_round_score": 0, "total_win_turn": 0,
        })
        fan_agg: Dict[Tuple[int, str], Dict[str, int]] = defaultdict(lambda: {f: 0 for f in FAN_FIELDS})
        processed = 0
        for game_id, user_id, match_type, opi, rank in gpr_rows:
            rec = record_map.get(game_id)
            if rec is None:
                continue
            try:
                orig_idx = int(opi)
            except (TypeError, ValueError):
                continue
            key = (int(user_id), str(match_type))
            a = agg[key]
            a["total_games"] += 1
            game_round = rec.get("game_round") or {}
            a["total_rounds"] += sum(1 for k in game_round if isinstance(k, str) and k.startswith("round_index_"))
            cnt = analyze_record_for_player(rec, orig_idx)
            if cnt:
                a["win_count"] += cnt["zimo"] + cnt["dianhe"]
                a["self_draw_count"] += cnt["zimo"]
                a["deal_in_count"] += cnt["fangchong"]
                a["total_fan_score"] += cnt["win_score"]
                a["total_fangchong_score"] += cnt["fangchong_score"]
                a["fulu_round_count"] += cnt["fulu_rounds"]
                a["cuohe_count"] += cnt["cuohe"]
                a["total_win_turn"] += cnt["win_turn"]
            a["total_round_score"] += sum_player_round_score(rec, orig_idx)
            r = int(rank) if rank is not None else 0
            if r == 1:
                a["first_place_count"] += 1
            elif r == 2:
                a["second_place_count"] += 1
            elif r == 3:
                a["third_place_count"] += 1
            elif r == 4:
                a["fourth_place_count"] += 1
            for field, c in collect_fans_for_player(rec, orig_idx).items():
                fan_agg[key][field] = fan_agg[key].get(field, 0) + c
            processed += 1

        cursor.execute("DELETE FROM guobiao_history_stats")
        cols = [
            "user_id", "rule", "mode", "total_games", "total_rounds", "win_count",
            "self_draw_count", "deal_in_count", "total_fan_score", "total_win_turn",
            "total_fangchong_score", "first_place_count", "second_place_count",
            "third_place_count", "fourth_place_count", "fulu_round_count",
            "cuohe_count", "total_round_score",
        ]
        placeholders = ", ".join(["%s"] * len(cols))
        col_list = ", ".join(cols)
        inserted = 0
        for (uid, mt), a in agg.items():
            values = [
                uid, "guobiao", mt, a["total_games"], a["total_rounds"], a["win_count"],
                a["self_draw_count"], a["deal_in_count"], a["total_fan_score"],
                a["total_win_turn"], a["total_fangchong_score"], a["first_place_count"],
                a["second_place_count"], a["third_place_count"], a["fourth_place_count"],
                a["fulu_round_count"], a["cuohe_count"], a["total_round_score"],
            ]
            cursor.execute(
                f"INSERT INTO guobiao_history_stats ({col_list}) VALUES ({placeholders})",
                values,
            )
            inserted += 1

        cursor.execute("DELETE FROM guobiao_fan_stats")
        fan_cols = ["user_id", "rule", "mode"] + FAN_FIELDS
        fan_placeholders = ", ".join(["%s"] * len(fan_cols))
        fan_col_list = ", ".join(fan_cols)
        fan_inserted = 0
        for (uid, mt), fans in fan_agg.items():
            if not any(v for v in fans.values()):
                continue
            fan_values = [uid, "guobiao", mt] + [fans.get(f, 0) for f in FAN_FIELDS]
            cursor.execute(
                f"INSERT INTO guobiao_fan_stats ({fan_col_list}) VALUES ({fan_placeholders})",
                fan_values,
            )
            fan_inserted += 1

        conn.commit()
        logger.info(
            "guobiao_history_stats 重建完成：处理 %d 条玩家对局，写入 %d 个 history 行 / %d 个 fan 行",
            processed, inserted, fan_inserted,
        )
    except Exception as e:
        logger.error("重建 guobiao_history_stats 失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
        raise
    finally:
        if conn:
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
    backfill_guobiao_history_stats(db)
    print("guobiao_history_stats 回填完成")
