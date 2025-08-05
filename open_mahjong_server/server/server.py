import debugpy
from fastapi import FastAPI, WebSocket
from typing import Dict, Optional, List
from pydantic import BaseModel
import json
import mysql.connector
from mysql.connector import Error
import asyncio
from response import Response
from GB_Game.ChineseGameState import ChineseGameState
from room_manager import RoomManager

import secrets,hashlib
import subprocess,os,signal,sys

# 0.0 原神启动
app = FastAPI()

# 0.1 连接数据库配置
DB_CONFIG = {
    'host': 'localhost',
    'user': 'root',
    'password': 'qwe123',
    'database': 'open_mahjong',
    'port': 3306
}

# 0.2 配置数据库
def init_database():
    try:
        conn = mysql.connector.connect(**DB_CONFIG)
        cursor = conn.cursor()
        create_table_sql = """
        CREATE TABLE IF NOT EXISTS open_mahjong (
            id INT AUTO_INCREMENT PRIMARY KEY,
            username VARCHAR(255) UNIQUE NOT NULL,
            password VARCHAR(255) NOT NULL,
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        )
        """
        cursor.execute(create_table_sql)
        conn.commit()
        print('数据表创建成功')
    except Error as e:
        print(f'数据表创建失败: {e}')
    finally:
        if conn.is_connected():
            cursor.close()
            conn.close()

# 0.3 初始化数据库
@app.on_event("startup")
async def startup_event():
    init_database()

# 0.4 启动聊天服务器
@app.on_event("startup")
async def start_chat_server():
    global HASH_SALT
    # 生成哈希用户名密钥,用于从用户名生成秘钥,用此秘钥可以在聊天服务器中注册
    HASH_SALT = secrets.token_urlsafe(16)
    
    # 保存秘钥到文件，供聊天服务器使用
    await save_secret_key_to_file(HASH_SALT)
    
    # 获取当前脚本所在目录
    script_dir = os.path.dirname(os.path.abspath(__file__))
    executable_path = os.path.join(script_dir, 'chatserver', 'open_mahjong_chatServer.exe')
    
    try:
        # 检查可执行文件是否存在
        if not os.path.exists(executable_path):
            print(f"聊天服务器可执行文件不存在: {executable_path}")
            return
            
        # 调试环境使用命令行窗口显示日志信息
        process = subprocess.Popen(
        ['cmd.exe', '/k', 'cd /d', script_dir, '&&', executable_path],
        creationflags=subprocess.CREATE_NEW_CONSOLE,
        )
        """
        部署环境使用stdout/stderr捕获，避免日志输出到控制台
        process = subprocess.Popen(
            [executable_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )
        """
        print(f"聊天服务器进程已启动，PID: {process.pid}")
        
    except Exception as e:
        print(f"启动聊天服务器失败: {e}")
        print(f"错误详情: {str(e)}")

async def save_secret_key_to_file(secret_key: str):
    """将秘钥保存到文件，供聊天服务器读取"""
    try:
        # 获取当前脚本所在目录
        self_dir = os.path.dirname(os.path.abspath(__file__))
        # 创建chatserver文件夹
        chatserver_dir = os.path.join(self_dir, 'chatserver')
        # 确保chatserver文件夹存在
        os.makedirs(chatserver_dir, exist_ok=True)
        # 如果chatserver文件夹存在secret_key.txt文件，则删除
        secret_file_path = os.path.join(chatserver_dir, 'secret_key.txt')
        if os.path.exists(secret_file_path):
            os.remove(secret_file_path)
        # 保存新秘钥到新的secret_key.txt文件中
        with open(secret_file_path, 'w') as f:
            f.write(secret_key)
        print(f"新秘钥已保存到 {secret_file_path}")
        print(f"秘钥: {secret_key[:20]}...")

    except Exception as e:
        print(f"保存秘钥失败: {e}")
        
# 哈希用户名
async def hash_username(username: str) -> str:
    return hashlib.sha256((username + HASH_SALT).encode()).hexdigest()



class PlayerConnection:
    def __init__(self, websocket: WebSocket, player_id: str):
        self.websocket = websocket
        self.player_id = player_id
        self.username = None
        self.current_room_id = None

class GameServer:
    def __init__(self):
        # 玩家连接集合保存websocket连接 uuid 用户名 当前房间id
        self.players: Dict[str, PlayerConnection] = {}
        # 用户名到玩家连接的映射
        self.username_to_connection: Dict[str, PlayerConnection] = {}
        # 房间管理器
        self.room_manager = RoomManager(self)
        # 房间id到游戏状态的映射
        self.room_id_to_ChineseGameState: Dict[str, ChineseGameState] = {}

    # 玩家连接：使用websocket为key 存储[sebsocket,uuid] : PlayerConnection[1,1,0,0]
    async def connect(self, websocket: WebSocket, player_id: str):
        await websocket.accept()
        self.players[player_id] = PlayerConnection(websocket, player_id)
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

    async def create_GB_room(self, player_id: str, room_name: str, gameround: int, password: str, roundTimerValue: int, stepTimerValue: int, tips: bool) -> Response:
        return await self.room_manager.create_GB_room(player_id, room_name, gameround, password, roundTimerValue, stepTimerValue, tips)

    def get_room_list(self) -> Response:
        return self.room_manager.get_room_list()

    async def join_room(self, player_id: str, room_id: str, password: str):
        response = await self.room_manager.join_room(player_id, room_id, password)
        await self.players[player_id].websocket.send_json(response.dict(exclude_none=True))

    async def leave_room(self, player_id: str, room_id: str):
        response = await self.room_manager.leave_room(player_id, room_id)
        await self.players[player_id].websocket.send_json(response.dict(exclude_none=True))

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
        if room_data["type"] == "guobiao":
            self.room_id_to_ChineseGameState[room_id] = ChineseGameState(self, room_data)
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
            await chinese_game_state.cut_tiles(player_id, message["cutClass"], message["TileId"])

        elif message["type"] == "send_action":
            room_id = message["room_id"]
            chinese_game_state = game_server.room_id_to_ChineseGameState[room_id]
            await chinese_game_state.get_action(player_id, message["action"])

async def player_login(username: str, password: str) -> Response:
    try:
        conn = mysql.connector.connect(**DB_CONFIG)
        cursor = conn.cursor(dictionary=True)
        cursor.execute(
            "SELECT * FROM open_mahjong WHERE username = %s",
            (username,)
        )
        player = cursor.fetchone()

        if player:
            if player['password'] == password:
                # 生成用户秘钥
                user_key = await hash_username(username)
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
            cursor.execute(
                "INSERT INTO open_mahjong (username, password) VALUES (%s, %s)",
                (username, password)
            )
            conn.commit()
            # 生成用户秘钥
            user_key = await hash_username(username)
            print(f" 生成用户秘钥{user_key} ")
            return Response(
                type="login",
                success=True,
                message="注册并登录成功",
                username=username,
                userkey=user_key
            )
    finally:
        if conn.is_connected():
            cursor.close()
            conn.close()

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "server:app",
        host="localhost",
        port=8081,
        reload=True
    ) 