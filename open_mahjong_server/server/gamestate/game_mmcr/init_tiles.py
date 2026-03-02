# 青雀麻将牌堆初始化
import random
import hashlib


def init_qingque_tiles(self):
    """初始化青雀麻将牌堆（每人13张，庄家第14张在游戏循环中单独发）"""
    # 标准牌堆（无花牌）
    sth_tiles_set = {
        11, 12, 13, 14, 15, 16, 17, 18, 19,  # 万
        21, 22, 23, 24, 25, 26, 27, 28, 29,  # 饼
        31, 32, 33, 34, 35, 36, 37, 38, 39,  # 条
        41, 42, 43, 44,                      # 东南西北
        45, 46, 47,                          # 中白发
    }
    # 生成牌堆
    self.tiles_list = []
    for tile in sth_tiles_set:
        self.tiles_list.extend([tile] * 4)

    # 生成本局随机种子并洗牌
    _shuffle_and_deal_qingque(self)


def _shuffle_and_deal_qingque(self) -> None:
    """青雀：种子生成、洗牌、发牌（每人13张，不额外发牌）"""
    combined = f"{self.game_random_seed}_{self.current_round}".encode("utf-8")
    hash_value = int(hashlib.md5(combined).hexdigest()[:8], 16)
    self.round_random_seed = hash_value % (2**32)
    random.seed(self.round_random_seed)
    random.shuffle(self.tiles_list)

    debug_mode = getattr(self, 'Debug', False)
    if debug_mode:
        # 青雀调试用牌例
        self.player_list[0].hand_tiles = [19,19,47,47,39]
        self.player_list[0].combination_tiles = ["k41","k26","k13"]
        self.player_list[0].combination_mask = [[1,41,1,41,1,41],[0,26,0,26,0,26],[1,13,1,13,1,13]]
        self.player_list[1].hand_tiles = []
        self.player_list[2].hand_tiles = []
        self.player_list[3].hand_tiles = []

        for tile in self.player_list[0].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[1].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[2].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[3].hand_tiles:
            self.tiles_list.remove(tile)

        for player in self.player_list:
            if not player.hand_tiles:
                for _ in range(13):
                    player.get_tile(self.tiles_list)
    else:
        # 分配每位玩家13张牌（青雀不在此处给庄家额外发牌）
        for player in self.player_list:
            for _ in range(13):
                player.get_tile(self.tiles_list)
