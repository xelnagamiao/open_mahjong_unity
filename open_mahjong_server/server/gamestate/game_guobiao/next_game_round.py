from typing import Dict
from ..public.logic_common import back_current_num

def next_game_round(self):
    """进入下一局游戏"""
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
        i.combination_mask = []
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

