"""
古典麻将统计数据获取模块
"""
from typing import Dict, Any, List
from psycopg2.extras import RealDictCursor
import logging
from .store_classical import FAN_FIELDS

logger = logging.getLogger(__name__)


def get_classical_history_stats(db_manager, user_id: int) -> List[Dict[str, Any]]:
    from ..get_rule_stats import get_rule_history
    return get_rule_history(db_manager, user_id, "classical", None)


def get_classical_fan_stats_total(db_manager, user_id: int) -> Dict[str, int]:
    from ..get_rule_stats import get_rule_fans
    return get_rule_fans(db_manager, user_id, "classical", None)
