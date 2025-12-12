from fastapi import FastAPI, WebSocket
from typing import Dict, Optional, List, Any
from pydantic import BaseModel
import json
import asyncio
from contextlib import asynccontextmanager
from .response import Response
from .game_gb.game_state import ChineseGameState
from .room_manager import RoomManager
from .database.db_manager import DatabaseManager
from .chat_server.chat_server import ChatServer
from .game_calculation.game_calculation_service import GameCalculationService
import secrets,hashlib
import subprocess,os,signal,sys
import time
from .config import host, user, password, database, port ,Debug

# 创建数据库实例
db_manager = DatabaseManager(
    host = host,
    user = user,
    password = password,
    database = database,
    port = port
)

# 创建聊天服务器实例
chat_server = ChatServer()

@asynccontextmanager
async def lifespan(app: FastAPI):
    # 启动时执行
    db_manager.init_database()
    # 只生成秘钥文件，不启动聊天服务器
    # 聊天服务器应由 supervisor/systemd 等进程管理工具独立管理
    await chat_server.generate_secret_key()
    # 测试环境下启动聊天服务器
    if Debug:
        await chat_server.start_chat_server()
    yield
    # 关闭时执行（如果需要清理资源，在这里添加）

# 创建游戏服务器实例
app = FastAPI(lifespan=lifespan)

# 玩家连接对象
class PlayerConnection:
    def __init__(self, websocket: WebSocket, Connect_id: str):
        self.websocket = websocket # WebSocket 连接实例
        self.Connect_id = Connect_id # 单次连接UUID
        self.user_id: Optional[int] = None  # 用户UID（数据库主键）
        self.username: Optional[str] = None  # 用户名
        self.current_room_id = None # 所在房间ID

# 主服务器
class GameServer:
    def __init__(self):
        # 玩家连接集合保存每个websocket连接的信息
        self.players: Dict[str, PlayerConnection] = {}
        # 用户ID到玩家连接的映射（用于游戏逻辑）
        self.user_id_to_connection: Dict[int, PlayerConnection] = {}
        # 房间管理器
        self.room_manager = RoomManager(self)
        # 管理不同房间id到已启动的游戏服务的映射
        self.room_id_to_ChineseGameState: Dict[str, ChineseGameState] = {}
        # 全局计算服务
        self.calculation_service = GameCalculationService()
        # 数据库管理器
        self.db_manager = db_manager
        # 聊天服务器
        self.chat_server = chat_server

    # 玩家连接：使用websocket为key 存储[sebsocket,uuid] : PlayerConnection[1,1,0,0]
    async def connect(self, websocket: WebSocket, Connect_id: str):
        await websocket.accept() # 接受玩家连接
        self.players[Connect_id] = PlayerConnection(websocket, Connect_id) # 存储玩家连接
        print(f"玩家 {Connect_id} 已连接")

    # 玩家断开连接：
    def disconnect(self, Connect_id: str):
        if Connect_id in self.players:
            player = self.players[Connect_id]
            # 删除用户ID到玩家连接的映射
            if player.user_id:
                self.user_id_to_connection.pop(player.user_id, None)
            # 帮助玩家自动离开房间
            if player.current_room_id:
                asyncio.create_task(self.room_manager.leave_room(Connect_id, player.current_room_id))
                player.current_room_id = None
            # 删除玩家连接
            del self.players[Connect_id]
            print(f"玩家 {Connect_id} 已断开连接")

    # 玩家登录：存储用户ID和用户名到玩家连接的映射
    def store_player_session(self, Connect_id: str, user_id: int, username: str):
        if Connect_id in self.players:
            player = self.players[Connect_id]
            player.user_id = user_id
            player.username = username
            self.user_id_to_connection[user_id] = player # 存储用户ID到玩家连接的映射
            print(f"已存储玩家 user_id={user_id}, username={username} 的会话数据")

    # 创建国标房间
    async def create_GB_room(self, Connect_id: str, room_name: str, gameround: int, password: str, roundTimerValue: int, stepTimerValue: int, tips: bool) -> Response:
        return await self.room_manager.create_GB_room(Connect_id, room_name, gameround, password, roundTimerValue, stepTimerValue, tips)

    # 获取房间列表
    def get_room_list(self) -> Response:
        return self.room_manager.get_room_list()

    # 加入房间
    async def join_room(self, Connect_id: str, room_id: str, password: str):
        response = await self.room_manager.join_room(Connect_id, room_id, password)
        await self.players[Connect_id].websocket.send_json(response.dict(exclude_none=True))

    # 离开房间
    async def leave_room(self, Connect_id: str, room_id: str):
        response = await self.room_manager.leave_room(Connect_id, room_id)
        await self.players[Connect_id].websocket.send_json(response.dict(exclude_none=True))

    # 开始游戏
    async def start_game(self, Connect_id: str, room_id: str):
        # 检查房间是否存在
        if room_id not in self.room_manager.rooms:
            return Response(type="error_message", success=False, message="房间不存在")
            
        room_data = self.room_manager.rooms[room_id]
        
        # 检查是否是房主
        player = self.players[Connect_id]
        if player.user_id != room_data["player_list"][0]:
            return Response(type="error_message", success=False, message="只有房主能开始游戏")
            
        # 检查人数是否满足
        if len(room_data["player_list"]) != 4:
            return Response(type="error_message", success=False, message="人数不足")
            
        # 创建游戏任务
        if room_data["room_type"] == "guobiao":
            self.room_id_to_ChineseGameState[room_id] = ChineseGameState(self, room_data, self.calculation_service,self.db_manager)
            asyncio.create_task(self.room_id_to_ChineseGameState[room_id].game_loop_chinese())

game_server = GameServer()

@app.websocket("/game/{Connect_id}")
async def message_input(websocket: WebSocket, Connect_id: str):
    print(f"收到新的连接请求: {Connect_id}")
    await game_server.connect(websocket, Connect_id)
    print(f"连接建立成功: {Connect_id}")

    while True:
        message = await websocket.receive_json()
        print(f"收到消息: {message}")

        if message["type"] == "login":
            print(f"登录请求 - 用户名: {message['username']}, 密码: {message['password']}")
            response = await player_login(message["username"], message["password"])
            if response.success and response.login_info:
                game_server.store_player_session(Connect_id, response.login_info.user_id, response.login_info.username)
            await websocket.send_json(response.dict(exclude_none=True))

        elif message["type"] == "create_GB_room":
            print(f"创建房间请求 - 用户名: {Connect_id}")
            response = await game_server.create_GB_room(
                Connect_id,
                message["roomname"],
                message["gameround"],
                message["password"],
                message["roundTimerValue"],
                message["stepTimerValue"],
                message["tips"]
            )
            await websocket.send_json(response.dict(exclude_none=True))

        elif message["type"] == "get_room_list":
            response = game_server.get_room_list()
            await websocket.send_json(response.dict(exclude_none=True))

        elif message["type"] == "join_room":
            await game_server.join_room(Connect_id, message["room_id"], message["password"])

        elif message["type"] == "leave_room":
            await game_server.leave_room(Connect_id, message["room_id"])

        elif message["type"] == "start_game":
            await game_server.start_game(Connect_id, message["room_id"])

        elif message["type"] == "CutTiles":
            room_id = message["room_id"]
            chinese_game_state = game_server.room_id_to_ChineseGameState[room_id]
            await chinese_game_state.get_action(Connect_id, "cut", message["cutClass"], message["TileId"], cutIndex = message["cutIndex"],target_tile=None)

        elif message["type"] == "send_action":
            room_id = message["room_id"]
            chinese_game_state = game_server.room_id_to_ChineseGameState[room_id]
            await chinese_game_state.get_action(Connect_id, message["action"],cutClass=None,TileId=None,cutIndex = None,target_tile=message["targetTile"])

        elif message["type"] == "get_record_list":
            # 获取当前登录用户的游戏记录列表
            player = game_server.players.get(Connect_id)
            if player and player.user_id:
                records = game_server.db_manager.get_record_list(player.user_id, limit=20)
                # 转换为 Record_info 列表
                from .response import Record_info, Player_record_info
                record_list = []
                for game_record in records:
                    # 将玩家信息转换为 Player_record_info 列表
                    players_info = []
                    for player_data in game_record['players']:
                        players_info.append(Player_record_info(
                            user_id=player_data['user_id'],
                            username=player_data['username'],
                            score=player_data['score'],
                            rank=player_data['rank'],
                            title_used=player_data.get('title_used'),
                            character_used=player_data.get('character_used'),
                            profile_used=player_data.get('profile_used'),
                            voice_used=player_data.get('voice_used')
                        ))
                    
                    record_info = Record_info(
                        game_id=game_record['game_id'],
                        rule=game_record['rule'],
                        record=game_record['record'],
                        created_at=game_record['created_at'],
                        players=players_info
                    )
                    record_list.append(record_info)
                
                response = Response(
                    type="get_record_list",
                    success=True,
                    message=f"获取到 {len(record_list)} 局游戏记录",
                    record_list=record_list
                )
            else:
                response = Response(
                    type="get_record_list",
                    success=False,
                    message="用户未登录"
                )
            await websocket.send_json(response.dict(exclude_none=True))

        elif message["type"] == "get_player_info":
            # 获取指定用户的统计信息
            from .response import Player_info_response, Player_stats_info
            try:
                target_user_id = int(message.get("userid"))
            except (ValueError, TypeError):
                response = Response(
                    type="get_player_info",
                    success=False,
                    message="无效的用户ID"
                )
                await websocket.send_json(response.dict(exclude_none=True))
                return
            
            # 获取用户设置信息（包含 username）
            from .response import UserSettings
            user_settings_data = db_manager.get_user_settings(target_user_id)
            
            # 如果用户设置不存在，说明用户不存在
            if not user_settings_data:
                response = Response(
                    type="get_player_info",
                    success=False,
                    message="用户不存在"
                )
                await websocket.send_json(response.dict(exclude_none=True))
                return
            
            # 获取统计数据
            stats_data = game_server.db_manager.get_player_stats(target_user_id)
            
            # 转换 GB 统计数据
            gb_stats_list = []
            for stats_row in stats_data['gb_stats']:
                # 分离基础字段和番种字段
                base_fields = {
                    'rule': stats_row.get('rule'),
                    'mode': stats_row.get('mode'),
                    'total_games': stats_row.get('total_games'),
                    'total_rounds': stats_row.get('total_rounds'),
                    'win_count': stats_row.get('win_count'),
                    'self_draw_count': stats_row.get('self_draw_count'),
                    'deal_in_count': stats_row.get('deal_in_count'),
                    'total_fan_score': stats_row.get('total_fan_score'),
                    'total_win_turn': stats_row.get('total_win_turn'),
                    'total_fangchong_score': stats_row.get('total_fangchong_score'),
                    'first_place_count': stats_row.get('first_place_count'),
                    'second_place_count': stats_row.get('second_place_count'),
                    'third_place_count': stats_row.get('third_place_count'),
                    'fourth_place_count': stats_row.get('fourth_place_count'),
                }
                
                # 提取番种字段（排除基础字段和时间字段）
                excluded_fields = {'user_id', 'rule', 'mode', 'total_games', 'total_rounds', 
                                 'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
                                 'total_win_turn', 'total_fangchong_score', 'first_place_count',
                                 'second_place_count', 'third_place_count', 'fourth_place_count',
                                 'created_at', 'updated_at'}
                fan_stats = {k: v for k, v in stats_row.items() 
                           if k not in excluded_fields and v is not None and v != 0}
                
                gb_stats_list.append(Player_stats_info(
                    **base_fields,
                    fan_stats=fan_stats if fan_stats else None
                ))
            
            # 转换 JP 统计数据
            jp_stats_list = []
            for stats_row in stats_data['jp_stats']:
                base_fields = {
                    'rule': stats_row.get('rule'),
                    'mode': stats_row.get('mode'),
                    'total_games': stats_row.get('total_games'),
                    'total_rounds': stats_row.get('total_rounds'),
                    'win_count': stats_row.get('win_count'),
                    'self_draw_count': stats_row.get('self_draw_count'),
                    'deal_in_count': stats_row.get('deal_in_count'),
                    'total_fan_score': stats_row.get('total_fan_score'),
                    'total_win_turn': stats_row.get('total_win_turn'),
                    'total_fangchong_score': stats_row.get('total_fangchong_score'),
                    'first_place_count': stats_row.get('first_place_count'),
                    'second_place_count': stats_row.get('second_place_count'),
                    'third_place_count': stats_row.get('third_place_count'),
                    'fourth_place_count': stats_row.get('fourth_place_count'),
                }
                
                excluded_fields = {'user_id', 'rule', 'mode', 'total_games', 'total_rounds', 
                                 'win_count', 'self_draw_count', 'deal_in_count', 'total_fan_score',
                                 'total_win_turn', 'total_fangchong_score', 'first_place_count',
                                 'second_place_count', 'third_place_count', 'fourth_place_count',
                                 'created_at', 'updated_at'}
                fan_stats = {k: v for k, v in stats_row.items() 
                           if k not in excluded_fields and v is not None and v != 0}
                
                jp_stats_list.append(Player_stats_info(
                    **base_fields,
                    fan_stats=fan_stats if fan_stats else None
                ))

            user_settings = UserSettings(
                user_id=user_settings_data.get('user_id'),
                username=user_settings_data.get('username'),
                title_id=user_settings_data.get('title_id'),
                profile_image_id=user_settings_data.get('profile_image_id'),
                character_id=user_settings_data.get('character_id'),
                voice_id=user_settings_data.get('voice_id')
            )
            
            player_info_response = Player_info_response(
                user_id=target_user_id,
                user_settings=user_settings,
                gb_stats=gb_stats_list,
                jp_stats=jp_stats_list,
            )
            
            response = Response(
                type="get_player_info",
                success=True,
                message="获取玩家信息成功",
                player_info=player_info_response
            )
            await websocket.send_json(response.dict(exclude_none=True))

async def player_login(username: str, password: str) -> Response:
    """
    玩家登录/注册功能
    如果用户不存在则自动注册，存在则验证密码
    Args:
        username: 用户名
        password: 密码
    Returns:
        Response 对象，包含登录结果和用户信息
    """
    # 检查用户是否存在
    player:Optional[Dict[str, Any]] = db_manager.get_user_by_username(username)
    
    if player is not None:
        # 用户存在，验证密码（直接使用已查询的用户数据）
        stored_password_hash = player.get('password')
        if stored_password_hash and db_manager.verify_password(password, stored_password_hash):
            # 生成用户秘钥
            user_key = await chat_server.hash_username(username)
            user_id = player.get('user_id')
            print(f" 生成用户秘钥{user_key} ")
            
            # 获取用户设置和游戏配置信息
            from .response import LoginInfo, UserSettings, UserConfig
            user_settings_data = db_manager.get_user_settings(user_id)
            user_config_data = db_manager.get_user_config(user_id)
            
            user_settings = None
            if user_settings_data:
                user_settings = UserSettings(
                    user_id=user_settings_data.get('user_id'),
                    username=user_settings_data.get('username'),
                    title_id=user_settings_data.get('title_id'),
                    profile_image_id=user_settings_data.get('profile_image_id'),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                )
            
            user_config = None
            if user_config_data:
                user_config = UserConfig(
                    user_id=user_config_data.get('user_id'),
                    volume=user_config_data.get('volume', 100)
                )
            
            login_info = LoginInfo(
                user_id=user_id,
                username=username,
                userkey=user_key
            )
            
            return Response(
                type="login",
                success=True,
                message="登录成功",
                login_info=login_info,
                user_settings=user_settings,
                user_config=user_config
            )
        else:
            return Response(
                type="login",
                success=False,
                message="密码错误"
            )
    else:
        # 用户不存在，创建新用户
        user_id = db_manager.create_user(username, password)
        if user_id:
            # 生成用户秘钥
            user_key = await chat_server.hash_username(username)
            print(f" 生成用户秘钥{user_key} ")
            
            # 获取用户设置和游戏配置信息（新创建的用户应该有默认配置）
            from .response import LoginInfo, UserSettings, UserConfig
            user_settings_data = db_manager.get_user_settings(user_id)
            user_config_data = db_manager.get_user_config(user_id)
            
            user_settings = None
            if user_settings_data:
                user_settings = UserSettings(
                    user_id=user_settings_data.get('user_id'),
                    title_id=user_settings_data.get('title_id'),
                    username=user_settings_data.get('username'),
                    profile_image_id=user_settings_data.get('profile_image_id'),
                    character_id=user_settings_data.get('character_id'),
                    voice_id=user_settings_data.get('voice_id')
                )
            
            user_config = None
            if user_config_data:
                user_config = UserConfig(
                    user_id=user_config_data.get('user_id'),
                    volume=user_config_data.get('volume', 100)
                )
            
            login_info = LoginInfo(
                user_id=user_id,
                username=username,
                userkey=user_key
            )
            
            return Response(
                type="login",
                success=True,
                message="注册并登录成功",
                login_info=login_info,
                user_settings=user_settings,
                user_config=user_config
            )
        else:
            return Response(
                type="login",
                success=False,
                message="注册失败",
                username=username
            )

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "server:app",
        host="localhost",
        port=8081,
        reload=True
    ) 