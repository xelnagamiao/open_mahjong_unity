from dataclasses import dataclass, field

from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class YixingPlayer(ZhongyongPlayer):
    huapai_list: list[int] = field(default_factory=list)
    passed_pungs: set[int] = field(default_factory=set)
    passed_wins: set[int] = field(default_factory=set)
    forbidden_discards: set[int] = field(default_factory=set)
    won_tiles: list[int] = field(default_factory=list)
    after_kong: bool = False
    last_draw_last_wall: bool = False
    draw_kind: str = ""
    draw_count: int = 0

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.huapai_list = []
        self.passed_pungs = set()
        self.passed_wins = set()
        self.forbidden_discards = set()
        self.won_tiles = []
        self.after_kong = self.last_draw_last_wall = False
        self.draw_kind = ""
        self.draw_count = 0
