"""
立直麻将游戏记录存储：与国标/古典相同，写入 game_records + game_player_records。
"""
import json
import logging
import string
import secrets
from typing import Optional
from psycopg2 import Error

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits
GAME_ID_LENGTH = 10


def _generate_game_id(length: int = GAME_ID_LENGTH) -> str:
    return ''.join(secrets.choice(GAME_ID_ALPHABET) for _ in range(length))


def store_riichi_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str) -> Optional[str]:
    if any(getattr(p, "user_id", 0) <= 10 for p in player_list):
        logger.info("立直对局包含机器人，跳过牌谱与对局记录保存")
        return None

    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()

        game_record_json = json.dumps(game_record, ensure_ascii=False, default=str)
        max_retries = 5
        game_id = None
        for _ in range(max_retries):
            candidate_id = _generate_game_id()
            try:
                cursor.execute(
                    "INSERT INTO game_records (game_id, record) VALUES (%s, %s)",
                    (candidate_id, game_record_json),
                )
                game_id = candidate_id
                break
            except Error:
                conn.rollback()
                continue
        if game_id is None:
            logger.error("立直牌谱多次生成 game_id 均失败")
            return None

        game_title = game_record.get("game_title") or {}
        rule = game_title["rule"]
        sub_rule = game_title["sub_rule"]
        saved_count = 0
        for player in player_list:
            rank = player.record_counter.rank_result
            try:
                cursor.execute("""
                    INSERT INTO game_player_records (
                        game_id, user_id, username, score, rank, original_player_index, rule, sub_rule, match_type, room_type,
                        title_used, character_used, profile_used, voice_used
                    ) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                """, (
                    game_id,
                    player.user_id,
                    player.username,
                    player.score,
                    rank,
                    player.original_player_index,
                    rule,
                    sub_rule,
                    match_type,
                    room_type,
                    getattr(player, "title_used", None),
                    getattr(player, "character_used", None),
                    getattr(player, "profile_used", None),
                    getattr(player, "voice_used", None),
                ))
                saved_count += 1
            except Error as e:
                logger.warning(f"跳过立直玩家对局记录: user_id={player.user_id}, error={e}")

        conn.commit()
        logger.info(f"立直牌谱已保存 game_id={game_id}，玩家记录 {saved_count} 条")
        return game_id
    except Exception as e:
        logger.error(f"立直牌谱存储失败: {e}", exc_info=True)
        if conn:
            conn.rollback()
        return None
    finally:
        if conn:
            db_manager._put_connection(conn)


def store_riichi_game_stats(db_manager, game_id: str, player_list: list, room_type: str, max_round: int, total_rounds: int) -> None:
    """存储立直麻将基础统计数据（含副露局数）。"""
    conn = None
    try:
        if any(getattr(p, "user_id", 0) <= 10 for p in player_list):
            logger.info("对局包含机器人，跳过立直基础统计保存")
            return

        conn = db_manager._get_connection()
        cursor = conn.cursor()

        rule = "riichi"
        mode = f"{max_round}/4"

        stats_columns = [
            "total_games", "total_rounds", "win_count", "self_draw_count",
            "deal_in_count", "total_fan_score", "total_win_turn",
            "total_fangchong_score", "first_place_count", "second_place_count",
            "third_place_count", "fourth_place_count", "fulu_round_count",
        ]

        for player in player_list:
            user_id = player.user_id
            if user_id <= 10000000:
                continue

            cursor.execute("SELECT 1 FROM users WHERE user_id = %s", (user_id,))
            if cursor.fetchone() is None:
                continue

            counter = player.record_counter
            win_count = counter.zimo_times + counter.dianhe_times
            stats_increment = {
                "total_games": 1,
                "total_rounds": total_rounds,
                "win_count": win_count,
                "self_draw_count": counter.zimo_times,
                "deal_in_count": counter.fangchong_times,
                "total_fan_score": counter.win_score,
                "total_win_turn": counter.win_turn,
                "total_fangchong_score": counter.fangchong_score,
                "first_place_count": 1 if counter.rank_result == 1 else 0,
                "second_place_count": 1 if counter.rank_result == 2 else 0,
                "third_place_count": 1 if counter.rank_result == 3 else 0,
                "fourth_place_count": 1 if counter.rank_result == 4 else 0,
                "fulu_round_count": counter.fulu_times,
            }

            insert_columns = ["user_id", "rule", "mode"] + stats_columns
            insert_values = [user_id, rule, mode] + [stats_increment.get(col, 0) for col in stats_columns]
            update_clauses = ", ".join(
                f"{col} = riichi_history_stats.{col} + EXCLUDED.{col}"
                for col in stats_columns
            )

            cursor.execute(f"""
                INSERT INTO riichi_history_stats (
                    {', '.join(insert_columns)}
                ) VALUES (
                    {', '.join(['%s'] * len(insert_columns))}
                )
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    {update_clauses},
                    updated_at = CURRENT_TIMESTAMP
            """, insert_values)

        conn.commit()
        logger.info("立直基础统计数据已保存，game_id: %s", game_id)
    except Error as e:
        logger.error("存储立直基础统计数据失败: %s", e, exc_info=True)
        if conn:
            conn.rollback()
    finally:
        if conn:
            cursor.close()
            db_manager._put_connection(conn)
