"""Legacy import for existing rooms; new Nanque games use the Zhongyong family."""
from ..game_zhongyong.NanqueGameState import NanqueGameState
from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer as JiandanPlayer

class JiandanGameState(NanqueGameState):
    pass
