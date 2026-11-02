"""Hongque game state, managed by the shared game/session lifecycle.

Match settlement, persisted records and spectating use the shared services.
Public tile codes match the Unity HQv3.1 resource names.
"""

from .HongqueGameState import HongqueGameState
from .player import HongquePlayer

__all__ = ["HongqueGameState", "HongquePlayer"]
