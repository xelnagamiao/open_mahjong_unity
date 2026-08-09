# -*- coding: utf-8 -*-
"""蓝十改（MCR 1001-2025）和牌检查。

牌型拆解、和牌张明暗转换以及基础番种识别全部复用标准国标实现；本模块只
描述蓝十与标准国标的差异：特殊牌型、番种归一化、新增组合、排除和分值。
"""

from __future__ import annotations

from collections import Counter
from itertools import combinations, permutations
from typing import Dict, List, Sequence, Tuple

try:
    from .guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles
except ImportError:  # 兼容直接运行 game_calculation_service.py
    from guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles  # type: ignore


class Lanshi_Hepai_Check(Chinese_Hepai_Check):
    """以标准国标计番流水线为基线的蓝十规则差异层。"""

    count_model_dict: Dict[str, int] = {
        "qixingdui": 100, "sitongshun": 100, "jiulianbaodeng": 100, "sigang": 100,
        "dasixi": 72, "qingyaojiu": 72,
        "sianke": 48, "shisanyao": 48,
        "ziyise": 40, "silianshun": 40, "silianke": 40,
        "xiaosixi": 32, "sangang": 32,
        "dasanyuan": 24, "shunwang": 24, "santongshun": 24, "shunlian": 24,
        "hunyaojiu": 16, "quanda": 16, "quanzhong": 16, "quanxiao": 16,
        "quandaiwu": 16, "santongke": 16, "xiaosanyuan": 16, "quanbukao": 16,
        "sanfengke": 12, "sananke": 12, "sanlianke": 12, "qingyise": 12,
        "sanlianshun": 12,
        "sanselianke": 8, "qingquandaiyao": 8, "shuanggang": 8, "dayuwu": 8,
        "xiaoyuwu": 8, "qiduizi": 8, "shunhuan": 8,
        "shuangjianke": 6, "qinglong": 6,
        "miaoshouhuichun": 5, "haidilaoyue": 5, "gangshangkaihua": 5,
        "qiangganghe": 5, "tianhe": 5, "dihe": 5,
        "hualong": 4, "sansetongshun": 4,
        "pengpenghe": 3, "hunquandaiyao": 3, "hunyise": 3, "sanselianshun": 3,
        "angang": 2, "shuanganke": 2, "wumenqi": 2, "shuangtongke": 2,
        "quanqiuren": 2, "siguiyi": 2, "yibangao": 2, "hejuezhang": 2,
        "jianke": 2, "quanfengke": 2, "menfengke": 2,
        "menqianqing": 1, "minggang": 1, "duanyao": 1, "xixiangfeng": 1,
        "lianliu": 1, "laoshaofu": 1, "yaojiuke": 1, "zimo": 1,
    }

    eng_to_chinese_dict = {
        "qixingdui": "七星对", "sitongshun": "四同顺", "jiulianbaodeng": "九莲宝灯",
        "sigang": "四杠", "dasixi": "大四喜", "qingyaojiu": "清幺九", "sianke": "四暗刻",
        "shisanyao": "十三幺", "ziyise": "字一色", "silianshun": "四连顺",
        "silianke": "四连刻", "xiaosixi": "小四喜", "sangang": "三杠",
        "dasanyuan": "大三元", "shunwang": "顺网", "santongshun": "三同顺",
        "shunlian": "顺链", "hunyaojiu": "混幺九", "quanda": "全大",
        "quanzhong": "全中", "quanxiao": "全小", "quandaiwu": "全带五",
        "santongke": "三同刻", "xiaosanyuan": "小三元", "quanbukao": "全不靠",
        "sanfengke": "三风刻", "sananke": "三暗刻", "sanlianke": "三连刻",
        "qingyise": "清一色", "sanlianshun": "三连顺", "sanselianke": "三色连刻",
        "qingquandaiyao": "清全带幺", "shuanggang": "双杠", "dayuwu": "大于五",
        "xiaoyuwu": "小于五", "qiduizi": "七对", "shunhuan": "顺环",
        "shuangjianke": "双箭刻", "qinglong": "清龙", "miaoshouhuichun": "妙手回春",
        "haidilaoyue": "海底捞月", "gangshangkaihua": "杠上开花", "qiangganghe": "抢杠和",
        "tianhe": "天和", "dihe": "地和", "hualong": "花龙",
        "sansetongshun": "三色同顺", "pengpenghe": "碰碰和",
        "hunquandaiyao": "混全带幺", "hunyise": "混一色",
        "sanselianshun": "三色连顺", "angang": "暗杠", "shuanganke": "双暗刻",
        "wumenqi": "五门齐", "shuangtongke": "双同刻", "quanqiuren": "全求人",
        "siguiyi": "四归一", "yibangao": "一般高", "hejuezhang": "和绝张",
        "jianke": "箭刻", "quanfengke": "圈风刻", "menfengke": "门风刻",
        "menqianqing": "门前清", "minggang": "明杠", "duanyao": "断幺",
        "xixiangfeng": "喜相逢", "lianliu": "连六", "laoshaofu": "老少副",
        "yaojiuke": "幺九刻", "zimo": "自摸",
    }

    _table_order = tuple(count_model_dict)
    _repeatable = {"siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "laoshaofu", "yaojiuke"}
    _occasional = (
        "miaoshouhuichun", "haidilaoyue", "gangshangkaihua",
        "qiangganghe", "tianhe", "dihe",
    )
    _unrelated_cases = (
        {11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47},
        {11, 14, 17, 32, 35, 38, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47},
        {21, 24, 27, 12, 15, 18, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47},
        {21, 24, 27, 32, 35, 38, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47},
        {31, 34, 37, 22, 25, 28, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47},
        {31, 34, 37, 12, 15, 18, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47},
    )

    # 标准国标原始番名中，能直接等价到蓝十番表的部分。
    _direct_names = {
        "dasixi", "dasanyuan", "jiulianbaodeng", "sigang", "shisanyao",
        "qingyaojiu", "xiaosixi", "xiaosanyuan", "ziyise", "sianke", "sangang",
        "hunyaojiu", "qiduizi", "qingyise", "quanda", "quanzhong", "quanxiao",
        "quandaiwu", "santongke", "sananke", "quanbukao", "dayuwu", "xiaoyuwu",
        "sanfengke", "miaoshouhuichun", "haidilaoyue", "gangshangkaihua",
        "qiangganghe", "pengpenghe", "hunyise", "wumenqi", "quanqiuren",
        "hejuezhang", "jianke", "quanfengke", "menfengke", "menqianqing", "siguiyi",
        "shuangtongke", "shuanganke", "angang", "duanyao", "yaojiuke", "minggang", "zimo",
        "qixingdui",
    }

    def QD_check(self, player_tiles: PlayerTiles, player_tiles_list: List[PlayerTiles]):
        """七对必须是七种不同对子，四张同牌不能拆成两个对子。"""
        counts = Counter(player_tiles.hand_tiles)
        if len(counts) != 7 or any(count != 2 for count in counts.values()):
            return False
        candidate = player_tiles.__deepcopy__(None)
        candidate.complete_step = 14
        candidate.fan_list.append("qixingdui" if set(counts) == self.zipai_set else "qiduizi")
        player_tiles_list.append(candidate)
        return False

    def QBK_check(self, player_tiles: PlayerTiles, player_tiles_list: List[PlayerTiles]):
        """蓝十没有组合龙和七星不靠，只保留规则表定义的全不靠。"""
        hand = player_tiles.hand_tiles
        if len(hand) == 14 and len(set(hand)) == 14 and any(set(hand) <= case for case in self._unrelated_cases):
            candidate = player_tiles.__deepcopy__(None)
            candidate.complete_step = 14
            candidate.fan_list.append("quanbukao")
            player_tiles_list.append(candidate)
        return False

    @staticmethod
    def _sequence(token: str) -> Tuple[int, int]:
        tile = int(token[1:])
        return tile // 10, tile % 10 - 1

    @staticmethod
    def _triplet(token: str) -> Tuple[int, int]:
        tile = int(token[1:])
        return tile // 10, tile % 10

    @staticmethod
    def _low_relation(a: Tuple[int, int], b: Tuple[int, int]) -> str | None:
        (suit_a, rank_a), (suit_b, rank_b) = a, b
        if a == b:
            return "yibangao"
        if rank_a == rank_b and suit_a != suit_b:
            return "xixiangfeng"
        if suit_a == suit_b and abs(rank_a - rank_b) == 3:
            return "lianliu"
        if suit_a == suit_b and {rank_a, rank_b} == {1, 7}:
            return "laoshaofu"
        return None

    @classmethod
    def _best_low_sequence_fans(
        cls,
        seqs: Sequence[Tuple[int, int]],
        occupied: frozenset[int] = frozenset(),
    ) -> List[str]:
        """按国标不循环组合原则，取双顺番的最高分无环组合。"""
        edges: List[Tuple[int, int, str]] = []
        for left, right in combinations(range(len(seqs)), 2):
            if left in occupied and right in occupied:
                continue
            relation = cls._low_relation(seqs[left], seqs[right])
            if relation:
                edges.append((left, right, relation))

        best: Tuple[int, Tuple[int, ...], List[str]] = (0, (), [])
        for size in range(len(edges) + 1):
            for selected in combinations(range(len(edges)), size):
                parent = list(range(len(seqs)))

                def root(node: int) -> int:
                    while parent[node] != node:
                        parent[node] = parent[parent[node]]
                        node = parent[node]
                    return node

                valid = True
                names: List[str] = []
                for edge_index in selected:
                    left, right, name = edges[edge_index]
                    left_root, right_root = root(left), root(right)
                    if left_root == right_root:
                        valid = False
                        break
                    parent[left_root] = right_root
                    names.append(name)
                if not valid:
                    continue
                score = sum(cls.count_model_dict[name] for name in names)
                tie = tuple(-cls._table_order.index(name) for name in names)
                if (score, tie) > (best[0], best[1]):
                    best = (score, tie, names)
        return best[2]

    @classmethod
    def _sequence_fans(cls, tokens: Sequence[str]) -> List[str]:
        """计算蓝十顺系列番，并记录高番实际占用的面子。"""
        seqs = [cls._sequence(token) for token in tokens if token and token[0] in "sS"]
        if not seqs:
            return []

        candidates: List[Tuple[int, int, str, frozenset[int]]] = []

        def add(name: str, indices: Sequence[int]) -> None:
            used = frozenset(indices)
            candidates.append((cls.count_model_dict[name], len(used), name, used))

        for value, indices in Counter(seqs).items():
            positions = [index for index, sequence in enumerate(seqs) if sequence == value]
            if len(positions) >= 4:
                add("sitongshun", positions[:4])
            elif len(positions) >= 3:
                add("santongshun", positions[:3])

        for suit in (1, 2, 3):
            for start in range(1, 5):
                indices = [next((i for i, value in enumerate(seqs) if value == (suit, start + step)), -1) for step in range(4)]
                if min(indices) >= 0:
                    add("silianshun", indices)
            for start in range(1, 6):
                indices = [next((i for i, value in enumerate(seqs) if value == (suit, start + step)), -1) for step in range(3)]
                if min(indices) >= 0:
                    add("sanlianshun", indices)
            indices = [next((i for i, value in enumerate(seqs) if value == (suit, rank)), -1) for rank in (1, 3, 5, 7)]
            if min(indices) >= 0:
                add("shunlian", indices)
            indices = [next((i for i, value in enumerate(seqs) if value == (suit, rank)), -1) for rank in (1, 4, 7)]
            if min(indices) >= 0:
                add("qinglong", indices)

        if len(seqs) == 4:
            relations = []
            for left, right in ((0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3)):
                relation = cls._low_relation(seqs[left], seqs[right])
                if relation in {"xixiangfeng", "lianliu", "laoshaofu"}:
                    relations.append((left, right, relation, frozenset((seqs[left], seqs[right]))))
            if any(first[2:] == second[2:] and {first[0], first[1]}.isdisjoint({second[0], second[1]})
                   for first, second in combinations(relations, 2)):
                add("shunwang", range(4))

            suits = {suit for suit, _rank in seqs}
            if len(suits) == 2:
                grouped = [sorted(rank for current, rank in seqs if current == suit) for suit in suits]
                if len(grouped[0]) == len(grouped[1]) == 2 and grouped[0] == grouped[1] and (
                    abs(grouped[0][0] - grouped[0][1]) == 3 or set(grouped[0]) == {1, 7}
                ):
                    add("shunhuan", range(4))

        for rank in range(1, 8):
            indices = [next((i for i, value in enumerate(seqs) if value == (suit, rank)), -1) for suit in (1, 2, 3)]
            if min(indices) >= 0:
                add("sansetongshun", indices)
        for suit_order in permutations((1, 2, 3)):
            dragon = [next((i for i, value in enumerate(seqs) if value == pair), -1)
                      for pair in zip(suit_order, (1, 4, 7))]
            if min(dragon) >= 0:
                add("hualong", dragon)
            for rank in range(1, 6):
                indices = [next((i for i, value in enumerate(seqs) if value == (suit, rank + offset)), -1)
                           for suit, offset in zip(suit_order, (0, 1, 2))]
                if min(indices) >= 0:
                    add("sanselianshun", indices)

        # 同一组面子只采用一个最高的三/四顺主体番；第四副顺子仍可与主体中的
        # 一副组成一个合法双顺番。主体内部的固有低番不再显示。
        if candidates:
            _score, _size, name, occupied = max(candidates, key=lambda item: (item[0], item[1], -cls._table_order.index(item[2])))
            if len(occupied) == 4:
                return [name]
            low = cls._best_low_sequence_fans(seqs, occupied)
            return [name] + low[:1]
        return cls._best_low_sequence_fans(seqs)

    @classmethod
    def _extra_triplet_fans(cls, tokens: Sequence[str]) -> List[str]:
        trips = [cls._triplet(token) for token in tokens if token and token[0] in "kKgG"]
        fans: List[str] = []
        for suit in (1, 2, 3):
            ranks = {rank for current_suit, rank in trips if current_suit == suit}
            if any(all(start + step in ranks for step in range(4)) for start in range(1, 7)):
                fans.append("silianke")
            elif any(all(start + step in ranks for step in range(3)) for start in range(1, 8)):
                fans.append("sanlianke")
        if not any(name in fans for name in ("silianke", "sanlianke")):
            for suit_order in permutations((1, 2, 3)):
                if any(all((suit, rank + offset) in trips for suit, offset in zip(suit_order, (0, 1, 2)))
                       for rank in range(1, 8)):
                    fans.append("sanselianke")
                    break
        return fans

    def _normalise_raw_fans(self, player_tiles: PlayerTiles, way: Sequence[str]) -> List[str]:
        raw = player_tiles.fan_list
        fans = [name for name in raw if name in self._direct_names]
        if "buqiuren" in raw:
            fans.extend(("menqianqing", "zimo"))

        tokens = [token for token in player_tiles.combination_list if token and token[0] in "sSkKgGq"]
        fans.extend(self._sequence_fans(tokens))
        fans.extend(self._extra_triplet_fans(tokens))

        if "quandaiyao" in raw:
            fans.append("hunquandaiyao" if any(int(token[1:]) >= 40 for token in tokens) else "qingquandaiyao")
        if any(name in raw for name in ("shuangangang", "shuangminggang", "mingangang")):
            fans.append("shuanggang")
        if "天和" in way:
            fans.append("tianhe")
        if "地和" in way:
            fans.append("dihe")
        return fans

    @staticmethod
    def _remove_once(fans: List[str], names: Sequence[str]) -> None:
        for name in names:
            if name in fans:
                fans.remove(name)

    def _apply_exclusions(self, fans: List[str], way: Sequence[str]) -> List[str]:
        # 满贯番不加计任何其他番种。
        hundred = next((name for name in self._table_order if name in fans and self.count_model_dict[name] == 100), None)
        if hundred:
            return [hundred]

        rules = {
            "dasixi": ["xiaosixi", "sanfengke", "pengpenghe", "quanfengke", "menfengke"] + ["yaojiuke"] * 4,
            "qingyaojiu": ["pengpenghe"] + ["yaojiuke"] * 4,
            "sianke": ["sananke", "shuanganke", "pengpenghe", "menqianqing"],
            "shisanyao": ["hunyaojiu", "wumenqi", "menqianqing"],
            "ziyise": ["pengpenghe"] + ["yaojiuke"] * 4,
            "silianke": ["sanlianke", "santongshun", "pengpenghe"],
            "xiaosixi": ["sanfengke"] + ["yaojiuke"] * 3,
            "sangang": ["shuanggang", "angang", "minggang"],
            "dasanyuan": ["xiaosanyuan", "shuangjianke", "jianke"] + ["yaojiuke"] * 3,
            "shunwang": ["qiduizi", "yibangao", "xixiangfeng", "lianliu", "laoshaofu"],
            "santongshun": ["sanlianke", "yibangao"],
            "hunyaojiu": ["qiduizi", "pengpenghe"] + ["yaojiuke"] * 4,
            "quanda": ["dayuwu"], "quanzhong": ["duanyao"], "quanxiao": ["xiaoyuwu"],
            "quandaiwu": ["duanyao"], "santongke": ["shuangtongke"],
            "xiaosanyuan": ["shuangjianke", "jianke"] + ["yaojiuke"] * 2,
            "quanbukao": ["wumenqi", "menqianqing"],
            "sanfengke": ["yaojiuke"] * 3, "sananke": ["shuanganke"],
            "sanlianke": ["santongshun"], "shuanggang": ["angang", "minggang"],
            "qiduizi": ["menqianqing"],
            "shuangjianke": ["jianke"] + ["yaojiuke"] * 2,
            "jianke": ["yaojiuke"],
        }
        result = list(fans)
        for fan in list(fans):
            self._remove_once(result, rules.get(fan, ()))

        wind_removals = int("quanfengke" in fans) + int("menfengke" in fans)
        if wind_removals == 2 and "门风圈风相同" in way:
            wind_removals = 1
        self._remove_once(result, ["yaojiuke"] * wind_removals)

        # 5 分偶然番：常规番不足 5 分时仅计偶然番；达到 5 分时反而不计偶然番。
        occasional = next((name for name in self._occasional if name in result), None)
        if occasional:
            regular = [name for name in result if name not in self._occasional]
            regular_score = sum(self.count_model_dict[name] for name in regular)
            return regular if regular_score >= 5 else [occasional]
        return result

    def _score(self, fans: Sequence[str]) -> Tuple[int, List[str]]:
        ordered = sorted(fans, key=lambda name: self._table_order.index(name))
        score = 0
        output: List[str] = []
        for name in self._table_order:
            count = ordered.count(name)
            if not count:
                continue
            if name in self._repeatable:
                score += count * self.count_model_dict[name]
                output.append(f"{self.eng_to_chinese_dict[name]}*{count}")
            else:
                score += self.count_model_dict[name]
                output.append(self.eng_to_chinese_dict[name])
        return min(score, 100), output

    def fan_count_output(self, player_tiles: PlayerTiles, combination_str, zimo_or_not, way_to_hepai):
        """接收标准国标流水线识别出的原始番，应用蓝十差异后输出。"""
        fans = self._normalise_raw_fans(player_tiles, way_to_hepai)
        return self._score(self._apply_exclusions(fans, way_to_hepai))

    def filter_zero_value_fans(self, fan_score: int, fan_count_list: List[str]) -> Tuple[int, List[str]]:
        # 蓝十番表没有 0 分番；保留接口兼容，但绝不返回 0 分占位。
        return min(fan_score, 100), [name for name in fan_count_list if not name.endswith("*0")]
