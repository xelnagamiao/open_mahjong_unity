import debugpy
from fastapi import FastAPI, WebSocket
from typing import Dict, Optional, List
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

# 创建数据库实例
db_manager = DatabaseManager(
    host='localhost',
    user='postgres',
    password='qwe123',
    database='open_mahjong',
    port=5432
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
    def __init__(self, websocket: WebSocket, player_id: str):
        self.websocket = websocket
        self.player_id = player_id
        self.username = None
        self.current_room_id = None

# 主服务器
class GameServer:
    def __init__(self):
        # 玩家连接集合保存每个websocket连接的信息
        self.players: Dict[str, PlayerConnection] = {}
        # 用户名到玩家连接的映射
        self.username_to_connection: Dict[str, PlayerConnection] = {}
        # 房间管理器
        self.room_manager = RoomManager(self)
        # 房间id到游戏状态的映射
        self.room_id_to_ChineseGameState: Dict[str, ChineseGameState] = {}
        # 全局计算服务
        self.calculation_service = GameCalculationService()

    # 玩家连接：使用websocket为key 存储[sebsocket,uuid] : PlayerConnection[1,1,0,0]
    async def connect(self, websocket: WebSocket, player_id: str):
        await websocket.accept() # 接受玩家连接
        self.players[player_id] = PlayerConnection(websocket, player_id) # 存储玩家连接
        print(f"玩家 {player_id} 已连接")

    # 玩家断开连接：
    def disconnect(self, player_id: str):
        if player_id in self.players:
            player = self.players[player_id]
            # 删除用户名到玩家连接的映射
            if player.username:
                self.username_to_connection.pop(player.username, None)
            # 帮助玩家自动离开房间
            if player.current_room_id:
                asyncio.create_task(self.room_manager.leave_room(player_id, player.current_room_id))
            # 删除玩家连接
            del self.players[player_id]
            print(f"玩家 {player_id} 已断开连接")

    # 玩家登录：存储 [用户名,用户名到玩家连接的映射] : PlayerConnection[0,0,1,1]
    def store_player_session(self, player_id: str, username: str):
        if player_id in self.players:
            player = self.players[player_id]
            player.username = username
            self.username_to_connection[username] = player
            print(f"已存储玩家 {username} 的会话数据")

    # 创建国标房间
    async def create_GB_room(self, player_id: str, room_name: str, gameround: int, password: str, roundTimerValue: int, stepTimerValue: int, tips: bool) -> Response:
        return await self.room_manager.create_GB_room(player_id, room_name, gameround, password, roundTimerValue, stepTimerValue, tips)

    # 获取房间列表
    def get_room_list(self) -> Response:
        return self.room_manager.get_room_list()

    # 加入房间
    async def join_room(self, player_id: str, room_id: str, password: str):
        response = await self.room_manager.join_room(player_id, room_id, password)
        await self.players[player_id].websocket.send_json(response.dict(exclude_none=True))

    # 离开房间
    async def leave_room(self, player_id: str, room_id: str):
        response = await self.room_manager.leave_room(player_id, room_id)
        await self.players[player_id].websocket.send_json(response.dict(exclude_none=True))

    # 开始游戏
    async def start_game(self, player_id: str, room_id: str):
        # 检查房间是否存在
        if room_id not in self.room_manager.rooms:
            return Response(type="error_message", success=False, message="房间不存在")
            
        room_data = self.room_manager.rooms[room_id]
        
        # 检查是否是房主
        player = self.players[player_id]
        if player.username != room_data["player_list"][0]:
            return Response(type="error_message", success=False, message="只有房主能开始游戏")
            
        # 检查人数是否满足
        if len(room_data["player_list"]) != 4:
            return Response(type="error_message", success=False, message="人数不足")
            
        # 创建游戏任务
        if room_data["room_type"] == "guobiao":
            self.room_id_to_ChineseGameState[room_id] = ChineseGameState(self, room_data, self.calculation_service)
            asyncio.create_task(self.room_id_to_ChineseGameState[room_id].game_loop_chinese())

game_server = GameServer()

@app.websocket("/game/{player_id}")
async def message_input(websocket: WebSocket, player_id: str):
    print(f"收到新的连接请求: {player_id}")
    await game_server.connect(websocket, player_id)
    print(f"连接建立成功: {player_id}")

    while True:
        message = await websocket.receive_json()
        print(f"收到消息: {message}")

        if message["type"] == "login":
            print(f"登录请求 - 用户名: {message['username']}, 密码: {message['password']}")
            response = await player_login(message["username"], message["password"])
            if response.success:
                game_server.store_player_session(player_id, message["username"])
            await websocket.send_json(response.dict(exclude_none=True))

        elif message["type"] == "create_GB_room":
            print(f"创建房间请求 - 用户名: {player_id}")
            response = await game_server.create_GB_room(
                player_id,
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
            await game_server.join_room(player_id, message["room_id"], message["password"])

        elif message["type"] == "leave_room":
            await game_server.leave_room(player_id, message["room_id"])

        elif message["type"] == "start_game":
            await game_server.start_game(player_id, message["room_id"])

        elif message["type"] == "CutTiles":
            room_id = message["room_id"]
            chinese_game_state = game_server.room_id_to_ChineseGameState[room_id]
            await chinese_game_state.get_action(player_id, "cut", message["cutClass"], message["TileId"], target_tile=message.get("targetTile", 0))

        elif message["type"] == "send_action":
            room_id = message["room_id"]
            chinese_game_state = game_server.room_id_to_ChineseGameState[room_id]
            await chinese_game_state.get_action(player_id, message["action"],cutClass=None,TileId=None,target_tile=message["targetTile"])

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
    player = db_manager.get_user_by_username(username)
    
    if player:
        # 用户存在，验证密码（传入已查询的用户数据，避免重复查询）
        if db_manager.verify_password(username, password, user_data=player):
            # 生成用户秘钥
            user_key = await chat_server.hash_username(username)
            print(f" 生成用户秘钥{user_key} ")
            return Response(
                type="login",
                success=True,
                message="登录成功",
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
        if db_manager.create_user(username, password):
            # 生成用户秘钥
            user_key = await chat_server.hash_username(username)
            print(f" 生成用户秘钥{user_key} ")
            return Response(
                type="login",
                success=True,
                message="注册并登录成功",
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