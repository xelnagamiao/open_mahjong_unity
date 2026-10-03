"""Per-hand rule state; match scores and stable identities use the shared model."""

from dataclasses import dataclass, field

from ..game_zhongyong.ZhongyongGameState import ZhongyongPlayer


@dataclass
class HongKongPlayer(ZhongyongPlayer):
    huapai_list: list[int] = field(default_factory=list)
    discard_riichi_flags: list[bool] = field(default_factory=list)
    declared_ready: bool = False
    ready_pending: bool = False
    ready_kind: str = ""
    ready_waits: set[int] = field(default_factory=set)
    immediate_ready: bool = False
    water: bool = False
    permanent_water: bool = False
    forbidden_discards: set[int] = field(default_factory=set)
    passed_claim_tiles: set[int] = field(default_factory=set)
    discarded_since_draw: set[int] = field(default_factory=set)
    replacement_kind: str = ""
    tail_draws: int = 0
    consecutive_kongs: int = 0
    discard_count: int = 0
    draw_count: int = 0
    liability_payer: int | None = None
    liability_kind: str = ""
    meld_suppliers: list[int | None] = field(default_factory=list)
    kong_liability_payer: int | None = None
    passed_win_fan: int = -1
    flower_choice_declined: set[str] = field(default_factory=set)
    initial_seven_pending: bool = False
    pending_ready_marker: bool = False

    def reset_for_round(self, round_time):
        super().reset_for_round(round_time)
        self.huapai_list = []
        self.discard_riichi_flags = []
        self.declared_ready = False
        self.ready_pending = False
        self.ready_kind = ""
        self.ready_waits = set()
        self.immediate_ready = False
        self.water = False
        self.permanent_water = False
        self.forbidden_discards = set()
        self.passed_claim_tiles = set()
        self.discarded_since_draw = set()
        self.replacement_kind = ""
        self.tail_draws = 0
        self.consecutive_kongs = 0
        self.discard_count = 0
        self.draw_count = 0
        self.liability_payer = None
        self.liability_kind = ""
        self.meld_suppliers = []
        self.kong_liability_payer = None
        self.passed_win_fan = -1
        self.flower_choice_declined = set()
        self.initial_seven_pending = False
        self.pending_ready_marker = False
