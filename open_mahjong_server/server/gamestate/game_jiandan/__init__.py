"""Jiandan game-state package."""

def __getattr__(name):
    if name in {"JiandanGameState", "JiandanPlayer"}:
        from .JiandanGameState import JiandanGameState, JiandanPlayer
        return {"JiandanGameState": JiandanGameState, "JiandanPlayer": JiandanPlayer}[name]
    if name == "JiandanActionPolicy":
        from .action_check import JiandanActionPolicy
        return JiandanActionPolicy
    if name == "JiandanSettlementPolicy":
        from .settlement import JiandanSettlementPolicy
        return JiandanSettlementPolicy
    raise AttributeError(name)
