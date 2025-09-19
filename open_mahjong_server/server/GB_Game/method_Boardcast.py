from response import Response,GameInfo,Ask_hand_action_info,Ask_other_action_info,Do_action_info,Show_result_info
from typing import List, Dict, Optional

# 广播游戏开始/重连 方法
async def broadcast_game_start(self):
    """广播游戏开始信息"""
    # 基础游戏信息
    base_game_info = {
        'room_id': self.room_id, # 房间ID
        'tips': self.tips, # 是否提示
        'current_player_index': self.current_player_index, # 当前轮到的玩家索引
        "action_tick": self.action_tick, # 操作帧
        'max_round': self.max_round, # 最大局数
        'tile_count': len(self.tiles_list), # 牌山剩余牌数
        'random_seed': self.random_seed, # 随机种子
        'current_round': self.current_round, # 当前轮数
        'step_time': self.step_time, # 步时
        'round_time': self.round_time, # 局时
        'players_info': [] # ↓玩家信息
    }
    # 为每个玩家准备信息
    for player in self.player_list: # 遍历玩家列表
        player_info = {
            'username': player.username, # 用户名
            'hand_tiles_count': len(player.hand_tiles), # 手牌数量
            'discard_tiles': player.discard_tiles, # 弃牌
            'combination_tiles': player.combination_tiles, # 组合
            "combination_mask": player.combination_mask, # 组合形状
            "huapai_list": player.huapai_list, # 花牌列表
            'remaining_time': player.remaining_time, # 剩余局时
            'player_index': player.player_index, # 东南西北位置
            'score': player.score # 分数
        }
        base_game_info['players_info'].append(player_info) # 将字典添加到列表中

    # 为每个玩家发送消息
    for current_player in self.player_list:
        try:
            # 如果player_list中有玩家在self.room.game_server.username_to_connection:
            if current_player.username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[current_player.username]
                
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
                print(f"已向玩家 {current_player.username} 发送游戏开始信息{response.dict(exclude_none=True)}")
        except Exception as e:
            print(f"向玩家 {current_player.username} 发送消息失败: {e}")

# 广播询问手牌操作 补花 加杠 暗杠 自摸 出牌
async def broadcast_ask_hand_action(self):
    self.action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        if i == self.current_player_index:
            # 对当前玩家发送包含摸牌信息的消息
            if current_player.username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[current_player.username]
                response = Response(
                    type="broadcast_hand_action_GB",
                    success=True,
                    message="发牌，并询问手牌操作",
                    ask_hand_action_info = Ask_hand_action_info(
                        remaining_time=current_player.remaining_time,
                        player_index= self.current_player_index,
                        deal_tiles=self.player_list[i].hand_tiles[-1],
                        remain_tiles=len(self.tiles_list),
                        action_list=self.action_dict[i],
                        action_tick=self.action_tick
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                print(f"已向玩家 {current_player.username} 广播手牌操作信息{response.dict(exclude_none=True)}")
        else:
            # 向其余玩家发送通用消息
            if current_player.username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[current_player.username]
                response = Response(
                    type="broadcast_hand_action_GB",
                    success=True,
                    message="发牌，并询问手牌操作",
                    ask_hand_action_info = Ask_hand_action_info(
                        remaining_time=current_player.remaining_time,
                        player_index= self.current_player_index,
                        deal_tiles=0,
                        remain_tiles=len(self.tiles_list),
                        action_list=self.action_dict[i],
                        action_tick=self.action_tick
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                print(f"已向玩家 {current_player.username} 广播手牌操作信息{response.dict(exclude_none=True)}")

# 广播询问切牌后操作 吃 碰 杠 胡
async def broadcast_ask_other_action(self):
    cut_tile = self.player_list[self.current_player_index].discard_tiles[-1]
    self.action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        if self.action_dict[i] != []:
            # 发送询问行动信息
            if current_player.username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[current_player.username]
                response = Response(
                    type="ask_other_action_GB",
                    success=True,
                    message="询问操作",
                    ask_other_action_info = Ask_other_action_info(
                        remaining_time=current_player.remaining_time,
                        action_list=self.action_dict[i],
                        cut_tile=cut_tile,
                        action_tick=self.action_tick
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                print(f"已向玩家 {current_player.username} 广播询问操作信息{response.dict(exclude_none=True)}")
        else:
            # 发送通用信息
            if current_player.username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[current_player.username]
                response = Response(
                    type="ask_other_action_GB",
                    success=True,
                    message="询问操作",
                    ask_other_action_info = Ask_other_action_info(
                        remaining_time=current_player.remaining_time,
                        action_list=[],
                        cut_tile=cut_tile,
                        action_tick=self.action_tick
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                print(f"已向玩家 {current_player.username} 广播通用询问操作信息{response.dict(exclude_none=True)}")

# 广播操作
async def broadcast_do_action(
    self, 
    action_list: List[str],
    action_player: int,
    cut_tile: int = None,
    cut_class: bool = None,
    deal_tile: int = None,
    buhua_tile: int = None,
    combination_target: str = None,
    combination_mask: List[int] = None
    ):
    
    self.action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        # 发送通用信息
        if current_player.username in self.game_server.username_to_connection:
            player_conn = self.game_server.username_to_connection[current_player.username]
            # 只传递有值的参数
            do_action_data = {
                "action_list": action_list,
                "action_player": action_player,
                "action_tick": self.action_tick
            }
            
            if cut_tile is not None:
                do_action_data["cut_tile"] = cut_tile
            if cut_class is not None:
                do_action_data["cut_class"] = cut_class
            if deal_tile is not None:
                do_action_data["deal_tile"] = deal_tile
            if buhua_tile is not None:
                do_action_data["buhua_tile"] = buhua_tile
            if combination_mask is not None:
                do_action_data["combination_mask"] = combination_mask
            if combination_target is not None:
                do_action_data["combination_target"] = combination_target
            do_action_info = Do_action_info(**do_action_data)
            
            response = Response(
                type="do_action_GB",
                success=True,
                message="返回操作内容",
                do_action_info=do_action_info
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            print(f"已向玩家 {current_player.username} 广播操作信息{response.dict(exclude_none=True)}")

# 广播结算结果
async def broadcast_result(self, 
                          hepai_player_index: Optional[int] = None, 
                          player_to_point: Optional[Dict[int, int]] = None, 
                          hu_point: Optional[int] = None, 
                          hu_fan: Optional[int] = None, 
                          hu_class: str = None,
                          hepai_player_hand: Optional[List[int]] = None,
                          hepai_player_huapai: Optional[List[int]] = None,
                          hepai_player_combinations_mask: Optional[List[int]] = None):
    self.action_tick += 1
    # 遍历列表时获取索引
    for i, current_player in enumerate(self.player_list):
        if current_player.username in self.game_server.username_to_connection:
            player_conn = self.game_server.username_to_connection[current_player.username]
            
            response = Response(
                type="show_result_GB",
                success=True,
                message="显示结算结果",
                show_result_info=Show_result_info(
                    hepai_player_index=hepai_player_index, # 和牌玩家索引
                    player_to_point=player_to_point, # 所有玩家分数
                    hu_point=hu_point, # 和牌分数
                    hu_fan=hu_fan, # 和牌番种
                    hu_class=hu_class, # 和牌类别
                    hepai_player_hand=hepai_player_hand, # 和牌玩家手牌
                    hepai_player_huapai=hepai_player_huapai, # 和牌玩家花牌列表
                    hepai_player_combinations_mask=hepai_player_combinations_mask, # 和牌玩家组合掩码
                    action_tick=self.action_tick
                )
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            print(f"已向玩家 {current_player.username} 广播结算结果信息{response.dict(exclude_none=True)}")

async def broadcast_game_end(self):
    self.action_tick += 1
    for i, current_player in enumerate(self.player_list):
        if current_player.username in self.game_server.username_to_connection:
            player_conn = self.game_server.username_to_connection[current_player.username]
            response = Response(
                type="game_end_GB",
                success=True,
                message="游戏结束",
            )