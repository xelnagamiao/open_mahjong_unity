from dataclasses import dataclass, field

from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class HangzhouPlayer(ZhongyongPlayer):
    after_kong: bool = False
    draw_kind: str = ""
    pre_draw_tiles: list[int] = field(default_factory=list)
    cai_piao_count: int = 0
    drawn_after_cai: bool = False
    chi_count: int = 0
    had_meld_action: bool = False

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.after_kong = self.drawn_after_cai = self.had_meld_action = False
        self.draw_kind = ""
        self.pre_draw_tiles = []
        self.cai_piao_count = self.chi_count = 0
