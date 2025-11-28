from typing import Dict, Any, List
from datetime import date, datetime

"""
# 牌谱格式示例 牌谱记录方法一般在boardcast_do_action方法前执行
{
'game_title': 
    {
    'rule': 'GB',
    'game_random_seed': 718078453, 
    'max_round': 4, 
    'start_time': datetime.datetime(2025, 11, 27, 4, 23, 19, 681525), 
    'player0_user_id': 10000000, 
    'player0_username': '1', 
    'player1_user_id': 10000001, 
    'player1_username': '2', 
    'player2_user_id': 10000002, 
    'player2_username': '3', 
    'player3_user_id': 10000003, 
    'player3_username': '4', 
    'end_time': datetime.datetime(2025, 11, 27, 7, 18, 21, 660461)}, 
    'game_round': {
        'round_index_1': {
            'round_random_seed': 818914569, 
            'current_round': 1, 
            'tiles_list': [], 
            'round_index': 1, 'action_ticks': [
            ['cut', 55, True], ['deal', 56], ['cut', 56, True], ['deal', 15], ['cut', 15, True], ['deal', 34], ['cut', 34, True], 
            ['deal', 39], ['cut', 39, True], ['deal', 46], ['cut', 46, True], ['deal', 37], ['cut', 37, True], ['deal', 32], ['cut', 32, True], 
            ['deal', 44], ['cut', 44, True],
        }
    }
}
"""
# 牌谱记录游戏头
def init_game_record(self):
    self.game_record["game_title"] = {
        "rule":"GB",
        "game_random_seed":self.game_random_seed,
        "max_round":self.max_round,
        "start_time": datetime.now(),
        "p0_user_id":self.player_list[0].user_id,
        "p0_username":self.player_list[0].username,
        "p1_user_id":self.player_list[1].user_id,
        "p1_username":self.player_list[1].username,
        "p2_user_id":self.player_list[2].user_id,
        "p2_username":self.player_list[2].username,
        "p3_user_id":self.player_list[3].user_id,
        "p3_username":self.player_list[3].username,
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
def player_action_record_buhua(self,max_tile: int):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["buhua",max_tile]
    )

# 牌谱记录摸牌
def player_action_record_deal(self,deal_tile: int):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["deal",deal_tile]
    )

# 牌谱记录切牌
def player_action_record_cut(self,
    cut_tile: int,
    is_moqie: bool = False):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["cut",cut_tile,is_moqie]
    )

# 牌谱记录暗杠
def player_action_record_angang(self,angang_tile: int):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["angang",angang_tile]
    )

# 牌谱记录加杠
def player_action_record_jiagang(self,jiagang_tile: int):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["jiagang",jiagang_tile]
    )

# 牌谱记录吃碰杠牌
def player_action_record_chipenggang(self,mingpai_tile: int,action_player: int):
    self.player_action_tick += 1
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["chipenggang",mingpai_tile,action_player]
    )

# 牌谱记录增加巡目
def player_action_record_nextxunmu(self):
    self.game_record["game_round"][f"round_index_{self.round_index}"]["action_ticks"].append(
        ["Next"]
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