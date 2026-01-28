# 国标麻将游戏逻辑处理
import random
import hashlib
from typing import Dict
from ..public.logic_common import back_current_num

def init_game_tiles(self):
    """初始化国标麻将牌堆"""
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
        self.player_list[0].hand_tiles = [11,11,12,12,12,12,13,13,13,14,14,14,15]
        self.player_list[1].hand_tiles = [11,11,13,14,15,21,21,21,22,22,22,23,23]
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
