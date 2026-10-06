"""四川麻将动作检查：无吃牌、定缺约束、四川和牌/听牌、刮风下雨判定。

约定：
- dingque_suit ∈ {1:万, 2:饼, 3:条, 0:未定缺}
- 已和退场玩家 player.is_hu=True：不再行动、不被询问、不被点炮。
- 和牌优先级 > 碰/杠（priority: hu_self=6, hu=5, peng/gang=2）。
- 一炮多响：本次弃牌所有可和玩家结果都暂存到 self.sichuan_hu_results[idx]。
- 顺和：跳过自摸/点炮/抢杠（含碰杠放弃）记录番数；听牌时立即生效，至下次摸牌前不可点和≤跳过番的牌（自摸不受限，tag: shunhe_N，仅本人可见）。
"""
from typing import Dict
from ..public.tactical_claim import add_tactical_force_pass_options
import logging
from ..public.hand_slot_utils import normalize_tile
from .shunhe import is_blocked_by_shunhe

logger = logging.getLogger(__name__)


def _suit(tile: int) -> int:
    return tile // 10


def _xueliu_angang_preserves_waiting(self, player, tile: int) -> bool:
    """血流: 和后暗杠只有在固定听口仍完整保留时才允许。"""
    locked = set(getattr(player, "locked_waiting_tiles", set()))
    if not locked:
        return True
    hand = list(player.hand_tiles)
    if hand.count(tile) < 4:
        return False
    for _ in range(4):
        hand.remove(tile)
    combos = list(player.combination_tiles) + [f"G{tile}"]
    waits = self.calculation_service.Sichuan_xueliu_tingpai_check(hand, combos, meld_count=getattr(self, "xueliu_meld_count", 3))
    return set(waits) == locked


def _xueliu_jiagang_preserves_waiting(self, player, tile: int) -> bool:
    locked = set(getattr(player, "locked_waiting_tiles", set()))
    hand = list(player.hand_tiles)
    melds = list(player.combination_tiles)
    if tile not in hand or f"k{tile}" not in melds:
        return False
    hand.remove(tile)
    melds[melds.index(f"k{tile}")] = f"g{tile}"
    return set(self.calculation_service.Sichuan_xueliu_tingpai_check(hand, melds, meld_count=getattr(self, "xueliu_meld_count", 3))) == locked


def refresh_waiting_tiles(self, player_index, is_first_action=False):
    """更新听牌（四川：一般型 + 七对），并剔除定缺花色的听牌张。"""
    player_item = self.player_list[player_index]
    if (
        getattr(self, "is_xueliu", False)
        and getattr(player_item, "post_hu_lock", False)
        and getattr(player_item, "locked_waiting_tiles", None)
    ):
        # 血流和后不能换张/改听；摸切后仍沿用首次和牌时的听口。
        player_item.waiting_tiles = set(player_item.locked_waiting_tiles)
        return
    hand_tiles = player_item.hand_tiles
    if is_first_action:
        hand_tiles = player_item.hand_tiles[:-1]
    if getattr(self, "is_xueliu", False):
        waits = self.calculation_service.Sichuan_xueliu_tingpai_check(
            hand_tiles, player_item.combination_tiles, meld_count=getattr(self, "xueliu_meld_count", 3)
        )
    else:
        waits = self.calculation_service.Sichuan_tingpai_check(hand_tiles, player_item.combination_tiles)
    dingque = getattr(player_item, "dingque_suit", 0)
    if dingque in (1, 2, 3):
        has_excluded = any(_suit(t) == dingque for t in hand_tiles) or any(int(m[1:]) // 10 == dingque for m in player_item.combination_tiles)
        waits = set() if has_excluded else {w for w in waits if _suit(w) != dingque}
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
        # 换三张的海底仅限最后一张自摸；末张放铳不额外增加根。
        exchange = getattr(self, "is_xueliu", False) and getattr(self, "xueliu_meld_count", 3) == 4
        if not exchange and len(self.tiles_list) <= self.dead_wall_count:
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
    if getattr(self, "is_xueliu", False):
        xueliu_way = list(way)
        if hepai_type == "handgot":
            xueliu_way.append("自摸")
        fan, fan_list = self.calculation_service.Sichuan_xueliu_hepai_check(
            tiles_list, player.combination_tiles, xueliu_way, hepai_tile, dingque, meld_count=getattr(self, "xueliu_meld_count", 3)
        )
    else:
        fan, fan_list = self.calculation_service.Sichuan_hepai_check(
            tiles_list, player.combination_tiles, way, hepai_tile, dingque
        )
    if not fan_list or fan < getattr(self, "hepai_limit", 0):
        return
    # 血流使用独立的 10/11 张牌型检查；标准血战继续使用 MIL/SBR 检查。
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
        if getattr(self, "is_xueliu", False) and getattr(item, "has_won", False):
            # 和后继续摸打/自摸或点炮，但不再通过弃牌碰牌改变手牌结构。
            continue
        if _suit(cut_tile) == getattr(item, "dingque_suit", 0):
            continue
        if item.hand_tiles.count(cut_tile) >= 2:
            temp_action_dict[item.player_index].append("peng")
        # 血流：和后仍留桌，但不得用弃牌开明杠；续杠/暗杠仍在
        # 自己摸牌的检查中处理。标准血战玩家没有 has_won 字段语义。
        if wall_ok and item.hand_tiles.count(cut_tile) == 3 and not (
            getattr(self, "is_xueliu", False) and getattr(item, "has_won", False)
        ):
            temp_action_dict[item.player_index].append("gang")

    for i in temp_action_dict:
        if temp_action_dict[i]:
            temp_action_dict[i].append("pass")

    temp_action_dict[self.current_player_index] = []
    return add_tactical_force_pass_options(self, temp_action_dict)


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
    return add_tactical_force_pass_options(self, temp_action_dict)


def check_action_hand_action(self, player_index, is_get_gang_tile=False, is_first_action=False):
    """摸牌后检查本家操作：自摸/暗杠/加杠/切牌。
    定缺约束：手牌仍含定缺花色时须优先切定缺牌、不可和牌，但仍可对非定缺花色暗杠与加杠。"""
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    # 清除本家旧和牌结果，避免杠后无自摸时仍残留 is_zimo，导致切牌误记顺和上限
    if getattr(self, "sichuan_hu_results", None) is None:
        self.sichuan_hu_results = {}
    else:
        self.sichuan_hu_results.pop(player_index, None)
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
                if getattr(self, "is_xueliu", False) and getattr(player, "post_hu_lock", False):
                    if not _xueliu_angang_preserves_waiting(self, player, tile):
                        processed.add(tile)
                        continue
                temp_action_dict[player_index].append("angang")
                processed.add(tile)
        jiagang_added = False
        for combo in player.combination_tiles:
            if combo.startswith("k") and not jiagang_added:
                jia_norm = normalize_tile(int(combo[1:]))
                if _suit(jia_norm) != dingque and any(
                    normalize_tile(t) == jia_norm for t in player.hand_tiles
                ):
                    if getattr(self, "is_xueliu", False) and player.post_hu_lock and not _xueliu_jiagang_preserves_waiting(self, player, jia_norm):
                        continue
                    temp_action_dict[player_index].append("jiagang")
                    jiagang_added = True

    temp_action_dict[player_index].append("cut")

    opening_win_blocked = getattr(self, "is_xueliu", False) and is_first_action
    if not opening_win_blocked and not has_dingque_in_hand and player.hand_tiles and player.hand_tiles[-1] in player.waiting_tiles:
        check_hepai(self, temp_action_dict, player.hand_tiles[-1], player_index, "handgot",
                    is_get_gang_tile=is_get_gang_tile)

    return temp_action_dict
