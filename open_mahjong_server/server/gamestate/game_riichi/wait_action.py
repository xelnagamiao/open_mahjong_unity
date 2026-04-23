"""
立直麻将等待行为：在古典逻辑基础上新增 riichi_cut 动作处理。
"""
import asyncio
import time
import logging

from .action_check import (
    check_action_after_cut,
    check_action_jiagang,
    refresh_waiting_tiles,
)
from .boardcast import broadcast_do_action, broadcast_ready_status, broadcast_declare_riichi
from ..public.logic_common import get_index_relative_position
from ..public.game_record_manager import (
    player_action_record_cut,
    player_action_record_angang,
    player_action_record_jiagang,
    player_action_record_chipenggang,
    player_action_record_riichi,
)

logger = logging.getLogger(__name__)


def _normalize(tile: int) -> int:
    if tile == 105:
        return 15
    if tile == 205:
        return 25
    if tile == 305:
        return 35
    return tile


async def wait_action(self):
    self.waiting_players_list = []
    used_time = 0

    for i in range(4):
        while not self.action_queues[i].empty():
            try:
                self.action_queues[i].get_nowait()
            except Exception:
                break

    for player_index, action_list in self.action_dict.items():
        if action_list:
            self.waiting_players_list.append(player_index)
            self.action_events[player_index].clear()

    player_index = None
    action_data = None
    action_type = None

    timeout_grace = 0 if self.game_status == "waiting_ready" else self.step_time

    while self.waiting_players_list and any(self.player_list[i].remaining_time + timeout_grace > used_time for i in self.waiting_players_list):
        task_list = []
        task_to_player = {}
        for waiting_player_index in self.waiting_players_list:
            t = asyncio.create_task(self.action_events[waiting_player_index].wait())
            task_list.append(t)
            task_to_player[t] = waiting_player_index
        timer_task = asyncio.create_task(asyncio.sleep(1))
        task_list.append(timer_task)

        time_start = time.time()
        done, pending = await asyncio.wait(task_list, return_when=asyncio.FIRST_COMPLETED)
        time_end = time.time()

        for t in pending:
            t.cancel()

        for t in done:
            if t == timer_task:
                used_time += 1
            else:
                temp_player_index = task_to_player[t]
                temp_action_data = await self.action_queues[temp_player_index].get()
                temp_action_type = temp_action_data.get("action_type")
                temp_action_data = dict(temp_action_data)

                used_time += time_end - time_start
                used_int_time = int(used_time)
                if timeout_grace > 0 and used_int_time >= timeout_grace:
                    self.player_list[temp_player_index].remaining_time -= (used_int_time - timeout_grace)

                self.action_dict[temp_player_index] = []
                if temp_player_index in self.waiting_players_list:
                    self.waiting_players_list.remove(temp_player_index)

                do_interrupt = True
                for check_player_index in self.waiting_players_list:
                    for action in self.action_dict[check_player_index]:
                        if self.action_priority[temp_action_type] < self.action_priority[action]:
                            do_interrupt = False

                if not action_data:
                    action_data = dict(temp_action_data)
                    action_type = temp_action_type
                    player_index = temp_player_index
                elif self.action_priority[temp_action_type] > self.action_priority[action_type]:
                    action_data = dict(temp_action_data)
                    action_type = temp_action_type
                    player_index = temp_player_index

                if do_interrupt:
                    self.waiting_players_list = []

    if self.waiting_players_list:
        for i in self.waiting_players_list:
            self.player_list[i].remaining_time = 0

    match self.game_status:
        case "waiting_hand_action":
            if action_data:
                if action_type == "cut":
                    await _do_cut(self, player_index, action_data, is_riichi=False)
                    return
                elif action_type == "riichi_cut":
                    await _do_cut(self, player_index, action_data, is_riichi=True)
                    return
                elif action_type == "angang":
                    angang_tile = action_data.get("target_tile")
                    normal_angang = _normalize(angang_tile)
                    # 暗杠 4 张中可能含有赤 5（105/205/305），按归一化移除并记录真实牌 ID
                    removed = [_remove_by_normal(self.player_list[self.current_player_index].hand_tiles, normal_angang) for _ in range(4)]
                    self.player_list[self.current_player_index].combination_tiles.append(f"G{normal_angang}")
                    mask = [2, removed[0], 2, removed[1], 2, removed[2], 2, removed[3]]
                    self.player_list[self.current_player_index].combination_mask.append(mask)
                    player_action_record_angang(self, angang_tile=normal_angang)
                    await broadcast_do_action(self, action_list=["angang"], action_player=self.current_player_index,
                                              combination_mask=mask, combination_target=f"G{normal_angang}")
                    # 立直一发消失
                    _clear_ippatsu(self)
                    self._last_kan_type = "ankan"
                    self.game_status = "deal_card_after_gang"
                    return
                elif action_type == "jiagang":
                    jiagang_tile = action_data.get("target_tile")
                    normal_jia = _normalize(jiagang_tile)
                    combination_index = -1
                    for i, combo in enumerate(self.player_list[self.current_player_index].combination_tiles):
                        if combo == f"k{normal_jia}":
                            combination_index = i
                            break
                    # 按归一化从手牌中移除加杠牌，得到真实牌 ID（可能是 105/205/305）
                    actual_jia = _remove_by_normal(self.player_list[self.current_player_index].hand_tiles, normal_jia)
                    for i, m in enumerate(self.player_list[self.current_player_index].combination_mask[combination_index]):
                        if m == 1:
                            self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, actual_jia)
                            self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, 3)
                            break

                    self.player_list[self.current_player_index].combination_tiles.remove(f"k{normal_jia}")
                    self.player_list[self.current_player_index].combination_tiles.append(f"g{normal_jia}")
                    player_action_record_jiagang(self, jiagang_tile=normal_jia)
                    await broadcast_do_action(self, action_list=["jiagang"],
                                              action_player=self.current_player_index,
                                              combination_mask=self.player_list[self.current_player_index].combination_mask[combination_index],
                                              combination_target=f"k{normal_jia}")
                    _clear_ippatsu(self)
                    self._last_kan_type = "shouminkan"
                    self.jiagang_tile = normal_jia
                    self.action_dict = check_action_jiagang(self, normal_jia)
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_qianggang"
                    else:
                        self.game_status = "deal_card_after_gang"
                    return
                elif action_type == "hu_self":
                    self.hu_class = "hu_self"
                    self.game_status = "END"
                    return
                elif action_type == "jiuzhongjiupai":
                    self.hu_class = "jiuzhongjiupai"
                    self.game_status = "END"
                    return
                else:
                    logger.error(f"waiting_hand_action 阶段未知动作: {action_type}")
                    return
            else:
                # 超时摸切
                is_moqie = True
                tile_id = self.player_list[self.current_player_index].hand_tiles[-1]
                await _execute_cut(self, self.current_player_index, tile_id, is_moqie, None, is_riichi=False)
                return

        case "waiting_action_after_cut":
            tile_id = self.player_list[self.current_player_index].discard_tiles[-1]
            combination_mask = []
            combination_target = ""
            if action_data:
                refresh_waiting_tiles(self, player_index)
                normal_tile = _normalize(tile_id)
                if action_type == "chi_left":
                    # 组合掩码中记录真实牌 ID（含赤 5 的 105/205/305），便于客户端正确从手牌移除并渲染副露
                    r1, r2 = _pick_chi_pair(self.player_list[player_index], action_type,
                                            normal_tile - 1, normal_tile - 2,
                                            int(action_data.get("chi_combo_index") or 0))
                    self.player_list[player_index].hand_tiles.remove(r1)
                    self.player_list[player_index].hand_tiles.remove(r2)
                    self.player_list[player_index].combination_tiles.append(f"s{normal_tile - 1}")
                    combination_target = f"s{normal_tile - 1}"
                    combination_mask = [1, tile_id, 0, r1, 0, r2]
                elif action_type == "chi_mid":
                    r1, r2 = _pick_chi_pair(self.player_list[player_index], action_type,
                                            normal_tile - 1, normal_tile + 1,
                                            int(action_data.get("chi_combo_index") or 0))
                    self.player_list[player_index].hand_tiles.remove(r1)
                    self.player_list[player_index].hand_tiles.remove(r2)
                    self.player_list[player_index].combination_tiles.append(f"s{normal_tile}")
                    combination_target = f"s{normal_tile}"
                    combination_mask = [1, tile_id, 0, r1, 0, r2]
                elif action_type == "chi_right":
                    r1, r2 = _pick_chi_pair(self.player_list[player_index], action_type,
                                            normal_tile + 1, normal_tile + 2,
                                            int(action_data.get("chi_combo_index") or 0))
                    self.player_list[player_index].hand_tiles.remove(r1)
                    self.player_list[player_index].hand_tiles.remove(r2)
                    self.player_list[player_index].combination_tiles.append(f"s{normal_tile + 1}")
                    combination_target = f"s{normal_tile + 1}"
                    combination_mask = [1, tile_id, 0, r1, 0, r2]
                elif action_type == "peng":
                    r1 = _remove_by_normal(self.player_list[player_index].hand_tiles, normal_tile)
                    r2 = _remove_by_normal(self.player_list[player_index].hand_tiles, normal_tile)
                    self.player_list[player_index].combination_tiles.append(f"k{normal_tile}")
                    rel = get_index_relative_position(player_index, self.current_player_index)
                    combination_target = f"k{normal_tile}"
                    if rel == "left":
                        combination_mask = [1, tile_id, 0, r1, 0, r2]
                    elif rel == "right":
                        combination_mask = [0, r1, 0, r2, 1, tile_id]
                    else:
                        combination_mask = [0, r1, 1, tile_id, 0, r2]
                elif action_type == "gang":
                    r1 = _remove_by_normal(self.player_list[player_index].hand_tiles, normal_tile)
                    r2 = _remove_by_normal(self.player_list[player_index].hand_tiles, normal_tile)
                    r3 = _remove_by_normal(self.player_list[player_index].hand_tiles, normal_tile)
                    self.player_list[player_index].combination_tiles.append(f"g{normal_tile}")
                    rel = get_index_relative_position(player_index, self.current_player_index)
                    combination_target = f"g{normal_tile}"
                    if rel == "left":
                        combination_mask = [1, tile_id, 0, r1, 0, r2, 0, r3]
                    elif rel == "right":
                        combination_mask = [0, r1, 0, r2, 0, r3, 1, tile_id]
                    else:
                        combination_mask = [0, r1, 1, tile_id, 0, r2, 0, r3]
                elif action_type in ("hu_first", "hu_second", "hu_third"):
                    self.player_list[player_index].hand_tiles.append(tile_id)
                    self.hu_class = action_type
                    self.ron_player_index = player_index
                    self.game_status = "END"
                    return

                if action_type in ("chi_left", "chi_mid", "chi_right", "peng", "gang"):
                    self.player_list[self.current_player_index].discard_tiles.pop(-1)
                    self.player_list[self.current_player_index].discard_origin_tiles.append(tile_id)
                    self.player_list[player_index].combination_mask.append(combination_mask)
                    self.current_player_index = player_index
                    player_action_record_chipenggang(self, action_type=action_type, mingpai_tile=tile_id, action_player=player_index)
                    await broadcast_do_action(self, action_list=[action_type], action_player=self.current_player_index,
                                              combination_mask=combination_mask, combination_target=combination_target)
                    _clear_ippatsu(self)
                    if action_type == "gang":
                        self._last_kan_type = "daiminkan"
                        self.game_status = "deal_card_after_gang"
                    else:
                        self.game_status = "onlycut_after_action"
                    return

                if action_type == "pass":
                    _commit_pending_riichi(self)
                    if getattr(self, "_pending_four_kan_abort", False):
                        self.hu_class = "four_kan_abort"
                        self.game_status = "END"
                    else:
                        self.game_status = "deal_card"
                    return
            else:
                _commit_pending_riichi(self)
                if getattr(self, "_pending_four_kan_abort", False):
                    self.hu_class = "four_kan_abort"
                    self.game_status = "END"
                else:
                    self.game_status = "deal_card"
                return

        case "onlycut_after_action":
            if action_data and action_type == "cut":
                is_moqie = action_data.get("cutClass")
                tile_id = action_data.get("TileId")
                await _execute_cut(self, self.current_player_index, tile_id, is_moqie, action_data.get("cutIndex"), is_riichi=False)
                return
            else:
                is_moqie = True
                tile_id = self.player_list[self.current_player_index].hand_tiles[-1]
                await _execute_cut(self, self.current_player_index, tile_id, is_moqie, None, is_riichi=False)
                return

        case "waiting_action_qianggang":
            temp_jiagang_tile = self.jiagang_tile
            self.jiagang_tile = None
            if action_data:
                if action_type in ("hu_first", "hu_second", "hu_third"):
                    self.player_list[player_index].hand_tiles.append(temp_jiagang_tile)
                    self.hu_class = action_type
                    self.ron_player_index = player_index
                    self.game_status = "END"
                    return
                else:
                    # 抢杠和不成立，继续加杠流程：摸岭上、翻宝牌指示
                    self.game_status = "deal_card_after_gang"
                    return
            else:
                self.game_status = "deal_card_after_gang"
                return

        case "waiting_ready":
            if action_data and action_type == "ready":
                await broadcast_ready_status(self)
                return True
            for wait_player_index, wait_actions in self.action_dict.items():
                if "ready" in wait_actions:
                    self.action_dict[wait_player_index] = []
            await broadcast_ready_status(self)
            return False


async def _do_cut(self, player_index: int, action_data: dict, is_riichi: bool):
    is_moqie = action_data.get("cutClass")
    tile_id = action_data.get("TileId")
    cut_tile_index = action_data.get("cutIndex")
    await _execute_cut(self, player_index, tile_id, is_moqie, cut_tile_index, is_riichi=is_riichi)


async def _execute_cut(self, player_index: int, tile_id: int, is_moqie: bool, cut_tile_index, is_riichi: bool):
    """切牌/立直切，更新振听并广播。"""
    player = self.player_list[player_index]
    if tile_id not in player.hand_tiles:
        logger.error(f"tile_id {tile_id} 不在玩家{player_index}手牌 {player.hand_tiles}")
        raise ValueError("非法切牌")

    player.hand_tiles.remove(tile_id)
    player.discard_tiles.append(tile_id)
    player_action_record_cut(self, cut_tile=tile_id, is_moqie=is_moqie)

    if self.current_player_index == 0:
        self.xunmu += 1

    if is_riichi:
        # 立直宣告后并未立刻收取立直棒——若本切牌未被荣和，则在 pass 后结算
        player.pending_riichi = True
        if self.xunmu == 1 and _is_first_discard_untouched(self, player_index):
            player.pending_daburu = True
        if "riichi" not in player.tag_list:
            player.tag_list.append("riichi")
        player.riichi_turn = self.xunmu
        player_action_record_riichi(self, player_index=player_index, is_daburu=player.pending_daburu)
        await broadcast_declare_riichi(self, player_index)

    await broadcast_do_action(self, action_list=["cut"], action_player=self.current_player_index,
                              cut_tile=tile_id, cut_class=is_moqie, cut_tile_index=cut_tile_index)

    # 明杠/加杠的杠宝牌指示牌在"打完牌"之后才翻（标准立直规则）。
    while getattr(self, "_pending_kan_dora_count", 0) > 0:
        self._pending_kan_dora_count -= 1
        await self._reveal_kan_dora()

    refresh_waiting_tiles(self, self.current_player_index)

    # 更新振听标签
    if any(_normalize(t) in player.waiting_tiles for t in player.discard_tiles):
        if "furiten" not in player.tag_list:
            player.tag_list.append("furiten")
    else:
        if "furiten" in player.tag_list:
            player.tag_list.remove("furiten")

    # 清一发
    _clear_ippatsu(self, keep_player_index=player_index if is_riichi else None)

    self.action_dict = check_action_after_cut(self, tile_id)

    # 四杠散了：由 ≥2 家合计 4 杠后，必须等到打完牌无人和牌才触发流局；期间禁止吃/碰/杠/加杠。
    if getattr(self, "_pending_four_kan_abort", False):
        for i in self.action_dict:
            self.action_dict[i] = [a for a in self.action_dict[i] if a in ("hu_first", "hu_second", "hu_third", "pass")]
        has_ron = any(a in ("hu_first", "hu_second", "hu_third") for acts in self.action_dict.values() for a in acts)
        if has_ron:
            self.game_status = "waiting_action_after_cut"
        else:
            _commit_pending_riichi(self)
            self.hu_class = "four_kan_abort"
            self.game_status = "END"
        return

    if any(self.action_dict[i] for i in self.action_dict):
        self.game_status = "waiting_action_after_cut"
    else:
        _commit_pending_riichi(self)
        self.game_status = "deal_card"


def _is_first_discard_untouched(self, player_index: int) -> bool:
    """两立直条件：所有人首巡内无鸣牌"""
    if self.xunmu != 1:
        return False
    for p in self.player_list:
        for c in p.combination_tiles:
            if c[0] in ("s", "k", "g"):
                return False
    return True


def _commit_pending_riichi(self):
    for p in self.player_list:
        if getattr(p, "pending_riichi", False):
            p.pending_riichi = False
            p.score -= 1000
            self.riichi_sticks += 1
            if "ippatsu" not in p.tag_list:
                p.tag_list.append("ippatsu")


def _clear_ippatsu(self, keep_player_index=None):
    for p in self.player_list:
        if keep_player_index is not None and p.player_index == keep_player_index:
            continue
        if "ippatsu" in p.tag_list:
            p.tag_list.remove("ippatsu")


def _pick_chi_pair(player, action_type: str, req_a: int, req_b: int, chi_combo_index: int):
    """按客户端选择的 chi_combo_index 从候选列表中取出 (r1, r2) 两张真实牌 ID。
    若候选不存在（例如 AI/兜底），退化为优先非赤 5 的默认策略。"""
    candidates = (player.chi_candidates or {}).get(action_type) or []
    if candidates:
        idx = chi_combo_index if 0 <= chi_combo_index < len(candidates) else 0
        pair = candidates[idx]
        return pair[0], pair[1]
    # 兜底：依归一化从手牌查找实际牌 ID（优先非赤 5）
    def find_actual(hand, normal):
        for t in hand:
            if t == normal:
                return t
        for t in hand:
            if _normalize(t) == normal:
                return t
        return normal
    return find_actual(player.hand_tiles, req_a), find_actual(player.hand_tiles, req_b)


def _remove_by_normal(hand: list, normal_tile: int) -> int:
    """按归一化后的牌值从手牌移除一张（优先移除非赤 5）。返回被移除的真实牌 ID，未找到返回 normal_tile。"""
    for i, t in enumerate(hand):
        if t == normal_tile:
            hand.pop(i)
            return t
    for i, t in enumerate(hand):
        if _normalize(t) == normal_tile:
            hand.pop(i)
            return t
    logger.error(f"hand 中不存在归一化牌 {normal_tile}, hand={hand}")
    return normal_tile
