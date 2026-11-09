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
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               generate_game_id)
