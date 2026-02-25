from typing import Dict, Any, List
from datetime import date, datetime

"""
# 牌谱格式示例 牌谱记录方法一般在boardcast_do_action方法前执行
# 操作短码: d=摸牌 gd=杠后摸牌 bd=补花后摸牌 bh=补花 c=切牌 ag=暗杠 jg=加杠 cl/cm/cr=吃 p=碰 g=明杠
# 和牌与流局保持全名: hu_self hu_first hu_second hu_third liuju
{
'game_title': 
    {
    'rule': 'guobiao',
    'game_random_seed': 718078453, 
    'max_round': 4, 
    'start_time': datetime.datetime(2025, 11, 27, 4, 23, 19, 681525), 
    'p0_uid': 10000000, 'p0_name': '1', 
    'p1_uid': 10000001, 'p1_name': '2', 
    'p2_uid': 10000002, 'p2_name': '3', 
    'p3_uid': 10000003, 'p3_name': '4', 
    'end_time': datetime.datetime(2025, 11, 27, 7, 18, 21, 660461)}, 
    'game_round': {
        'round_index_1': {
            'round_random_seed': 818914569, 
            'current_round': 1, 
            'tiles_list': [], 
            'round_index': 1, 'action_ticks': [
            ['c', 55, 'T'], ['d', 56], ['c', 56, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 34], ['c', 34, 'F'], 
            ['d', 39], ['c', 39, 'T'], ['d', 46], ['c', 46, 'T'], ['gd', 37], ['c', 37, 'T'],
        }
    }
}
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

# 牌谱记录和牌
def player_action_record_end(self,hu_class: str,hu_score: int,hu_fan: list,hepai_player_index: int):
    self.player_action_tick += 1
    if hu_class in ["hu_self","hu_first","hu_second","hu_third"]:
        self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
            [hu_class,hu_score,hu_fan,hepai_player_index]
        )
    else:
        self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
            ["liuju"]
        )