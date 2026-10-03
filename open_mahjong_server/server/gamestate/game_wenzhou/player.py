from dataclasses import dataclass, field

from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class WenzhouPlayer(ZhongyongPlayer):
    meld_records: list[dict] = field(default_factory=list)
    passed_win_multiplier: int = 0
    won_tiles: list[int] = field(default_factory=list)
    draw_kind: str = ""

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.meld_records = []
        self.passed_win_multiplier = 0
        self.won_tiles = []
        self.draw_kind = ""
