from dataclasses import dataclass, field
from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class FilipinoPlayer(ZhongyongPlayer):
    huapai_list: list[int] = field(default_factory=list)
    draw_kind: str = ""
    last_drawn_tile: int | None = None
    won_tiles: list[int] = field(default_factory=list)
    flower_awards: set[int] = field(default_factory=set)

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.huapai_list = []
        self.draw_kind = ""
        self.last_drawn_tile = None
        self.won_tiles = []
        self.flower_awards = set()
