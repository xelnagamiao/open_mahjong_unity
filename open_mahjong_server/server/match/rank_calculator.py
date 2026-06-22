"""
段位体系与 PT 计算
"""
import logging
from typing import Tuple, Optional

logger = logging.getLogger(__name__)

# 段位定义：(段位名, 起始分数, 升段分数, 是否可掉段)
RANK_TABLE = [
    ("10级", 0, 20, False),
    ("9级",  0, 20, False),
    ("8级",  0, 20, False),
    ("7级",  0, 20, False),
    ("6级",  0, 40, False),
    ("5级",  0, 40, False),
    ("4级",  0, 60, False),
    ("3级",  0, 60, True),
    ("2级",  0, 80, True),
    ("1级",  1, 100, True),
    ("初段", 200, 400, True),
    ("二段", 400, 800, True),
    ("三段", 600, 1200, True),
    ("四段", 800, 1600, True),
    ("五段", 1000, 2000, True),
    ("六段", 1200, 2400, True),
    ("七段", 1400, 2800, True),
    ("八段", 1600, 3200, True),
    ("九段", 2000, 4000, True),
]

RANK_NAME_TO_INDEX = {r[0]: i for i, r in enumerate(RANK_TABLE)}

# 场次基础均得分（全庄）
TIER_BASE_SCORE = {
    "beginner": 30,
    "intermediate": 65,
    "advanced": 95,
    "mcrpl": 135,
}

# 局制系数：全庄=1, 半庄=0.7, 东风=0.49
GAME_TYPE_MULTIPLIER = {
    "quanzhuang": 1.0,
    "banzhuang": 0.7,
    "dongfeng": 0.49,
}

# 名次系数 [1名, 2名, 3名, 4名]
RANK_COEFFICIENTS = [0.8, 0.2, 0.3, 0.7]

# 各段位“均失pt”（用于三四名扣分，和场次无关）
RANK_AVG_LOSS_PT = {
    "10级": 0,
    "9级": 0,
    "8级": 0,
    "7级": 0,
    "6级": 0,
    "5级": 0,
    "4级": 0,
    "3级": 0,
    "2级": 15,
    "1级": 35,
    "初段": 45,
    "二段": 55,
    "三段": 65,
    "四段": 90,
    "五段": 100,
    "六段": 110,
    "七段": 150,
    "八段": 175,
    "九段": 200,
}

# 场次准入段位等级（索引值，越大段位越高）
TIER_MIN_RANK_INDEX = {
    "beginner": 0,       # 所有人
    "intermediate": 7,   # 3级（index 7）
    "advanced": 13,      # 四段（index 13）
    "mcrpl": 0,          # MCRPL 由 is_mcrpl_qualified 控制，不用段位限制
}


def get_rank_index(rank_name: str) -> int:
    return RANK_NAME_TO_INDEX.get(rank_name, 0)


def can_play_tier(rank_name: str, tier: str, is_mcrpl_qualified: bool = False) -> bool:
    """判断段位是否有资格进入指定场次"""
    if tier == "mcrpl":
        return is_mcrpl_qualified
    rank_idx = get_rank_index(rank_name)
    return rank_idx >= TIER_MIN_RANK_INDEX.get(tier, 0)


def calculate_pt(tier: str, game_type: str, rank_position: int, rank_name: str) -> float:
    """
    计算 PT 值
    Args:
        tier: 场次 (beginner/intermediate/advanced/mcrpl)
        game_type: 局制 (dongfeng/banzhuang/quanzhuang)
        rank_position: 名次 (1-4)
        rank_name: 当前段位名
    Returns:
        PT 值（浮点数，由调用方决定是否取整）
    """
    if rank_position in (1, 2):
        base = TIER_BASE_SCORE.get(tier, 30)
        multiplier = GAME_TYPE_MULTIPLIER.get(game_type, 1.0)
        coeff = RANK_COEFFICIENTS[rank_position - 1]
        return round(base * multiplier * coeff, 2)

    # 第三/第四名扣分：由段位均失pt决定，与场次/局制无关
    avg_loss = RANK_AVG_LOSS_PT.get(rank_name, 0)
    coeff = RANK_COEFFICIENTS[rank_position - 1]
    return round(-avg_loss * coeff, 2)


def apply_pt(rank_name: str, score: float, pt: float) -> Tuple[str, float]:
    """
    将 PT 应用到当前分数，处理升降段。

    规则：
    - 升段：分数 >= 升段分数时升一段，溢出分带到下一段起始分（可溢出，单局最多升一段）
    - 降段：分数 < 0 时降一段；若目标段有起始分则落到起始分，否则按上一段升段分往回扣
    - 单次结算最多升/降一段
    - 不可降段时分数封底为 0
    """
    rank_idx = get_rank_index(rank_name)
    _, _, promote_score, can_demote = RANK_TABLE[rank_idx]
    new_score = score + pt

    if new_score >= promote_score and rank_idx < len(RANK_TABLE) - 1:
        overflow = new_score - promote_score
        rank_idx += 1
        next_start, next_promote = RANK_TABLE[rank_idx][1], RANK_TABLE[rank_idx][2]
        new_score = next_start + overflow
        if new_score > next_promote:
            new_score = next_promote
    elif new_score < 0 and can_demote and rank_idx > 0:
        deficit = -new_score
        rank_idx -= 1
        prev_start, prev_promote = RANK_TABLE[rank_idx][1], RANK_TABLE[rank_idx][2]
        if prev_start > 0:
            new_score = prev_start
        else:
            new_score = prev_promote - deficit
            if new_score < 0:
                new_score = 0
    elif new_score < 0:
        new_score = 0

    return RANK_TABLE[rank_idx][0], round(new_score, 2)


def parse_queue_type(queue_type: str) -> Optional[Tuple[str, str]]:
    """
    解析队列类型字符串为 (tier, game_type)
    如 "beginner_dongfeng" -> ("beginner", "dongfeng")
    """
    parts = queue_type.rsplit("_", 1)
    if len(parts) != 2:
        return None
    tier, game_type = parts
    if tier not in TIER_BASE_SCORE or game_type not in GAME_TYPE_MULTIPLIER:
        return None
    return tier, game_type


def queue_type_to_game_round(queue_type: str) -> int:
    """队列类型转游戏局数"""
    parsed = parse_queue_type(queue_type)
    if not parsed:
        return 4
    _, game_type = parsed
    return {"dongfeng": 1, "banzhuang": 2, "quanzhuang": 4}.get(game_type, 4)


def queue_type_to_match_type(queue_type: str) -> str:
    """队列类型转统计 mode（如 1/4_rank）"""
    game_round = queue_type_to_game_round(queue_type)
    return f"{game_round}/4_rank"


def queue_type_to_display_name(queue_type: str) -> str:
    """队列类型转显示名"""
    parsed = parse_queue_type(queue_type)
    if not parsed:
        return queue_type
    tier, game_type = parsed
    tier_names = {"beginner": "初级场", "intermediate": "中级场", "advanced": "高级场", "mcrpl": "MCRPL"}
    game_type_names = {"dongfeng": "东风战", "banzhuang": "半庄战", "quanzhuang": "全庄战"}
    return f"{tier_names.get(tier, tier)} - {game_type_names.get(game_type, game_type)}"


def queue_type_to_room_config(queue_type: str) -> dict:
    """队列类型转房间配置"""
    parsed = parse_queue_type(queue_type)
    if not parsed:
        return {}
    tier, game_type = parsed
    game_round = {"dongfeng": 1, "banzhuang": 2, "quanzhuang": 4}.get(game_type, 4)
    # 初级场有提示无错和，中级场及以上无提示有错和
    tips = (tier == "beginner")
    open_cuohe = (tier != "beginner")
    # 中级场及以上启用战术鸣牌
    tactical_call = tier in ("intermediate", "advanced", "mcrpl")
    return {
        "game_round": game_round,
        "tips": tips,
        "open_cuohe": open_cuohe,
        "show_moqie_hint": False,
        "hepai_limit": 8,
        "round_timer": 20,
        "step_timer": 5,
        "sub_rule": "guobiao/standard",
        "tactical_call": tactical_call,
    }
