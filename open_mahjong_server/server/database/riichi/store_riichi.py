"""
立直麻将游戏记录存储：与国标/古典相同，写入 game_records + game_player_records。
"""
import json
import logging
import string
import secrets
from typing import Optional
from psycopg2 import Error

logger = logging.getLogger(__name__)

GAME_ID_ALPHABET = string.ascii_letters + string.digits
GAME_ID_LENGTH = 10


def _generate_game_id(length: int = GAME_ID_LENGTH) -> str:
    return ''.join(secrets.choice(GAME_ID_ALPHABET) for _ in range(length))


def store_riichi_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str) -> Optional[str]:
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               _generate_game_id)


def store_riichi_game_stats(db_manager, game_id: str, player_list: list, room_type: str, max_round: int, total_rounds: int) -> None:
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "riichi")


# 役种定义映射（与 FanTextDictionary.FanToDisplayRiichi / Inactive 一致，不含错和）
FAN_NAME_TO_FIELD = {
    "立直": "riichi",
    "门前清自摸和": "menzen_tsumo",
    "平和": "pinfu",
    "断幺九": "tanyao",
    "一杯口": "iipeikou",
    "役牌·白": "yakuhai_haku",
    "役牌·发": "yakuhai_hatsu",
    "役牌·中": "yakuhai_chun",
    "自风·东": "jikaze_ton",
    "自风·南": "jikaze_nan",
    "自风·西": "jikaze_sha",
    "自风·北": "jikaze_pe",
    "场风·东": "bakaze_ton",
    "场风·南": "bakaze_nan",
    "场风·西": "bakaze_sha",
    "场风·北": "bakaze_pe",
    "岭上开花": "rinshan",
    "枪杠": "chankan",
    "海底捞月": "haitei",
    "河底捞鱼": "houtei",
    "一发": "ippatsu",
    "宝牌": "dora",
    "赤宝牌": "akadora",
    "里宝牌": "uradora",
    "双立直": "daburi_riichi",
    "三色同刻": "sanshoku_doukou",
    "三杠子": "san_kantsu",
    "对对和": "toitoi",
    "三暗刻": "sanankou",
    "小三元": "shousangen",
    "混老头": "honroutou",
    "七对子": "chiitoitsu",
    "混全带幺九": "chanta",
    "一气通贯": "ittsu",
    "三色同顺": "sanshoku_doujun",
    "一气通贯（门清）": "ittsu_menzen",
    "一气通贯（食下）": "ittsu_shitachi",
    "三色同顺（门清）": "sanshoku_doujun_menzen",
    "三色同顺（食下）": "sanshoku_doujun_shitachi",
    "混全带幺九（门清）": "chanta_menzen",
    "混全带幺九（食下）": "chanta_shitachi",
    "纯全带幺九（门清）": "junchan_menzen",
    "纯全带幺九（食下）": "junchan_shitachi",
    "混一色（门清）": "honitsu_menzen",
    "混一色（食下）": "honitsu_shitachi",
    "清一色（门清）": "chinitsu_menzen",
    "清一色（食下）": "chinitsu_shitachi",
    "二杯口": "ryanpeikou",
    "纯全带幺九": "junchan",
    "混一色": "honitsu",
    "清一色": "chinitsu",
    "天和": "tenhou",
    "地和": "chiihou",
    "大三元": "daisangen",
    "四暗刻": "suuankou",
    "字一色": "tsuuiisou",
    "绿一色": "ryuuiisou",
    "清老头": "chinroutou",
    "国士无双": "kokushi",
    "小四喜": "shousuushii",
    "四杠子": "suukantsu",
    "九莲宝灯": "chuuren",
    "四暗刻单骑": "suuankou_tanki",
    "国士无双十三面": "kokushi_juusan",
    "纯正九莲宝灯": "chuuren_junsei",
    "大四喜": "daisuushii",
    "开立直": "open_riichi",
    "双倍开立直": "double_open_riichi",
    "人和": "renhou",
    "流局满贯": "nagashi_mangan",
    "大七星": "daichisei",
    "大车轮": "daisharin",
    "八连庄": "paarenchan",
    "包牌": "sashikomi",
}

STACKABLE_FANS = ["宝牌", "里宝牌", "赤宝牌"]
EXCLUDED_FAN_NAMES = frozenset({"错和"})
FAN_FIELDS = list(dict.fromkeys(FAN_NAME_TO_FIELD.values()))


def _accumulate_yaku_stats(fan_increment: dict, yaku_names: list) -> None:
    for fan_name in yaku_names:
        if not isinstance(fan_name, str) or fan_name in EXCLUDED_FAN_NAMES:
            continue
        if "*" in fan_name:
            base_name, _, count_str = fan_name.partition("*")
            base_name = base_name.strip()
            if base_name in STACKABLE_FANS and base_name in FAN_NAME_TO_FIELD:
                try:
                    count_val = int(count_str.strip())
                except ValueError:
                    logger.warning("无法解析立直役种数量: %s", fan_name)
                    continue
                field = FAN_NAME_TO_FIELD[base_name]
                fan_increment[field] += count_val
        elif fan_name in FAN_NAME_TO_FIELD:
            field = FAN_NAME_TO_FIELD[fan_name]
            fan_increment[field] += 1


def store_riichi_fan_stats(db_manager, game_id: str, player_list: list, room_type: str, max_round: int) -> None:
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "riichi")
