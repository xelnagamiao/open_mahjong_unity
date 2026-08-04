from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache


_LETTERS = "ABCDEFG"


@dataclass(frozen=True, order=True)
class HongqueTile:
    """One HQv3.1 tile: a 14-step cyclic colour and a number from 1 to 9."""

    colour: int
    number: int

    def __post_init__(self) -> None:
        if not 0 <= self.colour < 14:
            raise ValueError("colour must be in 0..13")
        if not 1 <= self.number <= 9:
            raise ValueError("number must be in 1..9")

    @classmethod
    @lru_cache(maxsize=256)
    def parse(cls, code: str) -> "HongqueTile":
        text = str(code or "").strip().upper()
        if len(text) != 3 or text[0] not in _LETTERS or text[1] not in "XY" or not text[2].isdigit():
            raise ValueError(f"invalid Hongque tile code: {code!r}")
        colour = _LETTERS.index(text[0]) * 2 + (1 if text[1] == "Y" else 0)
        return cls(colour=colour, number=int(text[2]))

    @property
    def code(self) -> str:
        return f"{_LETTERS[self.colour // 2]}{'Y' if self.colour % 2 else 'X'}{self.number}"

    @property
    def primary_colours(self) -> frozenset[int]:
        base = self.colour // 2
        if self.colour % 2 == 0:
            return frozenset((base,))
        return frozenset((base, (base + 1) % 7))


def full_deck() -> list[str]:
    return [HongqueTile(colour, number).code for colour in range(14) for number in range(1, 10)]
