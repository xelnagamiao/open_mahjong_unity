import random
from typing import Dict
from ..public.logic_common import back_current_num
from .random_seed_manager import derive_round_seed

def next_game_round_switchseat(self):
    """进入下一局游戏"""
    # 局数+1
    self.current_round += 1
    self.round_index += 1
    self.current_player_index = 0
    self.xunmu = 1
    self.action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    self.backward_tiles_list_type = "double" # 重置倒序摸牌状态

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

def next_game_round(self):
    """进入下一局游戏"""
    # 局数+1
    self.current_round += 1
    self.round_index += 1 
    self.current_player_index = 0
    self.xunmu = 1
    self.action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    self.backward_tiles_list_type = "double" # 重置倒序摸牌状态

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

    # 创建一个新的排序列表，按player_index从小到大排列
    self.player_list.sort(key=lambda x: x.player_index)



def _next_game_round_with_dealer(
    self,
    keep_current_round: bool = False,
    keep_dealer_seat: bool = False,
    shuffle_on_wind_change: bool = False,
):
    """进入下一局；支持连庄。shuffle_on_wind_change 为 True 时在南/西/北圈首局随机重排座位（青雀）。"""
    if keep_current_round:
        self.round_index += 1
    else:
        self.current_round += 1
        self.round_index += 1
    self.current_player_index = 0
    self.xunmu = 1
    self.action_dict:Dict[int,list] = {0:[],1:[],2:[],3:[]}
    self.backward_tiles_list_type = "double"

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
        if not keep_dealer_seat:
            i.player_index = back_current_num(i.player_index)

    if (not keep_dealer_seat) and shuffle_on_wind_change and self.current_round in (5, 9, 13):
        seed = derive_round_seed(self.master_seed, self.current_round)
        rng = random.Random(seed)
        players = list(self.player_list)
        rng.shuffle(players)
        for idx, p in enumerate(players):
            p.player_index = idx

    self.player_list.sort(key=lambda x: x.player_index)


def next_game_round_classical_switchseat(self, keep_current_round: bool = False, keep_dealer_seat: bool = False):
    """古典麻将：过庄时顺延下家继位；连庄时保持座位与圈位局数不变。"""
    _next_game_round_with_dealer(self, keep_current_round, keep_dealer_seat, shuffle_on_wind_change=False)


def next_game_round_random_switchseat(self, keep_current_round: bool = False, keep_dealer_seat: bool = False):
    """青雀：过庄时顺延下家；南/西/北圈首局随机重排座位。连庄参数同古典。"""
    _next_game_round_with_dealer(self, keep_current_round, keep_dealer_seat, shuffle_on_wind_change=True)


def next_game_round_qingque_switchseat(self):
    """青雀：每局 back 轮转；5/9/13 风圈切换时用 derive_round_seed 随机重排座位。"""
    next_game_round_random_switchseat(self)
