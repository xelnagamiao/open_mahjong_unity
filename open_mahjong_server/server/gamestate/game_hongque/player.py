"""虹雀玩家领域模型。"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional


@dataclass
class HongquePlayer:
    user_id: int
    username: str
    index: int
    hand: list[str] = field(default_factory=list)
    discards: list[str] = field(default_factory=list)
    melds: list[dict] = field(default_factory=list)
    score: int = 0
    supplements: int = 0
    online: bool = True
    title_used: int = 1
    profile_used: int = 1
    character_used: int = 1
    voice_used: int = 1
    drawn_tile: Optional[str] = None
    last_draw_was_supplement: bool = False
    remaining_time: int = 20
    score_history: list[str] = field(default_factory=list)
    round_number_history: list[int] = field(default_factory=list)

    @property
    def is_bot(self) -> bool:
        return self.user_id <= 10

