"""Policy-driven universal joker support; see README.md for the stable API."""

from .models import (HONORS, NUMBERS, ORPHANS, TILES, ExternalMeld, JokerPolicy,
                     JokerUse, ResolvedGroup, WinningShape)
from .solver import (cache_info, can_form_melds, can_form_pairs, can_win,
                     clear_caches, iter_winning_shapes, structural_waits)

API_VERSION = "1.0"

__all__ = ["API_VERSION", "JokerPolicy", "ExternalMeld", "JokerUse", "ResolvedGroup",
           "WinningShape", "can_win", "structural_waits", "iter_winning_shapes",
           "can_form_melds", "can_form_pairs", "clear_caches", "cache_info",
           "TILES", "NUMBERS", "HONORS", "ORPHANS"]
