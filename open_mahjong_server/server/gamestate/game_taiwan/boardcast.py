"""台湾麻将广播。"""

from ...response import Response,GameInfo,Ask_hand_action_info,Ask_other_action_info,Do_action_info,Show_result_info,Game_end_info,Player_final_data,Switch_seat_info,Refresh_player_tag_list_info,Ready_status_info
from typing import List, Dict, Optional
import logging
import asyncio
from ..public.ai.auto_cut_ai import auto_cut_action
from ..public.offline import offline_auto_action
from ..public.ai.smart_bot_ai import smart_bot_action
from ..game_guobiao.combination_mask_view import (
    get_combination_fields_for_viewer,
    sanitize_angang_mask,
    sanitize_combination_target_for_viewer,
)
from ..public.deal_tile_view import sanitize_deal_tile_for_viewer
from ..public.hand_slot_utils import bot_ask_hand_game_status
from ..public.ask_timing import begin_ask_round, note_ask_delivered, reconnect_remaining_time
logger = logging.getLogger(__name__)


def _pending_other_action_tile(game_state) -> int:
    """返回台湾当前鸣牌或抢杠询问所针对的牌。"""
    if game_state.game_status == "waiting_action_qianggang" and game_state.jiagang_tile is not None:
        return game_state.jiagang_tile
    return game_state.player_list[game_state.current_player_index].discard_tiles[-1]

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
        "action_tick": self.server_action_tick, # 操作帧
        'max_round': self.max_round, # 最大局数
        'tile_count': self.playable_wall_count(), # 可摸牌山剩余牌数
        'commitment': self.commitment,  # 承诺值
        'salt': self.salt, # 盐字符串
        'current_round': self.current_round, # 当前轮数
        'step_time': self.step_time, # 步时
        'round_time': self.round_time, # 局时
        'room_type': self.room_type, # 房间类型（custom/match等）
        'room_rule': self.room_rule, # 房间规则
        'sub_rule': getattr(self, 'sub_rule', 'taiwan/standard'), # 子规则（台表显示）
        'hepai_limit': getattr(self, 'hepai_limit', 0), # 台湾起和默认为0
        'open_cuohe': self.open_cuohe, # 是否开启错和
        'show_moqie_hint': getattr(self, 'show_moqie_hint', False), # 手摸切灰显
        'tactical_call': getattr(self, 'tactical_call', False), # 战术鸣牌
        'claim_protection': getattr(self, 'claim_protection', False), # 鸣牌保护
        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed, # 是否玩家设置了随机种子
        'players_info': [], # ↓玩家信息
    }
    base_game_info.update(self.build_game_info_fields())
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
                    type="gamestate/taiwan/game_start",
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
        spectator_manager = self.spectator_manager
        spectator_manager.record_game_title()
        if spectator_manager.game_title:
            spectator_manager.game_title.update(self.build_record_title_fields())
        spectator_manager.record_round_start()


async def send_reconnect_game_state(self, reconnect_player):
    """向台湾麻将重连玩家恢复完整局面和私有手牌。"""
    base_game_info = {
        'room_id': self.room_id,
        'gamestate_id': self.gamestate_id,
        'tips': self.tips,
        'current_player_index': self.current_player_index,
        'action_tick': self.server_action_tick,
        'max_round': self.max_round,
        'tile_count': self.playable_wall_count(),
        'commitment': self.commitment,
        'salt': self.salt,
        'current_round': self.current_round,
        'step_time': self.step_time,
        'round_time': self.round_time,
        'room_type': self.room_type,
        'room_rule': self.room_rule,
        'sub_rule': self.sub_rule,
        'hepai_limit': self.hepai_limit,
        'open_cuohe': self.open_cuohe,
        'show_moqie_hint': getattr(self, 'show_moqie_hint', False),
        'tactical_call': getattr(self, 'tactical_call', False),
        'claim_protection': getattr(self, 'claim_protection', False),
        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed,
        'players_info': [],
    }
    base_game_info.update(self.build_game_info_fields())
    from ..public.game_record_manager import build_player_entry_order_fields
    base_game_info.update(build_player_entry_order_fields(self))

    for player in self.player_list:
        combo_tiles, combo_masks = get_combination_fields_for_viewer(
            player,
            reconnect_player.player_index,
        )
        base_game_info['players_info'].append({
            'user_id': player.user_id,
            'username': player.username,
            'hand_tiles_count': len(player.hand_tiles),
            'hand_tiles': (
                player.hand_tiles
                if player.user_id == reconnect_player.user_id
                else None
            ),
            'discard_tiles': player.discard_tiles,
            'discard_origin_tiles': player.discard_origin_tiles,
            'combination_tiles': combo_tiles,
            'combination_mask': combo_masks,
            'huapai_list': player.huapai_list,
            'remaining_time': player.remaining_time,
            'player_index': player.player_index,
            'original_player_index': player.original_player_index,
            'score': player.score,
            'title_used': player.title_used,
            'profile_used': player.profile_used,
            'character_used': player.character_used,
            'voice_used': player.voice_used,
            'score_history': player.score_history,
            'round_number_history': player.round_number_history,
            'tag_list': player.tag_list,
        })

    game_info = GameInfo(**base_game_info, self_hand_tiles=None)
    response = Response(
        type="gamestate/taiwan/game_start",
        success=True,
        message="重连成功，游戏继续",
        game_info=game_info,
    )
    player_conn = self.game_server.user_id_to_connection[reconnect_player.user_id]
    await player_conn.websocket.send_json(response.dict(exclude_none=True))
    logger.info(f"已向重连玩家 {reconnect_player.username} 发送游戏状态信息")
    await reconnected_send_pending_ask(self, reconnect_player.user_id)


async def send_realtime_spectator_snapshot(
    self,
    spectator_user_id: int,
    view_player_index: int,
):
    """按被观战座位视角补发台湾麻将局面与当前操作询问。"""
    if spectator_user_id not in self.game_server.user_id_to_connection:
        return
    if view_player_index < 0 or view_player_index >= len(self.player_list):
        return

    viewer = self.player_list[view_player_index]
    players_info = []
    for player in self.player_list:
        combo_tiles, combo_masks = get_combination_fields_for_viewer(
            player,
            view_player_index,
        )
        players_info.append({
            'user_id': player.user_id,
            'username': player.username,
            'hand_tiles_count': len(player.hand_tiles),
            'hand_tiles': (
                player.hand_tiles
                if player.player_index == viewer.player_index
                else None
            ),
            'discard_tiles': player.discard_tiles,
            'discard_origin_tiles': player.discard_origin_tiles,
            'combination_tiles': combo_tiles,
            'combination_mask': combo_masks,
            'huapai_list': player.huapai_list,
            'remaining_time': player.remaining_time,
            'player_index': player.player_index,
            'original_player_index': player.original_player_index,
            'score': player.score,
            'title_used': player.title_used,
            'profile_used': player.profile_used,
            'character_used': player.character_used,
            'voice_used': player.voice_used,
            'score_history': player.score_history,
            'round_number_history': player.round_number_history,
            'tag_list': player.tag_list,
        })

    game_info_fields = {
        'room_id': self.room_id,
        'gamestate_id': self.gamestate_id,
        'tips': self.tips,
        'current_player_index': self.current_player_index,
        'action_tick': self.server_action_tick,
        'max_round': self.max_round,
        'tile_count': self.playable_wall_count(),
        'commitment': self.commitment,
        'salt': self.salt,
        'current_round': self.current_round,
        'step_time': self.step_time,
        'round_time': self.round_time,
        'room_type': self.room_type,
        'room_rule': self.room_rule,
        'sub_rule': self.sub_rule,
        'hepai_limit': self.hepai_limit,
        'open_cuohe': self.open_cuohe,
        'show_moqie_hint': getattr(self, 'show_moqie_hint', False),
        'tactical_call': getattr(self, 'tactical_call', False),
        'claim_protection': getattr(self, 'claim_protection', False),
        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed,
        'players_info': players_info,
        'self_hand_tiles': None,
        'view_player_index': view_player_index,
    }
    game_info_fields.update(self.build_game_info_fields())
    from ..public.game_record_manager import build_player_entry_order_fields
    game_info_fields.update(build_player_entry_order_fields(self))

    conn = self.game_server.user_id_to_connection[spectator_user_id]
    response = Response(
        type="gamestate/taiwan/game_start",
        success=True,
        message="实时观战初始化",
        game_info=GameInfo(**game_info_fields),
    )
    await conn.websocket.send_json(response.dict(exclude_none=True))
    await reconnected_send_pending_ask_for_viewer(
        self,
        spectator_user_id,
        view_player_index,
    )


# 广播询问手牌操作 补花 加杠 暗杠 自摸 出牌
async def broadcast_ask_hand_action(self):
    self.server_action_tick += 1
    begin_ask_round(self)
    for current_player in self.player_list:
        try:
            seat_index = current_player.player_index
            player_actions = self.action_dict.get(seat_index, [])
            private_info = (
                self.build_private_hand_action_info(seat_index)
                if seat_index == self.current_player_index
                else {}
            )
            # 如果玩家掉线，启动自动操作并跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                if player_actions:
                    # 自动操作没有 websocket 送达回调；将创建自动响应任务的时刻视为该座位的逻辑送达时刻。
                    note_ask_delivered(self, seat_index)
                    asyncio.create_task(offline_auto_action(
                        self, seat_index, player_actions,
                        bot_ask_hand_game_status(self, seat_index)))
                continue

            # 如果是机器人，启动自动操作并跳过广播；保留 user_id < 10 整段视为机器人
            if current_player.user_id == 0:
                if player_actions:
                    note_ask_delivered(self, seat_index)
                    asyncio.create_task(auto_cut_action(
                        self, seat_index, player_actions,
                        bot_ask_hand_game_status(self, seat_index)))
                continue
            elif current_player.user_id == 2:
                if player_actions:
                    note_ask_delivered(self, seat_index)
                    asyncio.create_task(smart_bot_action(
                        self, seat_index, player_actions,
                        bot_ask_hand_game_status(self, seat_index)))
                continue
            elif current_player.user_id < 10:
                continue

            if current_player.user_id not in self.game_server.user_id_to_connection:
                logger.warning(
                    f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播"
                )
                continue

            player_conn = self.game_server.user_id_to_connection[current_player.user_id]
            response = Response(
                type="gamestate/taiwan/broadcast_hand_action",
                success=True,
                message="发牌，并询问手牌操作",
                ask_hand_action_info=Ask_hand_action_info(
                    remaining_time=current_player.remaining_time,
                    player_index=self.current_player_index,
                    remain_tiles=self.playable_wall_count(),
                    action_list=player_actions,
                    action_tick=self.server_action_tick,
                    **private_info,
                ),
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            note_ask_delivered(self, seat_index)
            await self.send_to_realtime_spectators(current_player.player_index, response)
            logger.info(f"已向玩家 {current_player.username} 广播手牌操作信息")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播手牌操作信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

    # 为观战系统记录 ask_hand tick（在循环外确保只记录一次）
    if hasattr(self, 'spectator_manager'):
        self.spectator_manager.record_ask_hand(self.current_player_index, self.action_dict.get(self.current_player_index, []))

# 广播询问切牌后操作 吃 碰 杠 胡
async def broadcast_ask_other_action(self):
    cut_tile = _pending_other_action_tile(self)
    self.server_action_tick += 1
    begin_ask_round(self)
    for current_player in self.player_list:
        try:
            seat_index = current_player.player_index
            player_actions = self.action_dict.get(seat_index, [])
            # 如果玩家掉线，启动自动操作并跳过广播
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                if player_actions:
                    note_ask_delivered(self, seat_index)
                    asyncio.create_task(offline_auto_action(
                        self, seat_index, player_actions, self.game_status))
                continue

            # 如果是机器人，启动自动操作并跳过广播；保留 user_id < 10 整段视为机器人
            if current_player.user_id == 0:
                if player_actions:
                    note_ask_delivered(self, seat_index)
                    asyncio.create_task(auto_cut_action(
                        self, seat_index, player_actions, self.game_status))
                continue
            elif current_player.user_id == 2:
                if player_actions:
                    note_ask_delivered(self, seat_index)
                    asyncio.create_task(smart_bot_action(
                        self, seat_index, player_actions, self.game_status))
                continue
            elif current_player.user_id < 10:
                continue

            if not player_actions:
                continue
            if current_player.user_id not in self.game_server.user_id_to_connection:
                logger.warning(
                    f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播"
                )
                continue

            player_conn = self.game_server.user_id_to_connection[current_player.user_id]
            response = Response(
                type="gamestate/taiwan/ask_other_action",
                success=True,
                message="询问操作",
                ask_other_action_info=Ask_other_action_info(
                    remaining_time=current_player.remaining_time,
                    action_list=player_actions,
                    cut_tile=cut_tile,
                    action_tick=self.server_action_tick,
                    player_index=seat_index,
                ),
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            note_ask_delivered(self, seat_index)
            await self.send_to_realtime_spectators(current_player.player_index, response)
            logger.info(f"已向玩家 {current_player.username} 广播询问操作信息")

        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播询问操作信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

    # 为观战系统记录 ask_other tick（汇总所有有操作的玩家）
    if hasattr(self, 'spectator_manager'):
        player_action_map = {}
        for idx, actions in self.action_dict.items():
            if actions:
                player_action_map[idx] = actions
        if player_action_map:
            self.spectator_manager.record_ask_other(player_action_map, cut_tile)


def _reconnect_remaining_time(self, player) -> int:
    """重连补发时沿用公共 ask 送达计时。"""
    return reconnect_remaining_time(self, player)


async def reconnected_send_pending_ask_for_viewer(
    self,
    connection_user_id: int,
    view_player_index: int,
):
    """按指定座位视角向连接补发当前操作询问。"""
    if connection_user_id not in self.game_server.user_id_to_connection:
        return
    if view_player_index < 0 or view_player_index >= len(self.player_list):
        return
    player_conn = self.game_server.user_id_to_connection[connection_user_id]
    player = self.player_list[view_player_index]
    remaining_sent = _reconnect_remaining_time(self, player)
    if self.game_status in ("waiting_hand_action", "waiting_buhua_round", "waiting_flower_choice"):
        if view_player_index == self.current_player_index:
            response = Response(
                type="gamestate/taiwan/broadcast_hand_action",
                success=True,
                message="发牌，并询问手牌操作",
                ask_hand_action_info=Ask_hand_action_info(
                    remaining_time=remaining_sent,
                    player_index=self.current_player_index,
                    remain_tiles=self.playable_wall_count(),
                    action_list=self.action_dict.get(view_player_index, []),
                    action_tick=self.server_action_tick,
                    **self.build_private_hand_action_info(view_player_index),
                ),
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            logger.info(f"重连补发 ask_hand 给玩家 {player.username}，剩余时间 {remaining_sent}s")
    elif self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang"):
        if self.action_dict.get(view_player_index):
            cut_tile = _pending_other_action_tile(self)
            response = Response(
                type="gamestate/taiwan/ask_other_action",
                success=True,
                message="询问操作",
                ask_other_action_info=Ask_other_action_info(
                    remaining_time=remaining_sent,
                    action_list=self.action_dict[view_player_index],
                    cut_tile=cut_tile,
                    action_tick=self.server_action_tick,
                    player_index=view_player_index,
                ),
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            logger.info(f"重连补发 ask_other 给玩家 {player.username}，剩余时间 {remaining_sent}s")


async def reconnected_send_pending_ask(self, user_id: int):
    """重连后向玩家补发当前操作询问。"""
    reconnect_idx = next(
        (i for i, player in enumerate(self.player_list) if player.user_id == user_id),
        None,
    )
    if reconnect_idx is None:
        return
    await reconnected_send_pending_ask_for_viewer(self, user_id, reconnect_idx)


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
    silent=False,
    is_mo_gang=None,
    is_mo_buhua=None,
    buhua_recipient=None,
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
    payload = {
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
        "silent": True if silent else None,
        "is_mo_gang": is_mo_gang,
        "is_mo_buhua": is_mo_buhua,
        "buhua_recipient": buhua_recipient,
        # 鸣牌（吃/碰/明杠）真正认走的打牌者座位；仅 meld 帧由 wait_action 显式传入，
        # 客户端据此精确移除对应玩家牌河的弃牌，彻底消除乱序/双同牌歧义。cut 帧等无需此字段。
        "cut_from_player": cut_from_player,
    }
    payload.update(self.build_private_do_action_info(action_player, viewer_index))
    return payload


async def _send_do_action_payload_to_viewer(self, viewer_index: int, payload: dict, msg_type: str = "gamestate/taiwan/do_action"):
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
    silent: bool = False,
    is_mo_gang: bool = None,
    is_mo_buhua: bool = None,
    buhua_recipient: int = None,
    cut_from_player: int = None,
    ):
    self.server_action_tick += 1
    if hasattr(self, "_ask_broadcast_time"):
        delattr(self, "_ask_broadcast_time")

    for i, current_player in enumerate(self.player_list):
        try:
            if "offline" in current_player.tag_list:
                logger.info(f"玩家 {current_player.username} 已掉线，跳过广播")
                continue
            if current_player.user_id < 10:
                continue

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
                silent=silent,
                is_mo_gang=is_mo_gang,
                is_mo_buhua=is_mo_buhua,
                buhua_recipient=buhua_recipient,
                cut_from_player=cut_from_player,
            )

            if current_player.user_id in self.game_server.user_id_to_connection:
                await _send_do_action_payload_to_viewer(self, i, payload)
                logger.info(f"已向玩家 {current_player.username} 广播操作信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播操作信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

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
                          next_status: Optional[str] = None,
                          hepai_tile: Optional[int] = None,
                          multi_ron: Optional[bool] = None,
                          is_qianggang: Optional[bool] = None,
                          ron_discarder_index: Optional[int] = None,
                          recycle_discard: Optional[bool] = None):
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
                    type="gamestate/taiwan/show_result",
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
                        hepai_tile=hepai_tile,
                        multi_ron=multi_ron,
                        is_qianggang=is_qianggang,
                        ron_discarder_index=ron_discarder_index,
                        recycle_discard=recycle_discard,
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                await self.send_to_realtime_spectators(current_player.player_index, response)
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
                    type="gamestate/taiwan/game_end",
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
                    type="gamestate/taiwan/ready_status",
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
