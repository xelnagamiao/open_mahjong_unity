from dataclasses import dataclass, field
from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class NanchangPlayer(ZhongyongPlayer):
    huapai_list: list = field(default_factory=list)
    won_tiles: list = field(default_factory=list)
    after_kong: bool = False
    draw_kind: str = ""
    declared_jingdiao: bool = False
    missed_fan: float = -1

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.huapai_list, self.won_tiles = [], []
        self.after_kong = self.declared_jingdiao = False
        self.draw_kind, self.missed_fan = "", -1
