from dataclasses import dataclass, field
from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class VietnamesePlayer(ZhongyongPlayer):
    huapai_list: list[int] = field(default_factory=list)
    replacements: list[dict] = field(default_factory=list)
    draw_kind: str = ''
    last_drawn_tile: int | None = None
    won_tiles: list[int] = field(default_factory=list)
    missed_win: set[int] = field(default_factory=set)
    missed_pung: set[int] = field(default_factory=set)
    nfnl_eligible: bool = True
    discarded_bonus: list[int] = field(default_factory=list)
    draw_count: int = 0
    liability_feeders: dict = field(default_factory=dict)
    lost_safe_by_call: set = field(default_factory=set)

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.huapai_list=[];self.replacements=[];self.draw_kind='';self.last_drawn_tile=None
        self.won_tiles=[];self.missed_win=set();self.missed_pung=set()
        self.nfnl_eligible=True;self.discarded_bonus=[];self.draw_count=0
        self.liability_feeders={};self.lost_safe_by_call=set()
