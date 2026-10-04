"""Versioned Hong Kong rule calculations, independent of transport and Unity."""

from .models import HongKongRules, HandContext, ScoreResult
from .solver import decompositions, structural_waits
from .scoring import score_hand

__all__ = ["HongKongRules", "HandContext", "ScoreResult", "decompositions", "structural_waits", "score_hand"]
