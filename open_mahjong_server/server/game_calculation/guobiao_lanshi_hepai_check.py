# -*- coding: utf-8 -*-
"""国标麻将蓝十改（MCR 1001—2025）和牌与番种计算。"""

from __future__ import annotations

from collections import Counter
from itertools import combinations, permutations
from typing import Dict, Iterable, List, Sequence, Tuple

try:
    from .guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles
except ImportError:  # 兼容直接运行 game_calculation_service.py
    from guobiao_hepai_check import Chinese_Hepai_Check, PlayerTiles  # type: ignore


class Lanshi_Hepai_Check(Chinese_Hepai_Check):
    """复用标准国标的牌型拆解，只重写蓝十改的番种评价。"""

    count_model_dict: Dict[str, int] = {
        "qixingdui": 100, "sitongshun": 100, "jiulianbaodeng": 100, "sigang": 100,
        "dasixi": 72, "qingyaojiu": 72,
        "sianke": 48, "shisanyao": 48,
        "ziyise": 40, "silianshun": 40, "silianke": 40,
        "xiaosixi": 32, "sangang": 32,
        "dasanyuan": 24, "shunwang": 24, "santongshun": 24, "shunlian": 24,
        "hunyaojiu": 16, "quanda": 16, "quanzhong": 16, "quanxiao": 16,
        "quandaiwu": 16, "santongke": 16, "xiaosanyuan": 16, "quanbukao": 16,
        "sanfengke": 12, "sananke": 12, "sanlianke": 12, "qingyise": 12, "sanlianshun": 12,
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
        "qixingdui": "七星对", "sitongshun": "四同顺", "jiulianbaodeng": "九莲宝灯", "sigang": "四杠",
        "dasixi": "大四喜", "qingyaojiu": "清幺九", "sianke": "四暗刻", "shisanyao": "十三幺",
        "ziyise": "字一色", "silianshun": "四连顺", "silianke": "四连刻", "xiaosixi": "小四喜",
        "sangang": "三杠", "dasanyuan": "大三元", "shunwang": "顺网", "santongshun": "三同顺",
        "shunlian": "顺链", "hunyaojiu": "混幺九", "quanda": "全大", "quanzhong": "全中",
        "quanxiao": "全小", "quandaiwu": "全带五", "santongke": "三同刻", "xiaosanyuan": "小三元",
        "quanbukao": "全不靠", "sanfengke": "三风刻", "sananke": "三暗刻", "sanlianke": "三连刻",
        "qingyise": "清一色", "sanlianshun": "三连顺", "sanselianke": "三色连刻",
        "qingquandaiyao": "清全带幺", "shuanggang": "双杠", "dayuwu": "大于五", "xiaoyuwu": "小于五",
        "qiduizi": "七对", "shunhuan": "顺环", "shuangjianke": "双箭刻", "qinglong": "清龙",
        "miaoshouhuichun": "妙手回春", "haidilaoyue": "海底捞月", "gangshangkaihua": "杠上开花",
        "qiangganghe": "抢杠和", "tianhe": "天和", "dihe": "地和", "hualong": "花龙",
        "sansetongshun": "三色同顺", "pengpenghe": "碰碰和", "hunquandaiyao": "混全带幺",
        "hunyise": "混一色", "sanselianshun": "三色连顺", "angang": "暗杠", "shuanganke": "双暗刻",
        "wumenqi": "五门齐", "shuangtongke": "双同刻", "quanqiuren": "全求人", "siguiyi": "四归一",
        "yibangao": "一般高", "hejuezhang": "和绝张", "jianke": "箭刻", "quanfengke": "圈风刻",
        "menfengke": "门风刻", "menqianqing": "门前清", "minggang": "明杠", "duanyao": "断幺",
        "xixiangfeng": "喜相逢", "lianliu": "连六", "laoshaofu": "老少副", "yaojiuke": "幺九刻",
        "zimo": "自摸",
    }

    _unrelated_cases = (
        {11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47},
        {11, 14, 17, 32, 35, 38, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47},
        {21, 24, 27, 12, 15, 18, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47},
        {21, 24, 27, 32, 35, 38, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47},
        {31, 34, 37, 22, 25, 28, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47},
        {31, 34, 37, 12, 15, 18, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47},
    )
    _special_five = (
        "miaoshouhuichun", "haidilaoyue", "gangshangkaihua",
        "qiangganghe", "tianhe", "dihe",
    )
    _repeatable = {"siguiyi", "yibangao", "xixiangfeng", "lianliu", "laoshaofu", "yaojiuke"}

    def QD_check(self, player_tiles: PlayerTiles, player_tiles_list: List[PlayerTiles]):
        """蓝十七对必须由七种不同对子组成，四张同牌不能拆成两对。"""
        counts = Counter(player_tiles.hand_tiles)
        if len(counts) != 7 or any(count != 2 for count in counts.values()):
            return False
        candidate = player_tiles.__deepcopy__(None)
        candidate.complete_step = 14
        candidate.fan_list.append("qixingdui" if set(counts) == self.zipai_set else "qiduizi")
        player_tiles_list.append(candidate)
        return False

    def QBK_check(self, player_tiles: PlayerTiles, player_tiles_list: List[PlayerTiles]):
        """蓝十没有组合龙/七星不靠，只接受表中定义的全不靠。"""
        hand = player_tiles.hand_tiles
        if len(hand) != 14 or len(set(hand)) != 14:
            return False
        if any(set(hand) <= case for case in self._unrelated_cases):
            candidate = player_tiles.__deepcopy__(None)
            candidate.complete_step = 14
            candidate.fan_list.append("quanbukao")
            player_tiles_list.append(candidate)
        return False

    @staticmethod
    def _token_tile(token: str) -> int:
        return int(token[1:])

    @classmethod
    def _tiles_for_tokens(cls, tokens: Sequence[str]) -> List[int]:
        tiles: List[int] = []
        for token in tokens:
            mapped = cls.combination_to_tiles_dict.get(token)
            if mapped:
                tiles.extend(mapped)
        return sorted(tiles)

    @staticmethod
    def _suit_rank(tile: int) -> Tuple[int, int]:
        return tile // 10, tile % 10

    @staticmethod
    def _contains_five(token: str) -> bool:
        tile = int(token[1:])
        if token[0] in "sS":
            return tile - 1 <= (tile // 10) * 10 + 5 <= tile + 1
        return tile % 10 == 5

    @staticmethod
    def _contains_terminal(token: str) -> bool:
        tile = int(token[1:])
        if tile >= 40:
            return True
        if token[0] in "sS":
            return tile % 10 in (2, 8)
        return tile % 10 in (1, 9)

    @staticmethod
    def _contains_numeric_terminal(token: str) -> bool:
        tile = int(token[1:])
        if tile >= 40:
            return False
        if token[0] in "sS":
            return tile % 10 in (2, 8)
        return tile % 10 in (1, 9)

    @staticmethod
    def _has_consecutive(items: Iterable[Tuple[int, int]], length: int) -> bool:
        by_suit: Dict[int, set] = {}
        for suit, rank in items:
            by_suit.setdefault(suit, set()).add(rank)
        return any(any(all(start + d in ranks for d in range(length)) for start in range(1, 10 - length + 1))
                   for ranks in by_suit.values())

    @staticmethod
    def _pair_relation(a: Tuple[int, int], b: Tuple[int, int]) -> str | None:
        (sa, ra), (sb, rb) = a, b
        if ra == rb and sa != sb:
            return "xixiangfeng"
        if sa == sb and abs(ra - rb) == 3:
            return "lianliu"
        if sa == sb and {ra, rb} == {1, 7}:
            return "laoshaofu"
        return None

    @classmethod
    def _low_sequence_fans(cls, seqs: Sequence[Tuple[int, int]]) -> List[str]:
        """按面子不重复原则求双顺番的最高分匹配。"""
        edges = []
        for i, j in combinations(range(len(seqs)), 2):
            rel = cls._pair_relation(seqs[i], seqs[j])
            if rel:
                edges.append((i, j, rel))
            if seqs[i] == seqs[j]:
                edges.append((i, j, "yibangao"))

        best: Tuple[int, List[str]] = (0, [])

        def visit(pos: int, used: set, fans: List[str], score: int) -> None:
            nonlocal best
            if score > best[0]:
                best = (score, list(fans))
            for k in range(pos, len(edges)):
                i, j, fan = edges[k]
                if i not in used and j not in used:
                    visit(k + 1, used | {i, j}, fans + [fan], score + cls.count_model_dict[fan])

        visit(0, set(), [], 0)
        return best[1]

    @staticmethod
    def _same_relation_twice(seqs: Sequence[Tuple[int, int]]) -> bool:
        if len(seqs) != 4:
            return False
        for order in permutations(range(4)):
            if order[0] > order[1] or order[2] > order[3] or order[0] > order[2]:
                continue
            a, b, c, d = (seqs[i] for i in order)
            relation = Lanshi_Hepai_Check._pair_relation(a, b)
            if relation and relation == Lanshi_Hepai_Check._pair_relation(c, d):
                if Counter((a, b)) == Counter((c, d)):
                    return True
        return False

    @staticmethod
    def _shunhuan(seqs: Sequence[Tuple[int, int]]) -> bool:
        if len(seqs) != 4:
            return False
        counts = Counter(seqs)
        suits = {s for s, _ in seqs}
        if len(suits) != 2:
            return False
        per_suit = {s: sorted(r for ss, r in seqs if ss == s) for s in suits}
        values = list(per_suit.values())
        return values[0] == values[1] and len(values[0]) == 2 and (
            abs(values[0][0] - values[0][1]) == 3 or set(values[0]) == {1, 7}
        ) and all(v == 1 for v in counts.values())

    @staticmethod
    def _parse_winds(way: Sequence[str]) -> Tuple[int | None, int | None]:
        seat = round_wind = None
        names = {"东": 41, "南": 42, "西": 43, "北": 44}
        for item in way:
            for name, tile in names.items():
                if f"自风{name}" in item:
                    seat = tile
                if f"场风{name}" in item:
                    round_wind = tile
        return seat, round_wind

    def _evaluate(self, player_tiles: PlayerTiles, get_tile: int, way: List[str]) -> Tuple[int, List[str]]:
        original_tokens = list(player_tiles.combination_list)
        tokens = list(original_tokens)
        is_self_draw = any(x in way for x in ("last_deal", "妙手回春", "自摸", "杠上开花", "天和"))

        # 点和牌组成刻子时，该副刻子不再是暗刻；只改本次候选，不污染其他拆解。
        if not is_self_draw:
            for index, token in enumerate(tokens):
                if token in (f"K{get_tile}", f"G{get_tile}"):
                    if not any(t.startswith("S") and get_tile in self.combination_to_tiles_dict.get(t, ()) for t in tokens):
                        tokens[index] = token.lower()
                        break

        raw_hand = sorted(player_tiles.hand_tiles)
        is_thirteen = len(raw_hand) == 14 and set(raw_hand) == self.yaojiu and any(raw_hand.count(t) == 2 for t in self.yaojiu)
        pair_counts = Counter(raw_hand)
        is_seven_pairs = len(raw_hand) == 14 and len(pair_counts) == 7 and all(v == 2 for v in pair_counts.values())
        is_qixing_pairs = is_seven_pairs and set(pair_counts) == self.zipai_set
        is_unrelated = len(raw_hand) == 14 and len(set(raw_hand)) == 14 and any(set(raw_hand) <= case for case in self._unrelated_cases)

        normal_tokens = [t for t in tokens if t and t[0] in "sSkKgGq"]
        full_tiles = raw_hand if (is_thirteen or is_seven_pairs or is_unrelated) else self._tiles_for_tokens(normal_tokens)
        seq_tokens = [t for t in normal_tokens if t[0] in "sS"]
        trip_tokens = [t for t in normal_tokens if t[0] in "kKgG"]
        pair_tokens = [t for t in normal_tokens if t[0] == "q"]
        seqs = [(int(t[1:]) // 10, int(t[1:]) % 10 - 1) for t in seq_tokens]
        trips = [self._suit_rank(int(t[1:])) for t in trip_tokens]
        trip_tiles = [int(t[1:]) for t in trip_tokens]
        pair_tile = int(pair_tokens[0][1:]) if pair_tokens else None
        closed = not any(t[0] in "skg" for t in tokens)
        all_triplet_shape = len(trip_tokens) == 4 and len(pair_tokens) == 1
        fans: List[str] = []

        if is_thirteen:
            fans.append("shisanyao")
        elif is_qixing_pairs:
            fans.append("qixingdui")
        elif is_seven_pairs:
            fans.append("qiduizi")
        elif is_unrelated:
            fans.append("quanbukao")

        if full_tiles and not (is_thirteen or is_unrelated):
            tile_set = set(full_tiles)
            numeric = [t for t in full_tiles if t < 40]
            honors = [t for t in full_tiles if t >= 40]
            suits = {t // 10 for t in numeric}
            ranks = [t % 10 for t in numeric]

            if all(t >= 40 for t in full_tiles):
                fans.append("ziyise")
            elif len(suits) == 1 and not honors:
                fans.append("qingyise")
            elif len(suits) == 1 and honors:
                fans.append("hunyise")
            if all(t in self.qingyaojiu_set for t in full_tiles) and all_triplet_shape:
                fans.append("qingyaojiu")
            elif all(t in self.hunyaojiu_set for t in full_tiles) and all_triplet_shape:
                fans.append("hunyaojiu")
            if numeric and not honors and all(r >= 7 for r in ranks):
                fans.append("quanda" if all(r in (7, 8, 9) for r in ranks) else "dayuwu")
            elif numeric and not honors and all(r <= 3 for r in ranks):
                fans.append("quanxiao" if all(r in (1, 2, 3) for r in ranks) else "xiaoyuwu")
            elif numeric and not honors and all(r > 5 for r in ranks):
                fans.append("dayuwu")
            elif numeric and not honors and all(r < 5 for r in ranks):
                fans.append("xiaoyuwu")
            if numeric and not honors and all(4 <= r <= 6 for r in ranks):
                fans.append("quanzhong")
            if all(t in self.duanyao_set for t in full_tiles):
                fans.append("duanyao")
            if all(any(t in suit_set for t in full_tiles) for suit_set in (self.wan_set, self.bing_set, self.tiao_set)) \
                    and any(t in self.feng_set for t in full_tiles) and any(t in self.zhongbaifa_set for t in full_tiles):
                fans.append("wumenqi")
            for tile, count in Counter(full_tiles).items():
                if count == 4 and not any(t[0] in "gG" and int(t[1:]) == tile for t in tokens):
                    fans.append("siguiyi")

            if closed and len(suits) == 1 and not honors and len(full_tiles) == 14:
                rest = list(ranks)
                rest.remove(get_tile % 10)
                if sorted(rest) == self.jiulianbaodeng_list:
                    fans.append("jiulianbaodeng")

        if normal_tokens:
            if all(self._contains_five(t) for t in normal_tokens):
                fans.append("quandaiwu")
            if all(self._contains_numeric_terminal(t) for t in normal_tokens):
                fans.append("qingquandaiyao")
            elif all(self._contains_terminal(t) for t in normal_tokens) and any(int(t[1:]) >= 40 for t in normal_tokens):
                fans.append("hunquandaiyao")

        wind_trips = [t for t in trip_tiles if 41 <= t <= 44]
        dragon_trips = [t for t in trip_tiles if 45 <= t <= 47]
        if len(wind_trips) == 4:
            fans.append("dasixi")
        elif len(wind_trips) == 3 and pair_tile in self.feng_set:
            fans.append("xiaosixi")
        elif len(wind_trips) == 3:
            fans.append("sanfengke")
        if len(dragon_trips) == 3:
            fans.append("dasanyuan")
        elif len(dragon_trips) == 2 and pair_tile in self.zhongbaifa_set:
            fans.append("xiaosanyuan")
        elif len(dragon_trips) == 2:
            fans.append("shuangjianke")
        elif len(dragon_trips) == 1:
            fans.append("jianke")

        kong_count = sum(t[0] in "gG" for t in tokens)
        if kong_count == 4:
            fans.append("sigang")
        elif kong_count == 3:
            fans.append("sangang")
        elif kong_count == 2:
            fans.append("shuanggang")
        elif kong_count == 1:
            fans.append("angang" if any(t[0] == "G" for t in tokens) else "minggang")

        concealed_triplets = sum(t[0] in "KG" for t in tokens)
        if concealed_triplets == 4:
            fans.append("sianke")
        elif concealed_triplets == 3:
            fans.append("sananke")
        elif concealed_triplets == 2:
            fans.append("shuanganke")
        if all_triplet_shape:
            fans.append("pengpenghe")

        seq_counts = Counter(seqs)
        if any(v == 4 for v in seq_counts.values()):
            fans.append("sitongshun")
        elif any(v == 3 for v in seq_counts.values()):
            fans.append("santongshun")
        if self._has_consecutive(seqs, 4):
            fans.append("silianshun")
        elif self._has_consecutive(seqs, 3):
            fans.append("sanlianshun")
        if any(all((suit, rank) in seqs for rank in (1, 3, 5, 7)) for suit in (1, 2, 3)):
            fans.append("shunlian")
        if self._same_relation_twice(seqs):
            fans.append("shunwang")
        if self._shunhuan(seqs):
            fans.append("shunhuan")
        if any(all((suit, rank) in seqs for rank in (1, 4, 7)) for suit in (1, 2, 3)):
            fans.append("qinglong")
        if any(all((suit, rank) in seqs for suit in (1, 2, 3)) for rank in range(1, 8)):
            fans.append("sansetongshun")
        if any(all((suit, rank) in seqs for suit, rank in zip(perm, (1, 4, 7))) for perm in permutations((1, 2, 3))):
            fans.append("hualong")
        if any(all((suit, rank + offset) in seqs for suit, offset in zip(perm, (0, 1, 2)))
               for rank in range(1, 6) for perm in permutations((1, 2, 3))):
            fans.append("sanselianshun")

        trip_counts = Counter(trips)
        if self._has_consecutive(trips, 4):
            fans.append("silianke")
        elif self._has_consecutive(trips, 3):
            fans.append("sanlianke")
        if any(all((suit, rank) in trips for suit in (1, 2, 3)) for rank in range(1, 10)):
            fans.append("santongke")
        elif any(sum((suit, rank) in trips for suit in (1, 2, 3)) >= 2 for rank in range(1, 10)):
            fans.append("shuangtongke")
        if any(all((suit, rank + offset) in trips for suit, offset in zip(perm, (0, 1, 2)))
               for rank in range(1, 8) for perm in permutations((1, 2, 3))):
            fans.append("sanselianke")

        for tile in trip_tiles:
            if tile in self.hunyaojiu_set:
                fans.append("yaojiuke")

        seat_wind, round_wind = self._parse_winds(way)
        if seat_wind in trip_tiles:
            fans.append("menfengke")
        if round_wind in trip_tiles:
            fans.append("quanfengke")
        if closed:
            fans.append("menqianqing")
        if is_self_draw:
            fans.append("zimo")
        if "点和" in way and normal_tokens and all(t[0] not in "SKG" for t in tokens):
            fans.append("quanqiuren")
        if "和绝张" in way:
            fans.append("hejuezhang")

        method_map = {
            "妙手回春": "miaoshouhuichun", "海底捞月": "haidilaoyue",
            "last_deal": "miaoshouhuichun", "last_cut": "haidilaoyue",
            "杠上开花": "gangshangkaihua", "抢杠和": "qiangganghe",
            "天和": "tianhe", "地和": "dihe",
        }
        for marker, fan in method_map.items():
            if marker in way:
                fans.append(fan)

        return self._score(self._apply_exclusions(fans, seqs, way))

    def _apply_exclusions(self, fans: List[str], seqs: Sequence[Tuple[int, int]], way: Sequence[str]) -> List[str]:
        # 5 分和牌方式番均“不计其他番种分”。
        for special in self._special_five:
            if special in fans:
                return [special]

        remove: List[str] = []
        rules = {
            "qixingdui": ["ziyise", "qiduizi", "menqianqing"],
            "sitongshun": ["siguiyi"] * 4 + ["yibangao"],
            "jiulianbaodeng": ["qingyise", "yaojiuke", "menqianqing"],
            "sigang": ["pengpenghe", "angang", "minggang", "shuanggang", "sangang"],
            "dasixi": ["xiaosixi", "sanfengke", "pengpenghe", "quanfengke", "menfengke"] + ["yaojiuke"] * 4,
            "qingyaojiu": ["pengpenghe", "qingquandaiyao"] + ["yaojiuke"] * 4,
            "sianke": ["sananke", "shuanganke", "pengpenghe", "menqianqing"],
            "shisanyao": ["hunyaojiu", "wumenqi", "menqianqing"],
            "ziyise": ["pengpenghe", "hunquandaiyao"] + ["yaojiuke"] * 4,
            "silianshun": ["sanlianshun", "lianliu"],
            "silianke": ["sanlianke", "pengpenghe"],
            "xiaosixi": ["sanfengke"] + ["yaojiuke"] * 3,
            "sangang": ["shuanggang", "angang", "minggang"],
            "dasanyuan": ["xiaosanyuan", "shuangjianke", "jianke"] + ["yaojiuke"] * 3,
            "shunwang": ["qiduizi", "yibangao", "xixiangfeng", "lianliu", "laoshaofu"],
            "santongshun": ["sanlianke", "yibangao"],
            "shunlian": ["laoshaofu"],
            "hunyaojiu": ["pengpenghe", "hunquandaiyao"] + ["yaojiuke"] * 4,
            "quanda": ["dayuwu"], "quanzhong": ["duanyao"], "quanxiao": ["xiaoyuwu"],
            "quandaiwu": ["duanyao"], "xiaosanyuan": ["shuangjianke", "jianke"] + ["yaojiuke"] * 2,
            "quanbukao": ["wumenqi", "menqianqing"],
            "sanfengke": ["yaojiuke"] * 3, "sananke": ["shuanganke"], "sanlianke": ["santongshun"],
            "qinglong": ["lianliu", "laoshaofu"], "qiduizi": ["menqianqing"],
            "shuangjianke": ["jianke"] + ["yaojiuke"] * 2,
            "jianke": ["yaojiuke"],
        }
        for fan in list(fans):
            remove.extend(rules.get(fan, ()))
        if "quanfengke" in fans:
            remove.append("yaojiuke")
        if "menfengke" in fans and not ({"quanfengke", "menfengke"} <= set(fans) and "门风圈风相同" in way):
            remove.append("yaojiuke")

        result = list(fans)
        for fan in remove:
            if fan in result:
                result.remove(fan)

        high_sequence = {"sitongshun", "silianshun", "shunwang", "shunlian", "shunhuan"}
        middle_sequence = {"santongshun", "sanlianshun", "qinglong", "hualong", "sansetongshun", "sanselianshun"}
        result = [f for f in result if f not in {"yibangao", "xixiangfeng", "lianliu", "laoshaofu"}]
        if not any(f in result for f in high_sequence):
            low = self._low_sequence_fans(seqs)
            if any(f in result for f in middle_sequence):
                low = low[:1]
            result.extend(low)
        return result

    def _score(self, fans: Sequence[str]) -> Tuple[int, List[str]]:
        ordered = sorted(fans, key=lambda fan: self.count_model_dict.get(fan, 0), reverse=True)
        score = 0
        output: List[str] = []
        seen = set()
        for fan in ordered:
            if fan in self._repeatable:
                continue
            if fan not in seen:
                score += self.count_model_dict[fan]
                output.append(self.eng_to_chinese_dict[fan])
                seen.add(fan)
        for fan in self._repeatable:
            count = ordered.count(fan)
            if count:
                score += count * self.count_model_dict[fan]
                output.append(f"{self.eng_to_chinese_dict[fan]}*{count}")
        return min(score, 100), output

    def fan_count(self, player_tiles: PlayerTiles, get_tile: int, way_to_hepai: List[str]):
        return self._evaluate(player_tiles, get_tile, list(way_to_hepai))

    def filter_zero_value_fans(self, fan_score: int, fan_count_list: List[str]) -> Tuple[int, List[str]]:
        return min(fan_score, 100), fan_count_list
