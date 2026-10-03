from dataclasses import dataclass, field

from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class GuizhouPlayer(ZhongyongPlayer):
    ready_kind: str = ""
    ready_pending: bool = False
    discard_count: int = 0
    draw_count: int = 0
    after_kong: bool = False
    initial_quads: set[int] = field(default_factory=set)
    passed_pungs: set[int] = field(default_factory=set)
    discarded_since_action: set[int] = field(default_factory=set)
    passed_points: int = -1
    permanent_ron_block: bool = False
    discard_riichi_flags: list[bool] = field(default_factory=list)
    pending_ready_marker: bool = False
    won_tiles: list[int] = field(default_factory=list)

    @property
    def declared_ready(self):
        return bool(self.ready_kind)

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.ready_kind = ""
        self.ready_pending = False
        self.discard_count = 0
        self.draw_count = 0
        self.after_kong = False
        self.initial_quads = set()
        self.passed_pungs = set()
        self.discarded_since_action = set()
        self.passed_points = -1
        self.permanent_ron_block = False
        self.discard_riichi_flags = []
        self.pending_ready_marker = False
        self.won_tiles = []
