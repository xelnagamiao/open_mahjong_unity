"""宜兴麻将：用户规则书的独立花数计算。"""

from .rules import RULE_VERSION, SUB_RULE, normalize_config, score_hand, waiting_tiles

__all__ = ["RULE_VERSION", "SUB_RULE", "normalize_config", "score_hand", "waiting_tiles"]
