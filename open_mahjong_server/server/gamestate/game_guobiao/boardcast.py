from ...response import Response,GameInfo,Ask_hand_action_info,Ask_other_action_info,Do_action_info,Show_result_info,Game_end_info,Player_final_data,Switch_seat_info,Refresh_player_tag_list_info,Ready_status_info
from typing import List, Dict, Optional
import logging
import asyncio
import time
from ..public.ai.auto_cut_ai import auto_cut_action
from ..public.offline import offline_auto_action
from ..public.ai.smart_bot_ai import smart_bot_action
from .combination_mask_view import (
    get_combination_fields_for_viewer,
    sanitize_angang_mask,
    sanitize_combination_target_for_viewer,
)
from ..public.deal_tile_view import sanitize_deal_tile_for_viewer
from ..public.hand_slot_utils import bot_ask_hand_game_status
from ..public.claim_protection import (
    claim_protection_enabled,
    init_claim_protection_state,
    is_protected_viewer,
    stash_protected_cut_payload,
    arm_claim_protection_timer,
    prepare_protected_meld_for_viewers,
    end_claim_protection_interval,
    mark_post_meld_gap,
    take_post_meld_gap_delay,
    REAL_MELD_ACTIONS,
)
from ..public.ask_timing import begin_ask_round, note_ask_delivered, reconnect_remaining_time

logger = logging.getLogger(__name__)


async def _send_ask_response_to_viewer(
    self, viewer_index: int, response, *, block: bool = True
) -> None:
    """经 outbound_pipe 发送 ask，保证排在延迟鸣牌/第二追赶之后；送达时起算计时。

    block=True：await 本条（用于当前行动者，立刻可操作）。
    block=False：仅 schedule 入队（旁观者可带 post_gap，不拖住主循环/行动者）。
    """
    from ..public.outbound_pipe import send_to_viewer, schedule_viewer_send

    current_player = self.player_list[viewer_index]
    if current_player.user_id not in self.game_server.user_id_to_connection:
        logger.warning(
            f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过 ask 广播"
        )
        return
    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
    delay_before = take_post_meld_gap_delay(self, viewer_index)

    async def _do():
        await player_conn.websocket.send_json(response.dict(exclude_none=True))
        await self.send_to_realtime_spectators(viewer_index, response)
        note_ask_delivered(self, viewer_index)

    if block:
        await send_to_viewer(self, viewer_index, _do, delay_before=delay_before)
    else:
        schedule_viewer_send(self, viewer_index, _do, delay_before=delay_before)


# 广播游戏开始/重连 方法
async def broadcast_game_start(self):
    """广播游戏开始信息"""
    # 重置操作帧（每次广播开始时重置）
    self.server_action_tick = 0
    
    # 基础游戏信息
    base_game_info = {
        'room_id': self.room_id, # 房间ID
        'gamestate_id': self.gamestate_id, # 游戏状态ID
        'tips': self.tips, # 是否提示
        'current_player_index': self.current_player_index, # 当前轮到的玩家索引
        'dealer_index': getattr(self, 'dealer_index', 0), # 本局庄家逻辑座位
        "action_tick": self.server_action_tick, # 操作帧
        'max_round': self.max_round, # 最大局数
        'tile_count': len(self.tiles_list), # 牌山剩余牌数
        'commitment': self.commitment,  # 承诺值
        'salt': self.salt, # 盐字符串
        'current_round': self.current_round, # 当前轮数
        'step_time': self.step_time, # 步时
        'round_time': self.round_time, # 局时
        'room_type': self.room_type, # 房间类型（custom/match等）
        'room_rule': self.room_rule, # 房间规则（guobiao/qingque等）
        'sub_rule': getattr(self, 'sub_rule', 'guobiao/standard'), # 子规则（番表显示）
        'hepai_limit': getattr(self, 'hepai_limit', 8), # 起和番限制（提示用）
        'open_cuohe': self.open_cuohe, # 是否开启错和
        'show_moqie_hint': getattr(self, 'show_moqie_hint', False), # 手摸切灰显
        'tactical_call': getattr(self, 'tactical_call', False), # 战术鸣牌
        'claim_protection': getattr(self, 'claim_protection', False), # 鸣牌保护
        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed, # 是否玩家设置了随机种子
        'players_info': [], # ↓玩家信息
    }
    from ..public.game_record_manager import build_player_entry_order_fields
    base_game_info.update(build_player_entry_order_fields(self))
    # 为每个玩家发送消息，并为每个玩家缓存不同的数据
    for current_player in self.player_list:
        try:
            # 如果玩家掉线，跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                continue
            
            # 机器人占用 user_id < 10 的保留段，无需网络广播
            if current_player.user_id < 10:
                continue
            
            # 为当前玩家构建玩家信息列表（当前玩家看到自己的手牌，其他人看不到）
            players_info_for_current = []
            for player in self.player_list:
                combo_tiles, combo_masks = get_combination_fields_for_viewer(player, current_player.player_index)
                player_info = {
                    'user_id': player.user_id,
                    'username': player.username,
                    'hand_tiles_count': len(player.hand_tiles),
                    'hand_tiles': player.hand_tiles if player.user_id == current_player.user_id else None,  # 只有自己的手牌
                    'discard_tiles': player.discard_tiles,
                    'discard_origin_tiles': player.discard_origin_tiles,
                    'combination_tiles': combo_tiles,
                    'combination_mask': combo_masks,
                    'huapai_list': player.huapai_list,
                    'remaining_time': player.remaining_time,
                    'player_index': player.player_index,
                    'original_player_index': player.original_player_index,
                    'score': player.score,
                    'guobiao_rank': player.guobiao_rank,
                    'guobiao_score': player.guobiao_score,
                    'has_draw_slot': player.has_draw_slot,
                    'title_used': player.title_used,
                    'profile_used': player.profile_used,
                    'character_used': player.character_used,
                    'voice_used': player.voice_used,
                    'score_history': player.score_history,
                    'round_number_history': player.round_number_history,
                    'tag_list': player.tag_list,
                }
                players_info_for_current.append(player_info)
            
            # 构建当前玩家的游戏信息
            game_info_for_current = {
                **base_game_info,
                'players_info': players_info_for_current,
                'self_hand_tiles': None  # 不再使用 self_hand_tiles，手牌在 PlayerInfo 中
            }
            
            # 如果player_list中有玩家在self.game_server.user_id_to_connection:
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                
                game_info = GameInfo(**game_info_for_current)
                response = Response(
                    type="gamestate/guobiao/game_start",
                    success=True,
                    message="游戏开始",
                    game_info=game_info
                )
                
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
                logger.info(f"已向玩家 {current_player.username} 发送游戏开始信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
            
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 发送消息失败: {e}")
            # 允许广播出错，继续向其他玩家广播
    
    # 为观战系统记录局开始数据
    if hasattr(self, 'spectator_manager'):
        self.spectator_manager.record_game_title()
        self.spectator_manager.record_round_start()

# 广播询问手牌操作 补花 加杠 暗杠 自摸 出牌
async def broadcast_ask_hand_action(self):
    self.server_action_tick += 1
    begin_ask_round(self)
    # 当前行动者优先发送并 await；其余座位只入队，避免被受保护观众的
    # delayed meld / post_gap 串行拖住（否则吃碰后要等 ~0.5–1.2s 才能出牌）。
    seat_order = [self.current_player_index] + [
        p.player_index for p in self.player_list if p.player_index != self.current_player_index
    ]
    for seat_index in seat_order:
        current_player = self.player_list[seat_index]
        try:
            player_actions = self.action_dict.get(seat_index, [])
            # 如果玩家掉线，启动自动操作并跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                if player_actions:
                    asyncio.create_task(offline_auto_action(self, seat_index, player_actions, bot_ask_hand_game_status(self, seat_index)))
                continue
            
            # 如果是机器人，启动自动操作并跳过广播；保留 user_id < 10 整段视为机器人
            if current_player.user_id == 0:
                if player_actions:
                    logger.info(f"派发摸切机器人操作 seat={seat_index} status={bot_ask_hand_game_status(self, seat_index)} actions={player_actions}")
                    asyncio.create_task(auto_cut_action(self, seat_index, player_actions, bot_ask_hand_game_status(self, seat_index)))
                continue
            elif current_player.user_id == 2:
                if player_actions:
                    logger.info(f"派发牌效机器人操作 seat={seat_index} status={bot_ask_hand_game_status(self, seat_index)} actions={player_actions}")
                    asyncio.create_task(smart_bot_action(self, seat_index, player_actions, bot_ask_hand_game_status(self, seat_index)))
                continue
            elif current_player.user_id < 10:
                if player_actions:
                    logger.warning(f"未知保留机器人 user_id={current_player.user_id}，按摸切机器人处理 seat={seat_index} actions={player_actions}")
                    asyncio.create_task(auto_cut_action(self, seat_index, player_actions, bot_ask_hand_game_status(self, seat_index)))
                continue
            
            response = Response(
                type="gamestate/guobiao/broadcast_hand_action",
                success=True,
                message="发牌，并询问手牌操作",
                ask_hand_action_info = Ask_hand_action_info(
                    remaining_time=current_player.remaining_time,
                    player_index= self.current_player_index,
                    remain_tiles=len(self.tiles_list),
                    action_list=player_actions,
                    action_tick=self.server_action_tick,
                    dealer_index=getattr(self, 'dealer_index', 0),
                    opening_buhua_complete=getattr(self, '_opening_buhua_complete_pending', False) or None,
                )
            )
            is_actor = seat_index == self.current_player_index
            await _send_ask_response_to_viewer(self, seat_index, response, block=is_actor)
            logger.info(f"已向玩家 {current_player.username} 广播手牌操作信息 block={is_actor}")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播手牌操作信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

    # 为观战系统记录 ask_hand tick（在循环外确保只记录一次）
    if hasattr(self, 'spectator_manager'):
        self.spectator_manager.record_ask_hand(self.current_player_index, self.action_dict.get(self.current_player_index, []))

# 广播询问切牌后操作 吃 碰 杠 胡（抢杠询问时 cut_tile 为加杠牌）
async def broadcast_ask_other_action(self, remaining_time_override: Optional[int] = None, is_tactical_recheck: bool = False):
    if self.game_status == "waiting_action_qianggang" and getattr(self, "jiagang_tile", None) is not None:
        cut_tile = self.jiagang_tile
    else:
        cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
    self.server_action_tick += 1
    # 战术打断再问：在派发 AI 前同步 waiting tick，避免机器人因 tick 不一致拒动
    if is_tactical_recheck:
        self._waiting_action_tick = self.server_action_tick
    begin_ask_round(self)
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        try:
            seat_index = current_player.player_index
            player_actions = self.action_dict.get(seat_index, [])
            # 如果玩家掉线，启动自动操作并跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                if player_actions:
                    asyncio.create_task(offline_auto_action(self, seat_index, player_actions, self.game_status))
                continue
            
            # 如果是机器人，启动自动操作并跳过广播；保留 user_id < 10 整段视为机器人
            if current_player.user_id == 0:
                if player_actions:
                    logger.info(f"派发摸切机器人操作 seat={seat_index} status={self.game_status} actions={player_actions}")
                    asyncio.create_task(auto_cut_action(self, seat_index, player_actions, self.game_status))
                continue
            elif current_player.user_id == 2:
                if player_actions:
                    logger.info(f"派发牌效机器人操作 seat={seat_index} status={self.game_status} actions={player_actions}")
                    asyncio.create_task(smart_bot_action(self, seat_index, player_actions, self.game_status))
                continue
            elif current_player.user_id < 10:
                if player_actions:
                    logger.warning(f"未知保留机器人 user_id={current_player.user_id}，按摸切机器人处理 seat={seat_index} actions={player_actions}")
                    asyncio.create_task(auto_cut_action(self, seat_index, player_actions, self.game_status))
                continue
            
            if player_actions:
                remaining_time_for_player = remaining_time_override if remaining_time_override is not None else current_player.remaining_time
                response = Response(
                    type="gamestate/guobiao/ask_other_action",
                    success=True,
                    message="询问操作",
                    ask_other_action_info = Ask_other_action_info(
                        remaining_time=remaining_time_for_player,
                        action_list=player_actions,
                        cut_tile=cut_tile,
                        action_tick=self.server_action_tick,
                        player_index=current_player.player_index,
                        is_tactical_recheck=is_tactical_recheck if is_tactical_recheck else None,
                    )
                )
                await _send_ask_response_to_viewer(self, seat_index, response)
                logger.info(f"已向玩家 {current_player.username} 广播询问操作信息")

        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播询问操作信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

    # 为观战系统记录 ask_other tick（汇总所有有操作的玩家），战术鸣牌的再次询问不影响观战录制
    if hasattr(self, 'spectator_manager') and not is_tactical_recheck:
        player_action_map = {}
        for idx, actions in self.action_dict.items():
            if actions:
                player_action_map[idx] = actions
        if player_action_map:
            self.spectator_manager.record_ask_other(player_action_map, cut_tile)


def _reconnect_remaining_time(self, player) -> int:
    """重连补发时按「ask 送达时刻 - 已过时间」重算剩余时间。"""
    return reconnect_remaining_time(self, player)


async def reconnected_send_pending_ask(self, user_id: int):
    """重连后若当前处于 ask_hand 或 ask_other 等待中，向该玩家补发对应消息；剩余时间按经过时间重算（与正常广播逻辑一致：ask_hand 仅当前出牌者收，ask_other 仅有待选操作的玩家收）。"""
    player = next((p for p in self.player_list if p.user_id == user_id), None)
    if player is None or user_id not in self.game_server.user_id_to_connection:
        return
    reconnect_idx = player.player_index
    player_conn = self.game_server.user_id_to_connection[user_id]
    remaining_sent = _reconnect_remaining_time(self, player)
    if self.game_status == "waiting_hand_action":
        if reconnect_idx == self.current_player_index:
            response = Response(
                type="gamestate/guobiao/broadcast_hand_action",
                success=True,
                message="发牌，并询问手牌操作",
                    ask_hand_action_info=Ask_hand_action_info(
                    remaining_time=remaining_sent,
                    player_index=self.current_player_index,
                    remain_tiles=len(self.tiles_list),
                    action_list=self.action_dict.get(reconnect_idx, []),
                    action_tick=self.server_action_tick,
                    dealer_index=getattr(self, 'dealer_index', 0),
                    opening_buhua_complete=getattr(self, '_opening_buhua_complete_pending', False) or None,
                ),
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            logger.info(f"重连补发 ask_hand 给玩家 {player.username}，剩余时间 {remaining_sent}s")
    elif self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang"):
        if self.action_dict.get(reconnect_idx):
            if self.game_status == "waiting_action_qianggang" and getattr(self, "jiagang_tile", None) is not None:
                cut_tile = self.jiagang_tile
            else:
                cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
            response = Response(
                type="gamestate/guobiao/ask_other_action",
                success=True,
                message="询问操作",
                ask_other_action_info=Ask_other_action_info(
                    remaining_time=remaining_sent,
                    action_list=self.action_dict[reconnect_idx],
                    cut_tile=cut_tile,
                    action_tick=self.server_action_tick,
                    player_index=player.player_index,
                ),
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            logger.info(f"重连补发 ask_other 给玩家 {player.username}，剩余时间 {remaining_sent}s")


# 广播操作
def _build_do_action_payload(
    self,
    action_list,
    action_player,
    viewer_index,
    *,
    cut_tile=None,
    cut_class=None,
    cut_tile_index=None,
    deal_tile=None,
    buhua_tile=None,
    combination_mask=None,
    combination_target=None,
    is_claim=False,
    silent=False,
    is_mo_gang=None,
    is_mo_buhua=None,
    cut_from_player=None,
):
    viewer_mask = combination_mask
    viewer_target = combination_target
    if action_list and "angang" in action_list:
        viewer_mask = sanitize_angang_mask(combination_mask, action_player, viewer_index)
        viewer_target = sanitize_combination_target_for_viewer(
            combination_target, action_player, viewer_index
        )
    viewer_deal_tile = sanitize_deal_tile_for_viewer(deal_tile, action_player, viewer_index)
    return {
        "action_list": action_list,
        "action_player": action_player,
        "action_tick": self.server_action_tick,
        "cut_tile": cut_tile,
        "cut_class": cut_class,
        "cut_tile_index": cut_tile_index,
        "deal_tile": viewer_deal_tile,
        "buhua_tile": buhua_tile,
        "combination_mask": viewer_mask,
        "combination_target": viewer_target,
        "is_claim": True if is_claim else None,
        "silent": True if silent else None,
        "is_mo_gang": is_mo_gang,
        "is_mo_buhua": is_mo_buhua,
        # 鸣牌（吃/碰/明杠）真正认走的打牌者座位；仅 meld 帧由 wait_action 显式传入，
        # 客户端据此精确移除对应玩家牌河的弃牌，彻底消除乱序/双同牌歧义。cut 帧等无需此字段。
        "cut_from_player": cut_from_player,
    }


async def _deliver_do_action_payload_to_viewer(self, viewer_index: int, payload: dict, msg_type: str = "gamestate/guobiao/do_action"):
    current_player = self.player_list[viewer_index]
    if "offline" in current_player.tag_list:
        return
    if current_player.user_id < 10:
        return
    if current_player.user_id not in self.game_server.user_id_to_connection:
        return
    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
    response = Response(
        type=msg_type,
        success=True,
        message="返回操作内容",
        do_action_info=Do_action_info(**payload),
    )
    await player_conn.websocket.send_json(response.dict(exclude_none=True))
    await self.send_to_realtime_spectators(current_player.player_index, response)


async def _send_do_action_payload_to_viewer(self, viewer_index: int, payload: dict, msg_type: str = "gamestate/guobiao/do_action"):
    from ..public.outbound_pipe import send_to_viewer

    delay_before = take_post_meld_gap_delay(self, viewer_index)

    async def _do():
        await _deliver_do_action_payload_to_viewer(self, viewer_index, payload, msg_type)

    await send_to_viewer(self, viewer_index, _do, delay_before=delay_before)


async def broadcast_do_action(
    self, 
    action_list: List[str],
    action_player: int,
    cut_tile: int = None,
    cut_class: bool = None,
    cut_tile_index: int = None,
    deal_tile: int = None,
    buhua_tile: int = None,
    combination_target: str = None,
    combination_mask: List[int] = None,
    is_claim: bool = False,
    silent: bool = False,
    is_mo_gang: bool = None,
    is_mo_buhua: bool = None,
    cut_from_player: int = None,
    ):
    # 战术鸣牌的实际行为静默执行：申请阶段已发声/动画，本次仅状态变更
    if not is_claim and not silent and getattr(self, "_tactical_silent_action", False):
        silent = True
        self._tactical_silent_action = False
    # 战术鸣牌的 is_claim 广播不递增操作帧，避免破坏客户端 action_tick 对齐
    # 实际行为（含 silent）正常递增 action_tick
    if not is_claim:
        self.server_action_tick += 1
        if hasattr(self, "_ask_broadcast_time"):
            delattr(self, "_ask_broadcast_time")
    elif action_list and cut_tile is not None:
        from ..public.game_record_manager import track_claim_application
        track_claim_application(self, action_player, action_list[0], cut_tile)
    # 鸣牌保护：仅在 after_cut 鸣牌区间内（_cp_active）生效；加杠/暗杠等手牌操作不受影响
    interval_active = claim_protection_enabled(self) and getattr(self, "_cp_active", False)
    is_cut = bool(action_list) and action_list[0] == "cut"
    is_real_meld = (not is_claim) and bool(action_list) and action_list[0] in REAL_MELD_ACTIONS
    # 本 do_action 调用前受保护观众是否已揭示过出牌（含 claim_protect_delay 超时 flush）。
    # 用于区分：已揭示且看过 is_claim 时实际鸣牌应静默（战术）；追赶 flush 的 cut 始终有声。
    cut_already_revealed = getattr(self, "_cp_cut_flushed", False)

    # 服务器驱动节奏：实际鸣牌与战术申请（is_claim）都先 flush 暂存 cut。
    # 受保护观众的鸣牌/申请经 outbound_pipe 延迟入队（主循环不阻塞）；非受保护观众立即发送。
    protected_meld_delay = 0.0
    if interval_active and (is_real_meld or is_claim):
        protected_meld_delay = await prepare_protected_meld_for_viewers(
            self,
            _send_do_action_payload_to_viewer,
        )

    deferred_protected_sends = []  # [(viewer_index, payload)] 经 pipe 延迟发给受保护观众

    for i, current_player in enumerate(self.player_list):
        try:
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                continue
            if current_player.user_id < 10:
                continue

            protected = interval_active and is_protected_viewer(self, i)

            # 受保护观众实际鸣牌：
            # - 出牌尚未揭示（本次广播刚 flush）：正常发声；
            # - 出牌已揭示（含超时后收到 is_claim）：尊重 silent（战术申请后静默执行）。
            if protected and is_real_meld:
                viewer_silent = silent if cut_already_revealed else False
            else:
                viewer_silent = silent

            payload = _build_do_action_payload(
                self,
                action_list,
                action_player,
                i,
                cut_tile=cut_tile,
                cut_class=cut_class,
                cut_tile_index=cut_tile_index,
                deal_tile=deal_tile,
                buhua_tile=buhua_tile,
                combination_mask=combination_mask,
                combination_target=combination_target,
                is_claim=is_claim,
                silent=viewer_silent,
                is_mo_gang=is_mo_gang,
                is_mo_buhua=is_mo_buhua,
                cut_from_player=cut_from_player,
            )

            # 出牌对受保护观众延迟：暂存，待鸣牌/申请/pass/超时触发 flush
            if protected and is_cut:
                stash_protected_cut_payload(self, i, payload)
                continue

            # 受保护观众的鸣牌/申请：pipe 延迟入队，不阻塞主循环
            if protected and (is_real_meld or is_claim) and protected_meld_delay > 0:
                deferred_protected_sends.append((i, payload, is_real_meld))
                continue

            if current_player.user_id in self.game_server.user_id_to_connection:
                await _send_do_action_payload_to_viewer(self, i, payload)
                if protected and is_real_meld:
                    mark_post_meld_gap(self, i)
                logger.info(f"已向玩家 {current_player.username} 广播操作信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播操作信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

    if deferred_protected_sends:
        from ..public.outbound_pipe import schedule_viewer_send

        for i, payload, defer_real_meld in deferred_protected_sends:
            def _make_send(vi=i, p=payload):
                async def _do():
                    await _deliver_do_action_payload_to_viewer(self, vi, p)
                return _do

            schedule_viewer_send(
                self, i, _make_send(), delay_before=protected_meld_delay,
            )
            if defer_real_meld:
                mark_post_meld_gap(self, i)

    # 出牌广播完成后，启动 claim_protect_delay 超时定时器：到点把暂存出牌发给受保护观众
    if interval_active and is_cut:
        arm_claim_protection_timer(self, _send_do_action_payload_to_viewer)

    # 实际鸣牌广播完成后结束本鸣牌保护区间（鸣牌者将切牌进入新区间）
    if interval_active and is_real_meld:
        end_claim_protection_interval(self)

# 广播结算结果
async def broadcast_result(self, 
                          hepai_player_index: Optional[int] = None, 
                          player_to_score: Optional[Dict[int, int]] = None, 
                          hu_score: Optional[int] = None, 
                          hu_fan: Optional[List[str]] = None, 
                          hu_class: str = None,
                          hepai_player_hand: Optional[List[int]] = None,
                          hepai_player_huapai: Optional[List[int]] = None,
                          hepai_player_combination_mask: Optional[List[List[int]]] = None,
                          score_changes: Optional[Dict[int, int]] = None,
                          revealed_angang_masks: Optional[dict] = None,
                          silent: bool = False,
                          next_status: Optional[str] = None):
    # 战术鸣牌：胡牌结算复用申请阶段的发声/动画，本次静默
    if not silent and getattr(self, "_tactical_silent_action", False):
        silent = True
        self._tactical_silent_action = False
    self.server_action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        try:
            # 如果玩家掉线，跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                continue

            # 机器人占用 user_id < 10 的保留段，无需网络广播
            if current_player.user_id < 10:
                continue

            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="gamestate/guobiao/show_result",
                    success=True,
                    message="显示结算结果",
                    show_result_info=Show_result_info(
                        hepai_player_index=hepai_player_index, # 和牌玩家索引
                        player_to_score=player_to_score, # 所有玩家分数
                        hu_score=hu_score, # 和牌分数
                        hu_fan=hu_fan, # 和牌番种
                        hu_class=hu_class, # 和牌类别
                        hepai_player_hand=hepai_player_hand, # 和牌玩家手牌
                        hepai_player_huapai=hepai_player_huapai, # 和牌玩家花牌列表
                        hepai_player_combination_mask=hepai_player_combination_mask, # 和牌玩家组合掩码
                        action_tick=self.server_action_tick,
                        score_changes=score_changes,
                        revealed_angang_masks=revealed_angang_masks,
                        silent=True if silent else None,
                        next_status=next_status,
                    )
                )
                from ..public.outbound_pipe import send_to_viewer

                delay_before = take_post_meld_gap_delay(self, i)

                async def _do(conn=player_conn, resp=response, idx=current_player.player_index):
                    await conn.websocket.send_json(resp.dict(exclude_none=True))
                    await self.send_to_realtime_spectators(idx, resp)

                await send_to_viewer(self, i, _do, delay_before=delay_before)
                logger.info(f"已向玩家 {current_player.username} 广播结算结果信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播结算结果信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

async def broadcast_game_end(self):
    """广播游戏结束信息"""
    self.server_action_tick += 1
    
    # 构建玩家最终数据字典，键为座位索引字符串 "0"～"3"（同分并列名次时 rank 可能重复，故不能用名次作键）
    player_final_data = {}
    for player in self.player_list:
        pt = getattr(player, 'pt', 0)
        player_final_data[str(player.player_index)] = Player_final_data(
            rank=player.record_counter.rank_result,
            score=player.score,
            pt=pt,
            username=player.username,
            original_player_index=player.original_player_index,
            rank_before=getattr(player, 'rank_before', None),
            score_before=getattr(player, 'score_before', None),
            rank_after=getattr(player, 'rank_after', None),
            score_after=getattr(player, 'score_after', None),
        )
    
    # 为每个玩家发送游戏结束信息
    for current_player in self.player_list:
        try:
            # 如果玩家掉线，跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                continue
            
            # 机器人占用 user_id < 10 的保留段，无需网络广播
            if current_player.user_id < 10:
                continue
            
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="gamestate/guobiao/game_end",
                    success=True,
                    message="游戏结束",
                    game_end_info=Game_end_info(
                        master_seed=self.master_seed,  # 游戏结束时发送完整随机种子供验证
                        commitment=self.commitment,
                        salt=self.salt,
                        player_final_data=player_final_data
                    )
                )

                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
                logger.info(f"已向玩家 user_id={current_player.user_id}, username={current_player.username} 广播游戏结束信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播游戏结束信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

# 广播换位信息
async def broadcast_switch_seat(self):
    """广播换位信息"""
    switch_seat_info = Switch_seat_info(
        current_round=self.current_round
    )

    # 为每个玩家发送换位信息
    for current_player in self.player_list:
        try:
            # 如果玩家掉线，跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                continue
            
            # 机器人占用 user_id < 10 的保留段，无需网络广播
            if current_player.user_id < 10:
                continue
            
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="switch_seat",
                    success=True,
                    message="换位信息",
                    switch_seat_info=switch_seat_info
                )

                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
                logger.info(f"已向玩家 {current_player.username} 发送换位信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 发送换位信息失败: {e}")

# 广播刷新玩家标签列表
async def broadcast_refresh_player_tag_list(self):
    """广播刷新所有玩家标签列表信息"""
    # 构建所有玩家的标签列表映射
    player_to_tag_list = {}
    for player in self.player_list:
        player_to_tag_list[player.player_index] = player.tag_list
    
    refresh_tag_info = Refresh_player_tag_list_info(
        player_to_tag_list=player_to_tag_list
    )

    # 为每个玩家发送刷新标签列表信息
    for current_player in self.player_list:
        try:

            # 如果玩家掉线，跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                continue
            
            # 机器人占用 user_id < 10 的保留段，无需网络广播
            if current_player.user_id < 10:
                continue
            
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="refresh_player_tag_list",
                    success=True,
                    message="刷新玩家标签列表",
                    refresh_player_tag_list_info=refresh_tag_info
                )

                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
                logger.info(f"已向玩家 {current_player.username} 发送刷新玩家标签列表信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 发送刷新玩家标签列表信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

# 广播准备状态
async def broadcast_ready_status(self):
    """广播所有玩家的准备状态"""
    # 判断准备状态
    player_to_ready = {}
    for player in self.player_list:
        # 如果玩家仍在等待准备，说明未准备
        # 使用.get()方法安全访问，如果键不存在或值为空列表，说明已准备
        action_list = self.action_dict.get(player.player_index, [])
        if action_list and "ready" in action_list:
            player_to_ready[player.player_index] = False
        else:
            player_to_ready[player.player_index] = True
    
    ready_info = Ready_status_info(
        player_to_ready=player_to_ready
    )
    
    # 为每个玩家发送准备状态信息
    for current_player in self.player_list:
        try:
            # 如果玩家掉线，跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                continue
            
            # 机器人占用 user_id < 10 的保留段，无需网络广播
            if current_player.user_id < 10:
                continue
            
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                
                response = Response(
                    type="gamestate/guobiao/ready_status",
                    success=True,
                    message="准备状态更新",
                    ready_status_info=ready_info
                )
                
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
                logger.info(f"已向玩家 {current_player.username} 发送准备状态信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 发送准备状态信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播
