"""好友 / 关注 / 实时观战 模块"""
from .friend_router import handle_friend_message
from .friend_manager import FriendManager, RealtimeSpectator

__all__ = ["handle_friend_message", "FriendManager", "RealtimeSpectator"]
