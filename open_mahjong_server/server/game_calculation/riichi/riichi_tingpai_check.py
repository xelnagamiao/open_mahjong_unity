"""
立直麻将听牌检查：一般型（借自古典）+ 七对子 + 国士无双
- 赤 5 (105/205/305) 先归一化为 15/25/35 再进行形状匹配
"""
from typing import List, Set
from time import time


def _normalize_tile(t: int) -> int:
    if t == 105:
        return 15
    if t == 205:
        return 25
    if t == 305:
        return 35
    return t


class PlayerTiles:
    def __init__(self, tiles_list: List[int], combination_list: List[str], complete_step: int):
        self.hand_tiles = sorted(tiles_list)
        self.combination_list = combination_list
        self.complete_step = complete_step

    def __deepcopy__(self, memo):
        return PlayerTiles(self.hand_tiles[:], self.combination_list[:], self.complete_step)


class Riichi_Tingpai_Check:
    yaojiu = {11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47}

    def __init__(self):
        self.waiting_tiles: Set[int] = set()

    def check_waiting_tiles(self, player_tiles: PlayerTiles):
        self.waiting_tiles.clear()

        if len(player_tiles.hand_tiles) == 13 and not player_tiles.combination_list:
            self.GS_check(player_tiles.hand_tiles)
            self.QD_check(player_tiles.hand_tiles)

        self.normal_check(player_tiles)
        return self.waiting_tiles

    def GS_check(self, hand_tiles: List[int]):
        GS_step_set = set()
        GS_allowed = True
        for tile_id in hand_tiles:
            if tile_id in self.yaojiu:
                GS_step_set.add(tile_id)
            else:
                GS_allowed = False
        if GS_allowed:
            if len(GS_step_set) == 12:
                for i in self.yaojiu:
                    if i not in hand_tiles:
                        self.waiting_tiles.add(i)
            elif len(GS_step_set) == 13:
                for i in self.yaojiu:
                    self.waiting_tiles.add(i)

    def QD_check(self, hand_tiles: List[int]):
        tile_counts = {}
        for tile_id in hand_tiles:
            tile_counts[tile_id] = tile_counts.get(tile_id, 0) + 1

        single = 0
        waiting_tile = None
        for tile_id, count in tile_counts.items():
            if count == 1 or count == 3:
                single += 1
                waiting_tile = tile_id
            if single >= 2:
                return
        if single == 1:
            self.waiting_tiles.add(waiting_tile)

    def normal_check(self, player_tiles: PlayerTiles):
        if not self.normal_check_block(player_tiles):
            return

        all_list = self.normal_check_traverse_quetou(player_tiles)
        end_list = []
        while all_list:
            temp_list = all_list.pop()
            self.normal_check_traverse_kezi(temp_list, all_list)
            self.normal_check_traverse_dazi(temp_list, all_list)
            if temp_list.complete_step >= 11:
                end_list.append(temp_list)

        for i in end_list:
            if len(i.hand_tiles) == 1:
                self.waiting_tiles.add(i.hand_tiles[0])
            elif len(i.hand_tiles) == 2:
                if i.hand_tiles[0] == i.hand_tiles[1]:
                    self.waiting_tiles.add(i.hand_tiles[0])
                elif i.hand_tiles[0] == i.hand_tiles[1] - 1:
                    if i.hand_tiles[0] - 1 < 40:
                        self.waiting_tiles.add(i.hand_tiles[0] - 1)
                    if i.hand_tiles[0] + 2 < 40:
                        self.waiting_tiles.add(i.hand_tiles[0] + 2)
                elif i.hand_tiles[0] == i.hand_tiles[1] - 2:
                    if i.hand_tiles[0] + 1 < 40:
                        self.waiting_tiles.add(i.hand_tiles[0] + 1)

    def normal_check_block(self, player_tiles: PlayerTiles):
        if not player_tiles.hand_tiles:
            return True
        block_count = len(player_tiles.combination_list)
        tile_id_pointer = player_tiles.hand_tiles[0]
        for tile_id in player_tiles.hand_tiles:
            if tile_id == tile_id_pointer or tile_id == tile_id_pointer + 1:
                pass
            else:
                block_count += 1
            tile_id_pointer = tile_id
        return block_count <= 6

    def normal_check_traverse_quetou(self, player_tiles: PlayerTiles):
        all_list = []
        quetou_id_pointer = 0
        for tile_id in player_tiles.hand_tiles:
            if player_tiles.hand_tiles.count(tile_id) >= 2 and tile_id != quetou_id_pointer:
                temp_list = player_tiles.__deepcopy__(None)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.complete_step += 2
                temp_list.combination_list.append(f"q{tile_id}")
                all_list.append(temp_list)
                quetou_id_pointer = tile_id
        all_list.append(player_tiles.__deepcopy__(None))
        return all_list

    def normal_check_traverse_kezi(self, player_tiles: PlayerTiles, all_list):
        same_tile_id = 0
        for tile_id in player_tiles.hand_tiles:
            if player_tiles.hand_tiles.count(tile_id) >= 3 and tile_id != same_tile_id:
                temp_list = player_tiles.__deepcopy__(None)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.hand_tiles.remove(tile_id)
                temp_list.complete_step += 3
                temp_list.combination_list.append(f"k{tile_id}")
                all_list.append(temp_list)
                same_tile_id = tile_id

    def normal_check_traverse_dazi(self, player_tiles: PlayerTiles, all_list):
        same_tile_id = 0
        for tile_id in player_tiles.hand_tiles:
            if tile_id <= 40:
                if tile_id + 1 in player_tiles.hand_tiles and tile_id + 2 in player_tiles.hand_tiles and tile_id != same_tile_id:
                    temp_list = player_tiles.__deepcopy__(None)
                    temp_list.hand_tiles.remove(tile_id)
                    temp_list.hand_tiles.remove(tile_id + 1)
                    temp_list.hand_tiles.remove(tile_id + 2)
                    temp_list.complete_step += 3
                    temp_list.combination_list.append(f"s{tile_id + 1}")
                    all_list.append(temp_list)
                    same_tile_id = tile_id

    def tingpai_check(self, hand_tile_list: List[int], combination_list: List[str]) -> Set[int]:
        normalized = [_normalize_tile(t) for t in hand_tile_list]
        test_tiles = PlayerTiles(normalized, combination_list.copy(), len(combination_list) * 3)
        self.check_waiting_tiles(test_tiles)
        self.waiting_tiles = {i for i in self.waiting_tiles if i not in {10, 20, 30, 40}}
        return self.waiting_tiles.copy()


if __name__ == "__main__":
    test_hand_tiles = [11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47]
    checker = Riichi_Tingpai_Check()
    t0 = time()
    result = checker.tingpai_check(test_hand_tiles, [])
    t1 = time()
    print("Riichi Tingpai Check")
    print("手牌:", test_hand_tiles)
    print("听牌:", sorted(result))
    print("耗时:", t1 - t0)
