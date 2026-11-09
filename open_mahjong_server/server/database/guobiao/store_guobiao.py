"""
国标麻将游戏记录和统计数据存储方法
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

# 番种定义映射（用于统计）
FAN_NAME_TO_FIELD = {
    "大四喜": "dasixi",
    "大三元": "dasanyuan",
    "绿一色": "lvyise",
    "九莲宝灯": "jiulianbaodeng",
    "四杠": "sigang",
    "连七对": "lianqidui",
    "十三幺": "shisanyao",
    "清幺九": "qingyaojiu",
    "小四喜": "xiaosixi",
    "小三元": "xiaosanyuan",
    "字一色": "ziyise",
    "四暗刻": "sianke",
    "一色双龙会": "yiseshuanglonghui",
    "一色四同顺": "yisesitongshun",
    "一色四节高": "yisesijiegao",
    "一色四步高": "yisesibugao",
    "三杠": "sangang",
    "混幺九": "hunyaojiu",
    "七对": "qiduizi",
    "七星不靠": "qixingbukao",
    "全双刻": "quanshuangke",
    "清一色": "qingyise",
    "一色三同顺": "yisesantongshun",
    "一色三节高": "yisesanjiegao",
    "全大": "quanda",
    "全中": "quanzhong",
    "全小": "quanxiao",
    "清龙": "qinglong",
    "三色双龙会": "sanseshuanglonghui",
    "一色三步高": "yisesanbugao",
    "全带五": "quandaiwu",
    "三同刻": "santongke",
    "三暗刻": "sananke",
    "全不靠": "quanbukao",
    "组合龙": "zuhelong",
    "大于五": "dayuwu",
    "小于五": "xiaoyuwu",
    "三风刻": "sanfengke",
    "花龙": "hualong",
    "推不倒": "tuibudao",
    "三色三同顺": "sansesantongshun",
    "三色三节高": "sansesanjiegao",
    "无番和": "wufanhe",
    "妙手回春": "miaoshouhuichun",
    "海底捞月": "haidilaoyue",
    "杠上开花": "gangshangkaihua",
    "抢杠和": "qiangganghe",
    "碰碰和": "pengpenghe",
    "混一色": "hunyise",
    "三色三步高": "sansesanbugao",
    "五门齐": "wumenqi",
    "全求人": "quanqiuren",
    "双暗杠": "shuangangang",
    "双箭刻": "shuangjianke",
    "全带幺": "quandaiyao",
    "不求人": "buqiuren",
    "双明杠": "shuangminggang",
    "和绝张": "hejuezhang",
    "箭刻": "jianke",
    "圈风刻": "quanfengke",
    "门风刻": "menfengke",
    "门前清": "menqianqing",
    "平和": "pinghe",
    "四归一": "siguiyi",
    "双同刻": "shuangtongke",
    "双暗刻": "shuanganke",
    "暗杠": "angang",
    "断幺": "duanyao",
    "一般高": "yibangao",
    "喜相逢": "xixiangfeng",
    "连六": "lianliu",
    "老少副": "laoshaofu",
    "幺九刻": "yaojiuke",
    "明杠": "minggang",
    "缺一门": "queyimen",
    "无字": "wuzi",
    "边张": "bianzhang",
    "嵌张": "qianzhang",
    "单钓将": "dandiaojiang",
    "自摸": "zimo",
    "花牌": "huapai",
    "明暗杠": "mingangang"
}

STACKABLE_FANS = ["花牌", "四归一", "双同刻", "一般高", "喜相逢", "幺九刻", "连六"]
FAN_FIELDS = list(dict.fromkeys(FAN_NAME_TO_FIELD.values()))

# 存储国标麻将游戏牌谱记录和玩家对局记录
def store_guobiao_game_record(db_manager, game_record: dict, player_list: list, room_type: str, match_type: str):
    """Publish replay, metrics and currency through the shared atomic path."""
    from ..record_persistence import persist_game_record
    return persist_game_record(db_manager, game_record, player_list, room_type, match_type,
                               generate_game_id, allow_duplicate=True)

# 存储国标麻将游戏基础统计数据到 guobiao_history_stats 表
def store_guobiao_game_stats(db_manager, game_id: str, player_list: list, room_type: str, match_type: str, total_rounds: int):
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "guobiao")

# 存储国标麻将游戏番种统计数据到 guobiao_fan_stats 表
def store_guobiao_fan_stats(db_manager, game_id: str, player_list: list, room_type: str, match_type: str):
    """兼容入口：按完整子规则从逐局指标覆盖累计，重试不会翻倍。"""
    from ..legacy_statistics import refresh_legacy_statistics
    refresh_legacy_statistics(db_manager, game_id, "guobiao")

