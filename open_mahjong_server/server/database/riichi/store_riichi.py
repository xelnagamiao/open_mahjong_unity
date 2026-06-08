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
    """立直历史统计暂未接入，保留接口供后续扩展。"""
    logger.debug(f"跳过立直聚合统计 game_id={game_id}")
