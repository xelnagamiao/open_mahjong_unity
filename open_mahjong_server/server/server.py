import debugpy
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
from .config import host, user, password, database, port

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
            if response.success and response.user_id:
                game_server.store_player_session(Connect_id, response.user_id, message["username"])
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
            return Response(
                type="login",
                success=True,
                message="登录成功",
                user_id=user_id,
                username=username,
                userkey=user_key
            )
        else:
            return Response(
                type="login",
                success=False,
                message="密码错误",
                username=username
            )
    else:
        # 用户不存在，创建新用户
        user_id = db_manager.create_user(username, password)
        if user_id:
            # 生成用户秘钥
            user_key = await chat_server.hash_username(username)
            print(f" 生成用户秘钥{user_key} ")
            return Response(
                type="login",
                success=True,
                message="注册并登录成功",
                user_id=user_id,
                username=username,
                userkey=user_key
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