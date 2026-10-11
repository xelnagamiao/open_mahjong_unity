"""Read Jiandan history and fan statistics."""

from __future__ import annotations

import logging
from typing import Any, Dict, List

from psycopg2.extras import RealDictCursor

from .store_jiandan import FAN_FIELDS

logger = logging.getLogger(__name__)


def get_jiandan_history_stats(db_manager, user_id: int) -> List[Dict[str, Any]]:
    from ..get_rule_stats import get_rule_history
    return get_rule_history(db_manager, user_id, "nanque", None)


def get_jiandan_fan_stats_total(db_manager, user_id: int) -> Dict[str, int]:
    from ..get_rule_stats import get_rule_fans
    return get_rule_fans(db_manager, user_id, "nanque", None)
