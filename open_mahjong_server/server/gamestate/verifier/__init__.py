"""国标对局 + 牌谱可视化验证器（本地 lab，不入库、不部署）。"""

from .session import VerifierError, VerifierSession

__all__ = ["VerifierError", "VerifierSession"]
