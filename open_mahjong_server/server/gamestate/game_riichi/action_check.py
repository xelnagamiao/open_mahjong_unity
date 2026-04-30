"""
立直麻将动作检查：吃碰杠/立直/自摸/荣和/振听/特殊流局。
动作优先级与 Classical 基本一致，仅新增 riichi 声明。
"""
from typing import Dict, List
import logging

from ..public.logic_common import get_index_relative_position, next_current_num

logger = logging.getLogger(__name__)


YAOCHUU = {11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47}


def check_kokushi(hand_tiles: List[int]) -> bool:
    if len(hand_tiles) != 13:
        return False
    if len(set(hand_tiles)) != 13:
        return False
    suits: Dict[int, list] = {}
    for t in hand_tiles:
        if t < 40:
            suit = t // 10
            suits.setdefault(suit, []).append(t % 10)
    for values in suits.values():
        values.sort()
        for i in range(len(values) - 1):
            if values[i + 1] - values[i] <= 2:
                return False
    return True


def check_jiuzhongjiupai(hand_tiles: List[int]) -> bool:
    if len(hand_tiles) != 14:
        return False
    yaochuu = set()
    for t in hand_tiles:
        if t >= 41:
            yaochuu.add(t)
        elif t % 10 == 1 or t % 10 == 9:
            yaochuu.add(t)
    return len(yaochuu) >= 9


def _normalize(tile: int) -> int:
    if tile == 105:
        return 15
    if tile == 205:
        return 25
    if tile == 305:
        return 35
    return tile


def _enumerate_chi_pairs(hand_tiles: List[int], req_a: int, req_b: int) -> List[List[int]]:
    """枚举所有可用于吃的两张真实手牌 ID 组合（含赤 5）。req_a/req_b 为归一化后的牌值。"""
    slots_a = sorted({t for t in hand_tiles if _normalize(t) == req_a})
    slots_b = sorted({t for t in hand_tiles if _normalize(t) == req_b})
    results: List[List[int]] = []
    for a in slots_a:
        for b in slots_b:
            results.append([a, b])
    return results


def check_action_after_cut(self, cut_tile: int):
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    normal_cut = _normalize(cut_tile)

    # 四杠已达上限后，任何玩家不得再开杠（含大明杠）。
    kan_allowed = getattr(self, "total_kans", 0) < 4

    # 每次计算前清空所有玩家吃牌候选，避免跨巡残留
    for p in self.player_list:
        p.chi_candidates = {}

    # 立直宣言后不能吃/碰/杠（除非听牌不变的暗杠，这里作为简化，立直者不主动鸣牌）
    if len(self.tiles_list) > self.dead_wall_count:
        next_player_index = next_current_num(self.current_player_index)
        np = self.player_list[next_player_index]
        if normal_cut < 40 and "riichi" not in np.tag_list:
            hand_norm = [_normalize(t) for t in np.hand_tiles]
            if (normal_cut - 2) in hand_norm and (normal_cut - 1) in hand_norm:
                temp_action_dict[next_player_index].append("chi_left")
                np.chi_candidates["chi_left"] = _enumerate_chi_pairs(np.hand_tiles, normal_cut - 1, normal_cut - 2)
            if (normal_cut - 1) in hand_norm and (normal_cut + 1) in hand_norm:
                temp_action_dict[next_player_index].append("chi_mid")
                np.chi_candidates["chi_mid"] = _enumerate_chi_pairs(np.hand_tiles, normal_cut - 1, normal_cut + 1)
            if (normal_cut + 1) in hand_norm and (normal_cut + 2) in hand_norm:
                temp_action_dict[next_player_index].append("chi_right")
                np.chi_candidates["chi_right"] = _enumerate_chi_pairs(np.hand_tiles, normal_cut + 1, normal_cut + 2)

        for item in self.player_list:
            if item.player_index == self.current_player_index:
                continue
            if "riichi" in item.tag_list:
                continue
            hand_norm = [_normalize(t) for t in item.hand_tiles]
            if hand_norm.count(normal_cut) >= 2:
                temp_action_dict[item.player_index].append("peng")
            if kan_allowed and hand_norm.count(normal_cut) >= 3 and len(self.tiles_list) > self.dead_wall_count:
                temp_action_dict[item.player_index].append("gang")

    for item in self.player_list:
        if item.player_index != self.current_player_index:
            check_hepai(self, temp_action_dict, cut_tile, item.player_index, "ron")

    for i in temp_action_dict:
        if temp_action_dict[i]:
            temp_action_dict[i].append("pass")

    temp_action_dict[self.current_player_index] = []

    self.dihe_possible = False
    return temp_action_dict


def check_action_jiagang(self, jiagang_tile: int):
    """抢杠和检查"""
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    normal_tile = _normalize(jiagang_tile)
    for item in self.player_list:
        if item.player_index == self.current_player_index:
            continue
        if normal_tile in item.waiting_tiles:
            check_hepai(self, temp_action_dict, jiagang_tile, item.player_index, "chankan")
    for i in temp_action_dict:
        if temp_action_dict[i]:
            temp_action_dict[i].append("pass")
    return temp_action_dict


def check_action_hand_action(self, player_index: int, is_get_gang_tile: bool = False, is_first_action: bool = False):
    """摸牌后的手牌动作检查：和牌/暗杠/加杠/立直/切牌"""
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    player_item = self.player_list[player_index]

    # 四杠已达上限后禁止任何开杠
    kan_allowed = getattr(self, "total_kans", 0) < 4
    if kan_allowed and len(self.tiles_list) > self.dead_wall_count:
        # 暗杠：非立直玩家或立直后不改变听牌的暗杠；此处简化处理，立直玩家不允许暗杠
        processed = set()
        if "riichi" not in player_item.tag_list:
            for carditem in player_item.hand_tiles:
                normal = _normalize(carditem)
                if normal in processed:
                    continue
                if sum(1 for t in player_item.hand_tiles if _normalize(t) == normal) == 4:
                    temp_action_dict[player_index].append("angang")
                    processed.add(normal)

            for combination_tile in player_item.combination_tiles:
                if combination_tile[0] == "k":
                    jiagang_tile = int(combination_tile[1:])
                    if jiagang_tile in [_normalize(t) for t in player_item.hand_tiles]:
                        temp_action_dict[player_index].append("jiagang")

    temp_action_dict[player_index].append("cut")

    # 立直宣告：门前清+听牌+点数>=1000+牌墙余>=4+未立直
    can_declare_riichi = (
        _is_menqianqing(player_item.combination_tiles)
        and "riichi" not in player_item.tag_list
        and player_item.score >= 1000
        and (len(self.tiles_list) - self.dead_wall_count) >= 4
    )
    if can_declare_riichi:
        refresh_waiting_tiles_after_cut(self, player_index)
        if player_item.riichi_candidate_cuts:
            temp_action_dict[player_index].append("riichi_cut")

    # 自摸和
    last_tile = player_item.hand_tiles[-1]
    normal_last = _normalize(last_tile)
    if normal_last in player_item.waiting_tiles:
        check_hepai(self, temp_action_dict, last_tile, player_index, "tsumo", is_first_action, is_get_gang_tile)
    else:
        logger.info(
            f"[riichi tsumo skip] player={player_index} last_tile={last_tile} normal={normal_last} "
            f"waiting_tiles={sorted(player_item.waiting_tiles)} hand={player_item.hand_tiles} "
            f"combos={player_item.combination_tiles}"
        )

    return temp_action_dict


def refresh_waiting_tiles(self, player_index: int, is_first_action: bool = False):
    player_item = self.player_list[player_index]
    current_hand = [_normalize(t) for t in player_item.hand_tiles]
    if is_first_action and len(current_hand) == 14:
        current_hand = current_hand[:-1]
    waiting = self.calculation_service.Riichi_tingpai_check(current_hand, player_item.combination_tiles)
    player_item.waiting_tiles = waiting


def refresh_waiting_tiles_after_cut(self, player_index: int):
    """枚举每张手牌切出后的听牌集合，得到立直可切牌及其听牌。"""
    player_item = self.player_list[player_index]
    candidates: Dict[int, list] = {}
    hand = list(player_item.hand_tiles)
    seen = set()
    for i, tile in enumerate(hand):
        key = _normalize(tile)
        if key in seen:
            continue
        seen.add(key)
        temp = hand[:i] + hand[i + 1:]
        temp_norm = [_normalize(t) for t in temp]
        waits = self.calculation_service.Riichi_tingpai_check(temp_norm, player_item.combination_tiles)
        if waits:
            candidates[tile] = sorted(list(waits))
    player_item.riichi_candidate_cuts = candidates


def _is_menqianqing(combination_tiles: list) -> bool:
    for c in combination_tiles:
        if c[0] in ("s", "k", "g"):
            return False
    return True


def _is_furiten(player, discards_own: List[int]) -> bool:
    """自家振听：听牌中任意一种在自家弃牌中出现过。"""
    own_norm = {_normalize(t) for t in discards_own}
    for w in player.waiting_tiles:
        if w in own_norm:
            return True
    return False


def check_hepai(self, temp_action_dict, hepai_tile: int, player_index: int, hepai_type: str, is_first_action: bool = False, is_get_gang_tile: bool = False):
    """调用服务端 mahjong 库进行和牌判定；成功则记入 temp_action_dict。"""
    player = self.player_list[player_index]

    tiles_list = list(player.hand_tiles)
    if hepai_type != "tsumo":
        tiles_list.append(hepai_tile)

    # 荣和方振听判定
    if hepai_type in ("ron", "chankan"):
        if _is_furiten(player, player.discard_tiles) or player.temp_furiten:
            return

    is_haitei = hepai_type == "tsumo" and len(self.tiles_list) <= self.dead_wall_count and not is_get_gang_tile
    is_houtei = hepai_type == "ron" and len(self.tiles_list) <= self.dead_wall_count

    ura_dora = self.ura_dora_indicators + getattr(self, "ura_kan_dora_indicators", []) if "riichi" in player.tag_list else []

    ctx = {
        "is_tsumo": hepai_type == "tsumo",
        "is_riichi": "riichi" in player.tag_list,
        "is_daburu_riichi": "daburu_riichi" in player.tag_list,
        "is_ippatsu": "ippatsu" in player.tag_list,
        "is_rinshan": is_get_gang_tile and hepai_type == "tsumo",
        "is_chankan": hepai_type == "chankan",
        "is_haitei": is_haitei,
        "is_houtei": is_houtei,
        "is_tenhou": is_first_action and hepai_type == "tsumo" and player.player_index == self.dealer_index and not player.combination_tiles,
        "is_chiihou": is_first_action and hepai_type == "tsumo" and player.player_index != self.dealer_index and not player.combination_tiles,
        "player_wind": (player.player_index - self.dealer_index) % 4,
        "round_wind": 0 if self.current_round <= 4 else 1,
        "has_open_tanyao": True,
        "dora_indicators": self.dora_indicators + self.kan_dora_indicators,
        "ura_dora_indicators": ura_dora,
        "aka_count": None,
        "kyoutaku_number": self.riichi_sticks,
        "tsumi_number": self.honba,
    }

    result = self.calculation_service.Riichi_hepai_check(
        tiles_list, player.combination_tiles, [], hepai_tile, ctx
    )

    if not result.get("is_valid"):
        logger.info(
            f"[riichi hepai invalid] player={player_index} type={hepai_type} tile={hepai_tile} "
            f"hand={tiles_list} combos={player.combination_tiles} error={result.get('error')}"
        )
        return

    # 起和番数限制：低于 hepai_limit 的和牌需开启 open_cuohe 才允许宣告，并在结算时走错和流程
    if int(result.get("han", 0)) < int(getattr(self, "hepai_limit", 1)):
        if not getattr(self, "open_cuohe", False):
            logger.info(
                f"[riichi hepai blocked by hepai_limit] player={player_index} type={hepai_type} "
                f"han={result.get('han')} limit={self.hepai_limit} yaku={result.get('yaku')}"
            )
            return

    rel = get_index_relative_position(player.player_index, self.current_player_index)
    if hepai_type == "tsumo":
        temp_action_dict[player.player_index].append("hu_self")
        self.result_dict["hu_self"] = result
    else:
        key = {"left": "hu_first", "top": "hu_second", "right": "hu_third"}.get(rel, "hu_first")
        temp_action_dict[player.player_index].append(key)
        self.result_dict[key] = result
