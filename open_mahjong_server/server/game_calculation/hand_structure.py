"""麻将手牌结构的通用定义。"""

from dataclasses import dataclass


@dataclass(frozen=True)
class HandStructure:
    """用固定面子数描述一类麻将的标准手牌结构。"""

    meld_count: int

    def __post_init__(self) -> None:
        if self.meld_count <= 0:
            raise ValueError("面子数必须为正数")

    @property
    def base_hand_tile_count(self) -> int:
        """未取得和牌张时的手牌张数。"""

        return self.meld_count * 3 + 1

    @property
    def complete_hand_tile_count(self) -> int:
        """取得和牌张后的手牌张数。"""

        return self.base_hand_tile_count + 1

    def concealed_meld_count(self, external_meld_count: int) -> int:
        """返回扣除副露后仍需由暗手组成的面子数。"""

        return self.meld_count - external_meld_count

    def concealed_tile_count(self, external_meld_count: int, *, complete: bool) -> int:
        """返回给定副露数下，暗手应有的标准张数。"""

        concealed_melds = self.concealed_meld_count(external_meld_count)
        return concealed_melds * 3 + (2 if complete else 1)


THIRTEEN_TILE_MAHJONG = HandStructure(meld_count=4)
SIXTEEN_TILE_MAHJONG = HandStructure(meld_count=5)
