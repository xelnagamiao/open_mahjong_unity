from dataclasses import dataclass, field
from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class NewChinesePlayer(ZhongyongPlayer):
    declared: bool = False
    after_kong: bool = False
    chain: int = 0
    longest_chain: int = 0
    level: int = 0
    passed_self: set = field(default_factory=set)
    riichi_flags: list = field(default_factory=list)

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.declared = self.after_kong = False
        self.chain = self.longest_chain = self.level = 0
        self.passed_self.clear()
        self.riichi_flags.clear()

    def pass_win(self, source, tile, waits):
        if source in ("black", "discard") and self.level < 2:
            self.discard_win_lockout_tiles.add(tile)
            self.level = 2 if waits <= self.discard_win_lockout_tiles else 1
        elif source == "self_draw" and self.level == 2:
            self.passed_self.add(tile)
            if waits <= self.passed_self:
                self.level = 3
        elif source == "bao_draw" and self.level >= 3:
            self.level = 4
