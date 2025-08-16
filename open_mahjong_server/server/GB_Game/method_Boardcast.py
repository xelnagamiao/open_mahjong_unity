from response import Response,GameInfo,Ask_hand_action_info,Ask_other_action_info,Do_action_info
from typing import List

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
                print(f"已向玩家 {current_player.username} 发送游戏开始信息")
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
                print(f"已向玩家 {current_player.username} 广播手牌操作信息")
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
                print(f"已向玩家 {current_player.username} 广播手牌操作信息")

# 广播询问切牌后操作 吃 碰 杠 胡
async def broadcast_ask_other_action(self):
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
                    ask_action_info = Ask_other_action_info(
                        remaining_time=current_player.remaining_time,
                        action_list=[item for item in self.action_dict[i]],
                        cut_tile=self.player_list[self.current_player_index].discard_tiles[-1],
                        action_tick=self.action_tick
                    )
                )
                print(response)
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                print(f"已向玩家 {current_player.username} 广播询问操作信息")
        else:
            # 发送通用信息
            if current_player.username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[current_player.username]
                response = Response(
                    type="ask_other_action_GB",
                    success=True,
                    message="询问操作",
                    ask_action_info = Ask_other_action_info(
                        remaining_time=current_player.remaining_time,
                        action_list=[],
                        cut_tile=self.player_list[self.current_player_index].discard_tiles[-1],
                        action_tick=self.action_tick
                    )
                )
                await player_conn.websocket.send_json(response.dict(exclude_none=True))
                print(f"已向玩家 {current_player.username} 广播询问操作信息")

# 广播操作
async def broadcast_do_action(
    self, 
    action_list: List[str],
    action_player: int,
    cut_tile: int = None,
    cut_class: bool = None,
    deal_tile: int = None,
    buhua_tile: int = None,
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
            
            do_action_info = Do_action_info(**do_action_data)
            
            response = Response(
                type="do_action_GB",
                success=True,
                message="返回操作内容",
                do_action_info=do_action_info
            )
            await player_conn.websocket.send_json(response.dict(exclude_none=True))
            print(f"已向玩家 {current_player.username} 广播操作信息")

# 建立一个data列表 传参 [jiagang,dealcard], # 各种参数 然后按传参的数量和变量依次生成data列表，最后由前端统一解析。
