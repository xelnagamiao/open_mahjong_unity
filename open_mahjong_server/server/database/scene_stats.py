"""
每玩家每局原始指标写入 game_player_metrics，供每天 04:00 聚合 scene_daily_stats。
复用各规则 record_counter 已有字段，避免重解牌谱 JSON。
"""
import logging
from psycopg2 import Error
from typing import Any, Dict, List, Optional

logger = logging.getLogger(__name__)

# match_type → 局制 game_type
_GAME_TYPE_MAP = {
    "1/4": "dongfeng", "1/4_rank": "dongfeng",
    "2/4": "banzhuang", "2/4_rank": "banzhuang",
    "3/4": "xifeng",
    "4/4": "quanzhuang", "4/4_rank": "quanzhuang",
}


def derive_game_type(match_type: Optional[str]) -> Optional[str]:
    if not match_type:
        return None
    return _GAME_TYPE_MAP.get(match_type)


def _count_rounds(game_record: Dict[str, Any]) -> int:
    game_round = game_record.get("game_round") or {}
    if not isinstance(game_round, dict):
        return 0
    return sum(1 for k in game_round.keys() if isinstance(k, str) and k.startswith("round_index_"))


def record_game_metrics(
    db_manager,
    game_id: str,
    game_record: Dict[str, Any],
    player_list: list,
    scene: Dict[str, Any],
) -> None:
    """
    将每位注册玩家的本局指标写入 game_player_metrics。

    Args:
        db_manager: DatabaseManager
        game_id: 牌谱 id
        game_record: 牌谱 dict（用于统计小局数）
        player_list: 玩家列表，每个玩家含 record_counter
        scene: {rule, sub_rule, room_type, match_tier, event_id, match_type}
    """
    conn = None
    try:
        # 含机器人或无注册玩家时不记录
        if any(getattr(p, "user_id", 0) <= 10 for p in player_list):
            return

        total_rounds = _count_rounds(game_record)
        match_type = scene.get("match_type")
        game_type = derive_game_type(match_type)

        conn = db_manager._get_connection()
        cursor = conn.cursor()
        saved = 0
        for player in player_list:
            user_id = getattr(player, "user_id", 0)
            # 跳过游客（仅统计注册用户 user_id > 10000000）
            if user_id <= 10000000:
                continue
            counter = getattr(player, "record_counter", None)
            if counter is None:
                continue
            zimo = getattr(counter, "zimo_times", 0) or 0
            dianhe = getattr(counter, "dianhe_times", 0) or 0
            fangchong = getattr(counter, "fangchong_times", 0) or 0
            rank = getattr(counter, "rank_result", 0) or 0

            cursor.execute("""
                INSERT INTO game_player_metrics (
                    game_id, user_id, username, rule, sub_rule, room_type, match_tier, event_id,
                    game_type, match_type, score, rank, total_rounds,
                    win_count, self_draw_count, deal_in_count, total_fan_score, total_win_turn,
                    total_fangchong_score, first_place_count, second_place_count, third_place_count,
                    fourth_place_count, fulu_round_count, cuohe_count, total_round_score
                ) VALUES (
                    %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s,
                    %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s
                )
            """, (
                game_id,
                user_id,
                getattr(player, "username", f"用户{user_id}"),
                scene.get("rule"),
                scene.get("sub_rule"),
                scene.get("room_type"),
                scene.get("match_tier"),
                scene.get("event_id"),
                game_type,
                match_type,
                getattr(player, "score", 0) or 0,
                rank,
                total_rounds,
                zimo + dianhe,
                zimo,
                fangchong,
                getattr(counter, "win_score", 0) or 0,
                getattr(counter, "win_turn", 0) or 0,
                getattr(counter, "fangchong_score", 0) or 0,
                1 if rank == 1 else 0,
                1 if rank == 2 else 0,
                1 if rank == 3 else 0,
                1 if rank == 4 else 0,
                getattr(counter, "fulu_times", 0) or 0,
                getattr(counter, "cuohe_times", 0) or 0,
                getattr(counter, "round_score_total", 0) or 0,
            ))
            saved += 1
        conn.commit()
        logger.info(f"game_player_metrics 已写入 {saved} 行，game_id={game_id}")
    except Error as e:
        logger.error(f"写入 game_player_metrics 失败: {e}", exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
