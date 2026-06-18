"""四川麻将动作检查：无吃牌、定缺约束、四川和牌/听牌、刮风下雨判定。

约定：
- dingque_suit ∈ {1:万, 2:饼, 3:条, 0:未定缺}
- 已和退场玩家 player.is_hu=True：不再行动、不被询问、不被点炮。
- 和牌优先级 > 碰/杠（priority: hu_self=6, hu=5, peng/gang=2）。
- 一炮多响：本次弃牌所有可和玩家结果都暂存到 self.sichuan_hu_results[idx]。
- 顺和：跳过自摸/点炮/抢杠（含碰杠放弃）记录番数；听牌出牌后至下次摸牌前不可点和≤跳过番的牌（自摸不受限，tag: shunhe_N，仅本人可见）。
"""
from typing import Dict, List
import logging
from ..public.logic_common import get_index_relative_position, next_current_num
from .shunhe import is_blocked_by_shunhe

logger = logging.getLogger(__name__)


def _suit(tile: int) -> int:
    return tile // 10


def refresh_waiting_tiles(self, player_index, is_first_action=False):
    """更新听牌（四川：一般型 + 七对），并剔除定缺花色的听牌张。"""
    player_item = self.player_list[player_index]
    hand_tiles = player_item.hand_tiles
    if is_first_action:
        hand_tiles = player_item.hand_tiles[:-1]
    waits = self.calculation_service.Sichuan_tingpai_check(hand_tiles, player_item.combination_tiles)
    dingque = getattr(player_item, "dingque_suit", 0)
    if dingque in (1, 2, 3):
        waits = {w for w in waits if _suit(w) != dingque}
    if waits != player_item.waiting_tiles:
        player_item.waiting_tiles = waits
        logger.info(f"四川玩家{player_index}听牌更新为{waits}")


def _build_way_tokens(self, player_index, hepai_type, is_get_gang_tile=False):
    """拼装四川情境番 token：杠上花/杠上炮/抢杠/海底。"""
    way = []
    if hepai_type == "qianggang":
        way.append("抢杠")
    elif hepai_type == "handgot":
        if is_get_gang_tile:
            way.append("杠上花")
        if len(self.tiles_list) <= self.dead_wall_count:
            way.append("海底")
    elif hepai_type == "dianhe":
        # 杠上炮：上一动作为开杠后打出的牌
        if getattr(self, "last_action_was_gang", False):
            way.append("杠上炮")
        if len(self.tiles_list) <= self.dead_wall_count:
            way.append("海底")
    return way


def check_hepai(self, temp_action_dict, hepai_tile, player_index, hepai_type, is_get_gang_tile=False):
    """四川和牌检查：成立则写入 action_dict 与 self.sichuan_hu_results[player_index]。"""
    player = self.player_list[player_index]
    if getattr(player, "is_hu", False):
        return
    dingque = getattr(player, "dingque_suit", 0)

    if hepai_type == "handgot":
        tiles_list = player.hand_tiles.copy()
    else:
        tiles_list = player.hand_tiles + [hepai_tile]

    way = _build_way_tokens(self, player_index, hepai_type, is_get_gang_tile)
    fan, fan_list = self.calculation_service.Sichuan_hepai_check(
        tiles_list, player.combination_tiles, way, hepai_tile, dingque
    )
    if not fan_list:
        return
    if hepai_type in ("dianhe", "qianggang") and is_blocked_by_shunhe(player, fan):
        logger.info(
            f"四川顺和拦截：player={player_index} type={hepai_type} tile={hepai_tile} "
            f"fan={fan} cap={getattr(player, 'shunhe_passed_max_fan', None)}"
        )
        return

    is_zimo = (hepai_type == "handgot")
    self.sichuan_hu_results[player_index] = {
        "fan": fan,
        "fan_list": fan_list,
        "is_zimo": is_zimo,
        "hepai_tile": hepai_tile,
        "way": way,
    }
    if is_zimo:
        temp_action_dict[player_index].append("hu_self")
    else:
        temp_action_dict[player_index].append("hu")


def check_action_after_cut(self, cut_tile):
    """切牌后检查其他家：碰/杠/和（无吃）。跳过已和退场玩家。"""
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    self.sichuan_hu_results = {}

    wall_ok = len(self.tiles_list) > self.dead_wall_count
    cut_suit = _suit(cut_tile)

    # 和牌优先（一炮多响：收集所有可和家）
    for item in self.player_list:
        if item.player_index == self.current_player_index:
            continue
        if getattr(item, "is_hu", False):
            continue
        if cut_tile in item.waiting_tiles:
            check_hepai(self, temp_action_dict, cut_tile, item.player_index, "dianhe")

    # 碰/杠（不允许碰杠定缺花色；已和玩家不参与）
    for item in self.player_list:
        if item.player_index == self.current_player_index or getattr(item, "is_hu", False):
            continue
        if _suit(cut_tile) == getattr(item, "dingque_suit", 0):
            continue
        if item.hand_tiles.count(cut_tile) >= 2:
            temp_action_dict[item.player_index].append("peng")
        if wall_ok and item.hand_tiles.count(cut_tile) == 3:
            temp_action_dict[item.player_index].append("gang")

    for i in temp_action_dict:
        if temp_action_dict[i]:
            temp_action_dict[i].append("pass")

    temp_action_dict[self.current_player_index] = []
    return temp_action_dict


def check_action_jiagang(self, jiagang_tile):
    """加杠检查抢杠（四川：抢杠和）。"""
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    self.sichuan_hu_results = {}
    for item in self.player_list:
        if item.player_index == self.current_player_index or getattr(item, "is_hu", False):
            continue
        if jiagang_tile in item.waiting_tiles:
            check_hepai(self, temp_action_dict, jiagang_tile, item.player_index, "qianggang")
    for i in temp_action_dict:
        if temp_action_dict[i]:
            temp_action_dict[i].append("pass")
    return temp_action_dict


def check_action_hand_action(self, player_index, is_get_gang_tile=False, is_first_action=False):
    """摸牌后检查本家操作：自摸/暗杠/加杠/切牌。
    定缺约束：手牌仍含定缺花色时须优先切定缺牌、不可和牌，但仍可对非定缺花色暗杠与加杠。"""
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    player = self.player_list[player_index]
    dingque = getattr(player, "dingque_suit", 0)
    has_dingque_in_hand = dingque in (1, 2, 3) and any(_suit(t) == dingque for t in player.hand_tiles)

    wall_ok = len(self.tiles_list) > self.dead_wall_count
    if wall_ok:
        # 定缺约束只针对“和牌/打出”，开杠不受限：手牌仍含定缺花色时，
        # 对非定缺花色的暗杠与加杠都允许（加杠的本质是补齐已有碰的牌，不影响定缺出清）。
        processed = set()
        for tile in player.hand_tiles:
            if tile not in processed and player.hand_tiles.count(tile) == 4 and _suit(tile) != dingque:
                temp_action_dict[player_index].append("angang")
                processed.add(tile)
        for combo in player.combination_tiles:
            if combo[0] == "k":
                jiatile = int(combo[1:])
                if jiatile in player.hand_tiles and _suit(jiatile) != dingque:
                    temp_action_dict[player_index].append("jiagang")

    temp_action_dict[player_index].append("cut")

    if not has_dingque_in_hand and player.hand_tiles and player.hand_tiles[-1] in player.waiting_tiles:
        check_hepai(self, temp_action_dict, player.hand_tiles[-1], player_index, "handgot",
                    is_get_gang_tile=is_get_gang_tile)

    return temp_action_dict
