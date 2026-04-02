from typing import Dict, List, Tuple


class PlayerTiles:
    def __init__(self, tiles_list, combination_list, complete_step):
        self.hand_tiles = sorted(tiles_list)
        self.combination_list = combination_list
        self.complete_step = complete_step  # +3 +3 +3 +3 +2 = 14
        self.fan_list = []
        self.point_count_dict = {}
        self.fan_count_list = []
        self.combination_str = ""
        self.hand_tiles_mapped = []

    def __deepcopy__(self, memo):
        new_instance = PlayerTiles(
            self.hand_tiles[:],
            self.combination_list[:],
            self.complete_step,
        )
        new_instance.fan_list = self.fan_list[:]
        return new_instance


class ClassicalCombinationSolver:
    """
    负责古典麻将的一般型组合解算（面子+雀头）与组合映射。
    """

    combination_to_tiles_dict: Dict[str, List[int]] = {
        "s12": [11, 12, 13], "s13": [12, 13, 14], "s14": [13, 14, 15], "s15": [14, 15, 16], "s16": [15, 16, 17], "s17": [16, 17, 18], "s18": [17, 18, 19],
        "s22": [21, 22, 23], "s23": [22, 23, 24], "s24": [23, 24, 25], "s25": [24, 25, 26], "s26": [25, 26, 27], "s27": [26, 27, 28], "s28": [27, 28, 29],
        "s32": [31, 32, 33], "s33": [32, 33, 34], "s34": [33, 34, 35], "s35": [34, 35, 36], "s36": [35, 36, 37], "s37": [36, 37, 38], "s38": [37, 38, 39],
        "S12": [11, 12, 13], "S13": [12, 13, 14], "S14": [13, 14, 15], "S15": [14, 15, 16], "S16": [15, 16, 17], "S17": [16, 17, 18], "S18": [17, 18, 19],
        "S22": [21, 22, 23], "S23": [22, 23, 24], "S24": [23, 24, 25], "S25": [24, 25, 26], "S26": [25, 26, 27], "S27": [26, 27, 28], "S28": [27, 28, 29],
        "S32": [31, 32, 33], "S33": [32, 33, 34], "S34": [33, 34, 35], "S35": [34, 35, 36], "S36": [35, 36, 37], "S37": [36, 37, 38], "S38": [37, 38, 39],
        "k11": [11, 11, 11], "k12": [12, 12, 12], "k13": [13, 13, 13], "k14": [14, 14, 14], "k15": [15, 15, 15], "k16": [16, 16, 16], "k17": [17, 17, 17], "k18": [18, 18, 18], "k19": [19, 19, 19],
        "k21": [21, 21, 21], "k22": [22, 22, 22], "k23": [23, 23, 23], "k24": [24, 24, 24], "k25": [25, 25, 25], "k26": [26, 26, 26], "k27": [27, 27, 27], "k28": [28, 28, 28], "k29": [29, 29, 29],
        "k31": [31, 31, 31], "k32": [32, 32, 32], "k33": [33, 33, 33], "k34": [34, 34, 34], "k35": [35, 35, 35], "k36": [36, 36, 36], "k37": [37, 37, 37], "k38": [38, 38, 38], "k39": [39, 39, 39],
        "k41": [41, 41, 41], "k42": [42, 42, 42], "k43": [43, 43, 43], "k44": [44, 44, 44], "k45": [45, 45, 45], "k46": [46, 46, 46], "k47": [47, 47, 47],
        "K11": [11, 11, 11], "K12": [12, 12, 12], "K13": [13, 13, 13], "K14": [14, 14, 14], "K15": [15, 15, 15], "K16": [16, 16, 16], "K17": [17, 17, 17], "K18": [18, 18, 18], "K19": [19, 19, 19],
        "K21": [21, 21, 21], "K22": [22, 22, 22], "K23": [23, 23, 23], "K24": [24, 24, 24], "K25": [25, 25, 25], "K26": [26, 26, 26], "K27": [27, 27, 27], "K28": [28, 28, 28], "K29": [29, 29, 29],
        "K31": [31, 31, 31], "K32": [32, 32, 32], "K33": [33, 33, 33], "K34": [34, 34, 34], "K35": [35, 35, 35], "K36": [36, 36, 36], "K37": [37, 37, 37], "K38": [38, 38, 38], "K39": [39, 39, 39],
        "K41": [41, 41, 41], "K42": [42, 42, 42], "K43": [43, 43, 43], "K44": [44, 44, 44], "K45": [45, 45, 45], "K46": [46, 46, 46], "K47": [47, 47, 47],
        "q11": [11, 11], "q12": [12, 12], "q13": [13, 13], "q14": [14, 14], "q15": [15, 15], "q16": [16, 16], "q17": [17, 17], "q18": [18, 18], "q19": [19, 19],
        "q21": [21, 21], "q22": [22, 22], "q23": [23, 23], "q24": [24, 24], "q25": [25, 25], "q26": [26, 26], "q27": [27, 27], "q28": [28, 28], "q29": [29, 29],
        "q31": [31, 31], "q32": [32, 32], "q33": [33, 33], "q34": [34, 34], "q35": [35, 35], "q36": [36, 36], "q37": [37, 37], "q38": [38, 38], "q39": [39, 39],
        "q41": [41, 41], "q42": [42, 42], "q43": [43, 43], "q44": [44, 44], "q45": [45, 45], "q46": [46, 46], "q47": [47, 47],
        "g11": [11, 11, 11], "g12": [12, 12, 12], "g13": [13, 13, 13], "g14": [14, 14, 14], "g15": [15, 15, 15], "g16": [16, 16, 16], "g17": [17, 17, 17], "g18": [18, 18, 18], "g19": [19, 19, 19],
        "g21": [21, 21, 21], "g22": [22, 22, 22], "g23": [23, 23, 23], "g24": [24, 24, 24], "g25": [25, 25, 25], "g26": [26, 26, 26], "g27": [27, 27, 27], "g28": [28, 28, 28], "g29": [29, 29, 29],
        "g31": [31, 31, 31], "g32": [32, 32, 32], "g33": [33, 33, 33], "g34": [34, 34, 34], "g35": [35, 35, 35], "g36": [36, 36, 36], "g37": [37, 37, 37], "g38": [38, 38, 38], "g39": [39, 39, 39],
        "g41": [41, 41, 41], "g42": [42, 42, 42], "g43": [43, 43, 43], "g44": [44, 44, 44], "g45": [45, 45, 45], "g46": [46, 46, 46], "g47": [47, 47, 47],
        "G11": [11, 11, 11], "G12": [12, 12, 12], "G13": [13, 13, 13], "G14": [14, 14, 14], "G15": [15, 15, 15], "G16": [16, 16, 16], "G17": [17, 17, 17], "G18": [18, 18, 18], "G19": [19, 19, 19],
        "G21": [21, 21, 21], "G22": [22, 22, 22], "G23": [23, 23, 23], "G24": [24, 24, 24], "G25": [25, 25, 25], "G26": [26, 26, 26], "G27": [27, 27, 27], "G28": [28, 28, 28], "G29": [29, 29, 29],
        "G31": [31, 31, 31], "G32": [32, 32, 32], "G33": [33, 33, 33], "G34": [34, 34, 34], "G35": [35, 35, 35], "G36": [36, 36, 36], "G37": [37, 37, 37], "G38": [38, 38, 38], "G39": [39, 39, 39],
        "G41": [41, 41, 41], "G42": [42, 42, 42], "G43": [43, 43, 43], "G44": [44, 44, 44], "G45": [45, 45, 45], "G46": [46, 46, 46], "G47": [47, 47, 47],
    }

    def build_hand_and_combination_mapping(self, player_tiles: PlayerTiles):
        # 将组合编码还原为完整手牌映射，供计分层复用。
        hand_tiles_list = []
        combination_str = ""
        for c in player_tiles.combination_list:
            if c in self.combination_to_tiles_dict:
                hand_tiles_list.extend(self.combination_to_tiles_dict[c])
            combination_str += c
        hand_tiles_list.sort()
        return hand_tiles_list, combination_str

    def normal_check(self, player_tiles: PlayerTiles, check_done_list: List[PlayerTiles]):
        # 纯一般型（4面子+1雀头）搜索，不处理特殊牌型。
        if player_tiles.complete_step == 14:
            check_done_list.append(player_tiles)
            return
        elif player_tiles.complete_step == 0:
            if not self.normal_check_block(player_tiles):
                return

        all_list = self.normal_check_traverse_quetou(player_tiles)
        end_list = []

        while all_list:
            temp_list = all_list.pop()
            self.normal_check_traverse_kezi(temp_list, all_list)
            self.normal_check_traverse_dazi(temp_list, all_list)
            if temp_list.complete_step == 14:
                end_list.append(temp_list)

        combination_class = None
        temp_unique = []
        for i in end_list:
            i.combination_list.sort()
            if i.combination_list != combination_class:
                combination_class = i.combination_list
                temp_unique.append(i)

        check_done_list.extend(temp_unique)

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
                temp_list.combination_list.append(f"K{tile_id}")
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
                    temp_list.combination_list.append(f"S{tile_id + 1}")
                    all_list.append(temp_list)
                    same_tile_id = tile_id

