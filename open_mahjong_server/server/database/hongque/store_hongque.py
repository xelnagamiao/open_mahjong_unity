"""虹雀牌谱写入通用表 game_records / game_player_records。含机器人则跳过入库。"""
import json
import logging
import secrets
import string
from psycopg2 import Error

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits
GAME_ID_LENGTH = 10


def generate_game_id(length: int = GAME_ID_LENGTH) -> str:
    return "".join(secrets.choice(GAME_ID_ALPHABET) for _ in range(length))


def store_hongque_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str):
    conn = None
    try:
        if any(getattr(player, "user_id", 0) <= 10 for player in player_list):
            logger.info("对局包含机器人，跳过虹雀牌谱与对局记录保存")
            return None

        conn = db_manager._get_connection()
        cursor = conn.cursor()
        game_record_json = json.dumps(game_record, ensure_ascii=False, default=str)
        game_id = None
        for _ in range(5):
            candidate_id = generate_game_id()
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
            logger.error("多次生成 game_id 均碰撞，虹雀牌谱存储失败")
            return None

        game_title = game_record.get("game_title") or {}
        rule = game_title.get("rule", "hongque")
        sub_rule = game_title.get("sub_rule", "hongque/v1.6")
        match_tier = game_title.get("match_tier")
        event_id = game_title.get("event_id")
        from ..scene_stats import normalize_scene_fields
        room_type, match_tier, event_id = normalize_scene_fields(room_type, match_tier, event_id)
        saved_count = 0
        for player in player_list:
            rank = getattr(getattr(player, "record_counter", None), "rank_result", 0) or 0
            try:
                cursor.execute(
                    """
                    INSERT INTO game_player_records (
                        game_id, user_id, username, score, rank, original_player_index,
                        rule, sub_rule, match_type, room_type, match_tier, event_id,
                        title_used, character_used, profile_used, voice_used
                    ) VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                    """,
                    (
                        game_id, player.user_id, player.username, player.score, rank,
                        player.original_player_index, rule, sub_rule, match_type, room_type,
                        match_tier, event_id,
                        getattr(player, "title_used", None),
                        getattr(player, "character_used", None),
                        getattr(player, "profile_used", None),
                        getattr(player, "voice_used", None),
                    ),
                )
                saved_count += 1
            except Error as error:
                logger.warning(
                    "跳过虹雀对局记录: user_id=%s error=%s",
                    getattr(player, "user_id", None), error,
                )
        from ..player_recent_records import update_player_recent_records
        update_player_recent_records(cursor, game_id, game_record)
        conn.commit()
        logger.info("虹雀牌谱已保存 game_id=%s players=%s", game_id, saved_count)
        try:
            from ..scene_stats import record_game_metrics
            record_game_metrics(db_manager, game_id, game_record, player_list, {
                "rule": rule, "sub_rule": sub_rule, "room_type": room_type,
                "match_tier": match_tier, "event_id": event_id, "match_type": match_type,
            })
        except Exception as error:
            logger.warning("写入 game_player_metrics 失败: %s", error)
        return game_id
    except Error as error:
        logger.error("存储虹雀牌谱失败: %s", error, exc_info=True)
        if conn:
            conn.rollback()
        return None
    finally:
        if conn:
            try:
                cursor.close()
            except Exception:
                pass
            db_manager._put_connection(conn)
