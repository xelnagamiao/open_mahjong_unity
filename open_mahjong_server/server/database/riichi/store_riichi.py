"""
立直麻将游戏记录与统计数据存储。

当前阶段提供接口骨架：如数据库未建表，方法打印日志后返回 None，不阻塞游戏流程。
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
    """保存立直麻将牌谱与玩家对局记录。
    若数据库未建对应表，捕获异常并返回 None 不阻塞游戏。"""
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
                    """
                    INSERT INTO riichi_game_records
                        (game_id, room_type, match_type, game_record, created_at)
                    VALUES (%s, %s, %s, %s, NOW())
                    ON CONFLICT (game_id) DO NOTHING
                    RETURNING game_id;
                    """,
                    (candidate_id, room_type, match_type, game_record_json),
                )
                row = cursor.fetchone()
                if row:
                    game_id = row[0]
                    break
            except Error as e:
                logger.warning(f"立直牌谱写入失败，放弃并跳过: {e}")
                conn.rollback()
                return None
        conn.commit()
        return game_id
    except Exception as e:
        logger.warning(f"立直牌谱存储异常（可能表未建立，跳过）: {e}")
        if conn:
            try:
                conn.rollback()
            except Exception:
                pass
        return None
    finally:
        if conn:
            db_manager._release_connection(conn)


def store_riichi_game_stats(db_manager, game_id: str, player_list: list, room_type: str, max_round: int, total_rounds: int) -> None:
    """保存立直对局玩家维度统计。底层表未建立时写入失败直接跳过。"""
    conn = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        for player in player_list:
            try:
                cursor.execute(
                    """
                    INSERT INTO riichi_history_stats
                        (game_id, user_id, room_type, max_round, total_rounds, final_score, rank)
                    VALUES (%s, %s, %s, %s, %s, %s, %s)
                    ON CONFLICT DO NOTHING;
                    """,
                    (
                        game_id,
                        player.user_id,
                        room_type,
                        max_round,
                        total_rounds,
                        player.score,
                        player.record_counter.rank_result,
                    ),
                )
            except Error as e:
                logger.warning(f"立直统计写入失败，跳过：user_id={player.user_id} {e}")
                conn.rollback()
                return
        conn.commit()
    except Exception as e:
        logger.warning(f"立直统计存储异常（可能表未建立，跳过）: {e}")
        if conn:
            try:
                conn.rollback()
            except Exception:
                pass
    finally:
        if conn:
            db_manager._release_connection(conn)
