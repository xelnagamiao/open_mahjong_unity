# 游戏状态管理器
import logging
import asyncio
import uuid
from typing import Dict, Any, Optional
from .game_guobiao.GuobiaoGameState import GuobiaoGameState
from ..response import Response, MessageInfo
from .game_mmcr.QingqueGameState import QingqueGameState
logger = logging.getLogger(__name__)

class GameStateManager:
    """管理所有游戏状态实例"""
    
    def __init__(self, game_server):
        """
        初始化游戏状态管理器
        
        Args:
            game_server: 游戏服务器实例
        """
        self.game_server = game_server
        # 管理不同房间id到已启动的游戏服务的映射（仅用于开始游戏时检查，之后不再使用）
        self.room_id_to_GuobiaoGameState: Dict[str, GuobiaoGameState] = {}
        self.room_id_to_QingqueGameState: Dict[str, QingqueGameState] = {}
        # gamestate_id 到游戏状态的映射（主要管理方式）
        self.gamestate_id_to_game_state: Dict[str, Any] = {}
        # 用户ID到游戏状态的映射（用于快速查找玩家所在的活跃游戏）
        self.user_id_to_game_state: Dict[int, Any] = {}
    
    async def start_game(self, Connect_id: str, room_id: str) -> Optional[Response]:
        """
        开始游戏
        
        Args:
            Connect_id: 连接ID
            room_id: 房间ID
            
        Returns:
            Response对象，如果成功返回None
        """
        # 检查房间是否存在
        if room_id not in self.game_server.room_manager.rooms:
            return Response(type="error_message", success=False, message="房间不存在")
            
        room_data = self.game_server.room_manager.rooms[room_id]
        
        # 检查是否是房主
        player = self.game_server.players[Connect_id]
        if player.user_id != room_data["player_list"][0]:
            return Response(type="error_message", success=False, message="只有房主能开始游戏")
            
        # 检查人数是否满足
        if len(room_data["player_list"]) != 4:
            return Response(type="error_message", success=False, message="人数不足")
        
        # 检查游戏是否已经在运行
        if room_data.get("is_game_running", False):
            return Response(type="error_message", success=False, message="游戏已在进行中")
            
        # 设置游戏运行状态
        room_data["is_game_running"] = True
        
        # 创建游戏任务
        if room_data["room_type"] == "guobiao":
            try:
                # 生成 gamestate_id
                gamestate_id = str(uuid.uuid4())
                
                game_state = GuobiaoGameState(
                    self.game_server, 
                    room_data, 
                    self.game_server.calculation_service,
                    self.game_server.db_manager,
                    gamestate_id
                )
                self.room_id_to_GuobiaoGameState[room_id] = game_state
                self.gamestate_id_to_game_state[gamestate_id] = game_state
                
                # 建立用户ID到游戏状态的映射
                for player_id in room_data["player_list"]:
                    self.user_id_to_game_state[player_id] = game_state
                
                # 创建并保存游戏循环任务引用（使用带顶层异常捕获的包装方法）
                game_state.game_task = asyncio.create_task(game_state.run_game_loop())
                logger.info(f"房间 {room_id} 的游戏已启动，gamestate_id: {gamestate_id}")
            except Exception as e:
                logger.error(f"创建游戏任务时发生异常，room_id: {room_id}, 错误: {e}", exc_info=True)
                # 重置游戏运行状态
                room_data["is_game_running"] = False
                # 清理已创建的游戏状态（如果已创建）
                if room_id in self.room_id_to_GuobiaoGameState:
                    game_state = self.room_id_to_GuobiaoGameState[room_id]
                    # 同时从 gamestate_id 映射中移除
                    if hasattr(game_state, 'gamestate_id') and game_state.gamestate_id in self.gamestate_id_to_game_state:
                        del self.gamestate_id_to_game_state[game_state.gamestate_id]
                    del self.room_id_to_GuobiaoGameState[room_id]
                return Response(type="error_message", success=False, message=f"启动游戏失败: {str(e)}")
        elif room_data["room_type"] == "qingque":
            try:
                # 生成 gamestate_id
                gamestate_id = str(uuid.uuid4())
                
                game_state = QingqueGameState(
                    self.game_server, 
                    room_data, 
                    self.game_server.calculation_service,
                    self.game_server.db_manager,
                    gamestate_id
                )
                self.room_id_to_QingqueGameState[room_id] = game_state
                self.gamestate_id_to_game_state[gamestate_id] = game_state
                
                # 建立用户ID到游戏状态的映射
                for player_id in room_data["player_list"]:
                    self.user_id_to_game_state[player_id] = game_state
                
                # 创建并保存游戏循环任务引用（使用带顶层异常捕获的包装方法）
                game_state.game_task = asyncio.create_task(game_state.run_game_loop())
                logger.info(f"房间 {room_id} 的游戏已启动，gamestate_id: {gamestate_id}")
            except Exception as e:
                logger.error(f"创建游戏任务时发生异常，room_id: {room_id}, 错误: {e}", exc_info=True)
                # 重置游戏运行状态
                room_data["is_game_running"] = False
                # 清理已创建的游戏状态（如果已创建）
                if room_id in self.room_id_to_QingqueGameState:
                    game_state = self.room_id_to_QingqueGameState[room_id]
                    # 同时从 gamestate_id 映射中移除
                    if hasattr(game_state, 'gamestate_id') and game_state.gamestate_id in self.gamestate_id_to_game_state:
                        del self.gamestate_id_to_game_state[game_state.gamestate_id]
                    del self.room_id_to_QingqueGameState[room_id]
                return Response(type="error_message", success=False, message=f"启动游戏失败: {str(e)}")
        else:
            return Response(type="error_message", success=False, message="房间类型不支持")
        return None
    
    async def check_player_reconnect(self, Connect_id: str, user_id: int):
        """
        检查玩家是否需要重连并发送提示
        
        Args:
            Connect_id: 连接ID
            user_id: 用户ID
        """
        if user_id in self.user_id_to_game_state:
            game_state = self.user_id_to_game_state[user_id]
            
            reconnect_message = Response(
                type="message",
                success=True,
                message="reconnect_ask",
                message_info=MessageInfo(
                    title="对局重连",
                    content=f"检测到您有一场正在进行的游戏，是否返回游戏？"
                )
            )
            
            # 在响应中添加额外字段方便客户端处理逻辑
            response_dict = reconnect_message.dict(exclude_none=True)
            
            if Connect_id in self.game_server.players:
                await self.game_server.players[Connect_id].websocket.send_json(response_dict)
                
                room_id = game_state.room_id
                logger.info(f"已向玩家 {user_id} 发送重连请求，房间 ID: {room_id}")
    
    async def player_disconnect(self, user_id: int):
        """
        处理玩家断开连接
        
        Args:
            user_id: 用户ID
        """
        if user_id in self.user_id_to_game_state:
            game_state = self.user_id_to_game_state[user_id]
            await game_state.player_disconnect(user_id)
    
    async def player_reconnect(self, user_id: int):
        """
        处理玩家重连
        
        Args:
            user_id: 用户ID
        """
        if user_id in self.user_id_to_game_state:
            game_state = self.user_id_to_game_state[user_id]
            await game_state.player_reconnect(user_id)
    
    def remove_player_from_game_state(self, user_id: int):
        """
        从游戏状态映射中移除玩家
        
        Args:
            user_id: 用户ID
        """
        if user_id in self.user_id_to_game_state:
            del self.user_id_to_game_state[user_id]
            logger.info(f"已从游戏状态映射中移除玩家 {user_id}")
    
    def get_game_state_by_room_id(self, room_id: str) -> Optional[Any]:
        """
        根据房间ID获取游戏状态（仅用于开始游戏时检查，之后不再使用）
        
        Args:
            room_id: 房间ID
            
        Returns:
            游戏状态对象，如果不存在返回None
        """
        if room_id in self.room_id_to_GuobiaoGameState:
            return self.room_id_to_GuobiaoGameState.get(room_id)
        elif room_id in self.room_id_to_QingqueGameState:
            return self.room_id_to_QingqueGameState.get(room_id)
        return None
    
    def get_game_state_by_gamestate_id(self, gamestate_id: str) -> Optional[Any]:
        """
        根据 gamestate_id 获取游戏状态
        
        Args:
            gamestate_id: 游戏状态ID
            
        Returns:
            游戏状态对象，如果不存在返回None
        """
        return self.gamestate_id_to_game_state.get(gamestate_id)
    
    def get_game_state_by_user_id(self, user_id: int) -> Optional[Any]:
        """
        根据用户ID获取游戏状态
        
        Args:
            user_id: 用户ID
            
        Returns:
            游戏状态对象，如果不存在返回None
        """
        return self.user_id_to_game_state.get(user_id)
    
    def get_playing_rooms_count(self) -> int:
        """
        获取正在进行的游戏房间数量
        
        Returns:
            正在进行的游戏房间数量
        """
        return len(self.gamestate_id_to_game_state)
    
    async def cleanup_game_state_complete(self, gamestate_id: str = None, room_id: str = None):
        """
        统一清理游戏状态：先清理所有映射关系，再调用游戏状态的清理方法取消协程
        
        Args:
            gamestate_id: 游戏状态ID（优先使用）
            room_id: 房间ID（如果未提供 gamestate_id）
        """
        # 获取游戏状态
        game_state = None
        if gamestate_id:
            game_state = self.get_game_state_by_gamestate_id(gamestate_id)
        elif room_id:
            game_state = self.get_game_state_by_room_id(room_id)
            # 如果通过 room_id 找到，获取其 gamestate_id
            if game_state:
                gamestate_id = game_state.gamestate_id
        
        if not game_state:
            logger.warning(f"未找到游戏状态，gamestate_id: {gamestate_id}, room_id: {room_id}")
            return
        
        # 先清理所有映射关系
        # 1. 清理玩家 ID 到游戏状态的映射
        for player in game_state.player_list:
            if player.user_id in self.user_id_to_game_state:
                del self.user_id_to_game_state[player.user_id]
                logger.debug(f"已从游戏状态映射中移除玩家 {player.user_id}")
        
        # 2. 清理房间到游戏状态的映射
        if game_state.room_id in self.room_id_to_GuobiaoGameState:
            del self.room_id_to_GuobiaoGameState[game_state.room_id]
        elif game_state.room_id in self.room_id_to_QingqueGameState:
            del self.room_id_to_QingqueGameState[game_state.room_id]
        
        # 3. 清理 gamestate_id 到游戏状态的映射
        if gamestate_id and gamestate_id in self.gamestate_id_to_game_state:
            del self.gamestate_id_to_game_state[gamestate_id]
        
        logger.info(f"已清理所有映射关系，gamestate_id: {gamestate_id}, room_id: {game_state.room_id}")
        
        # 然后调用游戏状态的清理方法（取消协程）
        await game_state.cleanup_game_state()
        logger.info(f"已清理游戏状态协程，gamestate_id: {gamestate_id}, room_id: {game_state.room_id}")

