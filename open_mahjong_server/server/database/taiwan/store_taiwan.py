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
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               _generate_game_id)
