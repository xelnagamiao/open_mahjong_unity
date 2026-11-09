"""
四川麻将（血战到底）牌谱记录存储方法。

牌谱本体写入通用表 game_records / game_player_records（与其它规则共用），
因此无需新建专用表即可支持四川牌谱回放与对局记录查询。
规则专用统计表（段位/番种）暂未接入，自定义房不强制落库。
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


def store_sichuan_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str):
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               generate_game_id, allow_duplicate=True)
