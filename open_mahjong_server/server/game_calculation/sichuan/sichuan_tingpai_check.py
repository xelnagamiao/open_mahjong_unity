"""
四川麻将听牌检查：一般型（4 面子 + 雀头）+ 七对。
只有万(11-19)/饼(21-29)/条(31-39)三门数牌，无字牌。
返回可使其和牌的牌集合（不做定缺过滤，由调用方按需排除定缺花色）。
"""
from typing import List, Set


class SichuanTingTiles:
    def __init__(self, tiles_list: List[int], combination_list: List[str], complete_step: int):
        self.hand_tiles = sorted(tiles_list)
        self.combination_list = combination_list
        self.complete_step = complete_step

    def __deepcopy__(self, memo):
        return SichuanTingTiles(self.hand_tiles[:], self.combination_list[:], self.complete_step)


class Sichuan_Tingpai_Check:
    def __init__(self):
        self.waiting_tiles: Set[int] = set()

    # ------------------------------------------------------------------ 一般型

    def _normal_waits(self, player_tiles: SichuanTingTiles) -> Set[int]:
        result: Set[int] = set()
        if not self._block(player_tiles):
            return result
        all_list = self._traverse_quetou(player_tiles)
        end_list = []
        while all_list:
            temp_list = all_list.pop()
            self._traverse_kezi(temp_list, all_list)
            self._traverse_dazi(temp_list, all_list)
            if temp_list.complete_step >= 11:
                end_list.append(temp_list)

        for i in end_list:
            if len(i.hand_tiles) == 1:
                result.add(i.hand_tiles[0])
            elif len(i.hand_tiles) == 2:
                a, b = i.hand_tiles[0], i.hand_tiles[1]
                if a == b:
                    result.add(a)
                elif a == b - 1:
                    if a - 1 > a // 10 * 10:
                        result.add(a - 1)
                    if a + 2 < (a // 10 * 10) + 10:
                        result.add(a + 2)
                elif a == b - 2:
                    result.add(a + 1)
        return result

    def _block(self, player_tiles: SichuanTingTiles) -> bool:
        if not player_tiles.hand_tiles:
            return True
        block_count = len(player_tiles.combination_list)
        pointer = player_tiles.hand_tiles[0]
        for tile_id in player_tiles.hand_tiles:
            if tile_id == pointer or tile_id == pointer + 1:
                pass
            else:
                block_count += 1
            pointer = tile_id
        return block_count <= 6

    def _traverse_quetou(self, player_tiles: SichuanTingTiles):
        all_list = []
        pointer = 0
        for tile_id in player_tiles.hand_tiles:
            if player_tiles.hand_tiles.count(tile_id) >= 2 and tile_id != pointer:
                temp = player_tiles.__deepcopy__(None)
                temp.hand_tiles.remove(tile_id)
                temp.hand_tiles.remove(tile_id)
                temp.complete_step += 2
                temp.combination_list.append(f"q{tile_id}")
                all_list.append(temp)
                pointer = tile_id
        all_list.append(player_tiles.__deepcopy__(None))
        return all_list

    def _traverse_kezi(self, player_tiles: SichuanTingTiles, all_list):
        same = 0
        for tile_id in player_tiles.hand_tiles:
            if player_tiles.hand_tiles.count(tile_id) >= 3 and tile_id != same:
                temp = player_tiles.__deepcopy__(None)
                temp.hand_tiles.remove(tile_id)
                temp.hand_tiles.remove(tile_id)
                temp.hand_tiles.remove(tile_id)
                temp.complete_step += 3
                temp.combination_list.append(f"k{tile_id}")
                all_list.append(temp)
                same = tile_id

    def _traverse_dazi(self, player_tiles: SichuanTingTiles, all_list):
        same = 0
        for tile_id in player_tiles.hand_tiles:
            if tile_id <= 40:
                if tile_id + 1 in player_tiles.hand_tiles and tile_id + 2 in player_tiles.hand_tiles and tile_id != same:
                    temp = player_tiles.__deepcopy__(None)
                    temp.hand_tiles.remove(tile_id)
                    temp.hand_tiles.remove(tile_id + 1)
                    temp.hand_tiles.remove(tile_id + 2)
                    temp.complete_step += 3
                    temp.combination_list.append(f"s{tile_id + 1}")
                    all_list.append(temp)
                    same = tile_id

    # ------------------------------------------------------------------ 七对

    def _seven_pair_waits(self, hand_tile_list: List[int]) -> Set[int]:
        if len(hand_tile_list) != 13:
            return set()
        counts: dict = {}
        for t in hand_tile_list:
            counts[t] = counts.get(t, 0) + 1
        singles = [t for t, c in counts.items() if c % 2 == 1]
        # 13 张听七对：恰好一张单张，其余全偶数 → 听这张单张
        odd = sum(1 for c in counts.values() if c % 2 == 1)
        if odd == 1 and len(singles) == 1:
            return {singles[0]}
        return set()

    # ------------------------------------------------------------------ 入口

    def tingpai_check(self, hand_tile_list: List[int], combination_list: List[str]) -> Set[int]:
        test = SichuanTingTiles(hand_tile_list.copy(), list(combination_list), len(combination_list) * 3)
        result = self._normal_waits(test)
        if not combination_list:
            result |= self._seven_pair_waits(hand_tile_list)
        result = {i for i in result if i not in {10, 20, 30, 40} and 11 <= i <= 39}
        return result


if __name__ == "__main__":
    checker = Sichuan_Tingpai_Check()
    print("一般听:", sorted(checker.tingpai_check([11, 12, 13, 14, 15, 16, 17, 18, 19, 31, 31, 33, 33], [])))
    print("七对听:", sorted(checker.tingpai_check([11, 11, 13, 13, 15, 15, 17, 17, 19, 19, 31, 31, 33], [])))
    print("副露听:", sorted(checker.tingpai_check([11, 11, 13, 14, 15], ["k31", "k33"])))
