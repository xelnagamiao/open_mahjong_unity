"""
四川麻将（血战到底）和牌检查与计番。

规则（用户竞技版，严格按此实现，勿套用网上常见番数）：
- 仅万(11-19)/饼(21-29)/条(31-39)三门数牌，无字牌花牌。
- 和牌时手牌只能有两种花色（必须缺一门 = 定缺花色不得出现在和牌手牌/副露中）。
- 不允许吃牌，故副露只有 碰(k)/明杠(g)/暗杠(G)/暗刻(K)；和牌拆解里另有顺子(s/S)、雀头(q)。

番种（各为独立加法标记，总番=各番之和）：
  平和            0 番（无其它番种时标记「平和」，基本分固定为 1）
  杠              每个开杠 +1 番（明杠/暗杠/加杠）
  根              每个“4 张相同但未开杠” +1 番
  大对子(对对和)  +1 番
  金钩钓(十三落抬) +1 番，且与大对子叠加
  清一色          +2 番
  七对            +2 番
  杠上花/杠上炮/抢杠/海底  各 +1 番（情境番，由 way_to_hepai 传入）

计分：base = 2 ** min(总番, 3)（1 番→2；2 番→4；≥3 番→8，3 番封顶）；仅平和(0番)时基本分固定为 1。
收支在 GameState 层处理（点炮收 base、自摸每家收 base+1、一炮多响分别收）。
查大叫取“理论最大番”时不计 杠上花/杠上炮/抢杠/海底（见 max_hepai_fan）。
"""
from typing import List, Tuple
from time import time

try:
    from ..classical.classical_solver import ClassicalCombinationSolver, PlayerTiles
except ImportError:  # 直接以脚本运行时的兜底导入
    try:
        from classical.classical_solver import ClassicalCombinationSolver, PlayerTiles  # type: ignore
    except ImportError:
        from game_calculation.classical.classical_solver import ClassicalCombinationSolver, PlayerTiles  # type: ignore


# 情境番 token（与 GameState 拼装的 way_to_hepai 对齐）
_SITUATIONAL_TOKENS = ("杠上花", "杠上炮", "抢杠", "海底")


def sichuan_base_from_fan(fan: int, fan_list: List[str] = None) -> int:
    """基本分 = 2 ** min(番数, 3)；仅平和(0番无其它番种)时基本分固定为 1。"""
    if fan_list is not None and fan_list == ["平和"]:
        return 1
    if fan < 0:
        fan = 0
    return 2 ** min(fan, 3)


class Sichuan_Hepai_Check(ClassicalCombinationSolver):
    def __init__(self, debug: bool = False):
        self.debug = debug

    # ------------------------------------------------------------------ 工具

    def _expand_meld(self, comb: str) -> List[int]:
        """把副露/拆解编码展开为物理牌列表（杠=4 张，刻=3，顺=3，雀头=2）。"""
        if not comb:
            return []
        sign = comb[0]
        try:
            tile = int(comb[1:])
        except ValueError:
            return []
        if sign in ("g", "G"):
            return [tile, tile, tile, tile]
        if sign in ("k", "K"):
            return [tile, tile, tile]
        if sign in ("s", "S"):
            return [tile - 1, tile, tile + 1]
        if sign == "q":
            return [tile, tile]
        return []

    def _expand_all(self, hand_list: List[int], tiles_combination: List[str]) -> List[int]:
        tiles = list(hand_list)
        for comb in tiles_combination:
            tiles.extend(self._expand_meld(comb))
        return tiles

    def _has_dingque(self, tiles: List[int], dingque_suit: int) -> bool:
        if dingque_suit not in (1, 2, 3):
            return False
        return any((t // 10) == dingque_suit for t in tiles)

    def _is_flush(self, tiles: List[int]) -> bool:
        suits = {t // 10 for t in tiles}
        return len(suits) == 1

    def _gen_count(self, hand_list: List[int], tiles_combination: List[str]) -> Tuple[int, int]:
        """返回 (杠数, 根数)。
        杠数 = g/G 副露个数；
        根数 = 物理出现 4 张且不属于杠的牌张数（含龙七对里的四张同牌）。
        """
        kong_count = 0
        kong_tiles = set()
        physical: dict = {}
        for t in hand_list:
            physical[t] = physical.get(t, 0) + 1
        for comb in tiles_combination:
            sign = comb[0]
            for t in self._expand_meld(comb):
                physical[t] = physical.get(t, 0) + 1
            if sign in ("g", "G"):
                kong_count += 1
                try:
                    kong_tiles.add(int(comb[1:]))
                except ValueError:
                    pass
        gen_count = sum(1 for tile, cnt in physical.items() if cnt == 4 and tile not in kong_tiles)
        return kong_count, gen_count

    def _is_seven_pairs(self, hand_list: List[int]) -> bool:
        if len(hand_list) != 14:
            return False
        counts: dict = {}
        for t in hand_list:
            counts[t] = counts.get(t, 0) + 1
        return all(c % 2 == 0 for c in counts.values())

    # ------------------------------------------------------------------ 番种

    def _global_fans(self, all_tiles: List[int], kong_count: int, gen_count: int,
                     way_to_hepai: List[str], include_situational: bool = True
                     ) -> Tuple[int, List[str]]:
        """与具体拆解无关的番种：杠、根、清一色、情境番。"""
        fan = 0
        names: List[str] = []
        for _ in range(kong_count):
            fan += 1
            names.append("杠")
        for _ in range(gen_count):
            fan += 1
            names.append("根")
        if self._is_flush(all_tiles):
            fan += 2
            names.append("清一色")
        if include_situational:
            for token in _SITUATIONAL_TOKENS:
                if token in way_to_hepai:
                    fan += 1
                    names.append(token)
        return fan, names

    def hepai_check(self, hand_list: List[int], tiles_combination: List[str],
                    way_to_hepai: List[str], get_tile: int, dingque_suit: int = 0,
                    include_situational: bool = True) -> Tuple[int, List[str]]:
        """返回 (总番数, 番名列表)。不能和返回 (0, [])。

        hand_list 为闭门手牌（含和牌张），tiles_combination 为副露（碰/杠）。
        dingque_suit ∈ {1:万, 2:饼, 3:条, 0:不校验}。
        """
        all_tiles = self._expand_all(hand_list, tiles_combination)

        if self._has_dingque(all_tiles, dingque_suit):
            return 0, []

        kong_count, gen_count = self._gen_count(hand_list, tiles_combination)
        base_global_fan, base_global_names = self._global_fans(
            all_tiles, kong_count, gen_count, way_to_hepai, include_situational
        )

        best_fan = -1
        best_names: List[str] = []

        # 七对（仅在无副露时成立）
        if not tiles_combination and self._is_seven_pairs(hand_list):
            fan = base_global_fan + 2
            names = base_global_names + ["七对"]
            if fan > best_fan:
                best_fan, best_names = fan, names

        # 一般型（4 面子 + 雀头）
        complete_step = len(tiles_combination) * 3
        player_tiles = PlayerTiles(list(hand_list), list(tiles_combination), complete_step)
        check_done_list: List[PlayerTiles] = []
        self.normal_check(player_tiles, check_done_list)

        is_jingoudiao = len(tiles_combination) == 4 and len(hand_list) == 2  # 4 副露 + 单吊将

        for item in check_done_list:
            seq_count = sum(1 for c in item.combination_list if c and c[0] in ("s", "S"))
            fan = base_global_fan
            names = list(base_global_names)
            if seq_count == 0:  # 全刻/杠 = 大对子（对对和）
                fan += 1
                names.append("大对子")
                if is_jingoudiao:
                    fan += 1
                    names.append("金钩钓")
            if fan > best_fan:
                best_fan, best_names = fan, names

        if best_fan < 0:
            return 0, []

        if best_fan == 0:
            return 0, ["平和"]
        return best_fan, best_names

    # ------------------------------------------------------------------ 查大叫

    def max_hepai_fan(self, hand_tile_list: List[int], combination_list: List[str],
                      dingque_suit: int = 0, tingpai_tiles=None) -> Tuple[int, List[str]]:
        """查大叫用：遍历听牌的所有和牌张，返回理论最大番（不计情境番）。

        返回 (最大番, 对应番名列表)；未听或全为定缺花色返回 (0, [])。
        """
        if tingpai_tiles is None:
            try:
                from .sichuan_tingpai_check import Sichuan_Tingpai_Check
            except ImportError:
                try:
                    from sichuan_tingpai_check import Sichuan_Tingpai_Check  # type: ignore
                except ImportError:
                    from game_calculation.sichuan.sichuan_tingpai_check import Sichuan_Tingpai_Check  # type: ignore
            tingpai_tiles = Sichuan_Tingpai_Check().tingpai_check(hand_tile_list, combination_list)

        best_fan = 0
        best_names: List[str] = []
        for w in tingpai_tiles:
            if dingque_suit in (1, 2, 3) and (w // 10) == dingque_suit:
                continue
            fan, names = self.hepai_check(
                list(hand_tile_list) + [w], list(combination_list), [], w,
                dingque_suit=dingque_suit, include_situational=False,
            )
            if fan > best_fan:
                best_fan, best_names = fan, names
        return best_fan, best_names


if __name__ == "__main__":
    checker = Sichuan_Hepai_Check(debug=True)

    # 平和：清一色否，缺饼(2)
    print("平和:", checker.hepai_check([11, 12, 13, 14, 15, 16, 17, 18, 19, 31, 31], ["k33"], [], 19, dingque_suit=2))
    # 清一色 + 对对和
    print("清对:", checker.hepai_check([11, 11, 11, 13, 13, 13, 15, 15], ["k17", "k19"], [], 15, dingque_suit=2))
    # 七对
    print("七对:", checker.hepai_check([11, 11, 13, 13, 15, 15, 17, 17, 19, 19, 31, 31, 33, 33], [], [], 33, dingque_suit=2))
    # 龙七对（含根）
    print("龙七对:", checker.hepai_check([11, 11, 11, 11, 15, 15, 17, 17, 19, 19, 31, 31, 33, 33], [], [], 33, dingque_suit=2))
    # 金钩钓（4 副露单吊）
    print("金钩钓:", checker.hepai_check([15, 15], ["k11", "k13", "k17", "k19"], [], 15, dingque_suit=2))
    # 花猪（含定缺花色）→ 不能和
    print("花猪:", checker.hepai_check([11, 12, 13, 21, 21, 21, 31, 31, 31, 33, 33], ["k15"], [], 33, dingque_suit=2))
