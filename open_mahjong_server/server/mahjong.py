import random


class chinese_player:
    def __init__(self, player_id: str, player_name: str, tiles: list):
        self.player_id = player_id # 玩家id
        self.player_name = player_name # 玩家名
        self.hand_tiles = tiles # 手牌
        self.discard_tiles = [] # 弃牌 
        self.win_tiles = [] # 胡牌
        self.current_player_index = 0 # 当前玩家索引
    # 摸牌方法
    def get_tile(self, tiles_list):
        element = tiles_list.pop(0)
        self.hand_tiles.append(element)

def current_next(current_index):
    if index == 3:
        current_index = 0
    else:
        current_index += 1

if __name__ == "__main__":
    # 游戏初始化 发送初始化信息
    # 1.玩家开始顺序 2.游戏房间内信息的说明 3.玩家的手牌

    # 1.设置玩家开始顺序
    player1 = chinese_player("1", "玩家1", [])
    player2 = chinese_player("2", "玩家2", [])
    player3 = chinese_player("3", "玩家3", [])
    player4 = chinese_player("4", "玩家4", [])
    # 创建玩家列表,打乱玩家
    player_list = [player1, player2, player3, player4]
    random.shuffle(player_list)
    # 设置玩家开始顺序
    for index,player in enumerate(player_list):
        player.current_player_index = index
        print(player.player_name,player.current_player_index)
    current_player_index = 0 # 0 == 东 1 == 南 2 == 西 3 == 北
    # 3.初始化手牌
    # 标准牌堆
    sth_tiles_set = {
        "11","12","13","14","15","16","17","18","19", # 万
        "21","22","23","24","25","26","27","28","29", # 饼
        "31","32","33","34","35","36","37","38","39", # 条
        "41","42","43","44", # 东南西北
        "45","46","47" # 中白发
    }
    # 花牌牌堆
    hua_tiles_set = {"51","52","53","54","55","56","57","58"} # 春夏秋冬 梅兰竹菊
    # 生成牌堆
    tiles_list = []
    for tile in sth_tiles_set:
        tiles_list.append(tile)
        tiles_list.append(tile)
        tiles_list.append(tile)
        tiles_list.append(tile)
    for tile in hua_tiles_set:
        tiles_list.append(tile)
    # 洗牌
    random.shuffle(tiles_list)
    print(tiles_list)
    print(len(tiles_list))
    # 分配每一位玩家13张牌
    for player in player_list:
        for i in range(13):
            player.get_tile(tiles_list)
    # 打印每一位玩家的手牌
    for player in player_list:
        print(player.player_name, player.hand_tiles)
    # 庄家摸牌
    player_list[current_player_index].get_tile(tiles_list)
    print(player_list[current_player_index].player_name, player_list[current_player_index].hand_tiles)
    print(len(tiles_list))









