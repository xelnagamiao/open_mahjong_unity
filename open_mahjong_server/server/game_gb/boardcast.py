from ..response import Response,GameInfo,Ask_hand_action_info,Ask_other_action_info,Do_action_info,Show_result_info,Game_end_info,Player_final_data,Switch_seat_info,Refresh_player_tag_list_info
from typing import List, Dict, Optional
import logging

logger = logging.getLogger(__name__)

# 广播游戏开始/重连 方法
async def broadcast_game_start(self):
    """广播游戏开始信息"""
    # 基础游戏信息
    base_game_info = {
        'room_id': self.room_id, # 房间ID
        'tips': self.tips, # 是否提示
        'current_player_index': self.current_player_index, # 当前轮到的玩家索引
        "action_tick": self.server_action_tick, # 操作帧
        'max_round': self.max_round, # 最大局数
        'tile_count': len(self.tiles_list), # 牌山剩余牌数
        'round_random_seed': self.round_random_seed, # 单局随机种子
        'current_round': self.current_round, # 当前轮数
        'step_time': self.step_time, # 步时
        'round_time': self.round_time, # 局时
        'room_type': self.room_type, # 房间类型
        'open_cuohe': self.open_cuohe, # 是否开启错和
        'isPlayerSetRandomSeed': self.isPlayerSetRandomSeed, # 是否玩家设置了随机种子
        'players_info': [] # ↓玩家信息
    }
    # 为每个玩家准备信息
    for player in self.player_list: # 遍历玩家列表
        player_info = {
            'user_id': player.user_id, # 用户ID
            'username': player.username, # 用户名（用于显示）
            'hand_tiles_count': len(player.hand_tiles), # 手牌数量
            'discard_tiles': player.discard_tiles, # 弃牌
            'discard_origin_tiles': player.discard_origin_tiles, # 理论弃牌
            'combination_tiles': player.combination_tiles, # 组合
            "combination_mask": player.combination_mask, # 组合形状
            "huapai_list": player.huapai_list, # 花牌列表
            'remaining_time': player.remaining_time, # 剩余局时
            'player_index': player.player_index, # 东南西北位置
            'original_player_index': player.original_player_index, # 原始玩家索引 东南西北 0 1 2 3
            'score': player.score, # 分数
            "title_used": player.title_used, # 称号ID
            'profile_used': player.profile_used, # 使用的头像ID
            'character_used': player.character_used, # 使用的角色ID
            'voice_used': player.voice_used, # 使用的音色ID
            'score_history': player.score_history, # 分数历史变化列表
            'tag_list': player.tag_list, # 标签列表
        }
        base_game_info['players_info'].append(player_info) # 将字典添加到列表中

    # 为每个玩家发送消息
    for current_player in self.player_list:
        try:
            # 如果player_list中有玩家在self.game_server.user_id_to_connection:
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                
                # 将游戏信息字典转换为 GameInfo 类 并添加 self_hand_tiles 字段
                game_info = GameInfo(
                    **base_game_info,
                    self_hand_tiles=current_player.hand_tiles  # 只包含当前玩家的手牌
                )

                response = Response(
                    type="game_start_GB",
                    success=True,
                    message="游戏开始",
                    game_info=game_info
                )
                
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                logger.info(f"已向玩家 {current_player.username} 发送游戏开始信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 发送消息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

# 广播询问手牌操作 补花 加杠 暗杠 自摸 出牌
async def broadcast_ask_hand_action(self):
    self.server_action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        try:
            if i == self.current_player_index:
                # 对当前玩家发送包含摸牌信息的消息
                if current_player.user_id in self.game_server.user_id_to_connection:
                    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                    response = Response(
                        type="broadcast_hand_action_GB",
                        success=True,
                        message="发牌，并询问手牌操作",
                        ask_hand_action_info = Ask_hand_action_info(
                            remaining_time=current_player.remaining_time,
                            player_index= self.current_player_index,
                            remain_tiles=len(self.tiles_list),
                            action_list=self.action_dict[i],
                            action_tick=self.server_action_tick
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    logger.info(f"已向玩家 {current_player.username} 广播手牌操作信息")
                else:
                    logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
            else:
                # 向其余玩家发送通用消息
                if current_player.user_id in self.game_server.user_id_to_connection:
                    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                    response = Response(
                        type="broadcast_hand_action_GB",
                        success=True,
                        message="发牌，并询问手牌操作",
                        ask_hand_action_info = Ask_hand_action_info(
                            remaining_time=current_player.remaining_time,
                            player_index= self.current_player_index,
                            remain_tiles=len(self.tiles_list),
                            action_list=self.action_dict[i],
                            action_tick=self.server_action_tick
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    logger.info(f"已向玩家 {current_player.username} 广播手牌操作信息")
                else:
                    logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播手牌操作信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

# 广播询问切牌后操作 吃 碰 杠 胡
async def broadcast_ask_other_action(self):
    cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
    self.server_action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        try:
            if self.action_dict[i] != []:
                # 发送询问行动信息
                if current_player.user_id in self.game_server.user_id_to_connection:
                    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                    response = Response(
                        type="ask_other_action_GB",
                        success=True,
                        message="询问操作",
                        ask_other_action_info = Ask_other_action_info(
                            remaining_time=current_player.remaining_time,
                            action_list=self.action_dict[i],
                            cut_tile=cut_tile,
                            action_tick=self.server_action_tick
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    logger.info(f"已向玩家 {current_player.username} 广播询问操作信息")
                else:
                    logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
            else:
                # 发送通用信息
                if current_player.user_id in self.game_server.user_id_to_connection:
                    player_conn = self.game_server.user_id_to_connection[current_player.user_id]
                    response = Response(
                        type="ask_other_action_GB",
                        success=True,
                        message="询问操作",
                        ask_other_action_info = Ask_other_action_info(
                            remaining_time=current_player.remaining_time,
                            action_list=[],
                            cut_tile=cut_tile,
                            action_tick=self.server_action_tick
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    logger.info(f"已向玩家 {current_player.username} 广播通用询问操作信息")
                else:
                    logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播询问操作信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

# 广播操作
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
    combination_mask: List[int] = None
    ):
    
    self.server_action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        try:
            # 发送通用信息
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="do_action_GB",
                    success=True,
                    message="返回操作内容",
                    do_action_info=Do_action_info(
                        action_list=action_list,
                        action_player=action_player,
                        action_tick=self.server_action_tick,
                        cut_tile=cut_tile,
                        cut_class=cut_class,
                        cut_tile_index = cut_tile_index,
                        deal_tile=deal_tile,
                        buhua_tile=buhua_tile,
                        combination_mask=combination_mask,
                        combination_target=combination_target
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
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
                          hepai_player_combination_mask: Optional[List[List[int]]] = None):
    self.server_action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        try:
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="show_result_GB",
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
                        action_tick=self.server_action_tick
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                logger.info(f"已向玩家 {current_player.username} 广播结算结果信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 广播结算结果信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播

async def broadcast_game_end(self):
    """广播游戏结束信息"""
    self.server_action_tick += 1
    
    # 构建玩家最终数据字典 {user_id: Player_final_data}
    player_final_data = {}
    for player in self.player_list:
        player_final_data[player.user_id] = Player_final_data(
            rank=player.record_counter.rank_result,
            score=player.score,
            pt=0,  # 默认0分
            username=player.username
        )
    
    # 为每个玩家发送游戏结束信息
    for current_player in self.player_list:
        try:
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="game_end_GB",
                    success=True,
                    message="游戏结束",
                    game_end_info=Game_end_info(
                        game_random_seed=self.game_random_seed,  # 游戏结束时发送完整随机种子供验证
                        player_final_data=player_final_data
                    )
                )

                await player_conn.websocket.send_json(response.dict(exclude_none=True))
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
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="switch_seat",
                    success=True,
                    message="换位信息",
                    switch_seat_info=switch_seat_info
                )

                await player_conn.websocket.send_json(response.dict(exclude_none=True))
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
            if current_player.user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[current_player.user_id]

                response = Response(
                    type="refresh_player_tag_list",
                    success=True,
                    message="刷新玩家标签列表",
                    refresh_player_tag_list_info=refresh_tag_info
                )

                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                logger.info(f"已向玩家 {current_player.username} 发送刷新玩家标签列表信息")
            else:
                logger.warning(f"玩家 {current_player.username} (user_id={current_player.user_id}) 未连接，跳过广播")
        except Exception as e:
            logger.error(f"向玩家 {current_player.username} (user_id={current_player.user_id}) 发送刷新玩家标签列表信息失败: {e}")
            # 允许广播出错，继续向其他玩家广播