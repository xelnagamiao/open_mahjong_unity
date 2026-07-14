"""Store Jiandan replays in the existing shared replay tables."""

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
        rule = title.get("rule", "jiandan")
        sub_rule = title.get("sub_rule", "jiandan/standard")
        match_tier = title.get("match_tier")
        event_id = title.get("event_id")

        for player in player_list:
            try:
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
            except Error as exc:
                logger.warning(
                    "Jiandan replay player row skipped: user_id=%s error=%s",
                    player.user_id,
                    exc,
                )

        conn.commit()

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
            logger.warning("Jiandan game metrics persistence failed: %s", exc)

        return game_id
    except Error as exc:
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
    """Accumulate Jiandan per-mode game statistics for registered users."""
    if any(getattr(player, "user_id", 0) <= 10 for player in player_list):
        logger.info("Jiandan game contains a bot; game statistics skipped")
        return

    conn = None
    cursor = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        mode = f"{game_round}/4"
        stats_columns = (
            "total_games",
            "total_rounds",
            "win_count",
            "self_draw_count",
            "deal_in_count",
            "total_fan_score",
            "total_win_turn",
            "total_fangchong_score",
            "first_place_count",
            "second_place_count",
            "third_place_count",
            "fourth_place_count",
            "fulu_round_count",
        )

        for player in player_list:
            if player.user_id <= 10000000:
                continue
            cursor.execute("SELECT 1 FROM users WHERE user_id = %s", (player.user_id,))
            if cursor.fetchone() is None:
                continue

            counter = player.record_counter
            increments = {
                "total_games": 1,
                "total_rounds": total_rounds,
                "win_count": counter.zimo_times + counter.dianhe_times,
                "self_draw_count": counter.zimo_times,
                "deal_in_count": counter.fangchong_times,
                "total_fan_score": counter.win_score,
                "total_win_turn": counter.win_turn,
                "total_fangchong_score": counter.fangchong_score,
                "first_place_count": int(counter.rank_result == 1),
                "second_place_count": int(counter.rank_result == 2),
                "third_place_count": int(counter.rank_result == 3),
                "fourth_place_count": int(counter.rank_result == 4),
                "fulu_round_count": counter.fulu_times,
            }
            insert_columns = ("user_id", "rule", "mode") + stats_columns
            insert_values = [player.user_id, "jiandan", mode] + [
                increments[column] for column in stats_columns
            ]
            update_clauses = ", ".join(
                f"{column} = jiandan_history_stats.{column} + EXCLUDED.{column}"
                for column in stats_columns
            )
            cursor.execute(
                f"""
                INSERT INTO jiandan_history_stats ({', '.join(insert_columns)})
                VALUES ({', '.join(['%s'] * len(insert_columns))})
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    {update_clauses},
                    updated_at = CURRENT_TIMESTAMP
                """,
                insert_values,
            )

        conn.commit()
        logger.info("Jiandan game statistics saved: game_id=%s", game_id)
    except Error as exc:
        logger.error("Jiandan game statistics persistence failed: %s", exc, exc_info=True)
        if conn is not None:
            conn.rollback()
    finally:
        if cursor is not None:
            cursor.close()
        if conn is not None:
            db_manager._put_connection(conn)


def store_jiandan_fan_stats(
    db_manager,
    game_id: str,
    player_list: list,
    room_type: str,
    game_round: int,
) -> None:
    """Accumulate Jiandan fan achievements using stable fan IDs as columns."""
    if any(getattr(player, "user_id", 0) <= 10 for player in player_list):
        logger.info("Jiandan game contains a bot; fan statistics skipped")
        return

    conn = None
    cursor = None
    try:
        conn = db_manager._get_connection()
        cursor = conn.cursor()
        mode = f"{game_round}/4"

        for player in player_list:
            if player.user_id <= 10000000:
                continue
            cursor.execute("SELECT 1 FROM users WHERE user_id = %s", (player.user_id,))
            if cursor.fetchone() is None:
                continue

            fan_increment = {fan_id: 0 for fan_id in FAN_FIELDS}
            for fan_entry in getattr(player.record_counter, "recorded_fans", []):
                fan_values = fan_entry if isinstance(fan_entry, list) else [fan_entry]
                for fan_value in fan_values:
                    if not isinstance(fan_value, str):
                        continue
                    fan_id = fan_value if fan_value in fan_increment else FAN_NAME_TO_FIELD.get(fan_value)
                    if fan_id is not None:
                        fan_increment[fan_id] += 1

            insert_columns = ("user_id", "rule", "mode") + FAN_FIELDS
            insert_values = [player.user_id, "jiandan", mode] + [
                fan_increment[fan_id] for fan_id in FAN_FIELDS
            ]
            update_clauses = ", ".join(
                f"{fan_id} = jiandan_fan_stats.{fan_id} + EXCLUDED.{fan_id}"
                for fan_id in FAN_FIELDS
            )
            cursor.execute(
                f"""
                INSERT INTO jiandan_fan_stats ({', '.join(insert_columns)})
                VALUES ({', '.join(['%s'] * len(insert_columns))})
                ON CONFLICT (user_id, rule, mode) DO UPDATE SET
                    {update_clauses},
                    updated_at = CURRENT_TIMESTAMP
                """,
                insert_values,
            )

        conn.commit()
        logger.info("Jiandan fan statistics saved: game_id=%s", game_id)
    except Error as exc:
        logger.error("Jiandan fan statistics persistence failed: %s", exc, exc_info=True)
        if conn is not None:
            conn.rollback()
    finally:
        if cursor is not None:
            cursor.close()
        if conn is not None:
            db_manager._put_connection(conn)
