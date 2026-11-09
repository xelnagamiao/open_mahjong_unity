"""
长沙麻将牌谱记录与基础统计存储。

牌谱本体写入通用表 game_records / game_player_records；
基础统计写入 changsha_history_stats（与立直等同结构）。
"""
import json
import logging
import string
import secrets
from psycopg2 import Error

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits
GAME_ID_LENGTH = 10


def generate_game_id(length: int = GAME_ID_LENGTH) -> str:
    return ''.join(secrets.choice(GAME_ID_ALPHABET) for _ in range(length))


def store_changsha_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str):
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               generate_game_id)


def store_changsha_game_stats(db_manager, game_id: str, player_list: list, room_type: str, max_round: int, total_rounds: int) -> None:
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "changsha")
