from typing import Dict, Any, List
from datetime import date, datetime

"""
# 牌谱格式示例 牌谱记录方法一般在boardcast_do_action方法前执行
game_record = {
    "game_title": {
        "rule": "GB",
        "game_random_seed": 3702716784,
        "tiles_list": [],
        "player0_user_id": 10000000,
        "player0_username": "1",
        "player1_user_id": 10000001,
        "player1_username": "2",
        "player2_user_id": 10000002,
        "player2_username": "3",
        "player3_user_id": 10000003,
        "player3_username": "4"
    },
    "game_round": {
        "round_index_1": {
            "round_random_seed": 454974849,
            "current_round": 1,
            "round_index": 1,
            "action_ticks": {},
            "1": {
                "action_type": "buhua",
                "buhua_tile": 55
            },
            "2": {
                "action_type": "deal",
                "deal_tile": 34
            },
            "3": {
                "action_type": "buhua",
                "buhua_tile": 57
            },
            "4": {
                "action_type": "deal",
                "deal_tile": 35
            },
            "5": {
                "action_type": "buhua",
                "buhua_tile": 56
            },
            "6": {
                "action_type": "deal",
                "deal_tile": 52
            },
            "7": {
                "action_type": "buhua",
                "buhua_tile": 52
            },
            "8": {
                "action_type": "deal",
                "deal_tile": 32
            },
            "9": {
                "action_type": "buhua",
                "buhua_tile": 54
            },
            "10": {
                "action_type": "deal",
                "deal_tile": 36
            },
            "11": {
                "action_type": "cut",
                "cut_tile": 34,
                "is_moqie": False
            },
            "12": {
                "action_type": "deal",
                "deal_tile": 31
            },
            "13": {
                "action_type": "cut",
                "cut_tile": 13,
                "is_moqie": False
            },
            "14": {
                "action_type": "end",
                "hu_class": "hu_third",
                "hu_score": None,
                "hu_fan": None,
                "hepai_player_index": None
            }
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
        "player0_user_id":self.player_list[0].user_id,
        "player0_username":self.player_list[0].username,
        "player1_user_id":self.player_list[1].user_id,
        "player1_username":self.player_list[1].username,
        "player2_user_id":self.player_list[2].user_id,
        "player2_username":self.player_list[2].username,
        "player3_user_id":self.player_list[3].user_id,
        "player3_username":self.player_list[3].username,
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
        "tiles_list": self.tiles_list,
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