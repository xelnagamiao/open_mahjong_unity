# 等待玩家操作处理
import asyncio
import time
import logging
from .action_check import check_action_after_cut, check_action_jiagang, refresh_waiting_tiles
from .boardcast import broadcast_do_action, broadcast_ready_status, broadcast_ask_other_action
from ..public.logic_common import get_index_relative_position
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
    compute_protected_meld_delay,
)
from ..public.tactical_claim import (
    init_tactical_round_state,
    apply_tactical_claim_if_needed,
)
from .boardcast import _send_do_action_payload_to_viewer

logger = logging.getLogger(__name__)

# 等待玩家行动
async def wait_action(self):
    self.waiting_players_list = [] # [2,3]
    used_time = 0 # 已用时间

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

    init_tactical_round_state(self)

    # 如果等待玩家列表不为空且有玩家剩余时间小于(已用时间-步时)，则停止等待
    player_index = None # 保存操作玩家索引 (如果玩家有操作则左侧三个变量有值 否则为None)
    action_data = None # 保存操作数据
    action_type = None # 保存操作类型

    # waiting_ready
    timeout_grace = 0 if self.game_status == "waiting_ready" else self.step_time

    while self.waiting_players_list and any(self.player_list[i].remaining_time + timeout_grace > used_time for i in self.waiting_players_list):

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

        logger.info(f"开始新一轮等待操作 waiting_players_list={self.waiting_players_list} action_dict={self.action_dict} used_time={used_time}")
        
        # 等待计时器完成1s等待或者任意玩家进行操作
        time_start = time.time()
        done, pending = await asyncio.wait(
            task_list,
            return_when=asyncio.FIRST_COMPLETED
        )
        time_end = time.time()

        # 取消未完成的任务
        for task in pending:
            task.cancel()

        # 处理完成的任务
        for task in done:
            # 计时器完成 增加已用时间 注意：这里不需要重置任务，因为下一次循环开始时会创建新的任务
            if task == timer_task: 
                used_time += 1
            # 玩家操作完成，获取玩家索引
            else:
                # 使用映射获取玩家索引
                temp_player_index = task_to_player[task]
                temp_action_data = await self.action_queues[temp_player_index].get() # 获取操作数据
                temp_action_type = temp_action_data.get("action_type") # 获取操作类型

                # 复制字典以避免引用问题
                temp_action_data = dict(temp_action_data)
                logger.info(f"复制后: temp_player_index={temp_player_index}, temp_action_data={temp_action_data}")

                used_time += time_end - time_start # 服务器计算操作时间
                used_int_time = int(used_time) # 变量整数时间
                if timeout_grace > 0 and used_int_time >= timeout_grace: # 扣除玩家超出步时的时间
                    self.player_list[temp_player_index].remaining_time -= (used_int_time - timeout_grace)
               
                self.action_dict[temp_player_index] = [] # 从可执行操作列表中移除操作
                # 主询问 pass 不记入战术 passed；低优先级鸣牌申请后仍从快照再问更高优先级（含已 pass 者）。
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
                    logger.info(f"设置action_data: player_index={player_index}, action_data={action_data}")

                # 在有人进行操作时，如果操作类型优先级更高，则覆盖上一个玩家的action_data
                elif self.action_priority[temp_action_type] > self.action_priority[action_type]:
                    action_data = dict(temp_action_data)  # 创建副本
                    action_type = temp_action_type
                    player_index = temp_player_index  # 更新为对应的玩家索引
                    logger.info(f"覆盖action_data: player_index={player_index}, action_data={action_data}")

                # 战术鸣牌：任一非 pass 提交立即结束主询问，不等待更高优先级竞争者（如 A 吃时不等 B 碰）。
                # 申请广播 + 0.5s 后再进入 5 秒打断窗口，高优先级可在该窗口内抢断。
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
        logger.info(f"player_index={player_index} action_type={action_type} action_data={action_data} game_status={self.game_status} player_hand_tiles={self.player_list[player_index].hand_tiles}")
    else:
        logger.info(f"操作超时")

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
        # 补花轮特殊case 只有在游戏开始时启用
        case "waiting_buhua_round":
            # 如果有操作
            if action_data:
                # 等待补花阶段的action_type只能是buhua
                if action_type == "buhua": 
                    if action_data:
                        return True # 补花以后如果能够补花继续询问
                    elif action_type == "pass":
                        return False # 如果玩家选择pass则停止该玩家补花
                    else: # 报错
                        raise ValueError("补花阶段action_data出现非buhua和pass的值")
            # 如果无操作结束补花 由于补花阶段是按索引进行循环补花的
            # 如果玩家不补花需要返回False 否则就无法同时处理 玩家同时需要补花2张和玩家拒绝补花的情况
            else:
                return False
            
        # 摸牌后手牌case 包含 切牌cut 暗杠gang 加杠jiagang 自摸hu 补花buhua 其中自摸是终结条件 补花 加杠 暗杠是循环行为 切牌是转移/历时行为
        case "waiting_hand_action":
            if action_data:
                if action_type == "cut": # 切牌
                    cut_result = await apply_player_cut(self, player_index, action_data)
                    if cut_result is None:
                        return
                    tile_id, is_moqie, cut_tile_index = cut_result
                    self.player_list[player_index].discard_tiles.append(tile_id)
                    player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                    # 广播切牌操作
                    if self.current_player_index == 0:
                        self.xunmu += 1
                    refresh_waiting_tiles(self, self.current_player_index)
                    pre_action_dict = check_action_after_cut(self, tile_id)
                    begin_claim_protection_interval(self, pre_action_dict, self.current_player_index)
                    await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie,cut_tile_index = cut_tile_index) # 广播切牌动画 切牌玩家索引 手模切 切牌id 操作帧
                    self.action_dict = pre_action_dict
                    
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut" # 转移行为
                    else:
                        self.game_status = "deal_card" # 历时行为

                
                elif action_type == "angang": 
                    angang_tile = action_data.get("target_tile")
                    normal_angang = normalize_tile(angang_tile)
                    player = self.player_list[self.current_player_index]
                    hand = player.hand_tiles
                    draw_slot = has_draw_slot(player)
                    is_mo_gang = resolve_is_mo_gang(hand, normal_angang, draw_slot=draw_slot)
                    removed = remove_angang_tiles(hand, normal_angang, draw_slot=draw_slot)
                    clear_draw_slot(player)
                    self.player_list[self.current_player_index].combination_tiles.append(f"G{normal_angang}")
                    add_combination_mask = [2, removed[0], 2, removed[1], 2, removed[2], 2, removed[3]]
                    self.player_list[self.current_player_index].combination_mask.append(add_combination_mask)
                    player_action_record_angang(self, angang_tile=normal_angang, is_mo_gang=is_mo_gang,
                                                combination_mask=add_combination_mask)
                    await broadcast_do_action(self,action_list = ["angang"],
                                                  action_player = self.current_player_index,
                                                  combination_mask = add_combination_mask,
                                                  combination_target = f"G{normal_angang}",
                                                  is_mo_gang=is_mo_gang)
                    
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
                    # 寻找当前玩家的组合牌是否有 k+加杠牌（归一化匹配，兼容 k105 / k15）
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

                    # 通过组合位置找到掩码位置
                    for i, mask in enumerate(self.player_list[self.current_player_index].combination_mask[combination_index]):
                        if mask == 1:  # 找到数组下标 [0,Tile,0,Tile,1,Tile] 获取1的位置 1代表碰牌横牌的位置
                            # 在碰牌横牌的后面插入加杠牌和3 如 结果[0,Tile,0,Tile,1,{Tile,3,}Tile]
                            self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, actual_jia)
                            self.player_list[self.current_player_index].combination_mask[combination_index].insert(i, 3) # 插入3代表加杠牌
                            break

                    self.player_list[self.current_player_index].combination_tiles[combination_index] = f"g{normal_jia}"

                    # 牌谱记录加杠
                    player_action_record_jiagang(self, jiagang_tile=normal_jia, is_mo_gang=is_mo_gang)

                    await broadcast_do_action(self,action_list = ["jiagang"],
                                                  action_player = self.current_player_index,
                                                  combination_mask = self.player_list[self.current_player_index].combination_mask[combination_index],
                                                  combination_target = f"k{normal_jia}",
                                                  is_mo_gang=is_mo_gang,
                                                  ) # 广播加杠动画

                    self.jiagang_tile = normal_jia # 存储抢杠牌
                    self.action_dict = check_action_jiagang(self,normal_jia) # 检查是否有人可以抢杠
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_qianggang" # 如果有则执行 等待抢杠行为 转移行为
                    else:
                        self.game_status = "deal_card_after_gang" # 历时行为
                    return
                
                elif action_type == "buhua": 
                    # 补花
                    self.game_status = "deal_card_after_buhua"
                    return
                
                elif action_type == "hu_self": # 自摸
                    # 和牌 (自摸)
                    self.hu_class = "hu_self"
                    self.game_status = "check_hepai"
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
                tile_id = hand[-1] if draw_slot else pick_timeout_discard_tile(hand)
                remove_cut_tile(hand, tile_id, is_moqie, draw_slot=draw_slot)
                clear_draw_slot(player)
                self.player_list[self.current_player_index].discard_tiles.append(tile_id)
                player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                # 广播摸切操作
                if self.current_player_index == 0:
                    self.xunmu += 1
                
                refresh_waiting_tiles(self, self.current_player_index)
                pre_action_dict = check_action_after_cut(self, tile_id)
                begin_claim_protection_interval(self, pre_action_dict, self.current_player_index)
                await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie) # 广播摸切动画 摸切玩家索引 手模切 摸切牌id 操作帧
                self.action_dict = pre_action_dict
                if any(self.action_dict[i] for i in self.action_dict):
                    self.game_status = "waiting_action_after_cut" # 转移行为
                else:
                    self.game_status = "deal_card" # 历时行为
                return
            
        # 切牌后手牌case 包含 吃 碰 杠 胡 其中吃碰杠是转移行为 胡是终结行为
        # 由于切后询问行为时的current_player_index还未进行历时操作 当前玩家弃牌堆的最后一张牌就是待吃碰杠和的牌
        case "waiting_action_after_cut":
            tile_id = self.player_list[self.current_player_index].discard_tiles[-1] # 获取操作牌
            combination_mask = []
            combination_target = ""
            if action_data:
                refresh_waiting_tiles(self,player_index) # 更新听牌
                if action_type == "chi_left": # [tile_id-2,tile_id-1,tile_id]
                    # 保护：校验吃牌所需手牌是否存在，避免 remove 抛异常导致主循环中断
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
                    print("peng")
                    normal_tile = normalize_tile(tile_id)
                    # 保护：必须至少有两张 tile_id（含赤宝等价）
                    if sum(1 for t in self.player_list[player_index].hand_tiles if normalize_tile(t) == normal_tile) < 2:
                        logger.error(
                            f"非法peng：玩家{player_index}手牌不足，tile_id={tile_id}, normal={normal_tile}, hand_tiles={self.player_list[player_index].hand_tiles}, action_data={action_data}"
                        )
                        self.game_status = "deal_card"
                        return
                    removed = 0
                    while removed < 2:
                        for t in list(self.player_list[player_index].hand_tiles):
                            if normalize_tile(t) == normal_tile:
                                self.player_list[player_index].hand_tiles.remove(t)
                                removed += 1
                                break
                    self.player_list[player_index].combination_tiles.append(f"k{normal_tile}")
                    # 获取相对位置 (操作者, 出牌者)
                    relative_position = get_index_relative_position(player_index, self.current_player_index)
                    combination_target = f"k{normal_tile}"
                    if relative_position == "left":
                        combination_mask = [1,tile_id,0,tile_id,0,tile_id]
                    elif relative_position == "right":
                        combination_mask = [0,tile_id,0,tile_id,1,tile_id]
                    elif relative_position == "top":
                        combination_mask = [0,tile_id,1,tile_id,0,tile_id]

                elif action_type == "gang": # [tile_id',tile_id,tile_id',tile_id]
                    normal_tile = normalize_tile(tile_id)
                    # 保护：明杠需要至少三张（含赤宝等价）
                    if sum(1 for t in self.player_list[player_index].hand_tiles if normalize_tile(t) == normal_tile) < 3:
                        logger.error(
                            f"非法gang：玩家{player_index}手牌不足，tile_id={tile_id}, normal={normal_tile}, hand_tiles={self.player_list[player_index].hand_tiles}, action_data={action_data}"
                        )
                        self.game_status = "deal_card"
                        return
                    removed = 0
                    while removed < 3:
                        for t in list(self.player_list[player_index].hand_tiles):
                            if normalize_tile(t) == normal_tile:
                                self.player_list[player_index].hand_tiles.remove(t)
                                removed += 1
                                break
                    self.player_list[player_index].combination_tiles.append(f"g{normal_tile}")
                    # 获取相对位置 (操作者, 出牌者)
                    relative_position = get_index_relative_position(player_index, self.current_player_index)
                    combination_target = f"g{normal_tile}"
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
                    # 荣和：先把暂存出牌发给受保护观众，再按 cut 揭示时刻 + gap 等待后进入结算。
                    had_claim_protection = getattr(self, "_cp_active", False)
                    await finalize_claim_protection(self, _send_do_action_payload_to_viewer)
                    if had_claim_protection:
                        delay = compute_protected_meld_delay(self)
                        if delay > 0:
                            await asyncio.sleep(delay)
                    # 和牌 （荣和）
                    self.player_list[player_index].hand_tiles.append(tile_id) # 将和牌牌加入手牌最后一张
                    self.hu_class = action_type
                    self.game_status = "check_hepai"
                    logger.info(f"处理和牌操作: player_index={player_index}, action_type={action_type}, hu_class={self.hu_class}, game_status={self.game_status}, tile_id={tile_id}")
                    return
                
                # 如果发生吃碰杠而不是和牌 则发生转移行为
                if action_type == "chi_left" or action_type == "chi_mid" or action_type == "chi_right" or action_type == "peng" or action_type == "gang":
                    discarder_index = self.current_player_index  # 转移前即为被认走的打牌者，供客户端精确移除其牌河弃牌
                    self.player_list[self.current_player_index].discard_tiles.pop(-1) # 删除弃牌堆的最后一张
                    self.player_list[self.current_player_index].discard_origin_tiles.append(tile_id) # 添加弃牌理论弃牌
                    self.player_list[player_index].combination_mask.append(combination_mask) # 添加组合掩码
                    clear_draw_slot(self.player_list[player_index])
                    self.current_player_index = player_index # 转移行为后 当前玩家索引变为操作玩家索引
                    flush_unexecuted_claim_applications(
                        self,
                        tile_id,
                        executed_player=player_index,
                        executed_action_type=action_type,
                    )
                    # 牌谱记录吃碰杠牌
                    player_action_record_chipenggang(self, action_type=action_type, mingpai_tile=tile_id,
                                                     action_player=player_index, combination_mask=combination_mask)
                    # 广播吃碰杠动画：cut_from_player + cut_tile 显式下发被认走的打牌者与牌张，
                    # 客户端不再依赖会被乱序覆盖的 lastDiscardPlayerPosition / currentAskCutTileId
                    await broadcast_do_action(self,action_list = [action_type],action_player = self.current_player_index,combination_mask = combination_mask,combination_target = combination_target,cut_from_player = discarder_index,cut_tile = tile_id)
                    if action_type == "gang":
                        self.game_status = "deal_card_after_gang" # 转移行为
                    else:
                        self.game_status = "onlycut_after_action" # 转移行为
                    return
                
                if action_type == "pass":
                    flush_unexecuted_claim_applications(self, tile_id)
                    await finalize_claim_protection(self, _send_do_action_payload_to_viewer)
                    self.game_status = "deal_card" # 历时行为
                    return

            else:
                # 如果超时则进行历时行为 继续下一个玩家摸牌
                flush_unexecuted_claim_applications(self, tile_id)
                await finalize_claim_protection(self, _send_do_action_payload_to_viewer)
                self.game_status = "deal_card" # 历时行为
                return
        
        # 在转移行为以后只能进行切牌操作
        case "onlycut_after_action":
            if action_data:
                if action_type == "cut": # 切牌
                    cut_result = await apply_player_cut(self, self.current_player_index, action_data)
                    if cut_result is None:
                        return
                    tile_id, is_moqie, cut_tile_index = cut_result
                    self.player_list[self.current_player_index].discard_tiles.append(tile_id)
                    player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                    # 广播切牌动画
                    refresh_waiting_tiles(self, self.current_player_index)
                    pre_action_dict = check_action_after_cut(self, tile_id)
                    begin_claim_protection_interval(self, pre_action_dict, self.current_player_index)
                    await broadcast_do_action(self,action_list = ["cut"],action_player = self.current_player_index,cut_tile = tile_id,cut_class = is_moqie,cut_tile_index = cut_tile_index)
                    self.action_dict = pre_action_dict
                    if any(self.action_dict[i] for i in self.action_dict):
                        self.game_status = "waiting_action_after_cut" # 转移行为
                    else:
                        self.game_status = "deal_card" # 历时行为
                    return
                else:
                    raise ValueError("在转移行为onlycut_afteraction阶段出现非cut的值")
            # 超时自动出牌（吃碰后无摸牌区，按牌值手切）
            else:
                player = self.player_list[self.current_player_index]
                hand = player.hand_tiles
                is_moqie = False
                tile_id = pick_timeout_discard_tile(hand)
                remove_cut_tile(hand, tile_id, is_moqie, draw_slot=False)
                clear_draw_slot(player)
                self.player_list[self.current_player_index].discard_tiles.append(tile_id) # 将出牌加入弃牌堆
                # 牌谱记录摸切
                player_action_record_cut(self,cut_tile = tile_id,is_moqie = is_moqie)
                refresh_waiting_tiles(self, self.current_player_index)
                pre_action_dict = check_action_after_cut(self, tile_id)
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
                    self.game_status = "deal_card" # 历时行为
                    return
                else:
                    raise ValueError("抢杠和阶段action_type出现非hu和pass的值")
            # 超时放弃抢杠
            else:
                self.game_status = "deal_card" # 历时行为
                return
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
