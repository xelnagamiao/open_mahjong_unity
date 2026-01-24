# 存储逻辑处理
import random
import hashlib
from typing import Dict

# 用于传递自身索引:对方索引 获取自己与对方的相对位置
def get_index_relative_position(self,self_index,other_index):
    if self_index == 0:
        if other_index == 1:
            return "right"
        elif other_index == 2:
            return "top"
        elif other_index == 3:
            return "left"
        elif other_index == 0:
            return "self"
    elif self_index == 1:
        if other_index == 0:
            return "left"
        elif other_index == 2:
            return "right"
        elif other_index == 3:
            return "top"
        elif other_index == 1:
            return "self"
    elif self_index == 2:
        if other_index == 0:
            return "top"
        elif other_index == 1:
            return "left"
        elif other_index == 3:
            return "right"
        elif other_index == 2:
            return "self"
    elif self_index == 3:
        if other_index == 0:
            return "right"
        elif other_index == 1:
            return "top"
        elif other_index == 2:
            return "left"
        elif other_index == 3:
            return "self"

# 获取下一个玩家索引 东 → 南 → 西 → 北 → 东 0 → 1 → 2 → 3 → 0
def next_current_index(self):
    if self.current_player_index == 3:
        self.current_player_index = 0
    else:
        self.current_player_index += 1

def next_current_num(num):
    if num == 3:
        return 0
    else:
        return num + 1

def back_current_num(num):
    if num == 0:
        return 3
    else:
        return num - 1

def init_game_tiles(self):
    # 标准牌堆
    sth_tiles_set = {
        11,12,13,14,15,16,17,18,19, # 万
        21,22,23,24,25,26,27,28,29, # 饼
        31,32,33,34,35,36,37,38,39, # 条
        41,42,43,44, # 东南西北
        45,46,47 # 中白发
    }
    # 花牌牌堆
    hua_tiles_set = {51,52,53,54,55,56,57,58} # 春夏秋冬 梅兰竹菊
    # 生成牌堆
    self.tiles_list = []
    for tile in sth_tiles_set:
        self.tiles_list.extend([tile] * 4)
    self.tiles_list.extend(hua_tiles_set)
    
    # 使用 MD5 哈希函数混合全局种子和局数
    # 公式: MD5(game_random_seed + "_" + current_round) 的前8位十六进制转换为整数，再取模 2^32
    combined = f"{self.game_random_seed}_{self.current_round}".encode('utf-8')
    hash_value = int(hashlib.md5(combined).hexdigest()[:8], 16)
    self.round_random_seed = hash_value % (2**32)
    random.seed(self.round_random_seed)
    random.shuffle(self.tiles_list)

    # 固定起始牌的测试
    if self.Debug:
        # 使用测试牌例
        self.player_list[0].hand_tiles = [11,11,11,12,12,12,13,13,13,14,14,14,15]
        self.player_list[1].hand_tiles = [11,12,13,14,15,21,21,21,22,22,22,23,23]
        self.player_list[2].hand_tiles = []
        self.player_list[3].hand_tiles = []

        # 删除牌山中测试牌例的卡牌
        for tile in self.player_list[0].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[1].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[2].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[3].hand_tiles:
            self.tiles_list.remove(tile)

        self.tiles_list.remove(55)

        # 分配每位玩家13张牌        
        for player in self.player_list:
            if player.hand_tiles == []:
                for _ in range(13):
                    player.get_tile(self.tiles_list)
        
        self.player_list[self.current_player_index].hand_tiles.append(55)

    else:
        # 分配每位玩家13张牌
        for player in self.player_list:
            for _ in range(13):
                player.get_tile(self.tiles_list)
        # 庄家额外摸一张
        self.player_list[0].get_tile(self.tiles_list)

def next_game_round(self):
    # 局数+1
    self.current_round += 1
    self.round_index += 1 
    self.current_player_index = 0
    self.xunmu = 0
    self.action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}

    # 清空花牌弃牌组合牌列表 重置时间
    self.hu_class = None
    for i in self.player_list:
        i.hand_tiles = []
        i.huapai_list = []
        i.discard_tiles = []
        i.waiting_tiles = set()
        i.combination_tiles = []
        i.remaining_time = self.round_time
        if "peida" in i.tag_list:
            i.tag_list.remove("peida")
        i.player_index = back_current_num(i.player_index) # 倒退玩家索引(0→3 1→0 2→1 3→2)

    # 如果需要座位换位
    if self.current_round in [5,9,13]:

        if self.current_round == 5:
            for i in self.player_list:
                if i.original_player_index == 0: # 东起：东[南]北西
                    i.player_index = 1
                elif i.original_player_index == 1: # 南起：南[东]西北
                    i.player_index = 0
                elif i.original_player_index == 2: # 西起：西[北]东南
                    i.player_index = 3
                elif i.original_player_index == 3: # 北起：北[西]南东
                    i.player_index = 2
            
        elif self.current_round == 9:
            for i in self.player_list:
                if i.original_player_index == 0: # 东起：东南[北]西
                    i.player_index = 3
                elif i.original_player_index == 1: # 南起：南东[西]北
                    i.player_index = 2
                elif i.original_player_index == 2: # 西起：西北[东]南
                    i.player_index = 0
                elif i.original_player_index == 3: # 北起：北西[南]东
                    i.player_index = 1
            
        elif self.current_round == 13:
            for i in self.player_list:
                if i.original_player_index == 0: # 东起：东南北[西]
                    i.player_index = 2
                elif i.original_player_index == 1: # 南起：南东西[北]
                    i.player_index = 3
                elif i.original_player_index == 2: # 西起：西北东[南]
                    i.player_index = 1
                elif i.original_player_index == 3: # 北起：北西南[东]
                    i.player_index = 0

    # 创建一个新的排序列表，按player_index从小到大排列
    self.player_list.sort(key=lambda x: x.player_index)