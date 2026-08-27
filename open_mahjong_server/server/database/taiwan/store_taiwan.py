"""
台湾麻将牌谱记录存储方法
"""
import json
import logging
import secrets
import string
from psycopg2 import Error

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits
GAME_ID_LENGTH = 10


def _generate_game_id() -> str:
    return "".join(secrets.choice(GAME_ID_ALPHABET) for _ in range(GAME_ID_LENGTH))


def store_taiwan_game_record(
    db_manager,
    game_record: dict,
    player_list: list,
    room_type: str,
    match_type: str,
):
    """保存牌谱和四名玩家的对局索引"""

    if any(getattr(player, "user_id", 0) <= 10 for player in player_list):
        logger.info("台湾麻将对局包含机器人，跳过牌谱与对局记录保存")
        return None

    conn = None
    cursor = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        record_json = json.dumps(game_record, ensure_ascii=False, default=str)

        game_id = None
        for _ in range(5):
            candidate = _generate_game_id()
            try:
                cursor.execute(
                    "INSERT INTO game_records (game_id, record) VALUES (%s, %s)",
                    (candidate, record_json),
                )
                game_id = candidate
                break
            except Error:
                conn.rollback()
                logger.warning("台湾麻将 game_id 碰撞: %s，重试", candidate)
        if game_id is None:
            logger.error("台湾麻将多次生成 game_id 均碰撞，存储失败")
            return None

        title = game_record.get("game_title") or {}
        rule = title.get("rule", "taiwan")
        sub_rule = title.get("sub_rule", "taiwan/standard")
        match_tier = title.get("match_tier")
        event_id = title.get("event_id")
        from ..scene_stats import normalize_scene_fields
        room_type, match_tier, event_id = normalize_scene_fields(room_type, match_tier, event_id)

        for player in player_list:
            cursor.execute(
                """
                INSERT INTO game_player_records (
                    game_id, user_id, username, score, rank,
                    original_player_index, rule, sub_rule, match_type,
                    room_type, match_tier, event_id, title_used,
                    character_used, profile_used, voice_used
                ) VALUES (
                    %s, %s, %s, %s, %s, %s, %s, %s,
                    %s, %s, %s, %s, %s, %s, %s, %s
                )
                """,
                (
                    game_id,
                    player.user_id,
                    player.username,
                    player.score,
                    player.record_counter.rank_result,
                    player.original_player_index,
                    rule,
                    sub_rule,
                    match_type,
                    room_type,
                    match_tier,
                    event_id,
                    getattr(player, "title_used", None),
                    getattr(player, "character_used", None),
                    getattr(player, "profile_used", None),
                    getattr(player, "voice_used", None),
                ),
            )

        conn.commit()
        logger.info("台湾麻将游戏记录已保存，game_id: %s", game_id)
        try:
            from ..scene_stats import record_game_metrics

            record_game_metrics(
                db_manager,
                game_id,
                game_record,
                player_list,
                {
                    "rule": rule,
                    "sub_rule": sub_rule,
                    "room_type": room_type,
                    "match_tier": match_tier,
                    "event_id": event_id,
                    "match_type": match_type,
                },
            )
        except Exception as exc:
            logger.warning("写入台湾麻将 game_player_metrics 失败: %s", exc)
        return game_id
    except Error as exc:
        logger.error("存储台湾麻将游戏记录失败: %s", exc, exc_info=True)
        if conn:
            conn.rollback()
        return None
    finally:
        if cursor:
            cursor.close()
        if conn:
            db_manager._put_connection(conn)
