from dataclasses import dataclass
from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class BeijingPlayer(ZhongyongPlayer):
    tian_ting: bool = False
    discarded_hun: bool = False
    after_kong: bool = False
    kong_payer: int | None = None
    fourth_call_payer: int | None = None
    open_calls: int = 0
    draw_kind: str = ""

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.tian_ting = self.discarded_hun = self.after_kong = False
        self.kong_payer = self.fourth_call_payer = None
        self.open_calls, self.draw_kind = 0, ""
