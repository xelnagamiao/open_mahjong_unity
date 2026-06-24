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
        n = _normalize(t)
        if n in YAOCHUU:
            yaochuu.add(n)
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


def _kuikae_forbidden_norms_for_chi(chi_type: str, called_tile_norm: int) -> set:
    """指定吃法与鸣牌归一化牌号，返回食替禁切归一化集合（与 compute_kuikae_forbidden 口径一致）。"""
    forbidden = set(_expand_with_red_dora(called_tile_norm))
    if called_tile_norm >= 40:
        return forbidden
    swap = None
    if chi_type == "chi_left":
        swap = called_tile_norm - 3
    elif chi_type == "chi_right":
        swap = called_tile_norm + 3
    if swap is not None:
        suit = called_tile_norm // 10
        if swap // 10 == suit and 1 <= swap % 10 <= 9:
            forbidden.update(_expand_with_red_dora(swap))
    return forbidden


def _chi_pair_has_valid_discard(
    hand_tiles: List[int], chi_type: str, called_tile: int, r1: int, r2: int
) -> bool:
    """吃完并移除 r1/r2 后，是否仍存在至少一张可合法切出的牌（非食替禁张）。"""
    remaining = list(hand_tiles)
    remaining.remove(r1)
    remaining.remove(r2)
    forbidden = {_normalize(t) for t in _kuikae_forbidden_norms_for_chi(chi_type, _normalize(called_tile))}
    return any(_normalize(t) not in forbidden for t in remaining)


def _filter_chi_pairs_by_kuikae(
    hand_tiles: List[int], chi_type: str, called_tile: int, pairs: List[List[int]]
) -> List[List[int]]:
    """过滤掉吃完后无合法切牌的吃牌候选组合。"""
    return [
        pair for pair in pairs
        if _chi_pair_has_valid_discard(hand_tiles, chi_type, called_tile, pair[0], pair[1])
    ]


def _maybe_add_chi_action(
    player, cut_tile: int, chi_type: str, req_a: int, req_b: int, temp_action_dict: Dict[int, list],
    enforce_kuikae: bool = True,
) -> None:
    """若存在至少一组合法吃牌候选，才向 action_dict 加入对应吃操作。
    enforce_kuikae=False（浪涌麻将可食替）时不按食替过滤，保留全部吃牌候选。"""
    pairs = _enumerate_chi_pairs(player.hand_tiles, req_a, req_b)
    if enforce_kuikae:
        valid_pairs = _filter_chi_pairs_by_kuikae(player.hand_tiles, chi_type, cut_tile, pairs)
    else:
        valid_pairs = pairs
    if not valid_pairs:
        return
    temp_action_dict[player.player_index].append(chi_type)
    player.chi_candidates[chi_type] = valid_pairs


def check_action_after_cut(self, cut_tile: int):
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    normal_cut = _normalize(cut_tile)

    # 四杠已达上限后，任何玩家不得再开杠（含大明杠）。
    kan_allowed = getattr(self, "total_kans", 0) < 4

    # 每次计算前清空所有玩家吃牌候选，避免跨巡残留
    for p in self.player_list:
        p.chi_candidates = {}

    # 浪涌麻将 / 房间开启食替：吃牌候选不按食替过滤
    enforce_kuikae = self._kuikae_enabled()

    # 立直宣言后不能吃/碰/杠（除非听牌不变的暗杠，这里作为简化，立直者不主动鸣牌）
    if len(self.tiles_list) > self.dead_wall_count:
        next_player_index = next_current_num(self.current_player_index)
        np = self.player_list[next_player_index]
        if normal_cut < 40 and "riichi" not in np.tag_list:
            hand_norm = [_normalize(t) for t in np.hand_tiles]
            if (normal_cut - 2) in hand_norm and (normal_cut - 1) in hand_norm:
                _maybe_add_chi_action(np, cut_tile, "chi_left", normal_cut - 1, normal_cut - 2, temp_action_dict, enforce_kuikae)
            if (normal_cut - 1) in hand_norm and (normal_cut + 1) in hand_norm:
                _maybe_add_chi_action(np, cut_tile, "chi_mid", normal_cut - 1, normal_cut + 1, temp_action_dict, enforce_kuikae)
            if (normal_cut + 1) in hand_norm and (normal_cut + 2) in hand_norm:
                _maybe_add_chi_action(np, cut_tile, "chi_right", normal_cut + 1, normal_cut + 2, temp_action_dict, enforce_kuikae)

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

    # 与国标一致：仅当舍牌在听牌表中才进入和牌判定，判定前先刷新听牌
    for item in self.player_list:
        if item.player_index == self.current_player_index:
            continue
        if normal_cut not in item.waiting_tiles:
            continue
        refresh_waiting_tiles(self, item.player_index)
        if normal_cut in item.waiting_tiles:
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
        if normal_tile not in item.waiting_tiles:
            continue
        refresh_waiting_tiles(self, item.player_index)
        if normal_tile in item.waiting_tiles:
            check_hepai(self, temp_action_dict, jiagang_tile, item.player_index, "chankan")
    for i in temp_action_dict:
        if temp_action_dict[i]:
            temp_action_dict[i].append("pass")
    return temp_action_dict


def check_action_hand_action(self, player_index: int, is_get_gang_tile: bool = False, is_first_action: bool = False):
    """摸牌后的手牌动作检查：和牌/暗杠/加杠/立直/切牌。
    立直已宣告者：不允许加杠，仅允许「不改变听牌张集合且不改变听牌结构」的暗杠；其余仅可切牌（强制摸切由主循环处理）。
    """
    temp_action_dict: Dict[int, list] = {0: [], 1: [], 2: [], 3: []}
    player_item = self.player_list[player_index]
    is_riichi = "riichi" in player_item.tag_list

    # 四杠已达上限后禁止任何开杠
    kan_allowed = getattr(self, "total_kans", 0) < 4
    if kan_allowed and len(self.tiles_list) > self.dead_wall_count:
        processed = set()
        for carditem in player_item.hand_tiles:
            normal = _normalize(carditem)
            if normal in processed:
                continue
            if sum(1 for t in player_item.hand_tiles if _normalize(t) == normal) != 4:
                continue
            if is_riichi:
                # 立直家暗杠：必须满足摸到的就是杠的牌（不能改变手牌结构）且杠后听牌集合不变
                last_norm = _normalize(player_item.hand_tiles[-1])
                if last_norm != normal:
                    processed.add(normal)
                    continue
                test_hand = [_normalize(t) for t in player_item.hand_tiles if _normalize(t) != normal]
                test_combos = list(player_item.combination_tiles) + [f"G{normal}"]
                new_waits = self.calculation_service.Riichi_tingpai_check(test_hand, test_combos)
                if new_waits == player_item.waiting_tiles:
                    temp_action_dict[player_index].append("angang")
                processed.add(normal)
            else:
                temp_action_dict[player_index].append("angang")
                processed.add(normal)

        # 加杠仅非立直家可用
        if not is_riichi:
            for combination_tile in player_item.combination_tiles:
                if combination_tile[0] == "k":
                    jiagang_tile = int(combination_tile[1:])
                    if jiagang_tile in [_normalize(t) for t in player_item.hand_tiles]:
                        temp_action_dict[player_index].append("jiagang")

    temp_action_dict[player_index].append("cut")

    # 立直宣告：门前清 + 听牌 + 击飞时点数须≥1000 + 未立直；并要求剩余可摸牌 ≥4（即剩余 ≤3 时禁止立直），
    # 与广播给客户端的 remain_tiles = max(0, len(tiles_list) - dead_wall_count) 同一口径。
    can_declare_riichi = (
        _is_menqianqing(player_item.combination_tiles)
        and not is_riichi
        and self._can_declare_riichi_by_score(player_item.score)
        and (len(self.tiles_list) - self.dead_wall_count) >= 4
    )
    if can_declare_riichi:
        refresh_waiting_tiles_after_cut(self, player_index)
        if player_item.riichi_candidate_cuts:
            temp_action_dict[player_index].append("riichi_cut")

    # 自摸和：与国标 check_action_hand_action 相同，依赖发牌前已刷新的 waiting_tiles
    last_tile = player_item.hand_tiles[-1]
    normal_last = _normalize(last_tile)
    if normal_last in player_item.waiting_tiles:
        check_hepai(self, temp_action_dict, last_tile, player_index, "tsumo", is_first_action, is_get_gang_tile)

    return temp_action_dict


def refresh_waiting_tiles(self, player_index: int, is_first_action: bool = False):
    """刷新听牌表。与国标相同：在摸牌前调用（13 张手牌）；归一化后写入 waiting_tiles。"""
    player_item = self.player_list[player_index]
    current_hand = [_normalize(t) for t in player_item.hand_tiles]
    # 听牌算法输入应为 13 张（或 3n+1）；若误在摸牌后 14 张时调用则去掉刚摸入的一张
    if len(current_hand) == 14:
        current_hand = current_hand[:-1]
    waiting = self.calculation_service.Riichi_tingpai_check(current_hand, player_item.combination_tiles)
    if waiting != player_item.waiting_tiles:
        player_item.waiting_tiles = waiting
        logger.info(f"玩家{player_index}的等待牌更新为{sorted(player_item.waiting_tiles)}")


def refresh_waiting_tiles_after_cut(self, player_index: int):
    """枚举每张手牌切出后的听牌集合，得到立直可切牌及其听牌。
    普通 5 与赤 5 听牌结构相同，但客户端需按真实 tile_id 选牌，故同归一化值的每张实牌都要列入候选。
    """
    player_item = self.player_list[player_index]
    candidates: Dict[int, list] = {}
    hand = list(player_item.hand_tiles)
    checked_norm: Dict[int, list] = {}
    for i, tile in enumerate(hand):
        key = _normalize(tile)
        if key not in checked_norm:
            temp = hand[:i] + hand[i + 1:]
            temp_norm = [_normalize(t) for t in temp]
            waits = self.calculation_service.Riichi_tingpai_check(temp_norm, player_item.combination_tiles)
            checked_norm[key] = sorted(list(waits)) if waits else []
        waits = checked_norm[key]
        if waits:
            candidates[tile] = waits
    player_item.riichi_candidate_cuts = candidates


def _is_menqianqing(combination_tiles: list) -> bool:
    for c in combination_tiles:
        if c[0] in ("s", "k", "g"):
            return False
    return True


def _is_furiten(player) -> bool:
    """自家振听：听牌中任意一种在自家理论弃牌（discard_origin_tiles）中出现过。"""
    own_norm = {_normalize(t) for t in player.discard_origin_tiles}
    for w in player.waiting_tiles:
        if w in own_norm:
            return True
    return False


def _expand_with_red_dora(tile_norm: int) -> List[int]:
    """对 5m/5p/5s 同时包含赤 5 实牌 id，使红 5 与普通 5 都纳入禁切判断。"""
    if tile_norm == 15:
        return [15, 105]
    if tile_norm == 25:
        return [25, 205]
    if tile_norm == 35:
        return [35, 305]
    return [tile_norm]


def compute_kuikae_forbidden(player) -> List[int]:
    """日麻食替禁切：吃/碰后到本家切出前，只禁两类牌——
    - X 自身（鸣来源；吃/碰的弃牌张，立即丢回等同没鸣，必禁）
    - 仅当用「两面搭子」吃时（chi_left = X 在副露最右端，X-1/X-2 自手；chi_right = X 在副露最左端，X+1/X+2 自手）：
      额外禁同色「另一面」X∓3（与副露另一头连成新顺子）。
    嵌张吃（chi_mid）与碰（peng）只禁 X，不禁筋牌。
    集合内同时包含普通牌与赤 5 实牌 id（如 35 与 305），让红 5 与普通 5 等同看待。
    """
    if not player.combination_tiles or not player.combination_mask:
        return []
    last_combo = player.combination_tiles[-1]
    last_mask = player.combination_mask[-1]
    if not last_combo or not last_mask:
        return []
    # mingpai 在 mask 中以 flag=1 标识；不存在则不是吃/碰副露（如暗杠/加杠）
    mingpai_tile = None
    for i in range(0, len(last_mask), 2):
        if last_mask[i] == 1:
            mingpai_tile = last_mask[i + 1]
            break
    if mingpai_tile is None:
        return []
    normal = _normalize(mingpai_tile)
    forbidden = set(_expand_with_red_dora(normal))
    if normal >= 40:
        return sorted(forbidden)
    head = last_combo[0]
    if head != "s":
        # 碰 (k...) / 杠 (g/G...) 仅禁 X
        return sorted(forbidden)
    try:
        mid = int(last_combo[1:])
    except ValueError:
        return sorted(forbidden)
    suit = normal // 10
    cand = None
    # X 在副露最左端 (chi_right): mid = X+1 → 禁 X+3；要求与 X 同色且数值 1..9
    if mid - normal == 1:
        cand = normal + 3
    # X 在副露最右端 (chi_left): mid = X-1 → 禁 X-3
    elif normal - mid == 1:
        cand = normal - 3
    if cand is not None and cand // 10 == suit and 1 <= cand % 10 <= 9:
        for tid in _expand_with_red_dora(cand):
            forbidden.add(tid)
    # mid == normal 即嵌张 (chi_mid)：仅禁 X，不再追加
    return sorted(forbidden)


def check_hepai(self, temp_action_dict, hepai_tile: int, player_index: int, hepai_type: str, is_first_action: bool = False, is_get_gang_tile: bool = False):
    """调用服务端 mahjong 库进行和牌判定；成功则记入 temp_action_dict。"""
    player = self.player_list[player_index]

    tiles_list = list(player.hand_tiles)
    if hepai_type != "tsumo":
        tiles_list.append(hepai_tile)

    # 荣和方振听判定（永久 / 同巡 / 立直）：任一成立都不能荣和/抢杠和（只能自摸），按规则正确拦截
    if hepai_type in ("ron", "chankan"):
        permanent_furiten = _is_furiten(player)
        if permanent_furiten or player.temp_furiten or getattr(player, "riichi_furiten", False):
            logger.info(
                f"[riichi furiten block] player={player_index} type={hepai_type} tile={hepai_tile} "
                f"permanent={permanent_furiten} temp={player.temp_furiten} "
                f"riichi_furiten={getattr(player, 'riichi_furiten', False)} "
                f"waiting={sorted(player.waiting_tiles)} discards={player.discard_origin_tiles}"
            )
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
        "is_tenhou": is_first_action and hepai_type == "tsumo" and player.player_index == 0 and not player.combination_tiles,
        "is_chiihou": is_first_action and hepai_type == "tsumo" and player.player_index != 0 and not player.combination_tiles,
        "player_wind": player.player_index,
        "round_wind": (self.current_round - 1) // 4 % 4,
        "has_open_tanyao": True,
        "dora_indicators": self.dora_indicators + self.kan_dora_indicators,
        "ura_dora_indicators": ura_dora,
        "aka_count": None,
        "kyoutaku_number": self.riichi_sticks,
        "tsumi_number": self.honba,
    }

    result = self.calculation_service.Riichi_hepai_check(
        tiles_list, player.combination_tiles, [], hepai_tile, ctx,
        combination_masks=player.combination_mask,
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
