"""虹雀玩家领域模型。"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional

from .record import codes_to_ids


class RecordCounter:
    def __init__(self) -> None:
        self.rank_result = 0


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
    record_counter: RecordCounter = field(default_factory=RecordCounter)

    @property
    def is_bot(self) -> bool:
        return self.user_id <= 10

    @property
    def hand_tiles(self) -> list[int]:
        return codes_to_ids(self.hand)

    # 共享对局接口（表情包、投票）使用 player_index / original_player_index / tag_list。
    # 虹雀座位不换座，index 即开局风位；掉线用 online 而不是 tag_list。
    @property
    def player_index(self) -> int:
        return self.index

    @property
    def original_player_index(self) -> int:
        return self.index

    @property
    def tag_list(self) -> list[str]:
        return [] if self.online else ["offline"]
