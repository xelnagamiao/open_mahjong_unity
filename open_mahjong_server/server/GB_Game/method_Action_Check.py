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
                if next_player_index not in temp_action_dict:
                    temp_action_dict[next_player_index] = []
                temp_action_dict[next_player_index].append("chi_left")
        # mid 中间吃牌 [a-1,a,a+1]
    if cut_tile-1 in self.player_list[next_player_index].hand_tiles:
        if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                if next_player_index not in temp_action_dict:
                    temp_action_dict[next_player_index] = []
                temp_action_dict[next_player_index].append("chi_mid")
        # right 右侧吃牌 [a,a+1,a+2]
        if cut_tile+2 in self.player_list[next_player_index].hand_tiles:
            if cut_tile+1 in self.player_list[next_player_index].hand_tiles:
                if next_player_index not in temp_action_dict:
                    temp_action_dict[next_player_index] = []
                temp_action_dict[next_player_index].append("chi_right")

    # 如果任意一家有C=2，则可以碰 如果C=3，则可以杠
    for item in self.player_list:
        if item.hand_tiles.count(cut_tile) == 2:
            if item.current_player_index not in temp_action_dict:
                temp_action_dict[item.current_player_index] = []
            temp_action_dict[item.current_player_index].append("peng")
            if item.hand_tiles.count(cut_tile) == 3:
                temp_action_dict[item.current_player_index].append("gang")

    # 如果该牌是任意家的等待牌，则可以胡
    for item in self.player_list:
        if cut_tile in item.waiting_tiles:
            if item.current_player_index not in temp_action_dict:
                temp_action_dict[item.current_player_index] = []
            temp_action_dict[item.current_player_index].append("hu")
    # 出牌玩家不可对自己的出牌进行操作
    temp_action_dict[self.current_player_index] = []
    return temp_action_dict

# 加杠后检查操作 存储 抢杠和hu
def check_action_after_jiagang(self,gang_tile):
    # 如果该牌是任意家的等待牌，则可以抢杠和
    temp_action_dict = {}
    for item in self.player_list:
        if gang_tile in item.waiting_tiles:
            if item.current_player_index not in temp_action_dict:
                temp_action_dict[item.current_player_index] = []
            temp_action_dict[item.current_player_index].append("hu")
    return temp_action_dict

# 开局检查补花操作 存储 补花buhua
def check_action_buhua(self,player_index):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    if any(carditem >= 50 for carditem in self.player_list[player_index].hand_tiles):
        temp_action_dict[player_index].append("buhua")
    return temp_action_dict

# 摸牌后检查操作 补花buhua 和牌hu 暗杠angang 加杠jiagang 切牌cut
def check_action_hand_action(self,player_index):
    temp_action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    if any(carditem >= 50 for carditem in self.player_list[player_index].hand_tiles):
        temp_action_dict[player_index].append("buhua")
    if self.player_list[player_index].hand_tiles[-1] in self.player_list[player_index].waiting_tiles:
        temp_action_dict[player_index].append("hu")
    if any(self.player_list[player_index].hand_tiles.count(carditem) == 4 for carditem in self.player_list[player_index].hand_tiles):
        temp_action_dict[player_index].append("angang")
    for combination_tile in self.player_list[player_index].combination_tiles:
        if combination_tile[0] == "k":
            jiagang_index = int(combination_tile[1:])  # 提取所有数字
            if jiagang_index in self.player_list[player_index].hand_tiles:
                temp_action_dict[player_index].append("jiagang")
    temp_action_dict[player_index].append("cut")
    # 给每个玩家广播这包含了一个摸牌行动
    # deal 操作需要广播给所有玩家 因为deal操作是摸牌操作 需要让所有玩家都知道其他人摸牌了
    for i in temp_action_dict:
        temp_action_dict[i].append("deal")
    return temp_action_dict
