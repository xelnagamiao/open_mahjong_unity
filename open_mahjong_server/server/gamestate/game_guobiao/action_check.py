from typing import Dict
import logging
from ..public.logic_common import get_index_relative_position, next_current_num

logger = logging.getLogger(__name__)

# 检查操作 返回 action_dict

# 切牌后检查 存储 吃chi_left chi_mid chi_right 碰peng 杠gang 胡hu 操作
def check_action_after_cut(self,cut_tile):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}

    # 如果牌堆内仍有牌则可以吃碰杠
    if self.tiles_list != []:
        # 如果切牌是万 饼 条 且下家有C+1和C-1 则可以吃
        next_player_index = next_current_num(self.current_player_index)
        if cut_tile <= 40:
            # left 左侧吃牌 [a-2,a-1,a]
            if cut_tile-2 in self.player_list[next_player_index].hand_tiles:
                if cut_tile-1 in self.player_list[next_player_index].hand_tiles:
                    temp_action_dict[next_player_index].append("chi_left")
            # mid 中间吃牌 [a-1,a,a+1]
            if cut_tile-1 in self.player_list[next_player_index].hand_tiles:
                if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                        temp_action_dict[next_player_index].append("chi_mid")
            # right 右侧吃牌 [a,a+1,a+2]
            if cut_tile+2 in self.player_list[next_player_index].hand_tiles:
                if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                    temp_action_dict[next_player_index].append("chi_right")

        # 如果任意一家有C=2，则可以碰
        for item in self.player_list:
            if item.hand_tiles.count(cut_tile) >= 2:
                temp_action_dict[item.player_index].append("peng")
                break
        
        # 检测杠牌：手牌中有3张相同的牌
        for item in self.player_list:
            if item.hand_tiles.count(cut_tile) == 3:
                if self.tiles_list != []:
                        temp_action_dict[item.player_index].append("gang")
                        break

    # 如果该牌是任意家的等待牌 且不是自己
    for item in self.player_list:
        if cut_tile in item.waiting_tiles and item.player_index != self.current_player_index:
            check_hepai(self,temp_action_dict,cut_tile,item.player_index,"dianhe")

    # 如果玩家有操作 则添加pass
    for i in temp_action_dict:
        if temp_action_dict[i] != []:
            temp_action_dict[i].append("pass")
    
    # 不能吃碰杠胡自己的牌
    temp_action_dict[self.current_player_index] = []

    # 陪打玩家不能完成鸣牌操作
    for item in self.player_list:
        if "peida" in item.tag_list:
            temp_action_dict[item.player_index] = []

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

# 开局检查补花操作 存储 补花buhua
def check_action_buhua(self,player_index):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    if any(carditem >= 50 for carditem in self.player_list[player_index].hand_tiles):
        temp_action_dict[player_index].append("buhua")
        temp_action_dict[player_index].append("pass")
    return temp_action_dict

# 摸牌后检查操作 补花buhua 和牌hu 暗杠angang 加杠jiagang 切牌cut
def check_action_hand_action(self,player_index,is_get_gang_tile=False,is_first_action=False):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    player_item = self.player_list[player_index]

    # 如果牌堆内仍有牌则可以补花暗杠加杠
    if self.tiles_list != []:
        # 如果手牌中有花牌 则可以补花
        if any(carditem >= 50 for carditem in player_item.hand_tiles):
            if self.tiles_list != []:
                temp_action_dict[player_index].append("buhua")

        # 如果手牌中有4张相同的牌 则可以暗杠
        processed_cards = set()
        for carditem in player_item.hand_tiles:
            if carditem not in processed_cards and player_item.hand_tiles.count(carditem) == 4:
                if self.tiles_list != []:
                    temp_action_dict[player_index].append("angang")
                    processed_cards.add(carditem)
                
        # 如果组合牌中有加杠 则可以加杠
        for combination_tile in player_item.combination_tiles:
            if combination_tile[0] == "k":
                jiagang_index = int(combination_tile[1:])  # 提取所有数字
                if jiagang_index in player_item.hand_tiles:
                    if self.tiles_list != []:
                        temp_action_dict[player_index].append("jiagang")

    # 摸牌后可以切牌
    temp_action_dict[player_index].append("cut")

    # 如果手牌中有等待牌 则检测和牌
    if player_item.hand_tiles[-1] in player_item.waiting_tiles:
        check_hepai(self,temp_action_dict,player_item.hand_tiles[-1],player_index,"handgot",is_first_action,is_get_gang_tile)

    # 如果玩家陪打，只允许加杠、暗杠、补花和切牌
    if "peida" in player_item.tag_list:
        allowed_actions = {"jiagang", "angang", "buhua", "cut"}
        temp_action_dict[player_index] = [action for action in temp_action_dict[player_index] if action in allowed_actions]

    return temp_action_dict

# 检查吃碰后切牌操作 存储 吃碰后切牌cut
def check_only_cut(self,player_index):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    temp_action_dict[player_index].append("cut")
    return temp_action_dict

# 检查等待牌操作 用来在玩家手牌发生改变时检测监听的卡牌
def refresh_waiting_tiles(self,player_index,is_first_action=False):
    # 获取GuobiaoPlayer
    player_item = self.player_list[player_index]
    # 获取手牌
    current_player_hand_tiles = player_item.hand_tiles
    if is_first_action:
        current_player_hand_tiles = player_item.hand_tiles[:-1] # 第一轮行动时只计算前13张牌
    # 获取组合牌
    current_player_combination_tiles = player_item.combination_tiles
    # 调用听牌检查（使用计算服务类）
    current_player_waiting_tiles = self.calculation_service.GB_tingpai_check(
        current_player_hand_tiles,
        current_player_combination_tiles
    )
    # 更新等待牌
    if current_player_waiting_tiles != self.player_list[player_index].waiting_tiles:
        self.player_list[player_index].waiting_tiles = current_player_waiting_tiles
        logger.info(f"玩家{player_index}的等待牌更新为{current_player_waiting_tiles}")

# 检查和牌操作
def check_hepai(self,temp_action_dict,hepai_tile,player_index,hepai_type,is_first_action=False,is_get_gang_tile=False):
    # 和牌操作
    tiles_list = self.player_list[player_index].hand_tiles + [hepai_tile]
    combination_tiles = self.player_list[player_index].combination_tiles
    way_to_hepai = ["花牌"] * len(self.player_list[player_index].huapai_list)

    # 抢杠和
    if hepai_type == "qianggang":
        temp_action_dict[player_index].append("抢杠和")

    # 荣和
    elif hepai_type == "dianhe":
        way_to_hepai.append("点和")
        if len(self.tiles_list) == 0:
            way_to_hepai.append("海底捞月")

    # 自摸 岭上开花
    elif hepai_type == "handgot":
        tiles_list = tiles_list[:-1] # 删除最后一张牌
        if is_get_gang_tile:
            way_to_hepai.append("杠上开花")
        else:
            way_to_hepai.append("自摸")
        if len(self.tiles_list) == 0:
            way_to_hepai.append("妙手回春")

    # 获取场风
    if self.current_round <= 4:
        way_to_hepai.append("场风东")
    elif self.current_round <= 8:
        way_to_hepai.append("场风南")
    elif self.current_round <= 12:
        way_to_hepai.append("场风西")
    elif self.current_round <= 16:
        way_to_hepai.append("场风北")
    # 自风检查
    if self.player_list[player_index].player_index == 0:
        way_to_hepai.append("自风东")
    elif self.player_list[player_index].player_index == 1:
        way_to_hepai.append("自风南")
    elif self.player_list[player_index].player_index == 2:
        way_to_hepai.append("自风西")
    elif self.player_list[player_index].player_index == 3:
        way_to_hepai.append("自风北")
    # 和单张检查
    if len(self.player_list[player_index].waiting_tiles) == 1:
        way_to_hepai.append("和单张")

    # 和绝张检查 弃牌+1 有顺子+1 有刻+2
    show_tiles_count = 0
    now_combinations = []
    for i in self.player_list:
        show_tiles_count += i.discard_tiles.count(hepai_tile)
        now_combinations.extend(i.combination_tiles)
    for i in now_combinations:
        if f"k{hepai_tile}" in i:
            show_tiles_count += 2
        if f"s{hepai_tile-1}" in i:
            show_tiles_count += 1
        if f"s{hepai_tile}" in i:
            show_tiles_count += 1
        if f"s{hepai_tile+1}" in i:
            show_tiles_count += 1
    if show_tiles_count == 4:
        way_to_hepai.append("和绝张")
    elif show_tiles_count == 3:
        if "自摸" in way_to_hepai:
            way_to_hepai.append("和绝张")

    # 第一轮行动时移除独听番种
    if is_first_action:
        if "和单张" in way_to_hepai:
            way_to_hepai.remove("和单张")
        elif "和绝张" in way_to_hepai:
            way_to_hepai.remove("和绝张")

    # 使用计算服务类检查和牌
    result = self.calculation_service.GB_hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tile)

   
    # 判断是否足够8番，减去花牌的数量
    huapai_count = way_to_hepai.count("花牌")
    if result[0] - huapai_count >= 8:
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
    else:
        # 如果开启错和，不满足8番的和牌也进行保存(嘻嘻)
        if self.open_cuohe:
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