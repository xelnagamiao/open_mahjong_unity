"""台湾麻将牌墙初始化。"""

import random

from ...game_calculation.hand_structure import SIXTEEN_TILE_MAHJONG
from ...game_calculation.taiwan.rules import FLOWER_TILES, STRUCTURE_TILES
from ..public.random_seed_manager import derive_round_seed


def init_taiwan_tiles(game_state) -> None:
    """洗牌并按十六张麻将结构配牌；花牌暂留手内等待分轮补花。"""

    wall = []
    for tile in STRUCTURE_TILES:
        wall.extend([tile] * 4)
    wall.extend(FLOWER_TILES)

    # 连庄时 current_round 不变，必须使用实际手数 round_index 派生不同牌墙。
    game_state.round_random_seed = derive_round_seed(game_state.master_seed, game_state.round_index)
    rng = random.Random(game_state.round_random_seed)
    rng.shuffle(wall)
    game_state.tiles_list = wall

    for player in game_state.player_list:
        player.hand_tiles = []
        for _ in range(SIXTEEN_TILE_MAHJONG.base_hand_tile_count):
            player.get_tile(game_state.tiles_list, mark_draw_slot=False)
    game_state.player_list[0].get_tile(game_state.tiles_list, mark_draw_slot=False)
