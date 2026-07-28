"""台湾麻将和牌检查与计番。"""

from typing import Any, Dict, List, Optional, Tuple

from .rules import HandContext, TaiwanRules
from .scoring import TaiwanScorer


class Taiwan_Hepai_Check:
    def __init__(self) -> None:
        self._scorer = TaiwanScorer()

    def hepai_detail(
        self,
        hand_list: List[int],
        tiles_combination: List[str],
        way_to_hepai: List[str],
        get_tile: int,
        context: Optional[Dict[str, Any]] = None,
    ) -> dict:
        raw = dict(context or {})
        rules = TaiwanRules.from_dict(raw.pop("rules", None))
        win_source = raw.pop("win_source", None) or self._win_source(way_to_hepai)
        flowers = raw.pop("flowers", None)
        if flowers is None:
            flowers = []
        hand_context = HandContext(
            hand_tiles=list(hand_list),
            meld_codes=list(tiles_combination),
            winning_tile=get_tile,
            win_source=win_source,
            flowers=list(flowers),
            out_with_replacement_tile=bool(raw.pop("out_with_replacement_tile", False) or "杠上开花" in way_to_hepai),
            last_tile=bool(raw.pop("last_tile", False) or "海底捞月" in way_to_hepai),
            heavenly_win=bool(raw.pop("heavenly_win", False) or "天胡" in way_to_hepai),
            earthly_win=bool(raw.pop("earthly_win", False) or "地胡" in way_to_hepai),
            human_win=bool(raw.pop("human_win", False) or "人胡" in way_to_hepai),
            rules=rules,
            **raw,
        )
        return self._scorer.score_hand(hand_context).as_dict()

    def hepai_check(
        self,
        hand_list: List[int],
        tiles_combination: List[str],
        way_to_hepai: List[str],
        get_tile: int,
        context: Optional[Dict[str, Any]] = None,
    ) -> Tuple[int, List[str]]:
        detail = self.hepai_detail(
            hand_list,
            tiles_combination,
            way_to_hepai,
            get_tile,
            context,
        )
        if not detail["is_win"]:
            return 0, []
        return detail["capped_tai"], detail["fan_names"]

    @staticmethod
    def _win_source(way_to_hepai: List[str]) -> str:
        if "抢杠" in way_to_hepai or "抢杠和" in way_to_hepai:
            return "robbing_kong"
        if "点胡" in way_to_hepai or "点和" in way_to_hepai:
            return "discard"
        return "self_draw"
