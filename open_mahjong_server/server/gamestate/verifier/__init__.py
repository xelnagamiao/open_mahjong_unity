"""国标对局 + 牌谱可视化验证器（本地 lab，不入库、不部署）。"""

__all__ = ["VerifierError", "VerifierSession", "TracePlayback"]


def __getattr__(name):
    if name in ("VerifierError", "VerifierSession", "TracePlayback"):
        from .session import TracePlayback, VerifierError, VerifierSession

        return {
            "VerifierError": VerifierError,
            "VerifierSession": VerifierSession,
            "TracePlayback": TracePlayback,
        }[name]
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")

