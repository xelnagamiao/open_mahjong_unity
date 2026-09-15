"""自由模式玩家。"""
from __future__ import annotations

from dataclasses import dataclass, field

VOTE_BLANK = "blank"
VOTE_END_ROUND = "end_round"
VOTE_RESTART_ROUND = "restart_round"
VOTE_END_MATCH = "end_match"
VALID_VOTES = {VOTE_BLANK, VOTE_END_ROUND, VOTE_RESTART_ROUND, VOTE_END_MATCH}


@dataclass
class FreePlayer:
    user_id: int
    username: str
    player_index: int
    original_player_index: int = 0
    remaining_time: int = 0
    hand_tiles: list[int] = field(default_factory=list)
    discard_tiles: list[int] = field(default_factory=list)
    discard_origin_tiles: list[int] = field(default_factory=list)
    combination_tiles: list[str] = field(default_factory=list)
    combination_mask: list = field(default_factory=list)
    huapai_list: list[int] = field(default_factory=list)
    score: int = 0
    tag_list: list[str] = field(default_factory=list)
    score_history: list[str] = field(default_factory=list)
    round_number_history: list[int] = field(default_factory=list)
    title_used: int = 1
    profile_used: int = 1
    character_used: int = 1
    voice_used: int = 1
    revealed: bool = False
    vote: str = VOTE_BLANK
    last_drawn_tile: int | None = None
    has_draw_slot: bool = False

    @property
    def is_bot(self) -> bool:
        return self.user_id <= 10

    def clear_table(self) -> None:
        self.hand_tiles.clear()
        self.discard_tiles.clear()
        self.discard_origin_tiles.clear()
        self.combination_tiles.clear()
        self.combination_mask.clear()
        self.huapai_list.clear()
        self.revealed = False
        self.vote = VOTE_BLANK
        self.last_drawn_tile = None
        self.has_draw_slot = False
        self.tag_list = [t for t in self.tag_list if t == "offline"]

    def take_hand_tile(self, tile_id: int) -> bool:
        try:
            self.hand_tiles.remove(tile_id)
        except ValueError:
            return False
        if self.last_drawn_tile == tile_id:
            self.last_drawn_tile = None
            self.has_draw_slot = False
        elif tile_id not in self.hand_tiles:
            self.has_draw_slot = False
        return True
