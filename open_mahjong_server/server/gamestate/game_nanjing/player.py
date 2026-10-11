from dataclasses import dataclass, field

from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class NanjingPlayer(ZhongyongPlayer):
    huapai_list: list[int] = field(default_factory=list)
    passed_pungs: set[int] = field(default_factory=set)
    passed_win_value: int | None = None
    meld_sources: list[int | None] = field(default_factory=list)
    flower_kongs: set[int] = field(default_factory=set)
    pung_claimed: bool = False
    pung_melds: set[int] = field(default_factory=set)
    after_kong: bool = False
    draw_kind: str = ""
    draw_count: int = 0
    last_draw_last_wall: bool = False
    won_tiles: list[int] = field(default_factory=list)

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.huapai_list = []
        self.passed_pungs = set()
        self.passed_win_value = None
        self.meld_sources = []
        self.pung_melds = set()
        self.flower_kongs = set()
        self.pung_claimed = self.after_kong = self.last_draw_last_wall = False
        self.draw_kind = ""
        self.draw_count = 0
        self.won_tiles = []
