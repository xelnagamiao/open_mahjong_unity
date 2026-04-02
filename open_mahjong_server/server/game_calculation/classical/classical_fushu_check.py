from typing import List

try:
    from .classical_hepai_check import Classical_Hepai_Check
except ImportError:
    from classical_hepai_check import Classical_Hepai_Check  # type: ignore


class Classical_Fushu_Check:
    """
    古典麻将基础副数计算器（独立文件）：
    - 仅返回可和牌型中的最大基础副数
    """

    def __init__(self, debug: bool = False):
        self._checker = Classical_Hepai_Check(debug=debug)

    def fushucheck(self, hand_list: List[int], tiles_combination: List[str], way_to_hepai: List[str], get_tile: int) -> int:
        return self._checker.fushucheck(hand_list, tiles_combination, way_to_hepai, get_tile)

