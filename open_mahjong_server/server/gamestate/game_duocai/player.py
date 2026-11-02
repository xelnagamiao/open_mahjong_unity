from dataclasses import dataclass, field

from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class DuocaiPlayer(ZhongyongPlayer):
    melds: list = field(default_factory=list)
    discard_riichi_flags: list = field(default_factory=list)
    pao_liability: dict = field(default_factory=dict)
    riichi: int = 0
    riichi_pending: bool = False
    ippatsu: bool = False
    temporary_furiten: bool = False
    riichi_furiten: bool = False
    after_kong: bool = False
    rinshan_payer: int | None = None
    draw_kind: str = ''

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.melds, self.discard_riichi_flags, self.pao_liability = [], [], {}
        self.riichi = 0
        self.riichi_pending = self.ippatsu = self.temporary_furiten = self.riichi_furiten = self.after_kong = False
        self.rinshan_payer, self.draw_kind = None, ''
