# 四川麻将牌堆初始化（108 张：万/饼/条 三门数牌，每种 4 张，无字牌花牌）
import random
from ..public.random_seed_manager import derive_round_seed


def init_sichuan_tiles(self):
    """初始化四川麻将牌堆并发牌：闲家 13 张，庄家 14 张（定缺前已摸好第 14 张）。"""
    if getattr(self, "duplicate_match", None) is not None:
        from .duplicate_wall import init_duplicate_board
        init_duplicate_board(self, self.duplicate_match.board(self.current_round, self.duplicate_table))
        return
    if getattr(self, "duplicate_key", None):
        raise ValueError("川麻复式必须使用成对创建的双打开闭室")
    sth_tiles_set = [
        11, 12, 13, 14, 15, 16, 17, 18, 19,  # 万
        21, 22, 23, 24, 25, 26, 27, 28, 29,  # 饼
        31, 32, 33, 34, 35, 36, 37, 38, 39,  # 条
    ]
    self.tiles_list = []
    for tile in sth_tiles_set:
        self.tiles_list.extend([tile] * 4)

    self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
    if getattr(self, "is_sbr_exchange", False):
        rng = random.Random(self.round_random_seed)
        rng.shuffle(self.tiles_list)
        self.deal_dice = [rng.randint(1, 6), rng.randint(1, 6)]
        wall_side = (sum(self.deal_dice) - 1) % 4
        # 108 张成 54 墩，庄家及对家各 14 墩，另两门各 13 墩。
        wall_offset = (0, 28, 54, 82)[wall_side] + 2 * min(self.deal_dice)
        self.tiles_list[:] = self.tiles_list[wall_offset:] + self.tiles_list[:wall_offset]
        seats = [self.player_list[(self.dealer_index + offset) % 4] for offset in range(4)]
        for _ in range(3):
            for player in seats:
                for _ in range(4):
                    player.get_tile(self.tiles_list, mark_draw_slot=False)
        for player in seats:
            player.get_tile(self.tiles_list, mark_draw_slot=False)
        seats[0].get_tile(self.tiles_list, mark_draw_slot=False)
        return
    random.seed(self.round_random_seed)
    random.shuffle(self.tiles_list)

    dealer_index = getattr(self, 'dealer_index', 0)
    for player in self.player_list:
        for _ in range(13):
            player.get_tile(self.tiles_list, mark_draw_slot=False)
    # 庄家额外摸一张后再定缺；不标记摸牌区，首打按手切处理（与国标一致）。
    self.player_list[dealer_index].get_tile(self.tiles_list, mark_draw_slot=False)
