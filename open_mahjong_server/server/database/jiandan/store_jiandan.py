"""Store Zhongyong/Nanque replays; jiandan_* table names are compatibility storage."""

from __future__ import annotations

import json
import logging
import secrets
import string

from psycopg2 import Error

from ...game_calculation.jiandan.fan_definitions import FANS

logger = logging.getLogger(__name__)

_GAME_ID_ALPHABET = string.ascii_letters + string.digits
_GAME_ID_LENGTH = 10
FAN_FIELDS = tuple(FANS.keys())
FAN_NAME_TO_FIELD = {definition.name: fan_id for fan_id, definition in FANS.items()}


def _generate_game_id(length: int = _GAME_ID_LENGTH) -> str:
    return "".join(secrets.choice(_GAME_ID_ALPHABET) for _ in range(length))


def create_jiandan_stats_tables(cursor) -> None:
    """Create Jiandan statistics tables from the stable server fan IDs."""
    cursor.execute(
        """
        CREATE TABLE IF NOT EXISTS jiandan_history_stats (
            user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
            rule VARCHAR(10) NOT NULL,
            mode VARCHAR(20) NOT NULL,
            total_games INT NOT NULL DEFAULT 0,
            total_rounds INT NOT NULL DEFAULT 0,
            win_count INT NOT NULL DEFAULT 0,
            self_draw_count INT NOT NULL DEFAULT 0,
            deal_in_count INT NOT NULL DEFAULT 0,
            total_fan_score INT NOT NULL DEFAULT 0,
            total_win_turn INT NOT NULL DEFAULT 0,
            total_fangchong_score INT NOT NULL DEFAULT 0,
            first_place_count INT NOT NULL DEFAULT 0,
            second_place_count INT NOT NULL DEFAULT 0,
            third_place_count INT NOT NULL DEFAULT 0,
            fourth_place_count INT NOT NULL DEFAULT 0,
            fulu_round_count INT NOT NULL DEFAULT 0,
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (user_id, rule, mode)
        );
        """
    )
    fan_columns = ",\n".join(
        f"            {fan_id} INT NOT NULL DEFAULT 0" for fan_id in FAN_FIELDS
    )
    cursor.execute(
        f"""
        CREATE TABLE IF NOT EXISTS jiandan_fan_stats (
            user_id BIGINT NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
            rule VARCHAR(10) NOT NULL,
            mode VARCHAR(20) NOT NULL,
{fan_columns},
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (user_id, rule, mode)
        );
        """
    )


def store_jiandan_game_record(
    db_manager,
    game_record: dict,
    player_list: list,
    room_type: str,
    match_type: str,
):
    """Persist one Jiandan game without changing shared database helpers."""
    if any(getattr(player, "user_id", 0) <= 10 for player in player_list):
        logger.info("Jiandan game contains a bot; replay persistence skipped")
        return None

    conn = None
    cursor = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        from ..rule_identity import canonical_game_record
        game_record = canonical_game_record(game_record)
        game_record_json = json.dumps(game_record, ensure_ascii=False, default=str)

        game_id = None
        for _ in range(5):
            candidate = _generate_game_id()
            try:
                cursor.execute(
                    "INSERT INTO game_records (game_id, record) VALUES (%s, %s)",
                    (candidate, game_record_json),
                )
                game_id = candidate
                break
            except Error:
                conn.rollback()
                logger.warning("Jiandan replay id collision: %s; retrying", candidate)

        if game_id is None:
            logger.error("Jiandan replay id generation exhausted retries")
            return None

        title = game_record.get("game_title") or {}
        rule = title.get("rule", "zhongyong")
        sub_rule = title.get("sub_rule", "zhongyong/nanque")
        match_tier = title.get("match_tier")
        event_id = title.get("event_id")
        from ..scene_stats import normalize_scene_fields
        room_type, match_tier, event_id = normalize_scene_fields(room_type, match_tier, event_id)

        for player in player_list:
            try:
                cursor.execute(
                    """
                    INSERT INTO game_player_records (
                        game_id, user_id, username, score, rank,
                        original_player_index, rule, sub_rule, match_type,
                        room_type, match_tier, event_id, title_used,
                        character_used, profile_used, voice_used, avatar_frame_used
                    ) VALUES (
                        %s, %s, %s, %s, %s, %s, %s, %s,
                        %s, %s, %s, %s, %s, %s, %s, %s, %s)
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
                        getattr(player, "avatar_frame_used", 0),
                    ),
                )
            except Exception as exc:
                logger.warning(
                    "Jiandan replay player row skipped: user_id=%s error=%s",
                    player.user_id,
                    exc,
                )

        from ..player_recent_records import update_player_recent_records
        update_player_recent_records(cursor, game_id, game_record)
        # 牌谱、参与者和统计同事务发布，成功保存不依赖后续回填。
        from ..scene_stats import record_game_metrics
        record_game_metrics(db_manager, game_id, game_record, player_list, {
            "rule": rule, "sub_rule": sub_rule, "room_type": room_type,
            "match_tier": match_tier, "event_id": event_id, "match_type": match_type,
        }, cursor=cursor)
        conn.commit()
        logger.info("牌谱及子规则统计已保存，game_id: %s", game_id)

        return game_id
    except Exception as exc:
        logger.error("Jiandan replay persistence failed: %s", exc, exc_info=True)
        if conn is not None:
            conn.rollback()
        return None
    finally:
        if cursor is not None:
            cursor.close()
        if conn is not None:
            db_manager._put_connection(conn)


def store_jiandan_game_stats(
    db_manager,
    game_id: str,
    player_list: list,
    room_type: str,
    game_round: int,
    total_rounds: int,
) -> None:
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "jiandan")


def store_jiandan_fan_stats(
    db_manager,
    game_id: str,
    player_list: list,
    room_type: str,
    game_round: int,
) -> None:
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "jiandan")
