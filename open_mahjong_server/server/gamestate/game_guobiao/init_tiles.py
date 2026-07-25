# 国标麻将牌堆初始化
import random
from ..public.random_seed_manager import derive_round_seed
from .guobiao_debug import apply_guobiao_debug_hands, prepare_debug_wall


def init_guobiao_tiles(self):
    """初始化国标麻将牌堆（每人13张 + 庄家额外1张）"""
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

    # 生成本局随机种子并洗牌
    _shuffle_and_deal_guobiao(self)


def _shuffle_and_deal_guobiao(self) -> None:
    """国标：种子生成、洗牌、发牌（每人13张 + 庄家额外1张）"""
    self.round_random_seed = derive_round_seed(self.master_seed, self.current_round)
    random.seed(self.round_random_seed)
    random.shuffle(self.tiles_list)

    debug_mode = getattr(self, 'Debug', False)
    if debug_mode:
        apply_guobiao_debug_hands(self)
        _remove_assigned_tiles_from_wall(self.tiles_list, self.player_list)
        prepare_debug_wall(self)
        for player in self.player_list:
            player.combination_tiles = []
            player.combination_mask = []
    else:
        # 分配每位玩家13张牌
        for player in self.player_list:
            for _ in range(13):
                player.get_tile(self.tiles_list, mark_draw_slot=False)
        # 国标：庄家额外摸一张。
        # 不标记摸牌区：客户端 InitHandCards 把 14 张平铺展示（无 currentGetTile 标记），
        # 庄家首打一律按手切处理，否则首打恰为服务端末张时会误判为摸切（手摸切不一致）。
        self.player_list[0].get_tile(self.tiles_list, mark_draw_slot=False)


def _remove_assigned_tiles_from_wall(tiles_list, player_list) -> None:
    """从牌山扣除 Debug 固定手牌；配置超出每种 4 张时抛出明确错误。"""
    from collections import Counter

    need = Counter()
    for player in player_list:
        need.update(player.hand_tiles)
    wall = Counter(tiles_list)
    for tile, count in need.items():
        if wall[tile] < count:
            raise ValueError(
                f"Debug 手牌配置错误: 牌 {tile} 共需 {count} 张，牌山仅剩 {wall[tile]} 张"
            )
    for tile, count in need.items():
        for _ in range(count):
            tiles_list.remove(tile)
