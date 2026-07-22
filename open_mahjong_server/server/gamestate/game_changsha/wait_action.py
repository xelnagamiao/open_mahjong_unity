# 等待玩家操作处理
import asyncio
import time
import logging
from .action_check import check_action_after_cut, check_action_after_batch_gang_forced_cut, check_action_jiagang, refresh_waiting_tiles
from .boardcast import broadcast_do_action, broadcast_ready_status, broadcast_ask_other_action
from ..public.logic_common import get_index_relative_position, next_current_num
from ..public.game_record_manager import (
    player_action_record_cut,
    player_action_record_angang,
    player_action_record_jiagang,
    player_action_record_chipenggang,
    flush_unexecuted_claim_applications,
)
from ..public.hand_action_notify import apply_player_cut
from ..public.hand_slot_utils import (
    clear_draw_slot,
    has_draw_slot,
    normalize_tile,
    pick_timeout_discard_tile,
    remove_angang_tiles,
    remove_cut_tile,
    resolve_is_mo_gang,
)
from ..public.claim_protection import (
    begin_claim_protection_interval,
    finalize_claim_protection,
    )
from ..public.tactical_claim import (
    init_tactical_round_state,
    apply_tactical_claim_if_needed,
)
from ..public.ask_timing import get_ask_elapsed, note_ask_delivered
from .boardcast import _send_do_action_payload_to_viewer

logger = logging.getLogger(__name__)

def _find_jiagang_combination_index(player, normal_tile: int) -> int:
    for i, combination in enumerate(player.combination_tiles):
        if combination.startswith("k") and normalize_tile(int(combination[1:])) == normal_tile:
            return i
    return -1

def _has_jiagang_target(player, normal_tile: int) -> bool:
    return _find_jiagang_combination_index(player, normal_tile) >= 0

def _consume_forced_gang_cut_tiles(self, player_index: int):
    forced_tiles = list(getattr(self, "forced_cut_tiles", []) or [])
    forced_tile = getattr(self, "forced_cut_tile", None)
    if not forced_tiles and forced_tile is not None:
        forced_tiles = [forced_tile]
    if not forced_tiles:
        return []

    hand = self.player_list[player_index].hand_tiles
    for tile in forced_tiles:
        if tile in hand:
            hand.remove(tile)
        else:
            logger.warning(
                f"Missing forced gang cut tile in hand: player={player_index}, tile={tile}, hand={hand}"
            )
    clear_draw_slot(self.player_list[player_index])
    self.forced_cut_tile = None
    self.forced_cut_tiles = []
    return forced_tiles


async def _apply_open_kong_locked_cut(self, player_index: int, action_data: dict):
    """开杠锁定期间始终切掉摸牌槽中的牌。"""
    player = self.player_list[player_index]
    if not getattr(player, "open_kong_locked", False) or not has_draw_slot(player):
        return await apply_player_cut(self, player_index, action_data)

    if not player.hand_tiles:
        logger.error("开杠锁定出牌失败：玩家 %s 手牌为空", player_index)
        return None

    draw_tile = player.hand_tiles[-1]
    requested_tile = action_data.get("TileId")
    if requested_tile != draw_tile or not action_data.get("cutClass"):
        logger.warning(
            "玩家 %s 开杠后尝试打出 %s，服务端强制改为摸牌 %s",
            player_index,
            requested_tile,
            draw_tile,
        )

    locked_action_data = dict(action_data)
    locked_action_data["TileId"] = draw_tile
    locked_action_data["cutClass"] = True
    locked_action_data["cutIndex"] = len(player.hand_tiles) - 1
    return await apply_player_cut(self, player_index, locked_action_data)

def _remove_claimed_discard(discard_tiles, tile_id):
    for i in range(len(discard_tiles) - 1, -1, -1):
        if discard_tiles[i] == tile_id:
            discard_tiles.pop(i)
            return
    if discard_tiles:
        discard_tiles.pop(-1)

async def _execute_angang_replacement(self, player_index: int, target_tile: int, broadcast_action: str, replacement_count: int, forced_discard: bool) -> None:
    normal_angang = normalize_tile(target_tile)
    player = self.player_list[player_index]
    hand = player.hand_tiles
    draw_slot = has_draw_slot(player)
    is_mo_gang = resolve_is_mo_gang(hand, normal_angang, draw_slot=draw_slot)
    removed = remove_angang_tiles(hand, normal_angang, draw_slot=draw_slot)
    clear_draw_slot(player)
    player.combination_tiles.append(f"G{normal_angang}")
    if forced_discard:
        player.open_kong_locked = True
    add_combination_mask = [0, removed[0], 0, removed[1], 0, removed[2], 0, removed[3]]
    player.combination_mask.append(add_combination_mask)
    player_action_record_angang(
        self,
        angang_tile=normal_angang,
        is_mo_gang=is_mo_gang,
        combination_mask=add_combination_mask,
    )
    await broadcast_do_action(
        self,
        action_list=[broadcast_action],
        action_player=player_index,
        combination_mask=add_combination_mask,
        combination_target=f"G{normal_angang}",
        is_mo_gang=is_mo_gang,
    )
    self.prepare_gang_replacement(replacement_count, forced_discard)
    self.game_status = "deal_card_after_gang"

async def _execute_jiagang_replacement(self, player_index: int, target_tile: int, broadcast_action: str, replacement_count: int, forced_discard: bool) -> None:
    normal_jia = normalize_tile(target_tile)
    player = self.player_list[player_index]
    hand = player.hand_tiles
    draw_slot = has_draw_slot(player)
    is_mo_gang = resolve_is_mo_gang(hand, normal_jia, draw_slot=draw_slot)
    actual_jia = remove_cut_tile(hand, target_tile, is_mo_gang, draw_slot=draw_slot)
    clear_draw_slot(player)

    combination_index = _find_jiagang_combination_index(player, normal_jia)
    if combination_index < 0:
        logger.error(
            f"非法jiagang：未找到可加杠的刻子 normal_jia={normal_jia}, combination_tiles={player.combination_tiles}"
        )
        self.game_status = "deal_card"
        return

    for i, mask in enumerate(player.combination_mask[combination_index]):
        if mask == 1:
            player.combination_mask[combination_index].insert(i, actual_jia)
            player.combination_mask[combination_index].insert(i, 3)
            break

    player.combination_tiles[combination_index] = f"g{normal_jia}"
    if forced_discard:
        player.open_kong_locked = True
    player_action_record_jiagang(self, jiagang_tile=normal_jia, is_mo_gang=is_mo_gang)

    await broadcast_do_action(
        self,
        action_list=[broadcast_action],
        action_player=player_index,
        combination_mask=player.combination_mask[combination_index],
        combination_target=f"k{normal_jia}",
        is_mo_gang=is_mo_gang,
    )

    self.jiagang_tile = normal_jia
    self.prepare_gang_replacement(replacement_count, forced_discard)
    self.action_dict = check_action_jiagang(self, normal_jia)
    if any(self.action_dict[i] for i in self.action_dict):
        self.game_status = "waiting_action_qianggang"
    else:
        self.game_status = "deal_card_after_gang"

# 等待玩家行动
async def wait_action(self):
    self.waiting_players_list = [] # [2,3]

    # 清空所有队列，防止上一轮残留的事件影响新一轮
    for i in range(4):
        while not self.action_queues[i].empty():
            try:
                self.action_queues[i].get_nowait()
                logger.debug(f"清空玩家{i}队列中的残留事件")
            except:
                break

    # 遍历所有可行动玩家，获取行动玩家列表和等待时间列表
    for player_index, action_list in self.action_dict.items():
        if action_list:  # 如果玩家有可用操作 将玩家加入列表并重置事件状态
            self.waiting_players_list.append(player_index)
            self.action_events[player_index].clear()

    for player_index in self.waiting_players_list:
        note_ask_delivered(self, player_index)

    init_tactical_round_state(self)

    # 如果等待玩家列表不为空且有玩家剩余时间小于(已用时间-步时)，则停止等待
    player_index = None # 保存操作玩家索引 (如果玩家有操作则左侧三个变量有值 否则为None)
    action_data = None # 保存操作数据
    action_type = None # 保存操作类型

    # waiting_ready
    timeout_grace = 0 if self.game_status == "waiting_ready" else self.step_time

    while self.waiting_players_list and any(
        self.player_list[i].remaining_time + timeout_grace > get_ask_elapsed(self, i)
        for i in self.waiting_players_list
    ):

        # 给每个可行动者创建一个消息队列任务，同时创建一个计时器任务
        task_list = []  # 任务列表
        task_to_player = {}  # 任务与玩家的映射

        for waiting_player_index in self.waiting_players_list:
            # 为可以行动的玩家添加行动任务
            action_task = asyncio.create_task(self.action_events[waiting_player_index].wait())
            task_list.append(action_task)
            task_to_player[action_task] = waiting_player_index  # 建立映射 行动任务 → 玩家索引
        # 添加计时器任务
        timer_task = asyncio.create_task(asyncio.sleep(1)) # 等待1s
        task_list.append(timer_task)

        # 等待计时器完成1s等待或者任意玩家进行操作
        done, pending = await asyncio.wait(
            task_list,
            return_when=asyncio.FIRST_COMPLETED
        )

        # 取消未完成的任务
        for task in pending:
            task.cancel()

        # 处理完成的任务
        for task in done:
            # 计时器完成：仅周期性重检 timeout
            if task == timer_task:
                continue
            # 玩家操作完成，获取玩家索引
            else:
                # 使用映射获取玩家索引
                temp_player_index = task_to_player[task]
                temp_action_data = await self.action_queues[temp_player_index].get() # 获取操作数据
                temp_action_type = temp_action_data.get("action_type") # 获取操作类型
                allowed_actions_before = list(self.action_dict.get(temp_player_index, []))

                # 复制字典以避免引用问题
                temp_action_data = dict(temp_action_data)
                logger.debug(f"复制后: temp_player_index={temp_player_index}, temp_action_data={temp_action_data}")

                used_int_time = int(get_ask_elapsed(self, temp_player_index))
                if timeout_grace > 0 and used_int_time >= timeout_grace: # 扣除玩家超出步时的时间
                    self.player_list[temp_player_index].remaining_time -= (used_int_time - timeout_grace)

                if temp_action_type == "pass" and hasattr(self, "record_hu_pass"):
                    self.record_hu_pass(temp_player_index, allowed_actions_before)

                self.action_dict[temp_player_index] = [] # 从可执行操作列表中移除操作
                # 主询问 pass 不记入战术 passed（见 tactical_claim.py）
                # 同一批完成任务中可能已有更高优先级操作清空等待列表，因此移除前先确认仍在等待。
                if temp_player_index in self.waiting_players_list:
                    self.waiting_players_list.remove(temp_player_index) # 从玩家等待列表中移除玩家

                # 检查当前操作是否是最高优先级的
                do_interrupt = True
                for check_player_index in self.waiting_players_list:
                    for action in self.action_dict[check_player_index]:
                        # 如果有其他更高优先级的操作，则继续等待
                        if self.action_priority[temp_action_type] < self.action_priority[action]:
                            do_interrupt = False

                # 如果action_data为空，添加action_data
                if not action_data:
                    action_data = dict(temp_action_data)  # 创建副本
                    action_type = temp_action_type
                    player_index = temp_player_index  # 保存对应的玩家索引
                    logger.debug(f"设置action_data: player_index={player_index}, action_data={action_data}")

                # 在有人进行操作时，如果操作类型优先级更高，则覆盖上一个玩家的action_data
                elif self.action_priority[temp_action_type] > self.action_priority[action_type]:
                    action_data = dict(temp_action_data)  # 创建副本
                    action_type = temp_action_type
                    player_index = temp_player_index  # 更新为对应的玩家索引
                    logger.debug(f"覆盖action_data: player_index={player_index}, action_data={action_data}")

                # 战术鸣牌：任一非 pass 提交立即结束主询问
                tactical_immediate_break = (
                    getattr(self, "tactical_call", False)
                    and temp_action_type != "pass"
                    and self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang")
                )
                if do_interrupt or tactical_immediate_break:
                    self.waiting_players_list = [] # 清空等待列表，强制结束循环

    # 等待行为结束,开始处理操作,pass,超时逻辑
    # 如果操作是最高优先级的直接结束循环
    # 如果操作并非最高优先级的,在最高优先级取消或者超时后结束循环
    # 如果action_data有值,说明有操作,如果action_data无值,说明操作超时
    # 首先将超时玩家剩余时间归零
    if self.waiting_players_list:
        for i in self.waiting_players_list:
            self.player_list[i].remaining_time = 0

    if action_data:
        logger.debug(f"player_index={player_index} action_type={action_type} action_data={action_data} game_status={self.game_status} player_hand_tiles={self.player_list[player_index].hand_tiles}")
    else:
        logger.debug("操作超时")

    action_type, player_index, action_data, _ = await apply_tactical_claim_if_needed(
        self,
        action_type,
        player_index,
        action_data,
        broadcast_do_action=broadcast_do_action,
        broadcast_ask_other_action=broadcast_ask_other_action,
    )

    # 情形处理
    match self.game_status:
        case "waiting_initial_hu":
            if action_data and action_type == "initial_hu":
                if hasattr(self, "_settle_initial_hu"):
                    await self._settle_initial_hu(player_index)
                return True
            if action_data and action_type != "pass":
                logger.error(f"起手胡阶段出现非法操作: action_type={action_type}, action_data={action_data}")
            return True

        case "waiting_sea_bottom":
            if action_data and action_type == "sea_bottom":
                if hasattr(self, "_take_sea_bottom_tile"):
                    await self._take_sea_bottom_tile(player_index)
                return True
            if action_data and action_type not in ("pass", "sea_bottom"):
                logger.error(f"海底漫游阶段出现非法操作: action_type={action_type}, action_data={action_data}")
            if hasattr(self, "_prepare_next_sea_bottom_choice") and self._prepare_next_sea_bottom_choice():
                return True
            self.game_status = "END"
            return True

        # 摸牌后手牌case 包含 切牌cut 暗杠gang 加杠jiagang 自摸hu
        # 长沙规则：不包含补花逻辑
        case "waiting_hand_action":
            if action_data:
                current_player = self.player_list[self.current_player_index]
                if (
                    getattr(current_player, "open_kong_locked", False)
                    and action_type not in ("cut", "angang", "hu_self")
                ):
                    logger.error(
                        "玩家 %s 开杠锁定期间提交非法动作 %s",
                        self.current_player_index,
                        action_type,
                    )
                    return
                if action_type == "cut": # 切牌
                    forced_cut_was_pending = bool(getattr(self, "forced_cut_tiles", []) or getattr(self, "forced_cut_tile", None) is not None)
                    if forced_cut_was_pending:
                        cut_tiles = _consume_forced_gang_cut_tiles(self, player_index)
                        if not cut_tiles:
                            return
                        tile_id = cut_tiles[-1]
                        is_moqie = True
                        cut_tile_index = None
                    else:
                        cut_result = await _apply_open_kong_locked_cut(self, player_index, action_data)
                        if cut_result is None:
                            return
                        tile_id, is_moqie, cut_tile_index = cut_result
                        cut_tiles = [tile_id]
                    for cut_item in cut_tiles:
                        self.player_list[player_index].discard_tiles.append(cut_item)
                        player_action_record_cut(self,cut_tile = cut_item,is_moqie = is_moqie)
                    if hasattr(self, "clear_hu_pass_after_own_discard"):
                        self.clear_hu_pass_after_own_discard(player_index)
                    # broadcast cut
                    if self.current_player_index == 0:
                        self.xunmu += 1
                    refresh_waiting_tiles(self,self.current_player_index) # refresh waiting tiles
                    pre_action_dict = (
                        check_action_after_batch_gang_forced_cut(self, cut_tiles)
                        if forced_cut_was_pending
                        else check_action_after_cut(self,tile_id)
                    )
                    self.last_draw_was_gang = False
                    begin_claim_protection_interval(self, pre_action_dict, self.current_player_index)
                    broadcast_cut_tile = getattr(self, "current_claim_cut_tile", None) or tile_id
                    await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = broadcast_cut_tile,cut_tiles = cut_tiles if len(cut_tiles) > 1 else None,cut_class = is_moqie,cut_tile_index = cut_tile_index) # broadcast cut action
                    self.action_dict = pre_action_dict

                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut" # 转移行为
                    else:
                        self.game_status = (
                            self.next_status_after_claim_window()
                            if forced_cut_was_pending and hasattr(self, "next_status_after_claim_window")
                            else "deal_card"
                        ) # 历时行为


                elif action_type == "pass":
                    forced_cut_was_pending = bool(getattr(self, "forced_cut_tiles", []) or getattr(self, "forced_cut_tile", None) is not None)
                    if forced_cut_was_pending and hasattr(self, "force_cut_gang_replacement_tiles"):
                        await self.force_cut_gang_replacement_tiles()
                        return
                    logger.error(f"摸牌后手牌阶段收到非法pass: action_data={action_data}")
                    return

                elif action_type == "buzhang":
                    buzhang_tile = action_data.get("target_tile")
                    normal_buzhang = normalize_tile(buzhang_tile)
                    player = self.player_list[self.current_player_index]
                    if _has_jiagang_target(player, normal_buzhang):
                        await _execute_jiagang_replacement(self, self.current_player_index, buzhang_tile, "buzhang", 1, False)
                    else:
                        await _execute_angang_replacement(self, self.current_player_index, buzhang_tile, "buzhang", 1, False)
                    return

                elif action_type == "angang":
                    angang_tile = action_data.get("target_tile")
                    normal_angang = normalize_tile(angang_tile)
                    player = self.player_list[self.current_player_index]
                    hand = player.hand_tiles
                    is_open_kong = (
                        hasattr(self, "_is_open_kong_ready_after_declared")
                        and self._is_open_kong_ready_after_declared(player, normal_angang)
                    )
                    draw_slot = has_draw_slot(player)
                    is_mo_gang = resolve_is_mo_gang(hand, normal_angang, draw_slot=draw_slot)
                    removed = remove_angang_tiles(hand, normal_angang, draw_slot=draw_slot)
                    clear_draw_slot(player)
                    self.player_list[self.current_player_index].combination_tiles.append(f"G{normal_angang}")
                    if is_open_kong:
                        self.player_list[self.current_player_index].open_kong_locked = True
                    add_combination_mask = [0, removed[0], 0, removed[1], 0, removed[2], 0, removed[3]]
                    self.player_list[self.current_player_index].combination_mask.append(add_combination_mask)
                    player_action_record_angang(self, angang_tile=normal_angang, is_mo_gang=is_mo_gang,
                                                combination_mask=add_combination_mask)
                    await broadcast_do_action(self,action_list = ["angang"],
                                                  action_player = self.current_player_index,
                                                  combination_mask = add_combination_mask,
                                                  combination_target = f"G{normal_angang}",
                                                  is_mo_gang=is_mo_gang)

                    replacement_count = getattr(self, "open_kong_replacement_count", 2) if is_open_kong else 1
                    self.prepare_gang_replacement(replacement_count, is_open_kong)
                    # 切换到杠后发牌历时行为
                    self.game_status = "deal_card_after_gang"

                elif action_type == "jiagang": # 加杠
                    # 加杠
                    jiagang_tile = action_data.get("target_tile") # 获取加杠牌
                    normal_jia = normalize_tile(jiagang_tile)
                    player = self.player_list[self.current_player_index]
                    hand = player.hand_tiles
                    draw_slot = has_draw_slot(player)
                    is_mo_gang = resolve_is_mo_gang(hand, normal_jia, draw_slot=draw_slot)
                    actual_jia = remove_cut_tile(hand, jiagang_tile, is_mo_gang, draw_slot=draw_slot)
                    clear_draw_slot(player)

                    combination_index = -1
                    for i, combination in enumerate(self.player_list[self.current_player_index].combination_tiles):
                        if combination.startswith("k") and normalize_tile(int(combination[1:])) == normal_jia:
                            combination_index = i
                            break

                    if combination_index < 0:
                        logger.error(
                            f"非法jiagang：未找到可加杠的刻子 normal_jia={normal_jia}, combination_tiles={self.player_list[self.current_player_index].combination_tiles}"
                        )
                        self.game_status = "deal_card"
                        return

                    for i, mask in enumerate(self.player_list[self.current_player_index].combination_mask[combination_index]):
                        if mask == 1:
                            self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, actual_jia)
                            self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, 3)
                            break

                    self.player_list[self.current_player_index].combination_tiles[combination_index] = f"g{normal_jia}"
                    self.player_list[self.current_player_index].open_kong_locked = True

                    # 牌谱记录加杠
                    player_action_record_jiagang(self, jiagang_tile=normal_jia, is_mo_gang=is_mo_gang)

                    await broadcast_do_action(self,action_list = ["jiagang"],
                                                  action_player = self.current_player_index,
                                                  combination_mask = self.player_list[self.current_player_index].combination_mask[combination_index],
                                                  combination_target = f"k{normal_jia}",
                                                  is_mo_gang=is_mo_gang,
                                                  ) # 广播加杠动画

                    self.jiagang_tile = normal_jia # 存储抢杠牌
                    self.prepare_gang_replacement(getattr(self, "open_kong_replacement_count", 2), True)
                    self.action_dict = check_action_jiagang(self,normal_jia) # 检查是否有人可以抢杠
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_qianggang" # 如果有则执行 等待抢杠行为 转移行为
                    else:
                        self.game_status = "deal_card_after_gang" # 历时行为
                    return

                elif action_type == "hu_self": # 自摸
                    # 和牌 (自摸)
                    self.hu_class = "hu_self"
                    self.game_status = "END"
                    logger.debug(f"处理自摸操作: player_index={player_index}, action_type={action_type}, hu_class={self.hu_class}, game_status={self.game_status}")
                    return
                else:
                    logger.error(f"摸牌后手牌阶段action_type出现非cut,angang,jiagang,buhua,hu_self的值: {action_type}")
                    return
            # 超时自动出牌（有摸牌区则摸切）
            else:
                player = self.player_list[self.current_player_index]
                hand = player.hand_tiles
                draw_slot = has_draw_slot(player)
                is_moqie = draw_slot
                forced_cut_was_pending = bool(getattr(self, "forced_cut_tiles", []) or getattr(self, "forced_cut_tile", None) is not None)
                if forced_cut_was_pending:
                    cut_tiles = _consume_forced_gang_cut_tiles(self, self.current_player_index)
                    if not cut_tiles:
                        return
                    tile_id = cut_tiles[-1]
                    is_moqie = True
                else:
                    tile_id = hand[-1] if draw_slot else pick_timeout_discard_tile(hand)
                    remove_cut_tile(hand, tile_id, is_moqie, draw_slot=draw_slot)
                    clear_draw_slot(player)
                    cut_tiles = [tile_id]
                for cut_item in cut_tiles:
                    self.player_list[self.current_player_index].discard_tiles.append(cut_item)
                    player_action_record_cut(self,cut_tile = cut_item,is_moqie = is_moqie)
                if hasattr(self, "clear_hu_pass_after_own_discard"):
                    self.clear_hu_pass_after_own_discard(self.current_player_index)
                # broadcast cut
                if self.current_player_index == 0:
                    self.xunmu += 1
                refresh_waiting_tiles(self,self.current_player_index) # refresh waiting tiles
                pre_action_dict = (
                    check_action_after_batch_gang_forced_cut(self, cut_tiles)
                    if forced_cut_was_pending
                    else check_action_after_cut(self,tile_id)
                )
                self.last_draw_was_gang = False
                begin_claim_protection_interval(self, pre_action_dict, self.current_player_index)
                broadcast_cut_tile = getattr(self, "current_claim_cut_tile", None) or tile_id
                await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = broadcast_cut_tile,cut_tiles = cut_tiles if len(cut_tiles) > 1 else None,cut_class = is_moqie)
                self.action_dict = pre_action_dict
                if any(self.action_dict[i] for i in self.action_dict):
                    self.game_status = "waiting_action_after_cut" # 转移行为
                else:
                    self.game_status = (
                        self.next_status_after_claim_window()
                        if forced_cut_was_pending and hasattr(self, "next_status_after_claim_window")
                        else "deal_card"
                    ) # 历时行为
                return

        # 切牌后手牌case 包含 碰 杠 胡 其中碰杠是转移行为 胡是终结行为
        # 由于切后询问行为时的current_player_index还未进行历时操作 当前玩家弃牌堆的最后一张牌就是待碰杠和的牌
        case "waiting_action_after_cut":
            tile_id = getattr(self, "current_claim_cut_tile", None)
            if tile_id is None:
                tile_id = self.player_list[self.current_player_index].discard_tiles[-1] # 获取操作牌
            combination_mask = []
            combination_target = ""
            if action_data:
                refresh_waiting_tiles(self,player_index) # 更新听牌
                if action_type == "chi_left": # [tile_id-2,tile_id-1,tile_id]
                    if player_index != next_current_num(self.current_player_index):
                        logger.error(
                            f"非法chi_left：只有上家可吃 player={player_index}, current={self.current_player_index}"
                        )
                        self.game_status = "deal_card"
                        return
                    if (tile_id - 1) not in self.player_list[player_index].hand_tiles or (tile_id - 2) not in self.player_list[player_index].hand_tiles:
                        logger.error(
                            f"非法chi_left：玩家{player_index}手牌不足，tile_id={tile_id}, hand_tiles={self.player_list[player_index].hand_tiles}, action_data={action_data}"
                        )
                        self.game_status = "deal_card"
                        return
                    self.player_list[player_index].hand_tiles.remove(tile_id-1)
                    self.player_list[player_index].hand_tiles.remove(tile_id-2)
                    self.player_list[player_index].combination_tiles.append(f"s{tile_id-1}")
                    combination_target = f"s{tile_id-1}"
                    combination_mask = [1,tile_id,0,tile_id-1,0,tile_id-2]

                elif action_type == "chi_mid": # [tile_id-1,tile_id,tile_id+1]
                    if player_index != next_current_num(self.current_player_index):
                        logger.error(
                            f"非法chi_mid：只有上家可吃 player={player_index}, current={self.current_player_index}"
                        )
                        self.game_status = "deal_card"
                        return
                    if (tile_id - 1) not in self.player_list[player_index].hand_tiles or (tile_id + 1) not in self.player_list[player_index].hand_tiles:
                        logger.error(
                            f"非法chi_mid：玩家{player_index}手牌不足，tile_id={tile_id}, hand_tiles={self.player_list[player_index].hand_tiles}, action_data={action_data}"
                        )
                        self.game_status = "deal_card"
                        return
                    self.player_list[player_index].hand_tiles.remove(tile_id-1)
                    self.player_list[player_index].hand_tiles.remove(tile_id+1)
                    self.player_list[player_index].combination_tiles.append(f"s{tile_id}")
                    combination_target = f"s{tile_id}"
                    combination_mask = [1,tile_id,0,tile_id-1,0,tile_id+1]

                elif action_type == "chi_right": # [tile_id,tile_id+1,tile_id+2]
                    if player_index != next_current_num(self.current_player_index):
                        logger.error(
                            f"非法chi_right：只有上家可吃 player={player_index}, current={self.current_player_index}"
                        )
                        self.game_status = "deal_card"
                        return
                    if (tile_id + 1) not in self.player_list[player_index].hand_tiles or (tile_id + 2) not in self.player_list[player_index].hand_tiles:
                        logger.error(
                            f"非法chi_right：玩家{player_index}手牌不足，tile_id={tile_id}, hand_tiles={self.player_list[player_index].hand_tiles}, action_data={action_data}"
                        )
                        self.game_status = "deal_card"
                        return
                    self.player_list[player_index].hand_tiles.remove(tile_id+1)
                    self.player_list[player_index].hand_tiles.remove(tile_id+2)
                    self.player_list[player_index].combination_tiles.append(f"s{tile_id+1}")
                    combination_target = f"s{tile_id+1}"
                    combination_mask = [1,tile_id,0,tile_id+1,0,tile_id+2]

                elif action_type == "peng": # [tile_id',tile_id',tile_id]
                    # 保护：必须至少有两张 tile_id
                    if self.player_list[player_index].hand_tiles.count(tile_id) < 2:
                        logger.error(
                            f"非法peng：玩家{player_index}手牌不足，tile_id={tile_id}, count={self.player_list[player_index].hand_tiles.count(tile_id)}, hand_tiles={self.player_list[player_index].hand_tiles}, action_data={action_data}"
                        )
                        self.game_status = "deal_card"
                        return
                    self.player_list[player_index].hand_tiles.remove(tile_id)
                    self.player_list[player_index].hand_tiles.remove(tile_id)
                    self.player_list[player_index].combination_tiles.append(f"k{tile_id}")
                    # 获取相对位置 (操作者, 出牌者)
                    relative_position = get_index_relative_position(player_index, self.current_player_index)
                    combination_target = f"k{tile_id}"
                    if relative_position == "left":
                        combination_mask = [1,tile_id,0,tile_id,0,tile_id]
                    elif relative_position == "right":
                        combination_mask = [0,tile_id,0,tile_id,1,tile_id]
                    elif relative_position == "top":
                        combination_mask = [0,tile_id,1,tile_id,0,tile_id]

                elif action_type == "gang": # [tile_id',tile_id,tile_id',tile_id]
                    # 保护：明杠需要至少三张 tile_id
                    if self.player_list[player_index].hand_tiles.count(tile_id) < 3:
                        logger.error(
                            f"非法gang：玩家{player_index}手牌不足，tile_id={tile_id}, count={self.player_list[player_index].hand_tiles.count(tile_id)}, hand_tiles={self.player_list[player_index].hand_tiles}, action_data={action_data}"
                        )
                        self.game_status = "deal_card"
                        return
                    self.player_list[player_index].hand_tiles.remove(tile_id)
                    self.player_list[player_index].hand_tiles.remove(tile_id)
                    self.player_list[player_index].hand_tiles.remove(tile_id)
                    self.player_list[player_index].combination_tiles.append(f"g{tile_id}")
                    self.player_list[player_index].open_kong_locked = True
                    # 获取相对位置 (操作者, 出牌者)
                    relative_position = get_index_relative_position(player_index, self.current_player_index)
                    combination_target = f"g{tile_id}"
                    if relative_position == "left":
                        combination_mask = [1,tile_id,0,tile_id,0,tile_id,0,tile_id]
                    elif relative_position == "right":
                        combination_mask = [0,tile_id,0,tile_id,0,tile_id,1,tile_id]
                    elif relative_position == "top":
                        combination_mask = [0,tile_id,1,tile_id,0,tile_id,0,tile_id]

                elif action_type == "hu_first" or action_type == "hu_second" or action_type == "hu_third": # 终结行为 可能有多人胡的情况
                    flush_unexecuted_claim_applications(
                        self,
                        tile_id,
                        executed_player=player_index,
                        executed_action_type=action_type,
                    )
                    had_claim_protection = getattr(self, "_cp_active", False)

                    await finalize_claim_protection(self, _send_do_action_payload_to_viewer)

                    # 受保护观众后续帧走 outbound_pipe FIFO，此处不再全局 sleep
                    # 和牌 （荣和）
                    self.player_list[player_index].hand_tiles.append(tile_id) # 将和牌牌加入手牌最后一张
                    self.hu_class = action_type
                    self.game_status = "END"
                    logger.debug(f"处理和牌操作: player_index={player_index}, action_type={action_type}, hu_class={self.hu_class}, game_status={self.game_status}, tile_id={tile_id}")
                    return

                # 如果发生吃碰杠而不是和牌 则发生转移行为
                if action_type in ("chi_left", "chi_mid", "chi_right", "peng", "gang"):
                    if getattr(self, "pending_gang_forced_discard", False):
                        self.prepare_gang_replacement(0, False)
                    discarder_index = self.current_player_index
                    _remove_claimed_discard(self.player_list[discarder_index].discard_tiles, tile_id) # 删除弃牌堆中被鸣走的牌
                    self.player_list[discarder_index].discard_origin_tiles.append(tile_id) # 添加弃牌理论弃牌
                    self.player_list[player_index].combination_mask.append(combination_mask) # 添加组合掩码
                    clear_draw_slot(self.player_list[player_index])
                    self.current_player_index = player_index # 转移行为后 当前玩家索引变为操作玩家索引
                    flush_unexecuted_claim_applications(
                        self,
                        tile_id,
                        executed_player=player_index,
                        executed_action_type=action_type,
                    )
                    # 牌谱记录碰杠牌
                    player_action_record_chipenggang(self, action_type=action_type, mingpai_tile=tile_id,
                                                     action_player=player_index, combination_mask=combination_mask)
                    # 广播碰杠动画
                    await broadcast_do_action(self,action_list = [action_type],action_player = self.current_player_index,combination_mask = combination_mask,combination_target = combination_target,cut_from_player = discarder_index,cut_tile = tile_id)
                    if action_type == "gang":
                        self.prepare_gang_replacement(getattr(self, "open_kong_replacement_count", 2), True)
                        self.game_status = "deal_card_after_gang" # 转移行为
                    else:
                        self.current_claim_cut_tile = None
                        self.game_status = "onlycut_after_action" # 转移行为
                    return

                if action_type == "pass":
                    flush_unexecuted_claim_applications(self, tile_id)
                    await finalize_claim_protection(self, _send_do_action_payload_to_viewer)
                    self.game_status = self.next_status_after_claim_window() if hasattr(self, "next_status_after_claim_window") else "deal_card"
                    self.current_claim_cut_tile = None
                    return

            else:
                # 如果超时则进行历时行为 继续下一个玩家摸牌
                flush_unexecuted_claim_applications(self, tile_id)
                await finalize_claim_protection(self, _send_do_action_payload_to_viewer)
                self.game_status = self.next_status_after_claim_window() if hasattr(self, "next_status_after_claim_window") else "deal_card"
                self.current_claim_cut_tile = None
                return

        # 在转移行为以后只能进行切牌操作
        case "onlycut_after_action":
            if action_data:
                if action_type == "buzhang":
                    buzhang_tile = action_data.get("target_tile")
                    normal_buzhang = normalize_tile(buzhang_tile)
                    player = self.player_list[self.current_player_index]
                    if _has_jiagang_target(player, normal_buzhang):
                        await _execute_jiagang_replacement(self, self.current_player_index, buzhang_tile, "buzhang", 1, False)
                    else:
                        await _execute_angang_replacement(self, self.current_player_index, buzhang_tile, "buzhang", 1, False)
                    return

                elif action_type == "angang":
                    angang_tile = action_data.get("target_tile")
                    normal_angang = normalize_tile(angang_tile)
                    player = self.player_list[self.current_player_index]
                    is_open_kong = (
                        hasattr(self, "_is_open_kong_ready_after_declared")
                        and self._is_open_kong_ready_after_declared(player, normal_angang)
                    )
                    replacement_count = getattr(self, "open_kong_replacement_count", 2) if is_open_kong else 1
                    await _execute_angang_replacement(
                        self,
                        self.current_player_index,
                        angang_tile,
                        "angang",
                        replacement_count,
                        is_open_kong,
                    )
                    return

                elif action_type == "jiagang":
                    jiagang_tile = action_data.get("target_tile")
                    await _execute_jiagang_replacement(
                        self,
                        self.current_player_index,
                        jiagang_tile,
                        "jiagang",
                        getattr(self, "open_kong_replacement_count", 2),
                        True,
                    )
                    return

                elif action_type == "cut": # 切牌
                    cut_result = await apply_player_cut(self, self.current_player_index, action_data)
                    if cut_result is None:
                        return
                    tile_id, is_moqie, cut_tile_index = cut_result
                    self.player_list[self.current_player_index].discard_tiles.append(tile_id)
                    player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                    if hasattr(self, "clear_hu_pass_after_own_discard"):
                        self.clear_hu_pass_after_own_discard(self.current_player_index)
                    # 广播切牌动画
                    refresh_waiting_tiles(self, self.current_player_index)
                    pre_action_dict = check_action_after_cut(self, tile_id)
                    self.last_draw_was_gang = False
                    begin_claim_protection_interval(self, pre_action_dict, self.current_player_index)
                    await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie,cut_tile_index = cut_tile_index)
                    self.action_dict = pre_action_dict
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut" # 转移行为
                    else:
                        self.game_status = "deal_card" # 历时行为
                    return
                else:
                    raise ValueError("在转移行为onlycut_afteraction阶段出现非cut/buzhang/angang/jiagang的值")
            # 超时自动出牌（碰后无摸牌区，按牌值手切）
            else:
                player = self.player_list[self.current_player_index]
                hand = player.hand_tiles
                is_moqie = False
                tile_id = pick_timeout_discard_tile(hand)
                remove_cut_tile(hand, tile_id, is_moqie, draw_slot=False)
                clear_draw_slot(player)
                self.player_list[self.current_player_index].discard_tiles.append(tile_id)
                # 牌谱记录摸切
                player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                if hasattr(self, "clear_hu_pass_after_own_discard"):
                    self.clear_hu_pass_after_own_discard(self.current_player_index)
                refresh_waiting_tiles(self,self.current_player_index) # 更新听牌
                pre_action_dict = check_action_after_cut(self,tile_id)
                self.last_draw_was_gang = False
                begin_claim_protection_interval(self, pre_action_dict, self.current_player_index)
                await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie)
                self.action_dict = pre_action_dict
                if any(self.action_dict[i] for i in self.action_dict):
                    self.game_status = "waiting_action_after_cut" # 转移行为
                else:
                    self.game_status = "deal_card" # 历时行为
                return

        # 在加杠以后的case当中只包含和牌和pass一个选项 如果超时或者pass则进行历时行为
        case "waiting_action_qianggang":
            temp_jiagang_tile = self.jiagang_tile # 存储抢杠牌
            self.jiagang_tile = None # 删除抢杠牌
            if action_data:
                if action_type == "hu_first" or action_type == "hu_second" or action_type == "hu_third": # 终结行为 可能有多人胡的情况
                    # 和牌 （荣和）
                    self.player_list[player_index].hand_tiles.append(temp_jiagang_tile) # 将和牌牌加入手牌最后一张
                    self.hu_class = action_type
                    self.game_status = "END"
                    return
                elif action_type == "pass":
                    self.game_status = "deal_card_after_gang" # 抢杠无人胡，原玩家继续补杠牌
                    return
                else:
                    raise ValueError("抢杠和阶段action_type出现非hu和pass的值")
            # 超时放弃抢杠
            else:
                self.game_status = "deal_card_after_gang" # 抢杠超时无人胡，原玩家继续补杠牌
                return

        # 等待准备阶段
        case "waiting_ready":
            # 准备阶段按“单次处理 + 上层循环”的方式执行
            if action_data:
                if action_type == "ready":
                    # 主循环里已将该玩家 action_dict 清空，这里广播最新准备状态
                    await broadcast_ready_status(self)
                    return True
                logger.error(f"等待准备阶段出现非ready的操作类型: {action_type}")
                return False

            # 超时：将仍未准备玩家视为放弃本轮准备，避免上层循环卡死
            for wait_player_index, wait_actions in self.action_dict.items():
                if "ready" in wait_actions:
                    self.action_dict[wait_player_index] = []
            await broadcast_ready_status(self)
            return False
