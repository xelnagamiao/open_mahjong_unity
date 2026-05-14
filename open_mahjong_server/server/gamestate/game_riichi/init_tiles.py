"""
立直麻将牌堆初始化（136 张：万筒索 4 花色 + 东南西北白发中字牌，每种 4 张）
- 可选的赤宝牌：在生成阶段，5 万 / 5 饼 / 5 索 的其中一张替换为 105/205/305（赤 5）。
- 牌山在洗牌后，最后 14 张作为王牌（dead wall），王牌倒数第 6 张为宝牌指示牌。
"""
import random
import hashlib


def init_riichi_tiles(self):
    tile_ids = [
        11, 12, 13, 14, 15, 16, 17, 18, 19,
        21, 22, 23, 24, 25, 26, 27, 28, 29,
        31, 32, 33, 34, 35, 36, 37, 38, 39,
        41, 42, 43, 44,
        45, 46, 47,
    ]
    self.tiles_list = []
    for tile in tile_ids:
        self.tiles_list.extend([tile] * 4)

    if getattr(self, "red_dora", False):
        for aka_id, normal_id in ((105, 15), (205, 25), (305, 35)):
            if normal_id in self.tiles_list:
                self.tiles_list.remove(normal_id)
                self.tiles_list.append(aka_id)

    _shuffle_and_deal(self)


def _shuffle_and_deal(self) -> None:
    combined = f"{self.game_random_seed}_{self.round_index}".encode("utf-8")
    hash_value = int(hashlib.md5(combined).hexdigest()[:8], 16)
    self.round_random_seed = hash_value % (2**32)
    random.seed(self.round_random_seed)
    random.shuffle(self.tiles_list)

    # 每人 13 张
    for player in self.player_list:
        for _ in range(13):
            player.get_tile(self.tiles_list)

    # 王牌区（dead wall）：最后 14 张固定保留；取第 5 张（倒数第 6）作为首张宝牌指示牌
    self.dead_wall_count = 14
    self.dora_indicators = [self.tiles_list[-5]]
    self.kan_dora_indicators = []
    self.ura_dora_indicators = [self.tiles_list[-6]]
    self.ura_kan_dora_indicators = []
    self.rinshan_count = 0
