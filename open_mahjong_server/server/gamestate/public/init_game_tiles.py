# 国标麻将游戏逻辑处理
import random
import hashlib
from typing import Dict
from ..public.logic_common import back_current_num


def _shuffle_and_deal(self) -> None:
    """
    通用：根据全局种子 + 局数生成本局随机种子，洗牌并分配起始牌。

    适用于：
    - 国标麻将（带花牌）
    - 青雀麻将（不带花牌）
    """
    # 使用 MD5 哈希函数混合全局种子和局数
    # 公式: MD5(game_random_seed + "_" + current_round) 的前8位十六进制转换为整数，再取模 2^32
    combined = f"{self.game_random_seed}_{self.current_round}".encode("utf-8")
    hash_value = int(hashlib.md5(combined).hexdigest()[:8], 16)
    self.round_random_seed = hash_value % (2**32)
    random.seed(self.round_random_seed)
    random.shuffle(self.tiles_list)

    # 固定起始牌的测试
    if self.Debug:
        # 使用测试牌例（共通调试用牌例）
        self.player_list[0].hand_tiles = [12, 12, 15, 15, 18, 18, 22, 22, 27, 27, 29, 29, 31]
        self.player_list[1].hand_tiles = []
        self.player_list[2].hand_tiles = []
        self.player_list[3].hand_tiles = []

        # 删除牌山中测试牌例的卡牌
        for tile in self.player_list[0].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[1].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[2].hand_tiles:
            self.tiles_list.remove(tile)
        for tile in self.player_list[3].hand_tiles:
            self.tiles_list.remove(tile)

        # 固定第一张自摸牌（55：示例花牌/特定牌）
        if 55 in self.tiles_list:
            self.tiles_list.remove(55)

        # 分配每位玩家13张牌
        for player in self.player_list:
            if not player.hand_tiles:
                for _ in range(13):
                    player.get_tile(self.tiles_list)

        # 当前玩家额外摸一张预设牌
        self.player_list[self.current_player_index].hand_tiles.append(55)

    else:
        # 分配每位玩家13张牌
        for player in self.player_list:
            for _ in range(13):
                player.get_tile(self.tiles_list)
        # 庄家额外摸一张
        self.player_list[0].get_tile(self.tiles_list)


def init_guobiao_tiles(self):
    """初始化国标麻将牌堆"""

    # 标准牌堆
    sth_tiles_set = {
        11, 12, 13, 14, 15, 16, 17, 18, 19,  # 万
        21, 22, 23, 24, 25, 26, 27, 28, 29,  # 饼
        31, 32, 33, 34, 35, 36, 37, 38, 39,  # 条
        41, 42, 43, 44,                      # 东南西北
        45, 46, 47,                          # 中白发
    }
    # 花牌牌堆
    hua_tiles_set = {51, 52, 53, 54, 55, 56, 57, 58}  # 春夏秋冬 梅兰竹菊
    # 生成牌堆
    self.tiles_list = []
    for tile in sth_tiles_set:
        self.tiles_list.extend([tile] * 4)
    self.tiles_list.extend(hua_tiles_set)

    # 通用洗牌 + 发牌逻辑
    _shuffle_and_deal(self)


def init_qingque_tiles(self):
    """初始化青雀麻将牌堆"""
    # 标准牌堆
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

    # 通用洗牌 + 发牌逻辑
    _shuffle_and_deal(self)