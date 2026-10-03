"""保留现有蓝十计算服务接口，规则实现见独立的第4版核心。"""

try:
    from .lanshi_v4 import FAN_VALUES, FAN_NAMES, Meld, evaluate, relation_options, score_fans, waiting_tiles
except ImportError:  # 兼容项目直接运行计算服务脚本的入口。
    from lanshi_v4 import FAN_VALUES, FAN_NAMES, Meld, evaluate, relation_options, score_fans, waiting_tiles


class Lanshi_Hepai_Check:
    count_model_dict = FAN_VALUES
    eng_to_chinese_dict = FAN_NAMES

    def __init__(self, debug=False):
        self.debug = debug

    def hepai_check(self, hand_list, tiles_combination, way_to_hepai, get_tile):
        candidates = self.hepai_decompose(hand_list, tiles_combination, way_to_hepai, get_tile)
        return (candidates[0]["score"], candidates[0]["fan_list"]) if candidates else (0, [])

    def hepai_decompose(self, hand_list, tiles_combination, way_to_hepai, get_tile):
        return evaluate(hand_list, tiles_combination, way_to_hepai, get_tile)

    @staticmethod
    def tingpai_check(hand_list, tiles_combination):
        return waiting_tiles(hand_list, tiles_combination)

    @staticmethod
    def _score(fans):
        return score_fans(fans)

    @staticmethod
    def _sequence_fans(tokens):
        groups = tuple(Meld(token[0], int(token[1:])) for token in tokens if token[0] in "sS")
        options = relation_options(groups)
        best = max(options, key=lambda relations: (sum(FAN_VALUES[r.fan] for r in relations),
                                                   tuple(-tuple(FAN_VALUES).index(r.fan) for r in relations)))
        return sorted((r.fan for r in best), key=tuple(FAN_VALUES).index)

    @staticmethod
    def filter_zero_value_fans(fan_score, fan_count_list):
        return min(100, fan_score), [name for name in fan_count_list if not name.endswith("*0")]
