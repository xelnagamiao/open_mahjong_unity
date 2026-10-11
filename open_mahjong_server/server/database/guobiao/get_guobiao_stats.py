"""
国标麻将统计数据获取模块
用于从数据库获取国标麻将相关的统计数据
"""
from typing import Dict, Any, List, Optional
from psycopg2.extras import RealDictCursor
import logging

logger = logging.getLogger(__name__)


def get_guobiao_history_stats(db_manager, user_id: int) -> List[Dict[str, Any]]:
    from ..get_rule_stats import get_rule_history
    return get_rule_history(db_manager, user_id, "guobiao", None)


def get_guobiao_fan_stats_total(db_manager, user_id: int) -> Dict[str, int]:
    from ..get_rule_stats import get_rule_fans
    return get_rule_fans(db_manager, user_id, "guobiao", None)


def get_guobiao_fan_stats_split(db_manager, user_id: int):
    """
    获取指定用户国标番种统计，按 普通对局 / 天梯对局(_rank) 分开汇总。

    Returns:
        (normal_stats, ranked_stats) 两个 {fan_field: count} 字典。
    """
    normal = _get_guobiao_fan_stats_summed(db_manager, user_id, ranked=False)
    ranked = _get_guobiao_fan_stats_summed(db_manager, user_id, ranked=True)
    return normal, ranked


def _get_guobiao_fan_stats_summed(db_manager, user_id: int, ranked=None) -> Dict[str, int]:
    from ..get_rule_stats import get_rule_fans
    return get_rule_fans(db_manager, user_id, "guobiao", "match" if ranked is True else "custom" if ranked is False else None)

