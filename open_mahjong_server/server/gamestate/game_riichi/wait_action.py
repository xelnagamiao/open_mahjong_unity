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
    compute_kuikae_forbidden,
    _chi_pair_has_valid_discard,
)
from .boardcast import broadcast_do_action, broadcast_ready_status, broadcast_declare_riichi, broadcast_refresh_player_tag_list
from ..public.logic_common import get_index_relative_position
from ..public.game_record_manager import (
    player_action_record_cut,
    player_action_record_angang,
    player_action_record_jiagang,
    player_action_record_chipenggang,
    player_action_record_riichi,
)
from ..public.hand_action_notify import apply_player_cut
from ..public.hand_slot_utils import (
    clear_draw_slot,
    hand_contains_tile,
    has_draw_slot,
    pick_timeout_discard_tile,
    remove_angang_tiles,
    remove_cut_tile,
    resolve_is_mo_gang,
)
from .ron_resolution import (
    RON_HU_ACTIONS,
    collect_ron_mode,
    record_ron_claim,
    resolve_collected_rons,
    should_interrupt_wait_for_action,
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


def _is_kuikae_forbidden_cut(player, tile_id: int) -> bool:
    return _normalize(tile_id) in {
        _normalize(t) for t in (getattr(player, "kuikae_forbidden_tiles", None) or [])
    }


def _pick_timeout_cut_tile(player) -> int:
    forbidden = {
        _normalize(t) for t in (getattr(player, "kuikae_forbidden_tiles", None) or [])
    }
    for tile_id in reversed(player.hand_tiles):
        if _normalize(tile_id) not in forbidden:
            return tile_id
    return player.hand_tiles[-1]


def _is_valid_cut_action(self, player_index: int, action_data: dict) -> bool:
    action_type = action_data.get("action_type")
    if action_type not in ("cut", "riichi_cut"):
        return True
    player = self.player_list[player_index]
    tile_id = action_data.get("TileId")
    if not hand_contains_tile(player.hand_tiles, tile_id):
        logger.warning(f"丢弃非法切牌：tile_id {tile_id} 不在玩家{player_index}手牌 {player.hand_tiles}")
        return False
    if _is_kuikae_forbidden_cut(player, tile_id):
        logger.warning(f"丢弃食替禁切：player {player_index}, tile_id={tile_id}, forbidden={player.kuikae_forbidden_tiles}")
        return False
    return True


async def wait_action(self):
    self.waiting_players_list = []
    self._pending_ron_claims = {}
    used_time = 0

    for i in range(4):
        while not self.action_queues[i].empty():
            try:
                self.action_queues[i].get_nowait()
            except Exception:
                break

    # 真实玩家立直后若只剩 "cut"，给客户端 0.5 秒提交自动摸切；超时后服务端强制摸切。
    # AI 仍进入自动行为入口，由 AI 统一停顿后提交摸切，保证视觉节奏一致。
    if self.game_status == "waiting_hand_action":
        cur = self.current_player_index
        cur_player = self.player_list[cur]
        is_riichi = "riichi" in cur_player.tag_list or "daburu_riichi" in cur_player.tag_list
        if is_riichi and cur_player.user_id > 10 and self.action_dict.get(cur, []) == ["cut"]:
            self.waiting_players_list = [cur]
            self.action_events[cur].clear()
            try:
                await asyncio.wait_for(self.action_events[cur].wait(), timeout=0.5)
            except asyncio.TimeoutError:
                pass
            if not self.action_queues[cur].empty():
                action_data = await self.action_queues[cur].get()
                if action_data.get("action_type") == "cut":
                    await _do_cut(self, cur, dict(action_data), is_riichi=False)
                    return
            forced_tile = _pick_timeout_cut_tile(cur_player)
            draw_slot = has_draw_slot(cur_player)
            is_moqie = draw_slot
            remove_cut_tile(cur_player.hand_tiles, forced_tile, is_moqie, draw_slot=draw_slot)
            clear_draw_slot(cur_player)
            await _execute_cut(self, cur, forced_tile, is_moqie, None, is_riichi=False, already_removed=True)
            return

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
                if not _is_valid_cut_action(self, temp_player_index, temp_action_data):
                    continue

                used_time += time_end - time_start
                used_int_time = int(used_time)
                if timeout_grace > 0 and used_int_time >= timeout_grace:
                    self.player_list[temp_player_index].remaining_time -= (used_int_time - timeout_grace)

                self.action_dict[temp_player_index] = []
                # 同一批完成任务中可能已有更高优先级操作清空等待列表，因此移除前先确认仍在等待。
                if temp_player_index in self.waiting_players_list:
                    self.waiting_players_list.remove(temp_player_index)

                if temp_action_type in RON_HU_ACTIONS and collect_ron_mode(getattr(self, "hepai_way", "head_bump")):
                    record_ron_claim(self, temp_player_index, temp_action_type)

                do_interrupt = should_interrupt_wait_for_action(
                    self, temp_action_type, self.waiting_players_list, self.action_dict
                )

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
                    player_score = self.player_list[player_index].score
                    if not self._can_declare_riichi_by_score(player_score):
                        logger.warning(
                            f"非法立直（击飞开启且点数不足 1000）：player={player_index}, score={player_score}"
                        )
                        return
                    await _do_cut(self, player_index, action_data, is_riichi=True)
                    return
                elif action_type == "angang":
                    angang_tile = action_data.get("target_tile")
                    normal_angang = _normalize(angang_tile)
                    player = self.player_list[self.current_player_index]
                    hand = player.hand_tiles
                    draw_slot = has_draw_slot(player)
                    is_mo_gang = resolve_is_mo_gang(hand, normal_angang, draw_slot=draw_slot)
                    removed = remove_angang_tiles(hand, normal_angang, draw_slot=draw_slot)
                    clear_draw_slot(player)
                    self.player_list[self.current_player_index].combination_tiles.append(f"G{normal_angang}")
                    mask = [2, removed[0], 0, removed[1], 0, removed[2], 2, removed[3]]
                    self.player_list[self.current_player_index].combination_mask.append(mask)
                    player_action_record_angang(self, angang_tile=normal_angang, is_mo_gang=is_mo_gang,
                                                combination_mask=mask)
                    await broadcast_do_action(self, action_list=["angang"], action_player=self.current_player_index,
                                              combination_mask=mask, combination_target=f"G{normal_angang}",
                                              is_mo_gang=is_mo_gang)
                    await self._broadcast_langyong_tags_if_changed()
                    # 立直一发消失
                    _clear_ippatsu(self)
                    self._last_kan_type = "ankan"
                    self.game_status = "deal_card_after_gang"
                    return
                elif action_type == "jiagang":
                    jiagang_tile = action_data.get("target_tile")
                    normal_jia = _normalize(jiagang_tile)
                    player = self.player_list[self.current_player_index]
                    hand = player.hand_tiles
                    draw_slot = has_draw_slot(player)
                    is_mo_gang = resolve_is_mo_gang(hand, normal_jia, draw_slot=draw_slot)
                    actual_jia = remove_cut_tile(hand, jiagang_tile, is_mo_gang, draw_slot=draw_slot)
                    clear_draw_slot(player)
                    combination_index = -1
                    for i, combo in enumerate(self.player_list[self.current_player_index].combination_tiles):
                        if combo == f"k{normal_jia}":
                            combination_index = i
                            break
                    for i, m in enumerate(self.player_list[self.current_player_index].combination_mask[combination_index]):
                        if m == 1:
                            self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, actual_jia)
                            self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, 3)
                            break

                    self.player_list[self.current_player_index].combination_tiles.remove(f"k{normal_jia}")
                    self.player_list[self.current_player_index].combination_tiles.append(f"g{normal_jia}")
                    player_action_record_jiagang(self, jiagang_tile=normal_jia, is_mo_gang=is_mo_gang)
                    await broadcast_do_action(self, action_list=["jiagang"],
                                              action_player=self.current_player_index,
                                              combination_mask=self.player_list[self.current_player_index].combination_mask[combination_index],
                                              combination_target=f"k{normal_jia}",
                                              is_mo_gang=is_mo_gang)
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
                    await _broadcast_hu_and_end(self, self.current_player_index, "hu_self")
                    return
                elif action_type == "jiuzhongjiupai":
                    self.hu_class = "jiuzhongjiupai"
                    self.game_status = "END"
                    return
                else:
                    logger.error(f"waiting_hand_action 阶段未知动作: {action_type}")
                    return
            else:
                player = self.player_list[self.current_player_index]
                draw_slot = has_draw_slot(player)
                is_moqie = draw_slot
                tile_id = player.hand_tiles[-1] if draw_slot else _pick_timeout_cut_tile(player)
                remove_cut_tile(player.hand_tiles, tile_id, is_moqie, draw_slot=draw_slot)
                clear_draw_slot(player)
                await _execute_cut(self, self.current_player_index, tile_id, is_moqie, None, is_riichi=False, already_removed=True)
                return

        case "waiting_action_after_cut":
            tile_id = self.player_list[self.current_player_index].discard_tiles[-1]
            combination_mask = []
            combination_target = ""
            # 记录本巡有荣和机会的玩家：若最终未荣和，则进入同巡振听（立直家额外锁立直振听）
            ron_eligible_indexes = [
                pi for pi, acts in self.action_dict.items()
                if any(a in ("hu_first", "hu_second", "hu_third") for a in acts)
            ]
            if self._pending_ron_claims:
                if await resolve_collected_rons(self, tile_id, ron_eligible_indexes):
                    return

            if action_data:
                refresh_waiting_tiles(self, player_index)
                normal_tile = _normalize(tile_id)
                if action_type not in ("hu_first", "hu_second", "hu_third", "pass"):
                    _apply_passed_ron_furiten(self, ron_eligible_indexes)
                    if self.sync_furiten_tags():
                        await broadcast_refresh_player_tag_list(self)
                if action_type == "chi_left":
                    # 组合掩码中记录真实牌 ID（含赤 5 的 105/205/305），便于客户端正确从手牌移除并渲染副露
                    r1, r2 = _pick_chi_pair(self.player_list[player_index], action_type,
                                            normal_tile - 1, normal_tile - 2,
                                            int(action_data.get("chi_combo_index") or 0))
                    if self._kuikae_enabled() and not _chi_pair_has_valid_discard(
                        self.player_list[player_index].hand_tiles, action_type, tile_id, r1, r2
                    ):
                        logger.warning(
                            f"非法chi_left（食替无合法切牌）：player={player_index}, tile_id={tile_id}, "
                            f"pair=({r1},{r2}), hand={self.player_list[player_index].hand_tiles}"
                        )
                        return
                    self.player_list[player_index].hand_tiles.remove(r1)
                    self.player_list[player_index].hand_tiles.remove(r2)
                    self.player_list[player_index].combination_tiles.append(f"s{normal_tile - 1}")
                    combination_target = f"s{normal_tile - 1}"
                    combination_mask = [1, tile_id, 0, r1, 0, r2]
                elif action_type == "chi_mid":
                    r1, r2 = _pick_chi_pair(self.player_list[player_index], action_type,
                                            normal_tile - 1, normal_tile + 1,
                                            int(action_data.get("chi_combo_index") or 0))
                    if self._kuikae_enabled() and not _chi_pair_has_valid_discard(
                        self.player_list[player_index].hand_tiles, action_type, tile_id, r1, r2
                    ):
                        logger.warning(
                            f"非法chi_mid（食替无合法切牌）：player={player_index}, tile_id={tile_id}, "
                            f"pair=({r1},{r2}), hand={self.player_list[player_index].hand_tiles}"
                        )
                        return
                    self.player_list[player_index].hand_tiles.remove(r1)
                    self.player_list[player_index].hand_tiles.remove(r2)
                    self.player_list[player_index].combination_tiles.append(f"s{normal_tile}")
                    combination_target = f"s{normal_tile}"
                    combination_mask = [1, tile_id, 0, r1, 0, r2]
                elif action_type == "chi_right":
                    r1, r2 = _pick_chi_pair(self.player_list[player_index], action_type,
                                            normal_tile + 1, normal_tile + 2,
                                            int(action_data.get("chi_combo_index") or 0))
                    if self._kuikae_enabled() and not _chi_pair_has_valid_discard(
                        self.player_list[player_index].hand_tiles, action_type, tile_id, r1, r2
                    ):
                        logger.warning(
                            f"非法chi_right（食替无合法切牌）：player={player_index}, tile_id={tile_id}, "
                            f"pair=({r1},{r2}), hand={self.player_list[player_index].hand_tiles}"
                        )
                        return
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
                    await _broadcast_hu_and_end(self, player_index, action_type, tile_id)
                    return

                if action_type in ("chi_left", "chi_mid", "chi_right", "peng", "gang"):
                    discarder = self.player_list[self.current_player_index]
                    discarder.discard_tiles.pop(-1)
                    # 同步移除横置标记；如果被吃/碰/明杠走的就是立直家刚摆出的横置弃牌，
                    # 则给该玩家 riichi_marker_pending 置位，使其下一张弃牌仍横置（视觉上"续横"）
                    if discarder.discard_riichi_flags:
                        was_horizontal = discarder.discard_riichi_flags.pop(-1)
                        if was_horizontal:
                            discarder.riichi_marker_pending = True
                    self.player_list[player_index].combination_mask.append(combination_mask)
                    clear_draw_slot(self.player_list[player_index])
                    self.current_player_index = player_index
                    player_action_record_chipenggang(self, action_type=action_type, mingpai_tile=tile_id,
                                                     action_player=player_index, combination_mask=combination_mask)
                    await broadcast_do_action(self, action_list=[action_type], action_player=self.current_player_index,
                                              combination_mask=combination_mask, combination_target=combination_target)
                    if self.sync_furiten_tags():
                        await broadcast_refresh_player_tag_list(self)
                    await self._broadcast_langyong_tags_if_changed()
                    _clear_ippatsu(self)
                    # 食替：吃/碰后到本家切牌前不可丢回的牌（吃来源 + 两面搭子的筋）
                    # 浪涌麻将可食替：不设禁切牌，允许吃什么打什么。
                    if self._kuikae_enabled() and action_type in ("chi_left", "chi_mid", "chi_right", "peng"):
                        self.player_list[self.current_player_index].kuikae_forbidden_tiles = compute_kuikae_forbidden(
                            self.player_list[self.current_player_index]
                        )
                    if action_type == "gang":
                        self._last_kan_type = "daiminkan"
                        self.game_status = "deal_card_after_gang"
                    else:
                        self.game_status = "onlycut_after_action"
                    return

                if action_type == "pass":
                    _commit_pending_riichi(self)
                    _apply_passed_ron_furiten(self, ron_eligible_indexes)
                    # 立直振听/同巡振听挂上后立刻同步给客户端，否则 furiten 图标需等到下次广播才显示
                    if self.sync_furiten_tags():
                        await broadcast_refresh_player_tag_list(self)
                    if getattr(self, "_pending_four_kan_abort", False):
                        self.hu_class = "four_kan_abort"
                        self.game_status = "END"
                    else:
                        self.game_status = "deal_card"
                    return
            else:
                _commit_pending_riichi(self)
                _apply_passed_ron_furiten(self, ron_eligible_indexes)
                if self.sync_furiten_tags():
                    await broadcast_refresh_player_tag_list(self)
                if getattr(self, "_pending_four_kan_abort", False):
                    self.hu_class = "four_kan_abort"
                    self.game_status = "END"
                else:
                    self.game_status = "deal_card"
                return

        case "onlycut_after_action":
            if action_data and action_type == "cut":
                await _do_cut(self, self.current_player_index, action_data, is_riichi=False)
                return
            else:
                player = self.player_list[self.current_player_index]
                forbidden = {
                    _normalize(t) for t in (getattr(player, "kuikae_forbidden_tiles", None) or [])
                }
                tile_id = pick_timeout_discard_tile(player.hand_tiles, forbidden)
                remove_cut_tile(player.hand_tiles, tile_id, False, draw_slot=False)
                clear_draw_slot(player)
                await _execute_cut(self, self.current_player_index, tile_id, False, None, is_riichi=False, already_removed=True)
                return

        case "waiting_action_qianggang":
            # 抢杠和：放过抢杠机会的玩家与放过荣和同等处理——同巡振听；立直家则永久振听到本局结束
            chankan_eligible_indexes = [
                pi for pi, acts in self.action_dict.items()
                if any(a in ("hu_first", "hu_second", "hu_third") for a in acts)
            ]
            temp_jiagang_tile = self.jiagang_tile
            self.jiagang_tile = None
            if self._pending_ron_claims:
                if await resolve_collected_rons(self, temp_jiagang_tile, chankan_eligible_indexes):
                    return
            if action_data:
                if action_type in ("hu_first", "hu_second", "hu_third"):
                    await _broadcast_hu_and_end(self, player_index, action_type, temp_jiagang_tile)
                    return
                else:
                    # 抢杠和不成立，继续加杠流程：摸岭上、翻宝牌指示；放过的玩家挂同巡/立直振听
                    _apply_passed_ron_furiten(self, chankan_eligible_indexes)
                    if self.sync_furiten_tags():
                        await broadcast_refresh_player_tag_list(self)
                    self.game_status = "deal_card_after_gang"
                    return
            else:
                _apply_passed_ron_furiten(self, chankan_eligible_indexes)
                if self.sync_furiten_tags():
                    await broadcast_refresh_player_tag_list(self)
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


async def _broadcast_hu_and_end(self, player_index: int, action_type: str, tile_id: int | None = None):
    """广播和牌/荣和行动后再进入 END，客户端可立即播放荣/自摸语音与动作文字。"""
    if tile_id is not None:
        self.player_list[player_index].hand_tiles.append(tile_id)
    await broadcast_do_action(self, action_list=[action_type], action_player=player_index)
    self.hu_class = action_type
    if action_type in ("hu_first", "hu_second", "hu_third"):
        self.ron_player_index = player_index
    self.game_status = "END"


async def _do_cut(self, player_index: int, action_data: dict, is_riichi: bool):
    player = self.player_list[player_index]
    if _is_kuikae_forbidden_cut(player, action_data.get("TileId")):
        logger.error(
            f"player {player_index} 试图食替切回禁牌 {action_data.get('TileId')}, "
            f"forbidden={player.kuikae_forbidden_tiles}"
        )
        return False
    cut_result = await apply_player_cut(self, player_index, action_data)
    if cut_result is None:
        return False
    removed, is_moqie, cut_tile_index = cut_result
    return await _execute_cut(
        self, player_index, removed, is_moqie, cut_tile_index, is_riichi=is_riichi, already_removed=True
    )


async def _execute_cut(
    self,
    player_index: int,
    tile_id: int,
    is_moqie: bool,
    cut_tile_index,
    is_riichi: bool,
    already_removed: bool = False,
):
    """切牌/立直切，更新振听并广播。
    立直家被鸣后续切的牌也需横置，由 player.riichi_marker_pending 触发——和 is_riichi 任一为真都会让本次弃牌标记为横置。"""
    player = self.player_list[player_index]
    if not already_removed:
        if not hand_contains_tile(player.hand_tiles, tile_id):
            logger.error(f"tile_id {tile_id} 不在玩家{player_index}手牌 {player.hand_tiles}")
            return False
        if _is_kuikae_forbidden_cut(player, tile_id):
            logger.error(f"player {player_index} 试图食替切回禁牌 {tile_id}, forbidden={player.kuikae_forbidden_tiles}")
            return False
        removed = remove_cut_tile(
            player.hand_tiles, tile_id, is_moqie, draw_slot=has_draw_slot(player)
        )
        if removed is None:
            logger.error(f"remove_cut_tile 失败 player={player_index} tile_id={tile_id}")
            return False
        tile_id = removed
        clear_draw_slot(player)

    horizontal_flag = bool(is_riichi or player.riichi_marker_pending)

    player.discard_tiles.append(tile_id)
    player.discard_origin_tiles.append(tile_id)
    player.discard_riichi_flags.append(horizontal_flag)
    player.kuikae_forbidden_tiles = []
    player.riichi_marker_pending = False
    player_action_record_cut(self, cut_tile=tile_id, is_moqie=is_moqie, is_riichi_horizontal=horizontal_flag)

    if self.current_player_index == 0:
        self.xunmu += 1

    if not is_riichi and "ippatsu" in player.tag_list:
        player.tag_list.remove("ippatsu")

    if is_riichi:
        # 立直宣告后并未立刻收取立直棒——若本切牌未被荣和，则在 pass 后结算
        player.pending_riichi = True
        if self.xunmu == 1 and _is_first_discard_untouched(self, player_index):
            player.pending_daburu = True
        if "riichi" not in player.tag_list:
            player.tag_list.append("riichi")
        player.riichi_turn = self.xunmu
        player_action_record_riichi(self, player_index=player_index, is_daburu=player.pending_daburu)
        await broadcast_declare_riichi(self, player_index, is_daburu=player.pending_daburu)

    await broadcast_do_action(self, action_list=["cut"], action_player=self.current_player_index,
                              cut_tile=tile_id, cut_class=is_moqie, cut_tile_index=cut_tile_index,
                              is_riichi_horizontal=horizontal_flag)

    # 明杠/加杠的杠宝牌指示牌在"打完牌"之后才翻（标准立直规则）。
    while getattr(self, "_pending_kan_dora_count", 0) > 0:
        self._pending_kan_dora_count -= 1
        await self._reveal_kan_dora()

    refresh_waiting_tiles(self, self.current_player_index)

    # 自家出牌后解除同巡振听，永久/立直振听仍由 sync_furiten_tags 保留。
    player.temp_furiten = False

    # 自家切牌后由 sync_furiten_tags 统一调整 furiten tag（永久/同巡/立直振听归一显示）
    if self.sync_furiten_tags():
        await broadcast_refresh_player_tag_list(self)

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
    return True


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
            if not self._can_declare_riichi_by_score(p.score):
                logger.warning(
                    f"跳过立直棒结算（击飞开启且点数不足 1000）：player={p.player_index}, score={p.score}"
                )
                p.pending_riichi = False
                continue
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


def _apply_passed_ron_furiten(self, ron_eligible_indexes):
    """有荣和机会但未在本巡和牌的玩家进入同巡振听；立直家则永久振听到本局结束。"""
    if not ron_eligible_indexes:
        return
    for pi in ron_eligible_indexes:
        p = self.player_list[pi]
        p.temp_furiten = True
        if "riichi" in p.tag_list or "daburu_riichi" in p.tag_list:
            p.riichi_furiten = True


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
