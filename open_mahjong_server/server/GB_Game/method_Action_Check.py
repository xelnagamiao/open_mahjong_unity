from typing import Dict

# 检查操作 返回 action_dict

# 切牌后检查 存储 吃chi_left chi_mid chi_right 碰peng 杠gang 胡hu 操作
def check_action_after_cut(self,cut_tile):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    # 如果切牌是万 饼 条 且下家有C+1和C-1 则可以吃
    next_player_index = self.next_current_num(self.current_player_index)
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

    # 如果任意一家有C=2，则可以碰 如果C=3，则可以杠
    for item in self.player_list:
        if item.hand_tiles.count(cut_tile) == 2:
            temp_action_dict[item.player_index].append("peng")
            if item.hand_tiles.count(cut_tile) == 3:
                if self.tiles_list != []:
                    temp_action_dict[item.player_index].append("gang")

    # 如果该牌是任意家的等待牌
    for item in self.player_list:
        if cut_tile in item.waiting_tiles:
            # 如果满足和牌条件则可以胡
            tiles_list = item.hand_tiles # 手牌列表
            combination_tiles = item.combination_tiles # 组合牌列表
            way_to_hepai = ["点和","花牌"*len(item.huapai_list)]
            # 场风检查
            if self.current_round <= 4:
                way_to_hepai.append("场风东")
            elif self.current_round <= 8:
                way_to_hepai.append("场风南")
            elif self.current_round <= 12:
                way_to_hepai.append("场风西")
            elif self.current_round <= 16:
                way_to_hepai.append("场风北")
            # 自风检查
            if item.player_index == 0:
                way_to_hepai.append("自风东")
            elif item.player_index == 1:
                way_to_hepai.append("自风南")
            elif item.player_index == 2:
                way_to_hepai.append("自风西")
            elif item.player_index == 3:
                way_to_hepai.append("自风北")
            # 和单张检查
            if len(item.waiting_tiles) == 1:
                way_to_hepai.append("和单张")
            # 和绝张检查
            if self.tiles_list.count(cut_tile) == 0:
                way_to_hepai.append("和绝张")
            # 海底捞月特检
            if len(self.tiles_list) == 0:
                way_to_hepai.append("海底捞月")

            hepai_tiles = item.waiting_tiles[cut_tile] # 和牌张
            result = self.Chinese_Hepai_Check.hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tiles)
            if result[0] >= 8:
                if self.get_index_relative_position(self.current_player_index,item.player_index) == "left":
                    temp_action_dict[item.player_index].append("hu_first") # 上家切牌 最高优先级和牌
                    self.result_dict["hu_first"] = result # 保存结算结果
                elif self.get_index_relative_position(self.current_player_index,item.player_index) == "top":
                    temp_action_dict[item.player_index].append("hu_second") # 对家切牌 次高优先级和牌
                    self.result_dict["hu_second"] = result # 保存结算结果
                elif self.get_index_relative_position(self.current_player_index,item.player_index) == "right":
                    temp_action_dict[item.player_index].append("hu_third") # 下家切牌 最低优先级和牌
                    self.result_dict["hu_third"] = result # 保存结算结果

    # 出牌玩家不可对自己的出牌进行操作
    temp_action_dict[self.current_player_index] = []

    # 如果玩家有操作 则添加pass
    for i in temp_action_dict:
        if i != []:
            temp_action_dict[i].append("pass")

    return temp_action_dict

# 加杠检查操作 存储 抢杠
def check_action_jiagang(self,gang_tile):
    # 如果该牌是任意家的等待牌，则可以抢杠和
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    # 如果该牌是任意家的等待牌
    for item in self.player_list:
        if gang_tile in item.waiting_tiles:
            # 如果满足和牌条件则可以胡
            tiles_list = item.hand_tiles # 手牌列表
            combination_tiles = item.combination_tiles # 组合牌列表
            way_to_hepai = ["抢杠和","花牌"*len(item.huapai_list)]
            # 场风检查
            if self.current_round <= 4:
                way_to_hepai.append("场风东")
            elif self.current_round <= 8:
                way_to_hepai.append("场风南")
            elif self.current_round <= 12:
                way_to_hepai.append("场风西")
            elif self.current_round <= 16:
                way_to_hepai.append("场风北")
            # 自风检查
            if item.player_index == 0:
                way_to_hepai.append("自风东")
            elif item.player_index == 1:
                way_to_hepai.append("自风南")
            elif item.player_index == 2:
                way_to_hepai.append("自风西")
            elif item.player_index == 3:
                way_to_hepai.append("自风北")
            # 和单张检查
            if len(item.waiting_tiles) == 1:
                way_to_hepai.append("和单张")
            # 和绝张检查
            if self.tiles_list.count(gang_tile) == 0:
                way_to_hepai.append("和绝张")

            hepai_tiles = item.waiting_tiles[gang_tile] # 和牌张
            result = self.Chinese_Hepai_Check.hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tiles)
            if result[0] >= 8:
                if self.get_index_relative_position(self.current_player_index,item.player_index) == "left":
                    temp_action_dict[item.player_index].append("hu_first") # 上家切牌 最高优先级和牌
                    self.result_dict["hu_first"] = result # 保存结算结果
                elif self.get_index_relative_position(self.current_player_index,item.player_index) == "top":
                    temp_action_dict[item.player_index].append("hu_second") # 对家切牌 次高优先级和牌
                    self.result_dict["hu_second"] = result # 保存结算结果
                elif self.get_index_relative_position(self.current_player_index,item.player_index) == "right":
                    temp_action_dict[item.player_index].append("hu_third") # 下家切牌 最低优先级和牌
                    self.result_dict["hu_third"] = result # 保存结算结果

    # 如果玩家有操作 则添加pass
    for i in temp_action_dict:
        if i != []:
            temp_action_dict[i].append("pass")

    # 不能抢自己的杠
    temp_action_dict[self.current_player_index] = []

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

    # 如果手牌中有花牌 则可以补花
    if any(carditem >= 50 for carditem in player_item.hand_tiles):
        if self.tiles_list != []:
            temp_action_dict[player_index].append("buhua")

    # 如果手牌中有4张相同的牌 则可以暗杠
    for carditem in player_item.hand_tiles:
        if player_item.hand_tiles.count(carditem) == 4:
            if self.tiles_list != []:
                temp_action_dict[player_index].append("angang")

    # 如果组合牌中有加杠 则可以加杠
    for combination_tile in player_item.combination_tiles:
        if combination_tile[0] == "k":
            jiagang_index = int(combination_tile[1:])  # 提取所有数字
            if jiagang_index in player_item.hand_tiles:
                if self.tiles_list != []:
                    temp_action_dict[player_index].append("jiagang")

    # 摸牌后可以切牌
    temp_action_dict[player_index].append("cut")

    # 给每个玩家广播中包含了摸牌行动
    # deal 操作需要广播给所有玩家 因为deal操作是摸牌操作 需要让所有玩家都知道其他人摸牌了
    for i in temp_action_dict:
        temp_action_dict[i].append("deal")

    # 如果手牌中有等待牌 则检测和牌
    if player_item.hand_tiles[-1] in player_item.waiting_tiles:
        temp_action_dict[player_index].append("hu_first")
        tiles_list = player_item.hand_tiles # 手牌列表
        combination_tiles = player_item.combination_tiles # 组合牌列表
        way_to_hepai = ["花牌"*len(player_item.huapai_list)] # 和牌方式
        # 场风检查
        if self.current_round <= 4:
            way_to_hepai.append("场风东")
        elif self.current_round <= 8:
            way_to_hepai.append("场风南")
        elif self.current_round <= 12:
            way_to_hepai.append("场风西")
        elif self.current_round <= 16:
            way_to_hepai.append("场风北")
        # 自风检查
        if player_item.player_index == 0:
            way_to_hepai.append("自风东")
        elif player_item.player_index == 1:
            way_to_hepai.append("自风南")
        elif player_item.player_index == 2:
            way_to_hepai.append("自风西")
        elif player_item.player_index == 3:
            way_to_hepai.append("自风北")
        # 和单张检查
        if len(player_item.waiting_tiles) == 1:
            way_to_hepai.append("和单张")
        # 和绝张检查
        if self.tiles_list.count(player_item.hand_tiles[-1]) == 0:
            way_to_hepai.append("和绝张")
        # 自摸方法特检
        if is_get_gang_tile:
            way_to_hepai.append("杠上开花")
        else:
            way_to_hepai.append("自摸")
        # 妙手回春特检
        if len(self.tiles_list) == 0:
            way_to_hepai.append("妙手回春")
        # 第一轮行动特检 移除独听番种
        if is_first_action:
            if "和单张" in way_to_hepai:
                way_to_hepai.remove("和单张")
            elif "和绝张" in way_to_hepai:
                way_to_hepai.remove("和绝张")
        
        hepai_tiles = player_item.waiting_tiles[player_item.hand_tiles[-1]] # 和牌张
        result = self.Chinese_Hepai_Check.hepai_check(tiles_list,combination_tiles,way_to_hepai,hepai_tiles)

        if result[0] >= 8:
            temp_action_dict[player_index].append("hu_self") # 上家切牌 最高优先级和牌
            self.result_dict["hu_self"] = result # 保存结算结果

    return temp_action_dict

# 检查吃碰后切牌操作 存储 吃碰后切牌cut
def check_only_cut(self,player_index):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    temp_action_dict[player_index].append("cut")
    return temp_action_dict

# 检查等待牌操作 用来在玩家手牌发生改变时检测监听的卡牌
def refresh_waiting_tiles(self,player_index):
    # 获取ChinesePlayer
    player_item = self.player_list[player_index]
    # 获取手牌
    current_player_hand_tiles = player_item.hand_tiles
    # 获取组合牌
    current_player_combination_tiles = player_item.combination_tiles
    # 调用听牌检查
    current_player_waiting_tiles = self.Chinese_Tingpai_Check.tingpai_check(
        current_player_hand_tiles,
        current_player_combination_tiles
    )
    # 更新等待牌
    player_item.waiting_tiles = current_player_waiting_tiles