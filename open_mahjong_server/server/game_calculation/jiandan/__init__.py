"""Jiandan scoring package.

This package is intentionally isolated from the live game state while the rule
calculator is being built and tested.
"""

from .scoring import HandContext, score_hand
from .tingpai import tingpai_check

__all__ = ["HandContext", "score_hand", "tingpai_check"]
