from dataclasses import dataclass
from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class ShangraoPlayer(ZhongyongPlayer):
    after_kong: bool = False
    draw_kind: str = ""
    drawn_bao_count: int = 0
    four_bao_paid: bool = False
    flying_seven_stars: bool = False

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.after_kong = self.four_bao_paid = self.flying_seven_stars = False
        self.drawn_bao_count = 0
        self.draw_kind = ""
