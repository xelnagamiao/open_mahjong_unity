from typing import Dict
import logging
from ..public.logic_common import get_index_relative_position, next_current_num

logger = logging.getLogger(__name__)

DEFAULT_ACTION_PRIORITY = {
    "hu_self": 6, "hu_first": 5, "hu_second": 4, "hu_third": 3,
    "peng": 2, "gang": 2,
    "chi_left": 1, "chi_mid": 1, "chi_right": 1,
    "pass": 0,
}

# 检查操作 返回 action_dict

def _is_number_tile(tile: int) -> bool:
    suit = tile // 10
    rank = tile % 10
    return suit in (1, 2, 3) and 1 <= rank <= 9


def _is_same_suit_sequence(tiles) -> bool:
    if not all(_is_number_tile(tile) for tile in tiles):
        return False
    return len({tile // 10 for tile in tiles}) == 1


def _has_tiles(hand_tiles, needed_tiles) -> bool:
    remaining = list(hand_tiles)
    for tile in needed_tiles:
        if tile not in remaining:
            return False
        remaining.remove(tile)
    return True


def _append_chi_actions(self, temp_action_dict: Dict[int, list], cut_tile: int) -> None:
    chi_player_index = next_current_num(self.current_player_index)
    chi_player = self.player_list[chi_player_index]
    if getattr(chi_player, "open_kong_locked", False):
        return
    chi_shapes = (
        ("chi_left", (cut_tile - 2, cut_tile - 1, cut_tile), (cut_tile - 2, cut_tile - 1)),
        ("chi_mid", (cut_tile - 1, cut_tile, cut_tile + 1), (cut_tile - 1, cut_tile + 1)),
        ("chi_right", (cut_tile, cut_tile + 1, cut_tile + 2), (cut_tile + 1, cut_tile + 2)),
    )
    for action, sequence, needed in chi_shapes:
        if _is_same_suit_sequence(sequence) and _has_tiles(chi_player.hand_tiles, needed):
            temp_action_dict[chi_player_index].append(action)


def _is_ready_for_open_kong_claim(self, player_item, tile: int) -> bool:
    checker = getattr(getattr(self, "calculation_service", None), "Changsha_tingpai_check", None)
    if checker is None:
        return False
    remaining = list(player_item.hand_tiles)
    for _ in range(3):
        if tile not in remaining:
            return False
        remaining.remove(tile)
    melds = list(player_item.combination_tiles) + [f"g{tile}"]
    return bool(checker(remaining, melds))

def _append_unique(actions: list, action: str) -> None:
    if action not in actions:
        actions.append(action)

# 切牌后检查 存储 吃chi 碰peng 杠gang 胡hu 操作
def check_action_after_cut(self,cut_tile):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}

    # 如果牌堆内仍有牌则可以碰杠
    if self.tiles_list != []:
        _append_chi_actions(self, temp_action_dict, cut_tile)

        # 所有符合条件的玩家都要被询问，不能只取第一家。
        for item in self.player_list:
            if (
                item.player_index != self.current_player_index
                and not getattr(item, "open_kong_locked", False)
                and item.hand_tiles.count(cut_tile) >= 2
            ):
                temp_action_dict[item.player_index].append("peng")

        # 检测杠牌：手牌中有3张相同的牌
        for item in self.player_list:
            if (
                item.player_index != self.current_player_index
                and not getattr(item, "open_kong_locked", False)
                and item.hand_tiles.count(cut_tile) == 3
            ):
                if self.tiles_list != [] and _is_ready_for_open_kong_claim(self, item, cut_tile):
                        temp_action_dict[item.player_index].append("gang")

    # 如果该牌是任意家的等待牌 且不是自己
    for item in self.player_list:
        if item.player_index != self.current_player_index:
            refresh_waiting_tiles(self, item.player_index)
        if cut_tile in item.waiting_tiles and item.player_index != self.current_player_index:
            check_hepai(self,temp_action_dict,cut_tile,item.player_index,"dianhe")

    # 如果玩家有操作 则添加pass
    for i in temp_action_dict:
        if temp_action_dict[i] != []:
            temp_action_dict[i].append("pass")

    # 不能碰杠胡自己的牌
    temp_action_dict[self.current_player_index] = []

    # 陪打玩家不能完成鸣牌操作
    for item in self.player_list:
        if "peida" in item.tag_list:
            temp_action_dict[item.player_index] = []

    # 地和仅在庄家首次切牌时有效，切牌检查完毕后关闭
    self.dihe_possible = False

    return temp_action_dict

# 加杠检查操作 存储 抢杠
def check_action_jiagang(self,jiagang_tile):
    # 如果该牌是任意家的等待牌，则可以抢杠和
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    # 如果该牌是任意家的等待牌 且不是自己
    for item in self.player_list:
        if jiagang_tile in item.waiting_tiles and item.player_index != self.current_player_index:
            check_hepai(self,temp_action_dict,jiagang_tile,item.player_index,"qianggang")

    # 如果玩家有操作 则添加pass
    for i in temp_action_dict:
        if temp_action_dict[i] != []:
            temp_action_dict[i].append("pass")

    # 陪打玩家不能抢杠操作
    for item in self.player_list:
        if "peida" in item.tag_list:
            temp_action_dict[item.player_index] = []

    return temp_action_dict

# 长沙麻将没有花牌，保留空实现供主循环兼容。
def check_action_buhua(self,player_index):
    return {0:[],1:[],2:[],3:[]}

def _append_self_kong_actions(self, temp_action_dict: Dict[int, list], player_index: int) -> None:
    player_item = self.player_list[player_index]
    # 如果牌堆内仍有牌则可以暗杠加杠
    if self.tiles_list != []:
        # 如果手牌中有4张相同的牌 则可以暗杠
        processed_cards = set()
        for carditem in player_item.hand_tiles:
            if carditem not in processed_cards and player_item.hand_tiles.count(carditem) == 4:
                if self.tiles_list != []:
                    _append_unique(temp_action_dict[player_index], "buzhang")
                    if hasattr(self, "_is_open_kong_ready_after_declared") and self._is_open_kong_ready_after_declared(player_item, carditem):
                        _append_unique(temp_action_dict[player_index], "angang")
                    processed_cards.add(carditem)

        # 如果组合牌中有加杠 则可以加杠
        for combination_tile in player_item.combination_tiles:
            if combination_tile[0] == "k":
                jiagang_index = int(combination_tile[1:])  # 提取所有数字
                if jiagang_index in player_item.hand_tiles:
                    if self.tiles_list != []:
                        _append_unique(temp_action_dict[player_index], "buzhang")
                        if hasattr(self, "_is_open_kong_ready_after_declared") and self._is_open_kong_ready_after_declared(player_item, jiagang_index):
                            _append_unique(temp_action_dict[player_index], "jiagang")


# 摸牌后检查操作 和牌hu 暗杠angang 加杠jiagang 切牌cut
def check_action_hand_action(self,player_index,is_get_gang_tile=False,is_first_action=False):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    player_item = self.player_list[player_index]

    _append_self_kong_actions(self, temp_action_dict, player_index)

    # 摸牌后可以切牌
    temp_action_dict[player_index].append("cut")

    # 如果手牌中有等待牌 则检测和牌
    if player_item.hand_tiles[-1] in player_item.waiting_tiles:
        check_hepai(self,temp_action_dict,player_item.hand_tiles[-1],player_index,"handgot",is_first_action,is_get_gang_tile)

    # 如果玩家陪打，只允许加杠、暗杠和切牌
    if "peida" in player_item.tag_list:
        allowed_actions = {"buzhang", "jiagang", "angang", "cut"}
        temp_action_dict[player_index] = [action for action in temp_action_dict[player_index] if action in allowed_actions]

    return temp_action_dict

# 检查吃碰后手牌操作：长沙吃碰后仍可立即补张/开杠，否则切牌。
def check_only_cut(self, player_index):
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    player_item = self.player_list[player_index]

    _append_self_kong_actions(self, temp_action_dict, player_index)
    temp_action_dict[player_index].append("cut")

    if "peida" in player_item.tag_list:
        temp_action_dict[player_index] = ["cut"]

    return temp_action_dict

def _max_claim_priority(self, action_dict):
    action_priority = getattr(self, "action_priority", DEFAULT_ACTION_PRIORITY)
    max_priority = 0
    for actions in action_dict.values():
        for action in actions:
            if action == "pass":
                continue
            max_priority = max(max_priority, action_priority.get(action, 0))
    return max_priority


def _filter_action_dict_to_priority(self, action_dict, priority):
    action_priority = getattr(self, "action_priority", DEFAULT_ACTION_PRIORITY)
    filtered: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    for player_index, actions in action_dict.items():
        kept = [
            action for action in actions
            if action != "pass" and action_priority.get(action, 0) == priority
        ]
        if kept:
            kept.append("pass")
        filtered[player_index] = kept
    filtered[self.current_player_index] = []
    return filtered


def check_action_after_gang_forced_cut(self, cut_tile):
    return check_action_after_batch_gang_forced_cut(self, [cut_tile])


def filter_open_kong_replacement_actions(actions: list) -> list:
    """开杠补牌阶段只保留自摸和牌，禁止改动原手牌。"""
    return ["hu_self"] if actions and "hu_self" in actions else []


def check_action_after_batch_gang_forced_cut(self, cut_tiles):
    original_result_dict = dict(getattr(self, "result_dict", {}))
    best_tile = None
    best_priority = 0

    for cut_tile in cut_tiles:
        self.result_dict = dict(original_result_dict)
        candidate_actions = check_action_after_cut(self, cut_tile)
        candidate_priority = _max_claim_priority(self, candidate_actions)
        if candidate_priority > best_priority:
            best_priority = candidate_priority
            best_tile = cut_tile

    self.result_dict = dict(original_result_dict)
    self.current_claim_cut_tile = best_tile
    if best_tile is None or best_priority <= 0:
        return {0: [], 1: [], 2: [], 3: []}

    final_actions = check_action_after_cut(self, best_tile)
    return _filter_action_dict_to_priority(self, final_actions, best_priority)

# 检查等待牌操作 用来在玩家手牌发生改变时检测监听的卡牌
def refresh_waiting_tiles(self,player_index,is_first_action=False):
    # 获取 ChangshaPlayer
    player_item = self.player_list[player_index]
    # 获取手牌
    current_player_hand_tiles = player_item.hand_tiles
    if is_first_action:
        current_player_hand_tiles = player_item.hand_tiles[:-1] # 第一轮行动时只计算前13张牌
    # 获取组合牌
    current_player_combination_tiles = player_item.combination_tiles
    # 调用听牌检查（使用计算服务类）
    current_player_waiting_tiles = self.calculation_service.Changsha_tingpai_check(
        current_player_hand_tiles,
        current_player_combination_tiles
    )
    # 更新等待牌
    if current_player_waiting_tiles != self.player_list[player_index].waiting_tiles:
        self.player_list[player_index].waiting_tiles = current_player_waiting_tiles
        logger.info(f"玩家{player_index}的等待牌更新为{current_player_waiting_tiles}")

# 检查和牌操作
def check_hepai(self,temp_action_dict,hepai_tile,player_index,hepai_type,is_first_action=False,is_get_gang_tile=False):
    # 构建手牌列表：自摸时手牌已包含和牌；点和/抢杠时需添加
    if hepai_type == "handgot":
        tiles_list = self.player_list[player_index].hand_tiles.copy()
    else:
        tiles_list = self.player_list[player_index].hand_tiles + [hepai_tile]

    combination_tiles = self.player_list[player_index].combination_tiles
    way_to_hepai = []

    # 抢杠和
    if hepai_type == "qianggang":
        way_to_hepai.append("抢杠")

    # 荣和 / 地和 / 河底捞鱼
    elif hepai_type == "dianhe":
        if getattr(self, 'dihe_possible', False):
            way_to_hepai.append("地和")
        if getattr(self, 'last_draw_was_gang', False):
            way_to_hepai.append("杠上炮")
        if len(self.tiles_list) == 0:
            way_to_hepai.append("河底捞鱼")

    # 自摸 / 天和 / 岭上开花 / 海底捞月
    elif hepai_type == "handgot":
        if is_first_action and self.player_list[player_index].player_index == 0:
            way_to_hepai.append("天和")
        elif is_get_gang_tile:
            way_to_hepai.append("杠上开花")
        else:
            way_to_hepai.append("自摸")
        if not is_first_action and len(self.tiles_list) == 0:
            way_to_hepai.append("海底捞月")

    # 自风检查
    if self.player_list[player_index].player_index == 0:
        way_to_hepai.append("自风东")
    elif self.player_list[player_index].player_index == 1:
        way_to_hepai.append("自风南")
    elif self.player_list[player_index].player_index == 2:
        way_to_hepai.append("自风西")
    elif self.player_list[player_index].player_index == 3:
        way_to_hepai.append("自风北")

    # 使用计算服务类检查和牌（长沙版本）
    result = self.calculation_service.Changsha_hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tile)


    if result[0] >= 1:
        passed_base = getattr(self, "player_passed_hu_base", {}).get(player_index)
        if hepai_type != "handgot" and passed_base is not None and result[0] <= passed_base:
            logger.info(
                f"玩家{player_index}已过同等或更低胡牌，跳过和牌提示 result={result[0]} passed={passed_base}"
            )
            return
        if get_index_relative_position(self.player_list[player_index].player_index, self.current_player_index) == "self":
            temp_action_dict[self.player_list[player_index].player_index].append("hu_self") # 自己切牌 最高优先级和牌
            self.result_dict["hu_self"] = result # 保存结算结果
        elif get_index_relative_position(self.player_list[player_index].player_index, self.current_player_index) == "left":
            temp_action_dict[self.player_list[player_index].player_index].append("hu_first") # 上家切牌 最高优先级和牌
            self.result_dict["hu_first"] = result # 保存结算结果
        elif get_index_relative_position(self.player_list[player_index].player_index, self.current_player_index) == "top":
            temp_action_dict[self.player_list[player_index].player_index].append("hu_second") # 对家切牌 次高优先级和牌
            self.result_dict["hu_second"] = result # 保存结算结果
        elif get_index_relative_position(self.player_list[player_index].player_index, self.current_player_index) == "right":
            temp_action_dict[self.player_list[player_index].player_index].append("hu_third") # 下家切牌 最低优先级和牌
            self.result_dict["hu_third"] = result # 保存结算结果


# 检查切牌后碰杠胡操作
