from typing import Dict, Set, List
import logging
from ..public.logic_common import get_index_relative_position, next_current_num

logger = logging.getLogger(__name__)

# 九莲宝灯的三种九面听集合
_JIULIAN_SETS = [
    {11, 12, 13, 14, 15, 16, 17, 18, 19},
    {21, 22, 23, 24, 25, 26, 27, 28, 29},
    {31, 32, 33, 34, 35, 36, 37, 38, 39},
]


def _is_menqianqing(combination_tiles: list) -> bool:
    """判断门前清：组合牌中无吃/碰/明杠（仅暗杠G允许）"""
    for c in combination_tiles:
        if c[0] in ('s', 'k', 'g'):
            return False
    return True


def _is_bianjiandiao(waiting_tiles: set) -> bool:
    """判断边嵌吊：仅有1种听牌"""
    return len(waiting_tiles) == 1


def _is_jiulian(waiting_tiles: set) -> bool:
    """判断九莲宝灯：听牌恰好是同花色的1-9共9种"""
    return any(waiting_tiles == s for s in _JIULIAN_SETS)


def check_jiuzhongjiupai(hand_tiles: List[int]) -> bool:
    """
    检查九种九牌：13张起手牌中包含9种或以上的幺九牌（1、9数牌及字牌）。
    """
    if len(hand_tiles) != 13:
        return False
    yaochuu = set()
    for t in hand_tiles:
        if t >= 41:
            yaochuu.add(t)
        elif t % 10 == 1 or t % 10 == 9:
            yaochuu.add(t)
    return len(yaochuu) >= 9


def check_kokushi(hand_tiles: List[int]) -> bool:
    """
    检查国士无双：13张起手牌之间无任何联系。
    条件：无重复牌，且数牌(< 40)同花色内任意两张距离 > 2。
    """
    if len(hand_tiles) != 13:
        return False
    if len(set(hand_tiles)) != 13:
        return False
    suits: Dict[int, list] = {}
    for t in hand_tiles:
        if t < 40:
            suit = t // 10
            suits.setdefault(suit, []).append(t % 10)
    for values in suits.values():
        values.sort()
        for i in range(len(values) - 1):
            if values[i + 1] - values[i] <= 2:
                return False
    return True


# 切牌后检查 存储 吃chi_left chi_mid chi_right 碰peng 杠gang 胡hu 操作
def check_action_after_cut(self, cut_tile):
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}

    if self.tiles_list != []:
        next_player_index = next_current_num(self.current_player_index)
        if cut_tile <= 40:
            if cut_tile - 2 in self.player_list[next_player_index].hand_tiles:
                if cut_tile - 1 in self.player_list[next_player_index].hand_tiles:
                    temp_action_dict[next_player_index].append("chi_left")
            if cut_tile - 1 in self.player_list[next_player_index].hand_tiles:
                if cut_tile + 1 in self.player_list[next_player_index].hand_tiles:
                    temp_action_dict[next_player_index].append("chi_mid")
            if cut_tile + 2 in self.player_list[next_player_index].hand_tiles:
                if cut_tile + 1 in self.player_list[next_player_index].hand_tiles:
                    temp_action_dict[next_player_index].append("chi_right")

        for item in self.player_list:
            if item.hand_tiles.count(cut_tile) >= 2:
                temp_action_dict[item.player_index].append("peng")
                break

        for item in self.player_list:
            if item.hand_tiles.count(cut_tile) == 3:
                if self.tiles_list != []:
                    temp_action_dict[item.player_index].append("gang")
                    break

    for item in self.player_list:
        if cut_tile in item.waiting_tiles and item.player_index != self.current_player_index:
            check_hepai(self, temp_action_dict, cut_tile, item.player_index, "dianhe")

    for i in temp_action_dict:
        if temp_action_dict[i] != []:
            temp_action_dict[i].append("pass")

    temp_action_dict[self.current_player_index] = []

    for item in self.player_list:
        if "peida" in item.tag_list:
            temp_action_dict[item.player_index] = []

    self.dihe_possible = False

    return temp_action_dict


# 加杠检查操作 存储 抢杠(金鸡夺食)
def check_action_jiagang(self, jiagang_tile):
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    for item in self.player_list:
        if jiagang_tile in item.waiting_tiles and item.player_index != self.current_player_index:
            check_hepai(self, temp_action_dict, jiagang_tile, item.player_index, "qianggang")

    for i in temp_action_dict:
        if temp_action_dict[i] != []:
            temp_action_dict[i].append("pass")

    for item in self.player_list:
        if "peida" in item.tag_list:
            temp_action_dict[item.player_index] = []

    return temp_action_dict


# 开局检查补花操作（古典麻将无花牌，保留空实现以兼容）
def check_action_buhua(self, player_index):
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    return temp_action_dict


# 摸牌后检查操作 和牌hu 暗杠angang 加杠jiagang 切牌cut
def check_action_hand_action(self, player_index, is_get_gang_tile=False, is_first_action=False):
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    player_item = self.player_list[player_index]

    if self.tiles_list != []:
        # 暗杠
        processed_cards = set()
        for carditem in player_item.hand_tiles:
            if carditem not in processed_cards and player_item.hand_tiles.count(carditem) == 4:
                if self.tiles_list != []:
                    temp_action_dict[player_index].append("angang")
                    processed_cards.add(carditem)

        # 加杠
        for combination_tile in player_item.combination_tiles:
            if combination_tile[0] == "k":
                jiagang_index = int(combination_tile[1:])
                if jiagang_index in player_item.hand_tiles:
                    if self.tiles_list != []:
                        temp_action_dict[player_index].append("jiagang")

    temp_action_dict[player_index].append("cut")

    if player_item.hand_tiles[-1] in player_item.waiting_tiles:
        check_hepai(self, temp_action_dict, player_item.hand_tiles[-1], player_index, "handgot", is_first_action, is_get_gang_tile)

    if "peida" in player_item.tag_list:
        allowed_actions = {"jiagang", "angang", "cut"}
        temp_action_dict[player_index] = [action for action in temp_action_dict[player_index] if action in allowed_actions]

    return temp_action_dict


# 检查吃碰后切牌操作
def check_only_cut(self, player_index):
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    temp_action_dict[player_index].append("cut")
    return temp_action_dict


# 检查等待牌操作（使用古典麻将听牌检查）
def refresh_waiting_tiles(self, player_index, is_first_action=False):
    player_item = self.player_list[player_index]
    current_player_hand_tiles = player_item.hand_tiles
    if is_first_action:
        current_player_hand_tiles = player_item.hand_tiles[:-1]
    current_player_combination_tiles = player_item.combination_tiles
    current_player_waiting_tiles = self.calculation_service.Classical_tingpai_check(
        current_player_hand_tiles,
        current_player_combination_tiles
    )
    if current_player_waiting_tiles != self.player_list[player_index].waiting_tiles:
        self.player_list[player_index].waiting_tiles = current_player_waiting_tiles
        logger.info(f"玩家{player_index}的等待牌更新为{current_player_waiting_tiles}")


# 检查和牌操作（使用古典麻将和牌检查，返回4元组）
def check_hepai(self, temp_action_dict, hepai_tile, player_index, hepai_type, is_first_action=False, is_get_gang_tile=False):
    if hepai_type == "handgot":
        tiles_list = self.player_list[player_index].hand_tiles.copy()
    else:
        tiles_list = self.player_list[player_index].hand_tiles + [hepai_tile]

    combination_tiles = self.player_list[player_index].combination_tiles
    way_to_hepai = ["和牌"]

    # 门前清
    if _is_menqianqing(combination_tiles):
        way_to_hepai.append("门前清")

    # 边嵌吊
    if _is_bianjiandiao(self.player_list[player_index].waiting_tiles):
        way_to_hepai.append("边嵌吊")

    # 金鸡夺食（抢杠和）
    if hepai_type == "qianggang":
        way_to_hepai.append("金鸡夺食")

    # 荣和 / 地和
    elif hepai_type == "dianhe":
        if getattr(self, 'dihe_possible', False):
            way_to_hepai.append("地和")

    # 自摸 / 天和 / 岭上开花 / 海底捞月
    elif hepai_type == "handgot":
        if is_first_action and self.player_list[player_index].player_index == 0:
            way_to_hepai.append("天和")
        elif is_get_gang_tile:
            way_to_hepai.append("岭上开花")
        else:
            way_to_hepai.append("自摸")
        if not is_first_action and len(self.tiles_list) == 0:
            way_to_hepai.append("海底捞月")

    # 九莲宝灯（九面听）
    if _is_jiulian(self.player_list[player_index].waiting_tiles):
        way_to_hepai.append("九莲宝灯")

    # 门风检查
    if self.player_list[player_index].player_index == 0:
        way_to_hepai.append("门风东")
    elif self.player_list[player_index].player_index == 1:
        way_to_hepai.append("门风南")
    elif self.player_list[player_index].player_index == 2:
        way_to_hepai.append("门风西")
    elif self.player_list[player_index].player_index == 3:
        way_to_hepai.append("门风北")

    # 使用古典麻将和牌检查（返回4元组：base_fu, total_fu, fu_fan_list, fan_list）
    result = self.calculation_service.Classical_hepai_check(tiles_list, combination_tiles, way_to_hepai, hepai_tile)

    if result[1] > 0:
        if get_index_relative_position(self.player_list[player_index].player_index, self.current_player_index) == "self":
            temp_action_dict[self.player_list[player_index].player_index].append("hu_self")
            self.result_dict["hu_self"] = result
        elif get_index_relative_position(self.player_list[player_index].player_index, self.current_player_index) == "left":
            temp_action_dict[self.player_list[player_index].player_index].append("hu_first")
            self.result_dict["hu_first"] = result
        elif get_index_relative_position(self.player_list[player_index].player_index, self.current_player_index) == "top":
            temp_action_dict[self.player_list[player_index].player_index].append("hu_second")
            self.result_dict["hu_second"] = result
        elif get_index_relative_position(self.player_list[player_index].player_index, self.current_player_index) == "right":
            temp_action_dict[self.player_list[player_index].player_index].append("hu_third")
            self.result_dict["hu_third"] = result
