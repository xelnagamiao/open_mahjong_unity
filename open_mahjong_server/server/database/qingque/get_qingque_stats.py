"""
青雀麻将统计数据获取模块
用于从数据库获取青雀麻将相关的统计数据
"""
from typing import Dict, Any, List
from psycopg2.extras import RealDictCursor
import logging
from .store_qingque import FAN_FIELDS

logger = logging.getLogger(__name__)


def get_qingque_history_stats(db_manager, user_id: int) -> List[Dict[str, Any]]:
    from ..get_rule_stats import get_rule_history
    return get_rule_history(db_manager, user_id, "qingque", None)


def get_qingque_fan_stats_total(db_manager, user_id: int, ranked=None) -> Dict[str, int]:
    from ..get_rule_stats import get_rule_fans
    return get_rule_fans(db_manager, user_id, "qingque", "match" if ranked is True else "custom" if ranked is False else None)
