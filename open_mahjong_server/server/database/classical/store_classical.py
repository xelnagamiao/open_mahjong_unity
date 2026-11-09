"""
古典麻将游戏记录和统计数据存储方法
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

FAN_NAME_TO_FIELD = {
    "自摸": "zimo",
    "混一色": "hunyise",
    "小三元": "xiaosanyuan",
    "清一色": "qingyise",
    "字一色": "ziyise",
    "鸾凤和鸣": "luanfengheming",
    "岭上开花": "lingshangkaihua",
    "海底捞月": "haidilaoyue",
    "金鸡夺食": "jinjidoushi",
    "大三元": "dasanyuan",
    "大四喜": "dasixi",
    "小四喜": "xiaosixi",
    "天和": "tianhe",
    "地和": "dihe",
    "九莲宝灯": "jiulianbaodeng",
    "国士无双": "guoshiwushuang",
}

FAN_FIELDS = list(dict.fromkeys(FAN_NAME_TO_FIELD.values()))


def store_classical_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str):
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               generate_game_id)


def store_classical_game_stats(db_manager, game_id: str, player_list: list, room_type: str, game_round: int, total_rounds: int):
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "classical")


def store_classical_fan_stats(db_manager, game_id: str, player_list: list, room_type: str, game_round: int):
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "classical")
