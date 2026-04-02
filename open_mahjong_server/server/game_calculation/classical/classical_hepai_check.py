from typing import Dict, List, Tuple
from time import time
import logging

try:
    from .classical_solver import ClassicalCombinationSolver, PlayerTiles
except ImportError:
    from classical_solver import ClassicalCombinationSolver, PlayerTiles  # type: ignore

logger = logging.getLogger(__name__)


class Classical_Hepai_Check(ClassicalCombinationSolver):
    _DRAGONS = {45, 46, 47}  # 中白发
    _WINDS = {41, 42, 43, 44}  # 东南西北
    _HONORS = _WINDS | _DRAGONS  # 全部字牌
    _TERMINALS = {11, 19, 21, 29, 31, 39}  # 幺九数牌
    _YAOJIU = _TERMINALS | _HONORS  # 幺九牌集合（含字牌）
    _FANPAI = _DRAGONS  # 默认役牌：仅三元牌；门风通过传参动态加入
    _MANGAN_FAN_SET = {"大三元", "大四喜", "小四喜", "天和", "地和", "九莲宝灯", "国士无双"}  # 满贯番型
    # 副名 -> 单次副值映射（副番名仅保留名称，不拼接“x副”）。
    _FU_VALUE_MAP: Dict[str, int] = {
        "和牌": 10,
        "自摸": 2,
        "边嵌吊": 2,
        "门前清": 2,
        "刻子": 2,
        "暗刻": 4,
        "明杠": 8,
        "暗杠": 16,
        "幺九刻": 4,
        "幺九暗刻": 8,
        "幺九明杠": 16,
        "幺九暗杠": 32,
        "番牌刻": 8,
        "番牌暗刻": 16,
        "番牌明杠": 32,
        "番牌暗杠": 64,
    }

    def __init__(self, debug=False):
        self.debug = debug

    def debug_print(self, *args, **kwargs):
        if self.debug:
            logger.debug(*args, **kwargs)
            print(*args, **kwargs)

    def hepai_check(self, hand_list: list, tiles_combination, way_to_hepai, get_tile):
        """
        返回 (副数, 总副数, 副番名列表, 番名列表)。
        """
        complete_step = len(tiles_combination) * 3
        player_tiles = PlayerTiles(hand_list, tiles_combination, complete_step)
        check_done_list: List[PlayerTiles] = []
        self.normal_check(player_tiles, check_done_list)

        best_base_fu = 0
        best_total_fu = 0
        best_fu_fans: List[str] = []
        best_fans: List[str] = []
        if not check_done_list:
            return best_base_fu, best_total_fu, best_fu_fans, best_fans

        for item in check_done_list:
            # 枚举所有可行拆解并取总副数最高方案。
            mapped_hand, combination_str = self.build_hand_and_combination_mapping(item)
            item.hand_tiles_mapped = mapped_hand
            item.combination_str = combination_str
            base_fu, total_fu, fu_fan_list, fan_list = self.fan_count(item, get_tile, way_to_hepai)
            if total_fu > best_total_fu:
                best_base_fu = base_fu
                best_total_fu = total_fu
                best_fu_fans = fu_fan_list
                best_fans = fan_list

        return best_base_fu, best_total_fu, best_fu_fans, best_fans

    def hepai_check_all(self, hand_list: list, tiles_combination, way_to_hepai, get_tile):
        complete_step = len(tiles_combination) * 3
        player_tiles = PlayerTiles(hand_list, tiles_combination, complete_step)
        check_done_list: List[PlayerTiles] = []
        self.normal_check(player_tiles, check_done_list)

        result = []
        for item in check_done_list:
            mapped_hand, combination_str = self.build_hand_and_combination_mapping(item)
            item.hand_tiles_mapped = mapped_hand
            item.combination_str = combination_str
            result.append({
                "combination_str": combination_str,
                "hand_tiles_mapped": mapped_hand,
                "combination_list": item.combination_list[:],
            })
        return result

    def fan_count(self, player_tiles: PlayerTiles, get_tile, way_to_hepai):
        # 先按自摸/点和修正和牌张所在刻杠的明暗标记。
        self._normalize_meld_visibility(player_tiles, get_tile, way_to_hepai)
        combination_list = player_tiles.combination_list[:]
        fanpai_set = self._get_active_fanpai_set(way_to_hepai)  # 本局有效役牌（中白发+门风）
        fu = 0
        fan = 0
        fu_fan_tags: List[str] = []
        fan_list: List[str] = []
        is_mangan = False

        # 和牌副与边嵌吊副均按外部 way_to_hepai 传参计算。
        if "和牌" in way_to_hepai:
            fu += self._FU_VALUE_MAP["和牌"]
            fu_fan_tags.append("和牌")
        if "自摸" in way_to_hepai:
            fu += self._FU_VALUE_MAP["自摸"]
            fu_fan_tags.append("自摸")
            fan_list.append("自摸")
        if "边嵌吊" in way_to_hepai:
            fu += self._FU_VALUE_MAP["边嵌吊"]
            fu_fan_tags.append("边嵌吊")
        """ 
        暂时不计门前清
        if "门前清" in way_to_hepai:
            fu += self._FU_VALUE_MAP["门前清"]
            fu_fan_tags.append("门前清")
        """
        triplets, pair_tile = self._extract_sets(combination_list)
        fu_repeat_count: Dict[str, int] = {}
        for sign, tile in triplets:
            add_fu, tag = self._calc_set_fu(sign, tile, fanpai_set)
            fu += add_fu
            fu_repeat_count[tag] = fu_repeat_count.get(tag, 0) + 1

        hand_tiles = player_tiles.hand_tiles_mapped[:]
        if not hand_tiles:
            hand_tiles, _ = self.build_hand_and_combination_mapping(player_tiles)
        suits = {t // 10 for t in hand_tiles if t not in self._HONORS}
        has_honor = any(t in self._HONORS for t in hand_tiles)
        if has_honor and len(suits) == 1:
            fan += 1
            fan_list.append("混一色")
        elif (not has_honor) and len(suits) == 1:
            fan += 3
            fan_list.append("清一色")
        elif all(t in self._HONORS for t in hand_tiles):
            fan += 3
            fan_list.append("字一色")

        if all(s in {"K", "k", "G", "g"} for s, _ in triplets):
            fan += 1
            fan_list.append("鸾凤和鸣")

        dragon_pungs = {t for _, t in triplets if t in self._DRAGONS}
        wind_pungs = {t for _, t in triplets if t in self._WINDS}
        if len(dragon_pungs) == 3:
            fan_list.append("大三元")
            is_mangan = True
        elif len(dragon_pungs) == 2 and pair_tile in self._DRAGONS:
            fan += 2
            fan_list.append("小三元")

        if len(wind_pungs) == 4:
            fan_list.append("大四喜")
            is_mangan = True
        elif len(wind_pungs) == 3 and pair_tile in self._WINDS:
            fan_list.append("小四喜")
            is_mangan = True

        if "岭上开花" in way_to_hepai or "杠上开花" in way_to_hepai:
            fan += 1
            fan_list.append("岭上开花")
        if "海底捞月" in way_to_hepai:
            fan += 1
            fan_list.append("海底捞月")
        if "金鸡夺食" in way_to_hepai or "抢杠和" in way_to_hepai:
            fan += 1
            fan_list.append("金鸡夺食")
        if "天和" in way_to_hepai:
            fan_list.append("天和")
            is_mangan = True
        if "地和" in way_to_hepai:
            fan_list.append("地和")
            is_mangan = True
        if "九莲宝灯" in way_to_hepai:
            fan_list.append("九莲宝灯")
            is_mangan = True
        if "国士无双" in way_to_hepai:
            fan_list.append("国士无双")
            is_mangan = True

        # 结果总分封顶 300；命中满贯番时仅保留满贯番种输出。
        base_fu = min(fu, 300)
        total_fu = min(int(base_fu * (2 ** fan)), 300)

        for tag, cnt in fu_repeat_count.items():
            fu_fan_tags.append(tag if cnt == 1 else f"{tag}*{cnt}")

        if is_mangan:
            only_mangan_fans = [f for f in fan_list if f in self._MANGAN_FAN_SET]
            return base_fu, 300, fu_fan_tags, only_mangan_fans

        return base_fu, total_fu, fu_fan_tags, fan_list

    def fushucheck(self, hand_list: list, tiles_combination, way_to_hepai, get_tile) -> int:
        # 只返回可和牌拆解中的最大基础副数，不返回番型文本。
        best_base_fu = 0

        complete_step = len(tiles_combination) * 3
        player_tiles = PlayerTiles(hand_list, tiles_combination, complete_step)
        check_done_list: List[PlayerTiles] = []
        self.normal_check(player_tiles, check_done_list)
        for item in check_done_list:
            mapped_hand, combination_str = self.build_hand_and_combination_mapping(item)
            item.hand_tiles_mapped = mapped_hand
            item.combination_str = combination_str
            base_fu, _, _, _ = self.fan_count(item, get_tile, way_to_hepai)
            if base_fu > best_base_fu:
                best_base_fu = base_fu
        return best_base_fu

    def _extract_sets(self, combination_list: List[str]) -> Tuple[List[Tuple[str, int]], int]:
        # 从组合串中提取刻/杠列表与雀头牌值。
        triplets: List[Tuple[str, int]] = []
        pair_tile = -1
        for c in combination_list:
            if len(c) < 2:
                continue
            sign = c[0]
            try:
                tile = int(c[1:])
            except ValueError:
                continue
            if sign in {"k", "K", "g", "G"}:
                triplets.append((sign, tile))
            elif sign == "q":
                pair_tile = tile
        return triplets, pair_tile

    def _calc_set_fu(self, sign: str, tile: int, fanpai_set: set) -> Tuple[int, str]:
        # 按“普通/幺九/役牌”与“明暗/刻杠”计算单组副数和副名。
        is_kong = sign in {"g", "G"}
        is_concealed = sign in {"K", "G"}
        is_fanpai = tile in fanpai_set
        is_yaojiu = tile in self._YAOJIU and not is_fanpai

        if is_kong:
            if is_fanpai:
                name = "番牌暗杠" if is_concealed else "番牌明杠"
                return self._FU_VALUE_MAP[name], name
            if is_yaojiu:
                name = "幺九暗杠" if is_concealed else "幺九明杠"
                return self._FU_VALUE_MAP[name], name
            name = "暗杠" if is_concealed else "明杠"
            return self._FU_VALUE_MAP[name], name

        if is_fanpai:
            name = "番牌暗刻" if is_concealed else "番牌刻"
            return self._FU_VALUE_MAP[name], name
        if is_yaojiu:
            name = "幺九暗刻" if is_concealed else "幺九刻"
            return self._FU_VALUE_MAP[name], name
        name = "暗刻" if is_concealed else "刻子"
        return self._FU_VALUE_MAP[name], name

    def _get_active_fanpai_set(self, way_to_hepai: List[str]) -> set:
        # 根据 way_to_hepai 中门风参数动态生成役牌集合（不含圈风）。
        fanpai = set(self._FANPAI)
        if "门风东" in way_to_hepai:
            fanpai.add(41)
        elif "门风南" in way_to_hepai:
            fanpai.add(42)
        elif "门风西" in way_to_hepai:
            fanpai.add(43)
        elif "门风北" in way_to_hepai:
            fanpai.add(44)
        return fanpai

    def _normalize_meld_visibility(self, player_tiles: PlayerTiles, get_tile: int, way_to_hepai: List[str]) -> None:
        # 点和时将和牌张所在暗刻/暗杠降为明刻/明杠（暗转明）。
        zimo_or_not = any(i in ["妙手回春", "自摸", "杠上开花", "岭上开花"] for i in way_to_hepai)
        if not zimo_or_not:
            for comb in player_tiles.combination_list:
                if comb == f"G{get_tile}":
                    if any(i in player_tiles.combination_list for i in [f"S{get_tile}", f"S{get_tile+1}", f"S{get_tile-1}"]):
                        continue
                    player_tiles.combination_list.remove(comb)
                    player_tiles.combination_list.append(f"g{comb[1]}{comb[2]}")
                    return
                if comb == f"K{get_tile}":
                    if any(i in player_tiles.combination_list for i in [f"S{get_tile}", f"S{get_tile+1}", f"S{get_tile-1}"]):
                        continue
                    player_tiles.combination_list.remove(comb)
                    player_tiles.combination_list.append(f"k{comb[1]}{comb[2]}")
                    return


if __name__ == "__main__":
    test_save = [
    ["s37"], [
    11,11,11,
    21, 21, 21, 
    31, 31, 31, 
    39, 39
    ], 31, ["自摸"]]
    way_to_hepai = test_save[3]
    hepai_tile = test_save[2]
    tiles_list = test_save[1]
    combination_list = test_save[0]

    checker = Classical_Hepai_Check(debug=True)
    t0 = time()
    best = checker.hepai_check(tiles_list, combination_list, way_to_hepai, hepai_tile)
    all_patterns = checker.hepai_check_all(tiles_list, combination_list, way_to_hepai, hepai_tile)
    t1 = time()

    print("best_base_fu:", best[0])
    print("best_total_fu:", best[1])
    print("best_fu_fans:", best[2])
    print("best_fans:", best[3])
    print("all_patterns_count:", len(all_patterns))
    for idx, pattern in enumerate(all_patterns):
        print(idx, pattern)
    print("elapsed:", t1 - t0)

