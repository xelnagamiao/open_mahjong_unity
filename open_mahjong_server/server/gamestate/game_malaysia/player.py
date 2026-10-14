from dataclasses import dataclass, field
from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class MalaysiaPlayer(ZhongyongPlayer):
    huapai_list: list = field(default_factory=list)
    melds: list = field(default_factory=list)
    won_tiles: list = field(default_factory=list)
    paid_bonus_sets: set = field(default_factory=set)
    draw_kind: str | None = None
    last_drawn_tile: int | None = None
    last_wall: bool = False
    consecutive_kongs: int = 0
    natural_draws: int = 0
    liability: int | None = None

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.huapai_list, self.melds, self.won_tiles = [], [], []
        self.paid_bonus_sets = set()
        self.draw_kind = self.last_drawn_tile = None
        self.last_wall = False
        self.consecutive_kongs = self.natural_draws = 0
        self.liability = None
