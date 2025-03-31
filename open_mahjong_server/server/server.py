import debugpy
from fastapi import FastAPI, WebSocket
from typing import Dict, Optional, List
from pydantic import BaseModel
import json
import mysql.connector
from mysql.connector import Error
import asyncio  # 添加这个导入
from response import *
from ChineseGameState import ChineseGameState,ChinesePlayer
from room import Room

# 0.0 原神启动
app = FastAPI()
# 0.1 连接数据库
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


""" 
0.[设置配置]
1.message_input 用于接收请求方法
2.GameServer 用于处理数据和储存数据
3.Response 返回数据的格式
4.通过message_input头,返回Response的各类函数
5.[启动配置] 
"""

# 1.message_input 接受请求方法 用户对服务器的请求发送至/game/路由下，同时建立对此路由的连接
@app.websocket("/game/{player_id}")
async def message_input(websocket: WebSocket, player_id: str):
    print(f"收到新的连接请求: {player_id}")
    await game_server.connect(websocket, player_id)
    print(f"连接建立成功: {player_id}")
    #try:
    while True:
    # 1.2 通过while循环接收消息，获得消息以后，根据消息类型进行处理
        # 解包数据
        message = await websocket.receive_json()
        print(f"收到消息: {message}")
        # 1.2.1 type == login 登录 执行
        if message["type"] == "login":
            # 向player_login方法传参username,password返回loginresponse
            response = await player_login(
                message["username"],
                message["password"]
            )
            print(f"发送响应: {response.dict()}")
            # reponse 接收loginresponse,如果属性success == True 则将playid添加入gameserver
            if response.success:
                game_server.store_player_session(player_id,message["username"])
            # 不管结果正确与否 使用send_json 将loginresponse.dict()发送
            await websocket.send_json(response.dict(exclude_none=True))
        # 1.2.2 type == create_room 建立房间
        elif message["type"] == "create_room":
            # 调用game_server.create_room方法以后发送返回的response
            response = await game_server.create_room(player_id,message["roomname"],message["gametime"],message["password"],message["cuttime"])
            await websocket.send_json(response.dict(exclude_none=True))
        # 1.2.3 type == get_room_list 获取房间列表
        elif message["type"] == "get_room_list":
            # 调用game_server.get_room_list方法以后发送返回的response
            response = game_server.get_room_list()
            await websocket.send_json(response.dict(exclude_none=True))
        # 1.2.4 type == join_room 加入房间
        elif message["type"] == "join_room":
            await game_server.join_room(player_id,message["room_id"],message["password"])
        # 1.2.5 type == leave_room 离开房间
        elif message["type"] == "leave_room":
            await game_server.leave_room(player_id,message["room_id"])
        # 1.2.6 type == kick_player 踢出玩家
        #elif message["type"] == "kick_player":
        #    await game_server.kick_player(player_id,message["room_id"],message["player_id"])
        # 1.2.7 type == start_game 开始游戏
        elif message["type"] == "start_game":
            await game_server.start_game(player_id,message["room_id"])
        elif message["type"] == "CutTiles":
            room_id = message["room_id"]
            chinese_game_state = game_server.room_id_to_ChineseGameState[room_id]
            await chinese_game_state.cut_tiles(player_id, message["cutClass"], message["TileId"])
        elif message["type"] == "send_action":
            room_id = message["room_id"]
            chinese_game_state = game_server.room_id_to_ChineseGameState[room_id]
            await chinese_game_state.get_action(player_id, message["action"])
        
    # 1.3 接受信息头类型错误打印错误值
    #except Exception as e:
    #    print(f"错误: {e}")
    # 1.4 连接断开后调用game_server.disconnect
    #finally:
    #    game_server.disconnect(player_id)

class PlayerConnection: # 玩家类
    def __init__(self, websocket: WebSocket, player_id: str):
        self.websocket = websocket      # WebSocket 连接
        self.player_id = player_id      # 玩家ID 机器id
        self.username = None            # 玩家用户名
        self.current_room_id = None     # 当前所在房间ID


class GameServer: # 游戏服务器类
    # 2.1 init方法创建了两个字典用于保存玩家、房间的数据和websocket连接
    def __init__(self):
        # players字典是玩家id到玩家连接的映射,
        self.players: Dict[str, PlayerConnection] = {}  # player_id -> PlayerConnection
        # 建立集合rooms[room_id,room_data]
        self.rooms: Dict[str, Room] = {}                # room_id -> Room
        # 建立用户名到连接的映射
        self.username_to_connection: Dict[str, PlayerConnection] = {}  # username -> PlayerConnection
        
        self.room_id_to_ChineseGameState: Dict[str, ChineseGameState] = {}  # room_id -> ChineseGameState
    # 2.2 connect方法用于将玩家添加入websocket池
    async def connect(self, websocket: WebSocket, player_id: str):
        await websocket.accept()
        self.players[player_id] = PlayerConnection(websocket, player_id)
        print(f"玩家 {player_id} 已连接")
    # 2.3 disconnect方法用于将玩家移除websocket池
    def disconnect(self, player_id: str):
        if player_id in self.players:
            player = self.players[player_id]
            if player.username:
                # 从用户名映射中移除
                self.username_to_connection.pop(player.username, None)
            if player.current_room_id:
                # 如果玩家在房间中，处理离开房间逻辑
                room_id = player.current_room_id
                if room_id in self.rooms:
                    self.rooms[room_id].player_list.remove(player.username)
            del self.players[player_id]
            print(f"玩家 {player_id} 已断开连接")
    # 2.4 store_player_session方法用于将玩家数据加入player_sessions[player_id,player_data]
    def store_player_session(self, player_id: str, username: str):
        if player_id in self.players:
            player = self.players[player_id] # 获取玩家数据
            player.username = username # 将玩家数据中的username赋值为username
            self.username_to_connection[username] = player # 将username赋值为player
            print(f"已存储玩家 {username} 的会话数据")
    # 2.5 create_room 方法用于给玩家创建房间 添加入GameServer.rooms [room_id,room_data]
    async def create_room(self, player_id: str, room_name: str, game_time: int, password: str, cuttime: int) -> Response:
        # 1. 检查玩家是否在 players 中
        if player_id not in self.players:
            return Response(
                type="error_message",
                success=False,
                message="请先登录"
            )
        # 2. 获取房主的用户名
        player = self.players[player_id]
        host_name = player.username
        
        # 3. 生成房间ID
        for i in range(1, 9999):
            if str(i) not in self.rooms:
                room_id = str(i)
                break

        # 4. 创建并存储房间
        self.rooms[room_id] = Room(
            room_id=room_id,
            room_name=room_name,
            game_time=game_time,
            player_list=[host_name],
            password=password,
            game_server=self,
            cuttime=cuttime,
            host_name=host_name
        )
        
        # 5. 广播房间信息给房主
        await self.rooms[room_id].broadcast_to_players()
        
        # 6. 返回创建房间的响应
        return Response(
            type="create_room",
            success=True,
            message="房间创建成功"
        )
    # 2.6 get_room_list用于获取房间列表
    def get_room_list(self) -> Response:
        # 将房间列表转换为RoomData格式
        room_list = []
        for room_id, room in self.rooms.items():
            room_list.append(RoomListData(
                room_id=room.room_id,
                host_name=room.host_name,
                room_name=room.room_name,
                game_time=room.game_time,
                player_list=room.player_list,
                player_count=room.player_count,
                cuttime=room.cuttime,
                has_password=bool(room.password)
            ))
        # 返回包含RoomData的Response
        return Response(
            type="get_room_list",
            success=True,
            message="获取房间列表成功",
            room_list=room_list
        )
    # 2.7 join_room 方法用于加入房间
    async def join_room(self, player_id: str, room_id: str, password: str):
        # 1. 检查玩家是否在 players 中
        if player_id not in self.players:
            return Response(type="error_message", success=False, message="请先登录")
            
        # 2. 检查房间是否存在
        if room_id not in self.rooms:
            return Response(type="error_message", success=False, message="房间不存在")
            
        # 3. 检查房间是否满员
        if self.rooms[room_id].player_count >= 4:
            return Response(type="error_message", success=False, message="房间已满")
            
        # 4. 检查密码是否正确
        if self.rooms[room_id].password and self.rooms[room_id].password != password:
            return Response(type="error_message", success=False, message="密码错误")
            
        # 5. 更新玩家和房间信息
        player = self.players[player_id]
        player.current_room_id = room_id
        self.rooms[room_id].player_list.append(player.username)
        self.rooms[room_id].player_count += 1
        
        # 6. 广播更新
        await self.rooms[room_id].broadcast_to_players()
    # 2.8 leave_room 方法用于离开房间
    async def leave_room(self, player_id: str, room_id: str):
        # 检查房间是否存在
        if room_id not in self.rooms:
            return Response(type="error_message", success=False, message="房间不存在")
            
        player = self.players[player_id]
        if player.username not in self.rooms[room_id].player_list:
            return Response(type="error_message", success=False, message="玩家不在房间中")
            
        # 更新玩家和房间信息
        player.current_room_id = None
        self.rooms[room_id].player_list.remove(player.username)
        self.rooms[room_id].player_count -= 1
        
        # 如果房间空了就删除
        if self.rooms[room_id].player_count == 0: # 删除后等于0
            del self.rooms[room_id]
            return Response(type="leave_room", success=True, message="房间已解散")
        else:
            await self.rooms[room_id].broadcast_to_players()
            return Response(type="leave_room", success=True, message="离开房间成功")
    # 2.9 start_game 方法用于开始游戏
    async def start_game(self, player_id: str, room_id: str):
        # 1. 检查房间是否存在
        if room_id not in self.rooms:
            return Response(type="error_message", success=False, message="房间不存在")
            
        room = self.rooms[room_id]
        
        # 2. 检查是否是房主
        player = self.players[player_id]
        if player.username != room.host_name:
            return Response(type="error_message", success=False, message="只有房主能开始游戏")
            
        # 3. 检查人数是否满足
        if room.player_count != 4:
            return Response(type="error_message", success=False, message="人数不足")
            
        # 4. 创建游戏任务
        self.room_id_to_ChineseGameState[room_id] = ChineseGameState(room_id, room)
        asyncio.create_task(self.room_id_to_ChineseGameState[room_id].game_loop_chinese())

game_server = GameServer() # 启动

# 4.0 发送登录方法 传参username,password 返回值Response
async def player_login(username: str, password: str) -> Response:
    try:
        # 3.1.1 建立和数据库的连接
        conn = mysql.connector.connect(**DB_CONFIG)
        # 3.1.2 用于创建游标对象，并设置查询结果以字典形式返回
        cursor = conn.cursor(dictionary=True)
        # 3.1.3 查询对应username的记录
        cursor.execute(
            "SELECT * FROM open_mahjong WHERE username = %s",
            (username,)
        )
        # 3.1.4 使用player接受查询到的第一条记录
        player = cursor.fetchone()
        # 3.1.5 判断是否登录成功
        if player:
            if player['password'] == password:  # 实际应使用密码哈希
                return LoginResponse(
                    type="login",
                    success=True,
                    username=username,  # 直接使用传入的 username
                    message="登录成功"
                )
            else:
                return LoginResponse(
                    type="login",
                    success=False,
                    username=username,  # 直接使用传入的 username
                    message="密码错误"
                )
        else:
            cursor.execute(
                "INSERT INTO open_mahjong (username, password) VALUES (%s, %s)",
                (username, password)
            )
            conn.commit()
            new_id = cursor.lastrowid
            return LoginResponse(
                type="login",
                success=True,
                username=username,  # 直接使用传入的 username
                message="注册并登录成功"
            )
        # 3.1.7 关闭数据库连接
    finally:
        if conn.is_connected():
            cursor.close()
            conn.close()

# 5.主程序运行
if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "server:app",
        host="localhost",
        port=8081,
        reload=True
    ) 