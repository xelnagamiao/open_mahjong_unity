"""台湾麻将听牌检查。"""

from typing import List, Optional, Set, Union

from .rules import TaiwanRules
from .solver import structural_waits


class Taiwan_Tingpai_Check:
    def tingpai_check(
        self,
        hand_tile_list: List[int],
        combination_list: List[str],
        rules: Optional[Union[dict, TaiwanRules]] = None,
    ) -> Set[int]:
        resolved = rules if isinstance(rules, TaiwanRules) else TaiwanRules.from_dict(rules)
        return structural_waits(hand_tile_list, combination_list, resolved)
