# 四川麻将牌堆初始化（108 张：万/饼/条 三门数牌，每种 4 张，无字牌花牌）
import random
from ..public.random_seed_manager import derive_round_seed


def init_sichuan_tiles(self):
    """初始化四川麻将牌堆并发牌：每人 13 张，庄家额外的第 14 张由主循环摸取。"""
    sth_tiles_set = [
        11, 12, 13, 14, 15, 16, 17, 18, 19,  # 万
        21, 22, 23, 24, 25, 26, 27, 28, 29,  # 饼
        31, 32, 33, 34, 35, 36, 37, 38, 39,  # 条
    ]
    self.tiles_list = []
    for tile in sth_tiles_set:
        self.tiles_list.extend([tile] * 4)

    self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
    random.seed(self.round_random_seed)
    random.shuffle(self.tiles_list)

    if getattr(self, 'Debug', False):
        for player in self.player_list:
            for _ in range(13):
                player.get_tile(self.tiles_list, mark_draw_slot=False)
    else:
        for player in self.player_list:
            for _ in range(13):
                player.get_tile(self.tiles_list, mark_draw_slot=False)
