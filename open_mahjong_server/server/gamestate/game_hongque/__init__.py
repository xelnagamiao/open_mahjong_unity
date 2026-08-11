"""Hongque 2 prototype game state.

The prototype is deliberately memory-only: it does not create statistics or
game records.  Public tile codes match the Unity HQv3.1 resource names.
"""

from .HongqueGameState import HongqueGameState
from .player import HongquePlayer

__all__ = ["HongqueGameState", "HongquePlayer"]
