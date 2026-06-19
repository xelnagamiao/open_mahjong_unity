from typing import Dict, Any, List
from datetime import date, datetime


def capture_player_entry_order(gs) -> None:
    """在 master_seed shuffle 前调用，记录对局入场顺序（自定义房/匹配通用，供验证随机座位）。"""
    gs.player_entry_order = [p.user_id for p in gs.player_list]


def build_player_entry_order_fields(gs) -> Dict[str, Any]:
    """GameInfo / 牌谱共用的 shuffle 前玩家入场顺序字段。"""
    order = getattr(gs, "player_entry_order", None)
    if order and len(order) == 4:
        return {"player_entry_order": list(order)}
    return {}

"""
# 牌谱格式示例
# 操作短码: d=摸牌 gd=杠后摸牌 bd=补花后摸牌 bh=补花 c=切牌 ag=暗杠(第3段 T=摸杠/F=手杠) jg=加杠(第3段 T=摸杠/F=手杠) cl/cm/cr=吃 p=碰 g=明杠




2026-02-26 02:19:02,336 - server.gamestate.game_guobiao.GuobiaoGameState - INFO - 最终游戏记录: {'game_title': {'rule': 'guobiao', 'game_random_seed': 4212994277, 'max_round': 1, 'start_time': datetime.datetime(2026, 2, 26, 2, 14, 54, 814436), 'open_cuohe': False, 'tips': True, 'is_player_set_random_seed': False, 'p0_uid': 9000456, 'p0_name': '游客_5S7KRcUgMzE', 'p1_uid': 9000457, 'p1_name': '游客_XDW96Ovt7oM', 'p2_uid': 10000009, 'p2_name': 'Xelnaga', 'p3_uid': 9000455, 'p3_name': '游客_ASJSgtnUoEs', 'end_time': datetime.datetime(2026, 2, 26, 2, 19, 2, 336092)}, 'game_round': {'round_index_1': {'round_random_seed': 4068050480, 'current_round': 1, 'p0_tiles': [53, 37, 31, 41, 18, 21, 39, 39, 27, 46, 35, 44, 42, 44], 'p1_tiles': [43, 23, 54, 16, 46, 51, 31, 41, 11, 28, 26, 12, 19], 'p2_tiles': [13, 12, 38, 14, 44, 17, 26, 23, 42, 36, 36, 24, 32], 'p3_tiles': [28, 23, 34, 18, 29, 38, 29, 32, 47, 19, 13, 12, 31], 'tiles_list': [43, 34, 46, 27, 56, 12, 14, 35, 41, 47, 11, 38, 17, 58, 35, 28, 33, 37, 32, 17, 37, 36, 24, 26, 39, 16, 13, 43, 27, 24, 34, 16, 44, 38, 11, 39, 37, 14, 23, 25, 35, 31, 22, 45, 19, 33, 21, 14, 46, 45, 57, 11, 18, 27, 16, 28, 15, 33, 21, 36, 42, 15, 22, 47, 45, 22, 45, 43, 15, 26, 41, 55, 25, 22, 33, 18, 24, 17, 29, 32, 21, 47, 13, 42, 25, 34, 29, 25, 52, 19, 15], 'round_index': 1, 'action_ticks': [['bh', 53, 0], ['bd', 43], ['bh', 54, 1], ['bd', 34], ['bh', 51, 1], ['bd', 46], ['c', 46, 'F'], ['d', 27], ['c', 27, 'T'], ['d', 56], ['bh', 56, 2], ['bd', 19], ['c', 44, 'F'], ['d', 12], ['c', 12, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 41], ['c', 42, 'F'], ['d', 47], ['c', 47, 'T'], ['d', 11], ['c', 11, 'T'], ['d', 38], ['c', 38, 'T'], ['d', 17], ['c', 19, 'F'], ['d', 58], ['bh', 58, 3], ['bd', 15], ['c', 15, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 28], ['c', 28, 'T'], ['d', 33], ['c', 41, 'F'], ['d', 37], ['c', 37, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 17], ['c', 17, 'T'], ['p', 17, 2], ['c', 26, 'F'], ['d', 37], ['c', 37, 'T'], ['d', 36], ['c', 36, 'T'], ['p', 36, 2], ['c', 38, 'F'], ['d', 24], ['c', 24, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 24], ['c', 23, 'F'], ['d', 34], ['c', 34, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 44], ['c', 44, 'T'], ['d', 38], ['c', 38, 'T'], ['d', 11], ['c', 11, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 31], ['c', 31, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 33], ['c', 33, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 46], ['c', 46, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 57], ['bh', 57, 3], ['bd', 25], ['c', 25, 'T'], ['d', 11], ['c', 11, 'T'], ['d', 18], ['c', 18, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 28], ['c', 28, 'T'], ['d', 15], ['c', 15, 'T'], ['cl', 15, 2], ['c', 12, 'F'], ['d', 33], ['c', 33, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 36], ['c', 36, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 47], ['c', 47, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 41], ['c', 41, 'T'], ['d', 55], ['bh', 55, 1], ['bd', 52], ['c', 52, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 33], ['c', 33, 'T'], ['d', 18], ['c', 18, 'T'], ['d', 24], ['c', 32, 'F'], ['d', 17], ['c', 17, 'T'], ['d', 29], ['c', 29, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 21], ['c', 33, 'F'], ['d', 47], ['c', 47, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 25], ['c', 21, 'F'], ['d', 34], ['c', 34, 'T'], ['d', 29], ['c', 29, 'T'], ['liuju'], ['end']]}, 'round_index_2': {'round_random_seed': 1752449488, 'current_round': 2, 'p0_tiles': [27, 26, 44, 38, 17, 28, 35, 45, 22, 42, 24, 22, 11, 25], 'p1_tiles': [37, 34, 29, 25, 11, 35, 21, 57, 43, 47, 35, 11, 18], 'p2_tiles': [18, 41, 22, 22, 17, 52, 47, 46, 45, 44, 29, 36, 14], 'p3_tiles': [32, 12, 13, 15, 36, 28, 25, 15, 47, 31, 38, 12, 58], 'tiles_list': [41, 38, 16, 29, 37, 18, 15, 16, 12, 11, 24, 25, 21, 12, 13, 42, 19, 36, 33, 41, 26, 26, 33, 56, 46, 54, 28, 37, 23, 43, 23, 43, 44, 31, 45, 24, 27, 23, 17, 17, 39, 39, 39, 39, 36, 31, 24, 32, 53, 51, 43, 21, 33, 46, 34, 21, 42, 35, 55, 14, 27, 14, 32, 34, 16, 15, 16, 47, 41, 26, 42, 14, 34, 44, 18, 32, 13, 45, 19, 23, 37, 33, 19, 27, 29, 28, 31, 38, 13, 19, 46], 'round_index': 2, 'action_ticks': [['bh', 57, 1], ['bd', 41], ['bh', 52, 2], ['bd', 38], ['bh', 58, 3], ['bd', 16], ['c', 25, 'F'], ['d', 29], ['c', 43, 'F'], ['d', 37], ['c', 37, 'T'], ['d', 18], ['c', 18, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 16], ['c', 41, 'F'], ['d', 12], ['c', 12, 'T'], ['d', 11], ['c', 11, 'T'], ['p', 11, 1], ['c', 47, 'F'], ['d', 24], ['c', 24, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 12], ['c', 21, 'F'], ['d', 13], ['c', 13, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 36], ['c', 12, 'F'], ['d', 33], ['c', 33, 'T'], ['d', 41], ['c', 41, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 26], ['c', 25, 'F'], ['d', 33], ['c', 33, 'T'], ['d', 56], ['bh', 56, 3], ['bd', 19], ['c', 19, 'T'], ['d', 46], ['c', 46, 'T'], ['d', 54], ['bh', 54, 1], ['bd', 46], ['c', 46, 'T'], ['d', 28], ['c', 28, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 44], ['c', 44, 'T'], ['d', 31], ['c', 31, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 24], ['c', 24, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 17], ['c', 17, 'T'], ['d', 17], ['c', 17, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 39], ['c', 39, 'T'], ['d', 36], ['c', 36, 'T'], ['cl', 36, 1], ['c', 26, 'F'], ['d', 31], ['c', 31, 'T'], ['d', 24], ['c', 24, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 53], ['bh', 53, 1], ['bd', 38], ['c', 18, 'F'], ['d', 51], ['bh', 51, 2], ['bd', 13], ['c', 13, 'T'], ['d', 43], ['c', 43, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 33], ['c', 16, 'F'], ['d', 46], ['c', 46, 'T'], ['d', 34], ['c', 34, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 55], ['bh', 55, 3], ['bd', 28], ['c', 28, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 34], ['c', 34, 'T'], ['cm', 34, 1], ['c', 29, 'F'], ['d', 16], ['c', 16, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 47], ['c', 29, 'F'], ['d', 41], ['c', 41, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 14], ['c', 47, 'F'], ['d', 34], ['c', 34, 'T'], ['d', 44], ['c', 44, 'T'], ['d', 18], ['c', 18, 'T'], ['d', 32], ['c', 14, 'F'], ['d', 13], ['c', 13, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 23], ['c', 32, 'F'], ['d', 37], ['c', 37, 'T'], ['d', 33], ['c', 33, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 29], ['c', 29, 'T'], ['d', 31], ['c', 31, 'T'], ['liuju'], ['end']]}, 'round_index_3': {'round_random_seed': 1794697739, 'current_round': 3, 'p0_tiles': [47, 33, 39, 39, 44, 43, 11, 47, 23, 51, 44, 17, 39, 19], 'p1_tiles': [38, 21, 46, 17, 55, 27, 45, 46, 24, 17, 25, 41, 44], 'p2_tiles': [41, 36, 46, 44, 31, 36, 35, 33, 22, 26, 32, 37, 35], 'p3_tiles': [12, 28, 27, 23, 37, 21, 21, 42, 41, 11, 23, 45, 58], 'tiles_list': [24, 23, 32, 32, 25, 12, 22, 34, 37, 14, 21, 22, 34, 13, 22, 15, 26, 42, 15, 16, 17, 33, 32, 14, 13, 45, 31, 14, 18, 38, 11, 39, 29, 36, 42, 43, 25, 26, 18, 29, 29, 53, 52, 15, 28, 38, 29, 56, 45, 14, 31, 35, 19, 47, 31, 38, 16, 19, 28, 42, 34, 47, 28, 12, 18, 24, 57, 11, 15, 16, 43, 26, 34, 35, 13, 16, 18, 46, 25, 27, 33, 19, 41, 27, 13, 12, 43, 24, 37, 36, 54], 'round_index': 3, 'action_ticks': [['bh', 51, 0], ['bd', 24], ['bh', 55, 1], ['bd', 23], ['bh', 58, 3], ['bd', 32], ['c', 11, 'F'], ['d', 32], ['c', 44, 'F'], ['p', 44, 0], ['c', 43, 'F'], ['d', 25], ['c', 25, 'T'], ['d', 12], ['c', 12, 'T'], ['d', 22], ['c', 22, 'T'], ['cr', 22, 0], ['c', 33, 'F'], ['d', 34], ['c', 34, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 34], ['c', 34, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 22], ['c', 22, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 26], ['c', 26, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 17], ['c', 17, 'T'], ['d', 33], ['c', 33, 'T'], ['d', 32], ['c', 32, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 45], ['c', 45, 'T'], ['d', 31], ['c', 31, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 18], ['c', 18, 'T'], ['hu_second', 0, 10, ['五门齐', '嵌张', '幺九刻*2', '花牌*1'], [-18, -8, 34, -8]], ['end']]}, 'round_index_4': {'round_random_seed': 635524321, 'current_round': 4, 'p0_tiles': [11, 22, 11, 29, 16, 33, 32, 43, 11, 56, 57, 13, 24, 47], 'p1_tiles': [32, 24, 42, 38, 25, 46, 17, 36, 27, 37, 11, 26, 19], 'p2_tiles': [31, 26, 16, 44, 31, 21, 35, 21, 34, 39, 45, 45, 52], 'p3_tiles': [19, 39, 27, 33, 28, 32, 34, 46, 35, 13, 58, 37, 18], 'tiles_list': [28, 44, 31, 14, 23, 22, 26, 15, 44, 28, 42, 16, 36, 23, 42, 41, 41, 29, 15, 25, 18, 17, 17, 37, 34, 38, 36, 24, 27, 37, 35, 23, 25, 53, 14, 24, 21, 12, 13, 15, 36, 51, 32, 16, 46, 47, 19, 55, 18, 44, 41, 42, 38, 54, 15, 31, 39, 43, 43, 12, 46, 39, 35, 41, 17, 25, 33, 14, 22, 12, 38, 28, 18, 34, 21, 27, 12, 29, 43, 14, 29, 47, 45, 47, 19, 23, 22, 26, 33, 45, 13], 'round_index': 4, 'action_ticks': [['bh', 57, 0], ['bd', 28], ['bh', 56, 0], ['bd', 44], ['bh', 52, 2], ['bd', 31], ['bh', 58, 3], ['bd', 14], ['c', 43, 'F'], ['d', 23], ['c', 42, 'F'], ['d', 22], ['c', 44, 'F'], ['d', 26], ['c', 46, 'F'], ['d', 15], ['c', 15, 'T'], ['d', 44], ['c', 44, 'T'], ['d', 28], ['c', 28, 'T'], ['cl', 28, 3], ['c', 39, 'F'], ['d', 42], ['c', 42, 'T'], ['d', 16], ['c', 16, 'T'], ['d', 36], ['c', 36, 'T'], ['cm', 36, 3], ['c', 28, 'F'], ['d', 23], ['c', 23, 'T'], ['d', 42], ['c', 42, 'T'], ['d', 41], ['c', 41, 'T'], ['d', 41], ['c', 41, 'T'], ['d', 29], ['c', 29, 'T'], ['d', 15], ['c', 15, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 18], ['c', 19, 'F'], ['d', 17], ['c', 17, 'T'], ['d', 17], ['c', 17, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 34], ['c', 34, 'T'], ['d', 38], ['c', 38, 'T'], ['d', 36], ['c', 36, 'T'], ['d', 24], ['c', 24, 'T'], ['d', 27], ['c', 27, 'T'], ['d', 37], ['c', 37, 'T'], ['d', 35], ['c', 35, 'T'], ['d', 23], ['c', 23, 'T'], ['d', 25], ['c', 25, 'T'], ['d', 53], ['bh', 53, 0], ['bd', 45], ['c', 45, 'T'], ['d', 14], ['c', 14, 'T'], ['d', 24], ['c', 24, 'T'], ['d', 21], ['c', 21, 'T'], ['d', 12], ['c', 12, 'T'], ['d', 13], ['c', 13, 'T'], ['d', 15], ['c', 15, 'T'], ['cl', 15, 3], ['c', 18, 'F'], ['d', 36], ['c', 36, 'T'], ['d', 51], ['bh', 51, 1], ['bd', 13], ['c', 13, 'T'], ['d', 32], ['c', 32, 'T'], ['cr', 32, 3], ['c', 32, 'F'], ['d', 16], ['c', 16, 'T'], ['d', 46], ['c', 46, 'T'], ['d', 47], ['c', 47, 'T'], ['d', 19], ['c', 19, 'T'], ['d', 55], ['bh', 55, 0], ['bd', 26], ['c', 26, 'T'], ['d', 18], ['c', 18, 'T'], ['hu_second', 3, 12, ['全求人', '断幺', '平和', '连六*1', '花牌*1'], [-20, -8, 36, -8]], ['end']]}}}

# 和牌: [hu_class, hepai_idx, hu_score, hu_fan[], [p0Δ,p1Δ,p2Δ,p3Δ], hepai_tile?]（错和无 end，游戏继续）
# 流局: ["liuju"]
# 回合结束: ["end"]（和牌或流局之后紧跟，错和除外）
"""
def build_game_title_data(gs) -> Dict[str, Any]:
    """构建 game_title（与落库牌谱 JSON 一致）。

    与观战 record_game_title 共用，避免两处手写 game_title 字段不一致
    （例如观战缺 seats 一类问题）。观战专用的 players_settings 仍在
    SpectatorManager 中单独维护，不入落库牌谱。
    """
    title: Dict[str, Any] = {
        # rule: 游戏规则 guobiao | qingque | classical | riichi
        "rule": gs.room_rule,
        # room_type: 房间类型 custom | match
        "room_type": gs.room_type,
        # sub_rule: 子规则，如 guobiao/standard、riichi/standard
        "sub_rule": getattr(gs, "sub_rule", None),
        # commitment_hex: 承诺值，64 位 hex 字符串（对局内广播，用于事后验证）
        "commitment_hex": format(gs.commitment, '064x'),
        # salt: 盐字符串（128 位 hex）
        "salt": gs.salt,
        # max_round: 风圈数（1=东风、2=半庄、4=全庄）
        "max_round": gs.max_round,
        # open_cuohe: 是否开启错和
        "open_cuohe": gs.open_cuohe,
        # tips: 是否开启提示
        "tips": gs.tips,
        # show_moqie_hint: 是否显示手摸切灰显
        "show_moqie_hint": getattr(gs, "show_moqie_hint", False),
        # is_player_set_random_seed: 是否玩家指定随机种子（复式）
        "is_player_set_random_seed": gs.isPlayerSetRandomSeed,
    }
    # hepai_limit: 起和番限制（国标/青雀/古典/立直，有该属性时写入）
    if hasattr(gs, "hepai_limit"):
        title["hepai_limit"] = gs.hepai_limit
    # 以下字段仅立直麻将 room_rule == "riichi"
    if getattr(gs, "room_rule", None) == "riichi":
        # red_dora: 是否启用赤宝牌
        title["red_dora"] = getattr(gs, "red_dora", False)
        # allow_kuikae: 是否允许食替（仅标准日麻；浪涌由子规则内置）
        if getattr(gs, "sub_rule", None) != "riichi/langyong":
            title["allow_kuikae"] = getattr(gs, "allow_kuikae", False)
        # hepai_way: 和牌方式 head_bump | multi_ron | three_ron_abort
        title["hepai_way"] = getattr(gs, "hepai_way", None)
        if hasattr(gs, "open_xiru"):
            # open_xiru: 是否西入
            title["open_xiru"] = gs.open_xiru
        if hasattr(gs, "open_tobi"):
            # open_tobi: 是否击飞
            title["open_tobi"] = gs.open_tobi
    # match_queue_type: 排位队列（如 beginner_quanzhuang），仅 match 房间写入，供天梯对局列表展示场次
    match_queue_type = getattr(gs, "match_queue_type", None)
    if match_queue_type:
        title["match_queue_type"] = match_queue_type
    # player_entry_order: shuffle 前对局入场顺序（user_id[4]，用于验证 master_seed 随机座位）
    player_entry_order = getattr(gs, "player_entry_order", None)
    if player_entry_order and len(player_entry_order) == 4:
        title["player_entry_order"] = list(player_entry_order)
    # p0_uid … p3_uid / p0_name … p3_name: 随机座位分配后的 original 0～3（整局不变）
    for i, player in enumerate(gs.player_list):
        title[f"p{i}_uid"] = player.user_id
        title[f"p{i}_name"] = player.username
    return title


# 牌谱记录游戏头
def init_game_record(self):
    self.game_record["game_title"] = build_game_title_data(self)
    self.game_record["game_title"]["start_time"] = datetime.now()
    self.game_record["game_round"] = {}

def apply_game_title_end_fields(gs, title: Dict[str, Any]) -> None:
    """整局结束时写入 game_title 终局字段（落库牌谱与观战完整牌谱共用）。

    主种子仅在整局结束后公开，与 WebSocket game_end_info 一致；牌谱存 64 位 hex，
    便于与 commitment_hex / random_seed_manager 验证逻辑对齐。
    """
    title["end_time"] = datetime.now()
    master_seed = getattr(gs, "master_seed", None)
    if master_seed is not None:
        title["master_seed_hex"] = format(master_seed, "064x")


def end_game_record(self):
    apply_game_title_end_fields(self, self.game_record["game_title"])

def build_round_header_data(gs) -> Dict[str, Any]:
    """构建局头快照（与牌谱 JSON 格式一致，供 init_game_round / 观战 record_round_start 共用）。"""
    seats = [0] * 4
    for p in gs.player_list:
        seats[p.original_player_index] = p.player_index
    round_data: Dict[str, Any] = {
        "current_round": gs.current_round,
        "seats": seats,
        "dealer_index": 0,
        "start_player_index": 0,
        "p0_tiles": gs.player_list[0].hand_tiles.copy(),
        "p1_tiles": gs.player_list[1].hand_tiles.copy(),
        "p2_tiles": gs.player_list[2].hand_tiles.copy(),
        "p3_tiles": gs.player_list[3].hand_tiles.copy(),
        "tiles_list": gs.tiles_list.copy(),
        "round_index": gs.round_index,
    }
    if getattr(gs, "room_rule", None) == "riichi":
        round_data["riichi"] = {
            "honba": gs.honba,
            "riichi_sticks": gs.riichi_sticks,
        }
    return round_data


# 牌谱记录对局头
def init_game_round(self):
    self.player_action_tick = 0
    round_data = build_round_header_data(self)
    round_data["action_ticks"] = []
    self.game_record["game_round"][f"round_index_{self.round_index}"] = round_data


def build_score_changes_by_seat(player_list, scores_before: Dict[int, int]) -> List[int]:
    """牌谱 tick 内 score_changes 按 player_index 排列。"""
    changes = [0, 0, 0, 0]
    for p in player_list:
        changes[p.player_index] = p.score - scores_before[p.original_player_index]
    return changes


def build_score_changes_dict(player_list, scores_before: Dict[int, int]) -> Dict[int, int]:
    """show_result 广播用 score_changes，键为 original_player_index（与日麻 broadcast 一致）。"""
    return {
        p.original_player_index: p.score - scores_before[p.original_player_index]
        for p in player_list
    }


def append_action_tick(gs, tick: list) -> None:
    """落库牌谱并同步观战增量（单一数据源，格式完全一致）。"""
    gs.player_action_tick += 1
    gs.game_record["game_round"][f"round_index_{gs.round_index}"]["action_ticks"].append(tick)
    spectator = getattr(gs, "spectator_manager", None)
    if spectator is not None and getattr(spectator, "enabled", False):
        spectator.record_tick(tick)


def resolve_hepai_tile_for_record(gs, hu_class: str, hepai_player_index: int):
    """和牌 tick 附带的和牌张（自摸/荣和均为结算时手牌末张）。"""
    if hepai_player_index is None or not (0 <= hepai_player_index < len(gs.player_list)):
        return None
    hand = gs.player_list[hepai_player_index].hand_tiles
    if not hand:
        return None
    tile = hand[-1]
    return tile if isinstance(tile, int) and tile > 10 else None


# 牌谱记录补花
def player_action_record_buhua(self,max_tile: int,action_player: int):
    append_action_tick(self, ["bh", max_tile, action_player])

# 牌谱记录摸牌 deal_type: "d" 普通摸牌, "gd" 杠后摸牌, "bd" 补花后摸牌
def player_action_record_deal(self, deal_tile: int, deal_type: str = "d"):
    append_action_tick(self, [deal_type, deal_tile])

# 牌谱记录切牌
def player_action_record_cut(self, cut_tile: int, is_moqie: bool = False, is_riichi_horizontal: bool = False):
    """牌谱切牌记录：is_riichi_horizontal 为 True 时该弃牌在回放时应当横置渲染（含立直宣告与续横）。"""
    entry = ["c", cut_tile, "T" if is_moqie else "F"]
    if is_riichi_horizontal:
        entry.append("H")
    append_action_tick(self, entry)

def _append_gang_score_changes(entry: list, gang_score_changes=None) -> list:
    """四川刮风下雨：tick 末尾追加 gs 标记 + 四家分变（全 0 则不追加）。"""
    if not gang_score_changes:
        return entry
    deltas = [gang_score_changes.get(i, 0) for i in range(4)]
    if not any(deltas):
        return entry
    entry.append("gs")
    entry.extend(deltas)
    return entry


# 牌谱记录暗杠；is_mo_gang True=摸杠 False=手杠
# combination_mask 存在时追加 4 张手牌侧真实 ID（含赤 5 的 105/205/305），与吃碰杠口径一致
def player_action_record_angang(self, angang_tile: int, is_mo_gang: bool = False, combination_mask=None,
                                gang_score_changes=None):
    entry = ["ag", angang_tile, "T" if is_mo_gang else "F"]
    if combination_mask:
        entry.extend(_extract_hand_tiles_from_mingpai_mask(combination_mask, angang_tile))
    entry = _append_gang_score_changes(entry, gang_score_changes)
    append_action_tick(self, entry)

# 牌谱记录加杠；is_mo_gang True=摸杠 False=手杠
def player_action_record_jiagang(self, jiagang_tile: int, is_mo_gang: bool = False, gang_score_changes=None):
    entry = ["jg", jiagang_tile, "T" if is_mo_gang else "F"]
    entry = _append_gang_score_changes(entry, gang_score_changes)
    append_action_tick(self, entry)

# 游戏逻辑动作名 → 牌谱短码
_ACTION_TO_RECORD = {
    "chi_left": "cl", "chi_mid": "cm", "chi_right": "cr",
    "peng": "p", "gang": "g",
}

def _extract_hand_tiles_from_mingpai_mask(combination_mask, mingpai_tile: int):
    """从 combination_mask 提取从手牌打出的真实牌 ID（跳过 direction==1 的被鸣牌位）。"""
    if not combination_mask:
        return []
    hand_tiles = []
    for i in range(0, len(combination_mask) - 1, 2):
        if combination_mask[i] == 1:
            continue
        tid = combination_mask[i + 1]
        if isinstance(tid, int) and tid > 10:
            hand_tiles.append(tid)
    return hand_tiles


# 牌谱记录吃碰杠牌；combination_mask 存在时追加手牌侧真实 ID（含赤 5 的 105/205/305）
def player_action_record_chipenggang(self, action_type: str, mingpai_tile: int, action_player: int,
                                     combination_mask=None, gang_score_changes=None):
    record_code = _ACTION_TO_RECORD.get(action_type, action_type)
    entry = [record_code, mingpai_tile, action_player]
    if combination_mask:
        entry.extend(_extract_hand_tiles_from_mingpai_mask(combination_mask, mingpai_tile))
    entry = _append_gang_score_changes(entry, gang_score_changes)
    append_action_tick(self, entry)

# 牌谱记录和牌 [hu_class, hepai_player_index, hu_score, hu_fan, score_changes, base_fu?, fu_fan_list?, hepai_tile?, multi_ron?, ron_discarder_index?, recycle_discard?]
def player_action_record_hu(self, hu_class: str, hu_score, hu_fan: list,
                            hepai_player_index: int, score_changes: List[int],
                            base_fu=None, fu_fan_list=None,
                            hepai_tile=None, multi_ron=None, ron_discarder_index=None,
                            recycle_discard=None):
    if hepai_tile is None:
        hepai_tile = resolve_hepai_tile_for_record(self, hu_class, hepai_player_index)
    tick = [hu_class, hepai_player_index, hu_score, hu_fan, score_changes]
    if base_fu is not None:
        tick.append(base_fu)
        tick.append(fu_fan_list or [])
    if hepai_tile is not None:
        tick.append(hepai_tile)
    if multi_ron is not None:
        tick.append(1 if multi_ron else 0)
    if ron_discarder_index is not None:
        tick.append(ron_discarder_index)
    if recycle_discard is not None:
        tick.append(1 if recycle_discard else 0)
    append_action_tick(self, tick)

# 四川血战·杠分即时退税（杠上炮/抢杠）["gr", "gs", d0, d1, d2, d3]
def player_action_record_gang_refund(self, gang_score_changes):
    deltas = [gang_score_changes.get(i, 0) for i in range(4)]
    append_action_tick(self, ["gr", "gs", *deltas])

# 四川血战·流局/终局分步牌谱 ["liuju", step, ...payload]
def player_action_record_sichuan_liuju_step(self, step: str, *payload):
    append_action_tick(self, ["liuju", step, *payload])

# 牌谱记录九老峰回流局 ["jiuzhongjiupai"]
def player_action_record_jiuzhongjiupai(self):
    append_action_tick(self, ["jiuzhongjiupai"])

# 牌谱记录流局 ["liuju"]
def player_action_record_liuju(self):
    append_action_tick(self, ["liuju"])

# 牌谱记录数和尾结算
# ["shuhewei", [p0_fu,p1_fu,p2_fu,p3_fu], [p0Δ,p1Δ,p2Δ,p3Δ], [[p0番...],[p1番...],[p2番...],[p3番...]], [[p0副种...],[p1副种...],[p2副种...],[p3副种...]], hu_class, hepai_player_index]
def player_action_record_shuhewei(
    self,
    player_fu: Dict[int, int],
    score_changes: Dict[int, int],
    player_fan: Dict[int, List[str]],
    player_fu_types: Dict[int, List[str]],
    hu_class: str,
    hepai_player_index: int,
):
    fu_list = [player_fu.get(i, 0) for i in range(4)]
    changes_list = [score_changes.get(i, 0) for i in range(4)]
    fan_list = [player_fan.get(i, []) for i in range(4)]
    fu_type_list = [player_fu_types.get(i, []) for i in range(4)]
    append_action_tick(self, ["shuhewei", fu_list, changes_list, fan_list, fu_type_list, hu_class, hepai_player_index])

# 立直麻将 - 牌谱记录宣告立直 ["riichi", player_index, is_daburu]
def player_action_record_riichi(self, player_index: int, is_daburu: bool = False):
    append_action_tick(self, ["riichi", player_index, 1 if is_daburu else 0])

# 立直麻将 - 牌谱记录新翻出宝牌指示牌 ["dora", tile_id]
def player_action_record_new_dora(self, tile_id: int):
    append_action_tick(self, ["dora", tile_id])

# 立直麻将 - 牌谱记录荒牌流局（含听牌/不听结算）
# ["ryuukyoku", [p0_tenpai..p3_tenpai], [p0Δ..p3Δ], reason]
def player_action_record_ryuukyoku(self, tenpai_flags: List[int], score_changes: List[int], reason: str = "exhaustive"):
    append_action_tick(self, ["ryuukyoku", list(tenpai_flags), list(score_changes), reason])

# 立直麻将 - 记录和牌细节 ["hu_riichi", hepai_player_index, hu_class, han, fu, yaku[], score_changes[], dora_indicators[], ura_dora_indicators[], aka_count]
def player_action_record_hu_riichi(
    self,
    hepai_player_index: int,
    hu_class: str,
    han: int,
    fu: int,
    yaku: List[str],
    score_changes: List[int],
    dora_indicators: List[int],
    ura_dora_indicators: List[int],
    aka_count: int,
    honba: int,
    riichi_sticks_collected: int,
):
    append_action_tick(self, [
        "hu_riichi",
        hepai_player_index,
        hu_class,
        han,
        fu,
        list(yaku),
        list(score_changes),
        list(dora_indicators),
        list(ura_dora_indicators),
        aka_count,
        honba,
        riichi_sticks_collected,
    ])

# 牌谱记录回合结束标记 ["end"]
def player_action_record_round_end(self):
    append_action_tick(self, ["end"])