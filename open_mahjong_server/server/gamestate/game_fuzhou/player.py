from dataclasses import dataclass, field

from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class FuzhouPlayer(ZhongyongPlayer):
    huapai_list: list[int] = field(default_factory=list)
    won_tiles: list[int] = field(default_factory=list)
    draw_kind: str = ""
    draw_count: int = 0
    discarded_gold: bool = False
    golden_middle: bool = False
    locked_waits: set[int] = field(default_factory=set)
    youjin: int = 0
    youjin_tile: int | None = None
    after_ready_kong: bool = False

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.huapai_list, self.won_tiles = [], []
        self.draw_kind, self.draw_count = "", 0
        self.discarded_gold = self.golden_middle = self.after_ready_kong = False
        self.locked_waits = set()
        self.youjin, self.youjin_tile = 0, None
