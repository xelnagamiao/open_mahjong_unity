# 观战系统管理器
import asyncio
import time
import logging
from typing import Dict, List, Any, Optional

logger = logging.getLogger(__name__)


class SpectatorManager:
    """观战系统管理器，负责管理延迟观战功能"""
    
    def __init__(self, gamestate, delay: float = 180.0, max_cache_size: int = 10000):
        """
        初始化观战管理器
        
        Args:
            gamestate: 游戏状态实例
            delay: 延迟时间（秒），默认180秒（3分钟）
            max_cache_size: 最大缓存消息数量
        """
        self.gamestate = gamestate
        self.spectator_connections: Dict[int, Any] = {}  # 观战玩家连接 {user_id: PlayerConnection}
        # 以回合为基础的缓存结构: {round_index: {'game_start': message, 'messages': [messages...]}}
        self.round_based_cache: Dict[int, Dict[str, Any]] = {}
        self.message_cache: List[Dict] = []  # 消息缓存列表，按时间顺序存储（用于向后兼容和快速查找）
        self.message_cache_lock = asyncio.Lock()  # 消息缓存锁
        self.spectator_delay = delay  # 延迟时间（秒）
        self.spectator_send_tasks: Dict[int, asyncio.Task] = {}  # 观战玩家的发送任务 {user_id: Task}
        self.max_cache_size = max_cache_size  # 最大缓存消息数量
    
    async def cache_broadcast_message(self, message_type: str, message_data: dict):
        """缓存广播消息，带时间戳，以回合为基础存储"""
        async with self.message_cache_lock:
            current_round = getattr(self.gamestate, 'current_round', 1)
            cached_message = {
                'timestamp': time.time(),  # 消息产生时间
                'type': message_type,
                'data': message_data,
                'action_tick': self.gamestate.server_action_tick,
                'round': current_round,
                'sent_to_spectators': set()  # 已发送给哪些观战玩家
            }
            
            # 添加到时间顺序缓存
            self.message_cache.append(cached_message)
            
            # 以回合为基础存储
            if current_round not in self.round_based_cache:
                self.round_based_cache[current_round] = {
                    'game_start': None,
                    'messages': []
                }
            
            # 如果是game_start，单独存储
            if message_type == 'game_start':
                self.round_based_cache[current_round]['game_start'] = cached_message
            else:
                # 其他消息添加到消息列表
                self.round_based_cache[current_round]['messages'].append(cached_message)
            
            # 限制缓存大小，只保留最近的消息
            if len(self.message_cache) > self.max_cache_size:
                # 保留最近的消息，删除最旧的
                self.message_cache = self.message_cache[-self.max_cache_size:]
                
                # 同时清理回合缓存中最旧的回合
                if self.round_based_cache:
                    oldest_round = min(self.round_based_cache.keys())
                    del self.round_based_cache[oldest_round]
    
    async def add_spectator(self, user_id: int, connection: Any):
        """添加观战玩家，检测延迟时间并发送初始状态"""
        from ...response import Response, MessageInfo
        
        if user_id in self.spectator_connections:
            logger.warning(f"观战玩家 {user_id} 已存在，将替换连接")
            await self.remove_spectator(user_id)
        
        # 计算当前时间和目标时间
        current_time = time.time()
        target_time = current_time - self.spectator_delay
        
        # 检查是否有符合条件的game_start消息
        async with self.message_cache_lock:
            # 找到最接近目标时间且时间戳 <= target_time 的game_start
            best_game_start = None
            best_game_start_round = None
            
            for round_index, round_data in self.round_based_cache.items():
                game_start = round_data.get('game_start')
                if game_start and game_start['timestamp'] <= target_time:
                    if best_game_start is None or game_start['timestamp'] > best_game_start['timestamp']:
                        best_game_start = game_start
                        best_game_start_round = round_index
            
            # 如果没有找到符合条件的game_start
            if best_game_start is None:
                # 找到最早的game_start，计算需要等待的时间
                earliest_game_start = None
                for round_data in self.round_based_cache.values():
                    game_start = round_data.get('game_start')
                    if game_start:
                        if earliest_game_start is None or game_start['timestamp'] < earliest_game_start['timestamp']:
                            earliest_game_start = game_start
                
                if earliest_game_start:
                    # 计算需要等待的时间
                    wait_time = earliest_game_start['timestamp'] + self.spectator_delay - current_time
                    wait_seconds = int(wait_time) if wait_time > 0 else 0
                    
                    response = Response(
                        type="spectator/add_spectator",
                        success=False,
                        message=f"需要等待 {wait_seconds} 秒后才能开始观战",
                        message_info=MessageInfo(
                            title="观战延迟",
                            content=f"由于延迟观战机制，您需要等待 {wait_seconds} 秒后才能开始观战"
                        )
                    )
                    try:
                        await connection.websocket.send_json(response.dict(exclude_none=True))
                    except Exception as e:
                        logger.error(f"向观战玩家 {user_id} 发送等待提示失败: {e}")
                    return
                else:
                    # 完全没有game_start消息
                    response = Response(
                        type="spectator/add_spectator",
                        success=False,
                        message="未能找到对应时间戳的开始游戏消息",
                        message_info=MessageInfo(
                            title="观战失败",
                            content="未能找到对应时间戳的开始游戏消息，无法开始观战"
                        )
                    )
                    try:
                        await connection.websocket.send_json(response.dict(exclude_none=True))
                    except Exception as e:
                        logger.error(f"向观战玩家 {user_id} 发送错误提示失败: {e}")
                    return
        
        # 找到了符合条件的game_start，添加观战玩家
        self.spectator_connections[user_id] = connection
        logger.info(f"添加观战玩家 {user_id}，room_id: {self.gamestate.room_id}")
        
        # 发送成功响应
        response = Response(
            type="spectator/add_spectator",
            success=True,
            message="已成功加入观战"
        )
        try:
            await connection.websocket.send_json(response.dict(exclude_none=True))
        except Exception as e:
            logger.error(f"向观战玩家 {user_id} 发送成功响应失败: {e}")
        
        # 启动延迟消息发送任务
        await self.start_spectator_message_delivery(user_id)
        
        # 发送初始状态（从找到的game_start开始，直到当前时间戳）
        await self.send_spectator_initial_state(user_id, best_game_start_round, best_game_start)
    
    async def remove_spectator(self, user_id: int):
        """移除观战玩家"""
        if user_id in self.spectator_connections:
            del self.spectator_connections[user_id]
            logger.info(f"移除观战玩家 {user_id}，room_id: {self.gamestate.room_id}")
        
        # 取消发送任务
        if user_id in self.spectator_send_tasks:
            task = self.spectator_send_tasks[user_id]
            if not task.done():
                task.cancel()
                try:
                    await task
                except asyncio.CancelledError:
                    pass
            del self.spectator_send_tasks[user_id]
    
    async def send_spectator_initial_state(self, user_id: int, start_round: int, start_game_start: dict):
        """向观战玩家发送初始状态（从指定的game_start开始，按时间顺序发送直到当前时间戳）"""
        if user_id not in self.spectator_connections:
            return
        
        # 计算目标时间（当前时间 - 延迟）
        current_time = time.time()
        target_time = current_time - self.spectator_delay
        
        # 从指定的game_start开始，发送所有消息直到当前时间戳
        async with self.message_cache_lock:
            # 先发送game_start
            if start_game_start:
                await self.send_message_to_spectator(user_id, start_game_start)
                start_game_start['sent_to_spectators'].add(user_id)
            
            # 从该回合开始，发送所有消息直到当前时间戳
            # 遍历所有回合，从start_round开始
            for round_index in sorted(self.round_based_cache.keys()):
                if round_index < start_round:
                    continue
                
                round_data = self.round_based_cache[round_index]
                
                # 发送该回合的所有消息（除了game_start，因为已经发送过了）
                for msg in round_data.get('messages', []):
                    # 只发送时间戳 <= 当前时间戳的消息（发送到当前，不是target_time）
                    if msg['timestamp'] <= current_time and user_id not in msg['sent_to_spectators']:
                        await self.send_message_to_spectator(user_id, msg)
                        msg['sent_to_spectators'].add(user_id)
    
    async def start_spectator_message_delivery(self, user_id: int):
        """为观战玩家启动延迟消息发送任务"""
        async def delivery_loop():
            try:
                while user_id in self.spectator_connections:
                    current_time = time.time()
                    target_time = current_time - self.spectator_delay
                    
                    # 获取需要发送的消息
                    messages_to_send = []
                    async with self.message_cache_lock:
                        for msg in self.message_cache:
                            if (msg['timestamp'] <= target_time and 
                                user_id not in msg.get('sent_to_spectators', set())):
                                messages_to_send.append(msg)
                    
                    # 批量发送消息
                    for msg in messages_to_send:
                        await self.send_message_to_spectator(user_id, msg)
                        async with self.message_cache_lock:
                            if 'sent_to_spectators' not in msg:
                                msg['sent_to_spectators'] = set()
                            msg['sent_to_spectators'].add(user_id)
                    
                    # 等待一小段时间再检查（0.1秒）
                    await asyncio.sleep(0.1)
            except asyncio.CancelledError:
                logger.info(f"观战玩家 {user_id} 的消息发送任务已取消")
            except Exception as e:
                logger.error(f"观战玩家 {user_id} 的消息发送任务出错: {e}")
                # 出错时移除观战玩家
                await self.remove_spectator(user_id)
        
        # 启动任务
        task = asyncio.create_task(delivery_loop())
        self.spectator_send_tasks[user_id] = task
    
    async def send_message_to_spectator(self, user_id: int, message: dict):
        """向观战玩家发送单条消息"""
        if user_id not in self.spectator_connections:
            return
        
        try:
            connection = self.spectator_connections[user_id]
            # 根据消息类型构建响应
            response = self.build_response_from_cached_message(message)
            if response:
                await connection.websocket.send_json(response.dict(exclude_none=True))
        except Exception as e:
            logger.error(f"向观战玩家 {user_id} 发送消息失败: {e}")
            # 如果发送失败，可能是连接断开，移除观战玩家
            await self.remove_spectator(user_id)
    
    def _get_rule_prefix(self) -> str:
        """根据游戏状态获取规则前缀（用于构建消息类型）"""
        room_type = getattr(self.gamestate, 'room_type', 'guobiao')
        # 映射规则类型到消息前缀
        rule_prefix_map = {
            'guobiao': 'guobiao',
            'qingque': 'qingque',
            'riichi': 'riichi'
        }
        return rule_prefix_map.get(room_type, 'guobiao')
    
    def build_response_from_cached_message(self, message: dict):
        """从缓存的消息构建响应对象（使用spectator路由）"""
        from ...response import Response, GameInfo, Ask_hand_action_info, Ask_other_action_info, Do_action_info, Show_result_info, Game_end_info, Player_final_data, Switch_seat_info, Refresh_player_tag_list_info
        
        message_type = message['type']
        data = message['data']
        rule_prefix = self._get_rule_prefix()  # 获取规则前缀
        
        try:
            if message_type == 'game_start':
                # 确保 self_hand_tiles 为 None（手牌在 PlayerInfo 中）
                game_info_data = data.copy()
                if 'self_hand_tiles' not in game_info_data:
                    game_info_data['self_hand_tiles'] = None
                return Response(
                    type=f"spectator/{rule_prefix}/game_start",
                    success=True,
                    message="游戏开始",
                    game_info=GameInfo(**game_info_data)
                )
            elif message_type == 'ask_hand_action':
                return Response(
                    type=f"spectator/{rule_prefix}/broadcast_hand_action",
                    success=True,
                    message="发牌，并询问手牌操作",
                    ask_hand_action_info=Ask_hand_action_info(**data)
                )
            elif message_type == 'ask_other_action':
                return Response(
                    type=f"spectator/{rule_prefix}/ask_other_action",
                    success=True,
                    message="询问操作",
                    ask_other_action_info=Ask_other_action_info(**data)
                )
            elif message_type == 'do_action':
                return Response(
                    type=f"spectator/{rule_prefix}/do_action",
                    success=True,
                    message="返回操作内容",
                    do_action_info=Do_action_info(**data)
                )
            elif message_type == 'show_result':
                return Response(
                    type=f"spectator/{rule_prefix}/show_result",
                    success=True,
                    message="显示结算结果",
                    show_result_info=Show_result_info(**data)
                )
            elif message_type == 'game_end':
                return Response(
                    type=f"spectator/{rule_prefix}/game_end",
                    success=True,
                    message="游戏结束",
                    game_end_info=Game_end_info(**data)
                )
            elif message_type == 'switch_seat':
                return Response(
                    type="spectator/switch_seat",
                    success=True,
                    message="换位信息",
                    switch_seat_info=Switch_seat_info(**data)
                )
            elif message_type == 'refresh_player_tag_list':
                return Response(
                    type="spectator/refresh_player_tag_list",
                    success=True,
                    message="刷新玩家标签列表",
                    refresh_player_tag_list_info=Refresh_player_tag_list_info(**data)
                )
            else:
                logger.warning(f"未知的消息类型: {message_type}")
                return None
        except Exception as e:
            logger.error(f"构建响应对象失败: {e}, message_type: {message_type}, data: {data}")
            return None
    
    async def send_full_game_record_to_spectators(self, game_record: dict):
        """向所有观战玩家发送完整的游戏记录数据（游戏结束时调用）"""
        import json
        from ...response import Response, MessageInfo
        
        if not self.spectator_connections:
            return
        
        rule_prefix = self._get_rule_prefix()
        
        # 构建完整的游戏记录数据
        full_game_record_data = {
            'game_record': game_record,
            'gamestate_id': self.gamestate.gamestate_id,
            'room_id': self.gamestate.room_id,
            'room_type': getattr(self.gamestate, 'room_type', 'guobiao')
        }
        
        # 向所有观战玩家发送完整游戏记录
        for user_id, connection in list(self.spectator_connections.items()):
            try:
                # 将完整记录序列化为 JSON 字符串，通过 message_info 传递
                record_json = json.dumps(full_game_record_data, ensure_ascii=False)
                response = Response(
                    type=f"spectator/{rule_prefix}/full_game_record",
                    success=True,
                    message="完整游戏记录",
                    message_info=MessageInfo(
                        title="完整游戏记录",
                        content=record_json
                    )
                )
                
                await connection.websocket.send_json(response.dict(exclude_none=True))
                logger.info(f"已向观战玩家 {user_id} 发送完整游戏记录")
            except Exception as e:
                logger.error(f"向观战玩家 {user_id} 发送完整游戏记录失败: {e}")
                # 发送失败时移除观战玩家
                await self.remove_spectator(user_id)
    
    async def cleanup(self):
        """清理所有观战资源"""
        # 取消所有观战发送任务
        for user_id, task in list(self.spectator_send_tasks.items()):
            if not task.done():
                task.cancel()
                try:
                    await task
                except asyncio.CancelledError:
                    pass
        self.spectator_send_tasks.clear()
        self.spectator_connections.clear()
        logger.info(f"已清理观战管理器，room_id: {self.gamestate.room_id}")

