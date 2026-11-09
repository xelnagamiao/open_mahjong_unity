"""
青雀麻将游戏记录和统计数据存储方法
"""
import json
import logging
import string
import secrets
from psycopg2 import Error

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits  # a-z A-Z 0-9
GAME_ID_LENGTH = 10

def generate_game_id(length: int = GAME_ID_LENGTH) -> str:
    return ''.join(secrets.choice(GAME_ID_ALPHABET) for _ in range(length))

# 番种定义映射（用于统计），基于 FanTextDictionary.cs 的 FanToDisplayQingque
FAN_NAME_TO_FIELD = {
    "和牌": "hepai",
    "天和": "tianhe",
    "地和": "dihe",
    "岭上开花": "lingshangkaihua",
    "海底捞月": "haidilaoyue",
    "河底捞鱼": "hedilaoyue",
    "抢杠": "qianggang",
    "七对": "qidui",
    "门前清": "menqianqing",
    "四暗杠": "siangang",
    "三暗杠": "sanangang",
    "双暗杠": "shuangangang",
    "暗杠": "angang",
    "四杠": "sigang",
    "三杠": "sangang",
    "双杠": "shuanggang",
    "四暗刻": "sianke",
    "三暗刻": "sananke",
    "对对和": "duiduihe",
    "十二归": "shiergui",
    "八归": "bagui",
    "三叠对": "sandiedui",
    "二叠对": "erdiedui",
    "叠对": "diedui",
    "字一色": "ziyise",
    "大四喜": "dasixi",
    "小四喜": "xiaosixi",
    "四喜对": "sixidui",
    "风牌三刻": "fengpaisanke",
    "风牌七对": "fengpaiqidui",
    "风牌六对": "fengpailiudui",
    "风牌五对": "fengpaiwudui",
    "风牌四对": "fengpaisidui",
    "大三元": "dasanyuan",
    "小三元": "xiaosanyuan",
    "三元六对": "sanyuanliudui",
    "三元对": "sanyuandui",
    "番牌四刻": "fanpaisike",
    "番牌三刻": "fanpaisanke",
    "番牌二刻": "fanpaierke",
    "番牌刻": "fanpaike",
    "番牌七对": "fanpaiqidui",
    "番牌六对": "fanpailiudui",
    "番牌五对": "fanpaiwudui",
    "番牌四副": "fanpaisifu",
    "番牌三副": "fanpaisanfu",
    "番牌二副": "fanpaierfu",
    "番牌": "fanpai",
    "清幺九": "qingyaojiu",
    "混幺九": "hunyaojiu",
    "清带幺": "qingdaiyao",
    "混带幺": "hundaiyao",
    "九莲宝灯": "jiulianbaodeng",
    "清一色": "qingyise",
    "混一色": "hunyise",
    "五门齐": "wumenqi",
    "混一数": "hunyishu",
    "二数": "ershu",
    "二聚": "erju",
    "三聚": "sanju",
    "四聚": "siju",
    "连数": "lianshu",
    "间数": "jianshu",
    "镜数": "jingshu",
    "映数": "yingshu",
    "满庭芳": "mantingfang",
    "四同顺": "sitongshun",
    "三同顺": "santongshun",
    "二般高": "erbangao",
    "一般高": "yibangao",
    "四连刻": "silianke",
    "三连刻": "sanlianke",
    "四步高": "sibugao",
    "三步高": "sanbugao",
    "四连环": "silianhuan",
    "三连环": "sanlianhuan",
    "一气贯通": "yiqiguantong",
    "七连对": "qiliandui",
    "六连对": "liuliandui",
    "五连对": "wuliandui",
    "四连对": "siliandui",
    "三色同刻": "sansetongke",
    "三色同顺": "sansetongshun",
    "三色二对": "sanseedui",
    "三色同对": "sansetongdui",
    "三色连刻": "sanselianke",
    "三色贯通": "sanseguantong",
    "镜同": "jingtong",
    "镜同三对": "jingtongsandui",
    "镜同二对": "jingtongerdui",
    "双龙会": "shuanglonghui",
}

FAN_FIELDS = list(dict.fromkeys(FAN_NAME_TO_FIELD.values()))


def store_qingque_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str):
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               generate_game_id)


def store_qingque_game_stats(db_manager, game_id: str, player_list: list, room_type: str, game_round: int, total_rounds: int):
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "qingque")


def store_qingque_fan_stats(db_manager, game_id: str, player_list: list, room_type: str, game_round: int):
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "qingque")
