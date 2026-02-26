from typing import Dict, Any, List
from datetime import date, datetime

"""
# 牌谱格式示例
# 操作短码: d=摸牌 gd=杠后摸牌 bd=补花后摸牌 bh=补花 c=切牌 ag=暗杠 jg=加杠 cl/cm/cr=吃 p=碰 g=明杠




2026-02-26 02:19:02,336 - server.gamestate.game_guobiao.GuobiaoGameState - INFO - 最终游戏记录: {'game_title': {'rule': 'guobiao', 'game_random_seed': 4212994277, 'max_round': 1, 'start_time': datetime.datetime(2026, 2, 26, 2, 14, 54, 814436), 'open_cuohe': False, 'tips': True, 'is_player_set_random_seed': False, 'p0_uid': 9000456, 'p0_name': '游客_5S7KRcUgMzE', 'p1_uid': 9000457, 'p1_name': '游客_XDW96Ovt7oM', 'p2_uid': 10000009, 'p2_name': 'Xelnaga', 'p3_uid': 9000455, 'p3_name': '游客_ASJSgtnUoEs', 'end_time': datetime.datetime(2026, 2, 26, 2, 19, 2, 336092)}, 'game_round': {'round_index_1': {'round_random_seed': 4068050480, 'current_round': 1, 'p0_tiles': [53, 37, 31, 41, 18, 21, 39, 39, 27, 46, 35, 44, 42, 44], 'p1_tiles': [43, 23, 54, 16, 46, 51, 31, 41, 11, 28, 26, 12, 19], 'p2_tiles': [13, 12, 38, 14, 44, 17, 26, 23, 42, 36, 36, 24, 32], 'p3_tiles': [28, 23, 34, 18, 29, 38, 29, 32, 47, 19, 13, 12, 31], 'tiles_list': [43, 34, 46, 27, 56, 12, 14, 35, 41, 47, 11, 38, 17, 58, 35, 28, 33, 37, 32, 17, 37, 36, 24, 26, 39, 16, 13, 43, 27, 24, 34, 16, 44, 38, 11, 39, 37, 14, 23, 25, 35, 31, 22, 45, 19, 33, 21, 14, 46, 45, 57, 11, 18, 27, 16, 28, 15, 33, 21, 36, 42, 15, 22, 47, 45, 22, 45, 43, 15, 26, 41, 55, 25, 22, 33, 18, 24, 17, 29, 32, 21, 47, 13, 42, 25, 34, 29, 25, 52, 19, 15], 'round_index': 1, 'action_ticks': [['bh', 53, 0], ['bd', 43], ['bh', 54, 1], ['bd', 34], ['bh', 51, 1], ['bd', 46], ['c', 46, 'F'], ['d', 27], ['c', 27, 'T'], ['d', 56], ['bh', 56, 2], ['bd', 19], ['c', 44, 'F'], ['d', 12], ['c', 12, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 41], ['c', 42, 'F'], ['d', 47], ['c', 47, 'T'], ['d', 11], ['c', 11, 'T'], ['d', 38], ['c', 38, 'T'], ['d', 17], ['c', 19, 'F'], ['d', 58], ['bh', 58, 3], ['bd', 15], ['c', 15, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 28], ['c', 28, 'T'], ['d', 33], ['c', 41, 'F'], ['d', 37], ['c', 37, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 17], ['c', 17, 'T'], ['p', 17, 2], ['c', 26, 'F'], ['d', 37], ['c', 37, 'T'], ['d', 36], ['c', 36, 'T'], ['p', 36, 2], ['c', 38, 'F'], ['d', 24], ['c', 24, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 24], ['c', 23, 'F'], ['d', 34], ['c', 34, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 44], ['c', 44, 'T'], ['d', 38], ['c', 38, 'T'], ['d', 11], ['c', 11, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 31], ['c', 31, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 33], ['c', 33, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 46], ['c', 46, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 57], ['bh', 57, 3], ['bd', 25], ['c', 25, 'T'], ['d', 11], ['c', 11, 'T'], ['d', 18], ['c', 18, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 28], ['c', 28, 'T'], ['d', 15], ['c', 15, 'T'], ['cl', 15, 2], ['c', 12, 'F'], ['d', 33], ['c', 33, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 36], ['c', 36, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 47], ['c', 47, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 41], ['c', 41, 'T'], ['d', 55], ['bh', 55, 1], ['bd', 52], ['c', 52, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 33], ['c', 33, 'T'], ['d', 18], ['c', 18, 'T'], ['d', 24], ['c', 32, 'F'], ['d', 17], ['c', 17, 'T'], ['d', 29], ['c', 29, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 21], ['c', 33, 'F'], ['d', 47], ['c', 47, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 25], ['c', 21, 'F'], ['d', 34], ['c', 34, 'T'], ['d', 29], ['c', 29, 'T'], ['liuju'], ['end']]}, 'round_index_2': {'round_random_seed': 1752449488, 'current_round': 2, 'p0_tiles': [27, 26, 44, 38, 17, 28, 35, 45, 22, 42, 24, 22, 11, 25], 'p1_tiles': [37, 34, 29, 25, 11, 35, 21, 57, 43, 47, 35, 11, 18], 'p2_tiles': [18, 41, 22, 22, 17, 52, 47, 46, 45, 44, 29, 36, 14], 'p3_tiles': [32, 12, 13, 15, 36, 28, 25, 15, 47, 31, 38, 12, 58], 'tiles_list': [41, 38, 16, 29, 37, 18, 15, 16, 12, 11, 24, 25, 21, 12, 13, 42, 19, 36, 33, 41, 26, 26, 33, 56, 46, 54, 28, 37, 23, 43, 23, 43, 44, 31, 45, 24, 27, 23, 17, 17, 39, 39, 39, 39, 36, 31, 24, 32, 53, 51, 43, 21, 33, 46, 34, 21, 42, 35, 55, 14, 27, 14, 32, 34, 16, 15, 16, 47, 41, 26, 42, 14, 34, 44, 18, 32, 13, 45, 19, 23, 37, 33, 19, 27, 29, 28, 31, 38, 13, 19, 46], 'round_index': 2, 'action_ticks': [['bh', 57, 1], ['bd', 41], ['bh', 52, 2], ['bd', 38], ['bh', 58, 3], ['bd', 16], ['c', 25, 'F'], ['d', 29], ['c', 43, 'F'], ['d', 37], ['c', 37, 'T'], ['d', 18], ['c', 18, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 16], ['c', 41, 'F'], ['d', 12], ['c', 12, 'T'], ['d', 11], ['c', 11, 'T'], ['p', 11, 1], ['c', 47, 'F'], ['d', 24], ['c', 24, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 12], ['c', 21, 'F'], ['d', 13], ['c', 13, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 36], ['c', 12, 'F'], ['d', 33], ['c', 33, 'T'], ['d', 41], ['c', 41, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 26], ['c', 25, 'F'], ['d', 33], ['c', 33, 'T'], ['d', 56], ['bh', 56, 3], ['bd', 19], ['c', 19, 'T'], ['d', 46], ['c', 46, 'T'], ['d', 54], ['bh', 54, 1], ['bd', 46], ['c', 46, 'T'], ['d', 28], ['c', 28, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 44], ['c', 44, 'T'], ['d', 31], ['c', 31, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 24], ['c', 24, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 17], ['c', 17, 'T'], ['d', 17], ['c', 17, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 36], ['c', 36, 'T'], ['cl', 36, 1], ['c', 26, 'F'], ['d', 31], ['c', 31, 'T'], ['d', 24], ['c', 24, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 53], ['bh', 53, 1], ['bd', 38], ['c', 18, 'F'], ['d', 51], ['bh', 51, 2], ['bd', 13], ['c', 13, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 33], ['c', 16, 'F'], ['d', 46], ['c', 46, 'T'], ['d', 34], ['c', 34, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 55], ['bh', 55, 3], ['bd', 28], ['c', 28, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 34], ['c', 34, 'T'], ['cm', 34, 1], ['c', 29, 'F'], ['d', 16], ['c', 16, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 47], ['c', 29, 'F'], ['d', 41], ['c', 41, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 14], ['c', 47, 'F'], ['d', 34], ['c', 34, 'T'], ['d', 44], ['c', 44, 'T'], ['d', 18], ['c', 18, 'T'], ['d', 32], ['c', 14, 'F'], ['d', 13], ['c', 13, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 23], ['c', 32, 'F'], ['d', 37], ['c', 37, 'T'], ['d', 33], ['c', 33, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 29], ['c', 29, 'T'], ['d', 31], ['c', 31, 'T'], ['liuju'], ['end']]}, 'round_index_3': {'round_random_seed': 1794697739, 'current_round': 3, 'p0_tiles': [47, 33, 39, 39, 44, 43, 11, 47, 23, 51, 44, 17, 39, 19], 'p1_tiles': [38, 21, 46, 17, 55, 27, 45, 46, 24, 17, 25, 41, 44], 'p2_tiles': [41, 36, 46, 44, 31, 36, 35, 33, 22, 26, 32, 37, 35], 'p3_tiles': [12, 28, 27, 23, 37, 21, 21, 42, 41, 11, 23, 45, 58], 'tiles_list': [24, 23, 32, 32, 25, 12, 22, 34, 37, 14, 21, 22, 34, 13, 22, 15, 26, 42, 15, 16, 17, 33, 32, 14, 13, 45, 31, 14, 18, 38, 11, 39, 29, 36, 42, 43, 25, 26, 18, 29, 29, 53, 52, 15, 28, 38, 29, 56, 45, 14, 31, 35, 19, 47, 31, 38, 16, 19, 28, 42, 34, 47, 28, 12, 18, 24, 57, 11, 15, 16, 43, 26, 34, 35, 13, 16, 18, 46, 25, 27, 33, 19, 41, 27, 13, 12, 43, 24, 37, 36, 54], 'round_index': 3, 'action_ticks': [['bh', 51, 0], ['bd', 24], ['bh', 55, 1], ['bd', 23], ['bh', 58, 3], ['bd', 32], ['c', 11, 'F'], ['d', 32], ['c', 44, 'F'], ['p', 44, 0], ['c', 43, 'F'], ['d', 25], ['c', 25, 'T'], ['d', 12], ['c', 12, 'T'], ['d', 22], ['c', 22, 'T'], ['cr', 22, 0], ['c', 33, 'F'], ['d', 34], ['c', 34, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 34], ['c', 34, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 17], ['c', 17, 'T'], ['d', 33], ['c', 33, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 31], ['c', 31, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 18], ['c', 18, 'T'], ['hu_second', 0, 10, ['五门齐', '嵌张', '幺九刻*2', '花牌*1'], [-18, -8, 34, -8]], ['end']]}, 'round_index_4': {'round_random_seed': 635524321, 'current_round': 4, 'p0_tiles': [11, 22, 11, 29, 16, 33, 32, 43, 11, 56, 57, 13, 24, 47], 'p1_tiles': [32, 24, 42, 38, 25, 46, 17, 36, 27, 37, 11, 26, 19], 'p2_tiles': [31, 26, 16, 44, 31, 21, 35, 21, 34, 39, 45, 45, 52], 'p3_tiles': [19, 39, 27, 33, 28, 32, 34, 46, 35, 13, 58, 37, 18], 'tiles_list': [28, 44, 31, 14, 23, 22, 26, 15, 44, 28, 42, 16, 36, 23, 42, 41, 41, 29, 15, 25, 18, 17, 17, 37, 34, 38, 36, 24, 27, 37, 35, 23, 25, 53, 14, 24, 21, 12, 13, 15, 36, 51, 32, 16, 46, 47, 19, 55, 18, 44, 41, 42, 38, 54, 15, 31, 39, 43, 43, 12, 46, 39, 35, 41, 17, 25, 33, 14, 22, 12, 38, 28, 18, 34, 21, 27, 12, 29, 43, 14, 29, 47, 45, 47, 19, 23, 22, 26, 33, 45, 13], 'round_index': 4, 'action_ticks': [['bh', 57, 0], ['bd', 28], ['bh', 56, 0], ['bd', 44], ['bh', 52, 2], ['bd', 31], ['bh', 58, 3], ['bd', 14], ['c', 43, 'F'], ['d', 23], ['c', 42, 'F'], ['d', 22], ['c', 44, 'F'], ['d', 26], ['c', 46, 'F'], ['d', 15], ['c', 15, 'T'], ['d', 44], ['c', 44, 'T'], ['d', 28], ['c', 28, 'T'], ['cl', 28, 3], ['c', 39, 'F'], ['d', 42], ['c', 42, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 36], ['c', 36, 'T'], ['cm', 36, 3], ['c', 28, 'F'], ['d', 23], ['c', 23, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 41], ['c', 41, 'T'], ['d', 41], ['c', 41, 'T'], ['d', 29], ['c', 29, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 18], ['c', 19, 'F'], ['d', 17], ['c', 17, 'T'], ['d', 17], ['c', 17, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 34], ['c', 34, 'T'], ['d', 38], ['c', 38, 'T'], ['d', 36], ['c', 36, 'T'], ['d', 24], ['c', 24, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 53], ['bh', 53, 0], ['bd', 45], ['c', 45, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 24], ['c', 24, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 12], ['c', 12, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 15], ['c', 15, 'T'], ['cl', 15, 3], ['c', 18, 'F'], ['d', 36], ['c', 36, 'T'], ['d', 51], ['bh', 51, 1], ['bd', 13], ['c', 13, 'T'], ['d', 32], ['c', 32, 'T'], ['cr', 32, 3], ['c', 32, 'F'], ['d', 16], ['c', 16, 'T'], ['d', 46], ['c', 46, 'T'], ['d', 47], ['c', 47, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 55], ['bh', 55, 0], ['bd', 26], ['c', 26, 'T'], ['d', 18], ['c', 18, 'T'], ['hu_second', 3, 12, ['全求人', '断幺', '平和', '连六*1', '花牌*1'], [-20, -8, 36, -8]], ['end']]}}}






# 和牌: [hu_class, hepai_idx, hu_score, hu_fan[], [p0Δ,p1Δ,p2Δ,p3Δ]]（错和无 end，游戏继续）
# 流局: ["liuju"]
# 回合结束: ["end"]（和牌或流局之后紧跟，错和除外）
"""
# 牌谱记录游戏头
def init_game_record(self):
    self.game_record["game_title"] = {
        "rule":self.room_type, # 规则
        "game_random_seed":self.game_random_seed, # 随机种子
        "max_round":self.max_round, # 最大局数
        "start_time": datetime.now(), # 开始时间
        "open_cuohe":self.open_cuohe, # 是否开启错和
        "tips":self.tips, # 是否开启提示
        "is_player_set_random_seed":self.isPlayerSetRandomSeed, # 是否玩家设置了随机种子
        "p0_uid":self.player_list[0].user_id,
        "p0_name":self.player_list[0].username,
        "p1_uid":self.player_list[1].user_id,
        "p1_name":self.player_list[1].username,
        "p2_uid":self.player_list[2].user_id,
        "p2_name":self.player_list[2].username,
        "p3_uid":self.player_list[3].user_id,
        "p3_name":self.player_list[3].username,
    }
    self.game_record["game_round"] = {}

def end_game_record(self):
    # 记录对局结束时间
    self.game_record["game_title"]["end_time"] = datetime.now()

# 牌谱记录对局头
def init_game_round(self):
    self.player_action_tick = 0
    self.game_record["game_round"][f"round_index_{self.round_index}"] = {
        "round_random_seed": self.round_random_seed,
        "current_round": self.current_round,
        "p0_tiles": self.player_list[0].hand_tiles.copy(),  # 记录初始手牌
        "p1_tiles": self.player_list[1].hand_tiles.copy(),
        "p2_tiles": self.player_list[2].hand_tiles.copy(),
        "p3_tiles": self.player_list[3].hand_tiles.copy(),
        "tiles_list": self.tiles_list.copy(),  # 记录初始牌堆（打乱后、分配手牌前的完整牌堆）
        "round_index": self.round_index,
        "action_ticks": []
    }

# 牌谱记录补花
def player_action_record_buhua(self,max_tile: int,action_player: int):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["bh",max_tile,action_player]
    )

# 牌谱记录摸牌 deal_type: "d" 普通摸牌, "gd" 杠后摸牌, "bd" 补花后摸牌
def player_action_record_deal(self, deal_tile: int, deal_type: str = "d"):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        [deal_type, deal_tile]
    )

# 牌谱记录切牌
def player_action_record_cut(self, cut_tile: int,is_moqie: bool = False):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["c", cut_tile, "T" if is_moqie else "F"]
    )

# 牌谱记录暗杠
def player_action_record_angang(self,angang_tile: int):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["ag",angang_tile]
    )

# 牌谱记录加杠
def player_action_record_jiagang(self,jiagang_tile: int):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["jg",jiagang_tile]
    )

# 游戏逻辑动作名 → 牌谱短码
_ACTION_TO_RECORD = {
    "chi_left": "cl", "chi_mid": "cm", "chi_right": "cr",
    "peng": "p", "gang": "g",
}

# 牌谱记录吃碰杠牌
def player_action_record_chipenggang(self,action_type: str,mingpai_tile: int,action_player: int):
    self.player_action_tick += 1
    record_code = _ACTION_TO_RECORD.get(action_type, action_type)
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        [record_code,mingpai_tile,action_player]
    )

# 牌谱记录和牌 [hu_class, hepai_player_index, hu_score, hu_fan, score_changes]
def player_action_record_hu(self, hu_class: str, hu_score, hu_fan: list,
                            hepai_player_index: int, score_changes: List[int]):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        [hu_class, hepai_player_index, hu_score, hu_fan, score_changes]
    )

# 牌谱记录流局 ["liuju"]
def player_action_record_liuju(self):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["liuju"]
    )

# 牌谱记录回合结束标记 ["end"]
def player_action_record_round_end(self):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["end"]
    )