import debugpy
from fastapi import FastAPI, WebSocket
from typing import Dict, Optional, List
from pydantic import BaseModel
import json
import mysql.connector
from mysql.connector import Error
import asyncio  # 添加这个导入
import random

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
# 0.4 定义发送数据格式BaseModel系列 能够创建符合json定义的格式
class RoomListData(BaseModel):
    room_id: str
    host_name: str
    room_name: str
    game_time: int
    player_list: list[str]
    player_count: int
    cuttime: int
    has_password: bool

class RoomResponse(BaseModel):
    player_list: list[str]
    player_count: int
    host_name: str
    game_time: int
    room_id: str
    room_name: str
    cuttime: int

class PlayerPosition(BaseModel):
    username: str
    position: int

class GameInfo(BaseModel):
    player_list: list[str]
    player_positions: list[PlayerPosition]  # 改用列表
    current_player_index: int
    tile_count: int
    random_seed: int
    game_status: str
    corrent_round: int
    players_info: list[dict]
    cuttime: int
    game_time: int
    self_hand_tiles: Optional[list[str]] = None

class LoginResponse(BaseModel):
    # 消息头
    type: str
    success: bool
    message: str
    username: str

class Cut_response(BaseModel):
    cut_player_index: int
    cut_class: bool
    cut_tiles: str

class Deal_tile_info(BaseModel):
    remaining_time: int
    deal_player_index: int
    deal_tiles: str

class Response(BaseModel):
    # 消息头
    type: str
    success: bool
    message: str
    # 消息体
    room_list: Optional[list[RoomListData]] = None # 用于执行get_room_list时返回房间列表数据
    room_info: Optional[RoomResponse] = None # 用于在join_room和房间信息更新时广播房间信息
    game_info: Optional[GameInfo] = None # 用于执行game_start_chinese时返回游戏信息
    cut_info: Optional[Cut_response] = None # 用于执行cut_tiles时返回切牌信息
    deal_tile_info: Optional[Deal_tile_info] = None # 用于执行deal_tile时返回发牌信息



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
            await game_server.cut_tiles(player_id, message["room_id"], message["cutClass"], message["TileId"])
        
    # 1.3 接受信息头类型错误打印错误值
    #except Exception as e:
    #    print(f"错误: {e}")
    # 1.4 连接断开后调用game_server.disconnect
    #finally:
    #    game_server.disconnect(player_id)

# 2.gameserver 用于保存服务器的数据
class Room: # 房间类
    # 初始化房间数据
    def __init__(self, room_id: str, host_name: str, room_name: str, game_time: int, player_list: list, password: str, game_server, cuttime: int):
        self.room_id = room_id
        self.room_name = room_name
        self.game_time = game_time
        self.player_list = player_list
        self.player_count = 1
        self.password = password
        self.game_server = game_server
        self.host_name = player_list[0]  # 房主总是第一个玩家
        self.cuttime = cuttime
    # 在房间有变化时广播消息给房间内所有玩家
    async def broadcast_to_players(self):
        self.host_name = self.player_list[0]  # 更新房主名字
        room_info = RoomResponse(
            player_list=self.player_list,
            player_count=self.player_count,
            host_name=self.host_name,
            game_time=self.game_time,
            room_id=self.room_id,
            room_name=self.room_name,
            cuttime=self.cuttime
        )
        response = Response(
            type="get_room_info",
            success=True,
            message="房间信息更新",
            room_info=room_info
        )

        # 使用 username_to_connection 映射直接获取连接
        for username in self.player_list:
            if username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[username]
                try:
                    print(f"正在广播给玩家 {username}")
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"广播成功")
                except Exception as e:
                    print(f"广播给玩家 {username} 失败: {e}")

class PlayerConnection: # 玩家类
    def __init__(self, websocket: WebSocket, player_id: str):
        self.websocket = websocket      # WebSocket 连接
        self.player_id = player_id      # 玩家ID 机器id
        self.username = None            # 玩家用户名
        self.current_room_id = None     # 当前所在房间ID

class ChinesePlayer:
    def __init__(self, username: str, tiles: list):
        self.username = username        # 玩家名
        self.hand_tiles = tiles         # 手牌
        self.hand_tiles_count = len(tiles) # 手牌数量
        self.discard_tiles = []         # 弃牌
        self.combination_tiles = []     # 组合牌
        self.score = 0                  # 分数
        self.remaining_time = 20        # 剩余时间 （局时）
        self.current_player_index = 0   # 当前玩家索引 东南西北

    def get_tile(self, tiles_list):
        element = tiles_list.pop(0)
        self.hand_tiles.append(element)

class GameState:
    # gamestate负责一个游戏对局进程，init属性包含游戏房间状态 player_list 包含玩家数据
    def __init__(self, room_id: str, room):
        self.room_id = room_id # 房间号
        self.room = room # 房间类
        
        self.player_list: List[ChinesePlayer] = [] # 玩家列表 包含chinesePlayer类
        self.tiles_list = [] # 牌堆
        self.current_player_index = 0 # 目前轮到的玩家
        self.random_seed = 0 # 随机种子
        self.game_status = "waiting"  # waiting, playing, finished
        self.cuttime = room.cuttime # 切牌时间
        self.game_time = room.game_time # 游戏时间
        self.current_round = 0 # 第几轮 # 默认4轮

        # 新增：用于玩家操作的事件和队列
        self.action_events = {}  # 玩家索引 -> Event
        self.action_queues = {}  # 玩家索引 -> Queue

    async def next_current_index(self):
        if self.current_player_index == 3:
            self.current_player_index = 0
        else:
            self.current_player_index += 1

    def init_tiles(self):
        # 标准牌堆
        sth_tiles_set = {
            "11","12","13","14","15","16","17","18","19", # 万
            "21","22","23","24","25","26","27","28","29", # 饼
            "31","32","33","34","35","36","37","38","39", # 条
            "41","42","43","44", # 东南西北
            "45","46","47" # 中白发
        }
        # 花牌牌堆
        hua_tiles_set = {"51","52","53","54","55","56","57","58"} # 春夏秋冬 梅兰竹菊
        # 生成牌堆 tiles_list
        self.tiles_list = []
        for tile in sth_tiles_set:
            self.tiles_list.extend([tile] * 4)
        self.tiles_list.extend(hua_tiles_set)
        random.shuffle(self.tiles_list)

    def deal_initial_tiles(self):
        # 分配每位玩家13张牌
        for player in self.player_list:
            for _ in range(13):
                player.get_tile(self.tiles_list)
        # 庄家额外摸一张
        self.player_list[0].get_tile(self.tiles_list)

    async def broadcast_game_start(self):
        """广播游戏开始信息"""
        # 基础游戏信息
        base_game_info = {
            'player_list': [p.username for p in self.player_list], # 玩家列表
            'player_positions': [  # 转换为列表格式
                {'username': p.username, 'position': p.current_player_index}
                for p in self.player_list
            ],
            'current_player_index': self.current_player_index, # 当前轮到的玩家索引
            'tile_count': len(self.tiles_list), # 牌山剩余牌数
            'random_seed': self.random_seed, # 随机种子
            'game_status': self.game_status, # 游戏状态
            'corrent_round': self.current_round, # 当前轮数
            'cuttime': self.cuttime, # 切牌时间
            'game_time': self.game_time, # 游戏时间
            'players_info': [] # ↓玩家信息
        }

        # 为每个玩家准备信息
        for player in self.player_list: # 遍历玩家列表
            player_info = {
                'username': player.username, # 用户名
                'hand_tiles_count': len(player.hand_tiles), # 手牌数量
                'discard_tiles': player.discard_tiles, # 弃牌
                'combination_tiles': player.combination_tiles, # 组合
                'remaining_time': player.remaining_time, # 剩余时间
                'current_player_index': player.current_player_index, # 东南西北位置
                'score': player.score # 分数
            }
            base_game_info['players_info'].append(player_info) # 将字典添加到列表中

        # 为每个玩家发送消息
        for current_player in self.player_list:
            try:
                # 如果player_list中有玩家在self.room.game_server.username_to_connection:
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    
                    # 将游戏信息字典转换为 GameInfo 类 并添加 self_hand_tiles 字段
                    game_info = GameInfo(
                        **base_game_info,
                        self_hand_tiles=current_player.hand_tiles  # 只包含当前玩家的手牌
                    )

                    response = Response(
                        type="game_start_chinese",
                        success=True,
                        message="游戏开始",
                        game_info=game_info
                    )
                    
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 发送游戏开始信息")
            except Exception as e:
                print(f"向玩家 {current_player.username} 发送消息失败: {e}")

    async def wait_cut_action(self):
        """
        wait_action方法使用消息队列,接受cut_tiles传递的event状态和queue数据
        获取当前玩家,找到当前玩家的索引,将当前玩家的索引添加进入action_events和action_queues中,其中action_events
        代表当前玩家是否出牌,是一个事件布尔值,action_queues代表具体的操作数据,是一个队列
        每当循环开始,重置事件布尔值,并计算总计时时间(切牌时间 + 剩余时间)
        根据总等待时间进行计时循环,添加两个任务,等待1秒的time_task和等待玩家操作action_task
        await asyncio.wait 监听一个任务列表,通过设置return_when=asyncio.FIRST_COMPLETED 
        决定当任意一个任务完成时,决定返回完成的任务为第一个列表,随后则可以通过for task in pending 取消未完成的任务
        如果是action_task完成,则获取操作数据,处理出牌操作,如果time_task一直完成,直到总等待时间结束,则自动出牌。
        """
        # 获取当前玩家
        current_player = self.player_list[self.current_player_index]
        
        # 初始化事件和队列
        if self.current_player_index not in self.action_events:
            self.action_events[self.current_player_index] = asyncio.Event()
            self.action_queues[self.current_player_index] = asyncio.Queue()
        
        # 重置事件状态
        self.action_events[self.current_player_index].clear()
        
        # 标记是否已出牌
        is_cut = False
        
        # 计算总计时时间（切牌时间 + 剩余时间）
        total_time = self.cuttime + current_player.remaining_time
        used_time = 0
        # 变量初始化，确保广播时有值
        cut_tile = None  
        cut_class = True
        
        try:
            for _ in range(total_time):
                # 创建两个任务：等待1秒和等待玩家操作
                timer_task = asyncio.create_task(asyncio.sleep(1))
                action_task = asyncio.create_task(self.action_events[self.current_player_index].wait())
                # 等待任意一个任务完成
                done, pending = await asyncio.wait(
                    [timer_task, action_task],
                    return_when=asyncio.FIRST_COMPLETED
                )
                # 取消未完成的任务
                for task in pending:
                    task.cancel()
                # 检查是否收到玩家操作
                if action_task in done:
                    action_data = await self.action_queues[self.current_player_index].get()
                    cut_class = action_data.get("cutClass")  # 布尔值
                    tile_id = action_data.get("TileId")     # 字符串
                    if tile_id in current_player.hand_tiles:
                        current_player.hand_tiles.remove(tile_id)
                        current_player.discard_tiles.append(tile_id)
                        cut_tile = tile_id
                        is_cut = True
                        break
                    else:
                        print(f"找不到牌 {tile_id} 在玩家 {current_player.username} 的手牌中")
                        continue
                else:
                    used_time += 1
                    print(f"used_time={used_time}")
            # 如果is_cut为False,则自动出牌
            if not is_cut:
                cut_tile = current_player.hand_tiles[-1]
                current_player.hand_tiles.pop()
                current_player.discard_tiles.append(cut_tile)
            if used_time >= self.cuttime:
                current_player.remaining_time -= (used_time - self.cuttime)
            await self.broadcast_cut_tiles(self.current_player_index,cut_class,cut_tile)
        except Exception as e:
            print(f"等待切牌操作时发生错误: {e}")
        finally:
            print("出牌结束")

    async def broadcast_cut_tiles(self, current_player_index: int, cut_class: bool, cut_tiles: str):
        """广播切牌信息"""
        for current_player in self.player_list:
            try:
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    
                    cut_info = Cut_response(
                        cut_player_index=current_player_index,
                        cut_class=cut_class,
                        cut_tiles=cut_tiles
                    )

                    response = Response(
                        type="cut_tiles_chinese",
                        success=True,
                        message="切牌信息",
                        cut_info=cut_info
                    )
                    
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播切牌信息")
            except Exception as e:
                print(f"向玩家 {current_player.username} 广播切牌信息失败: {e}")

    async def broadcast_deal_tile(self):
        # 遍历列表时获取索引
        for i, current_player in enumerate(self.player_list):
            if i == self.current_player_index:
                # 发送实际卡牌信息
                self.player_list[i].get_tile(self.tiles_list)
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    response = Response(
                        type="deal_tile_chinese",
                        success=True,
                        message="发牌",
                        deal_tile_info = Deal_tile_info(
                            remaining_time=current_player.remaining_time,
                            deal_player_index= self.current_player_index,
                            deal_tiles=self.player_list[i].hand_tiles[-1]
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播发牌信息")
            else:
                # 发送通用信息
                if current_player.username in self.room.game_server.username_to_connection:
                    player_conn = self.room.game_server.username_to_connection[current_player.username]
                    response = Response(
                        type="deal_tile_chinese",
                        success=True,
                        message="发牌",
                        deal_tile_info = Deal_tile_info(
                            remaining_time=current_player.remaining_time,
                            deal_player_index= self.current_player_index,
                            deal_tiles="0"
                        )
                    )
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"已向玩家 {current_player.username} 广播发牌信息")






class GameManager:
    def __init__(self):
        self.games: Dict[str, GameState] = {}  # room_id -> GameState
    # create_game方法用于传参game_state room_id room 房间名和 房间类 存储在games[room_id,game_state]当中 并返回game_state
    def create_game(self, room_id: str, room) -> GameState:
        game_state = GameState(room_id, room)
        self.games[room_id] = game_state
        return game_state

    def get_game(self, room_id: str) -> Optional[GameState]:
        return self.games.get(room_id)

    def remove_game(self, room_id: str):
        if room_id in self.games:
            del self.games[room_id]

class GameServer: # 游戏服务器类
    # 2.1 init方法创建了两个字典用于保存玩家、房间的数据和websocket连接
    def __init__(self):
        # 建立集合players[player_id,player_data]
        self.players: Dict[str, PlayerConnection] = {}  # player_id -> PlayerConnection
        # 建立集合rooms[room_id,room_data]
        self.rooms: Dict[str, Room] = {}                # room_id -> Room
        # 建立用户名到连接的映射
        self.username_to_connection: Dict[str, PlayerConnection] = {}  # username -> PlayerConnection
        self.game_manager = GameManager()
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
            
        room = self.rooms[room_id] # 拿到传入的房间id在rooms中对应的room
        
        # 2. 检查是否是房主
        player = self.players[player_id] # 拿到传入的player_id在players中对应的player
        if player.username != room.host_name:
            return Response(type="error_message", success=False, message="只有房主能开始游戏")
            
        # 3. 检查人数是否满足
        if room.player_count != 4:
            return Response(type="error_message", success=False, message="人数不足")
            
        # 4. 创建游戏任务
        asyncio.create_task(self.game_loop_chinese(room_id))
        
        # 5. 等待game_loop_chinese 广播游戏开始 ↓
    # 2.10 game_loop_chinese 方法用于游戏循环
    async def game_loop_chinese(self, room_id: str):
        room = self.rooms[room_id] # 拿到传入roomid对应的room
        
        # 1. game_manager会创建game_state 添加入games[room_id,game_state] 同时返回game_state
        # 我们使用game_state内定义的方法来操纵整个游戏 game_state的create_game是一个init方法
        game_state = self.game_manager.create_game(room_id, room)
        
        # 2. 遍历room.player_list 创建chineseplayer类 添加入game_state.player_list
        for username in room.player_list:
            # 创建玩家对象
            player = ChinesePlayer(
                username=username,  # 现在ChinesePlayer只需要username
                tiles=[]
            )
            game_state.player_list.append(player)
        
        # 打乱玩家顺序
        random.shuffle(game_state.player_list)

        # 枚举game_state的player_list 设置current_player_index,也就是东南西北
        for index, player in enumerate(game_state.player_list):
            player.current_player_index = index
        
        # 3. 初始化游戏
        game_state.init_tiles() # 初始化牌堆
        game_state.deal_initial_tiles() # 给每个玩家发13张初始牌 庄家额外摸一张
        game_state.game_status = "playing" # 设置游戏状态为playing

        # 4. 广播游戏开始 这里使用的是game_state的广播方法
        await game_state.broadcast_game_start()
        
        # 5. 游戏主循环
        while game_state.game_status == "playing":
            # 等待当前玩家的操作
            await game_state.wait_cut_action()
            # 移动到下一个玩家
            await game_state.next_current_index()
            # 发牌并广播
            await game_state.broadcast_deal_tile()
            
            

            # 检测吃听碰杠和切牌 现在只完成切牌功能

            # 开始→切牌→广播→吃听→广播→摸牌→广播→切牌

            
            # 这里可以添加其他游戏逻辑，如检查胡牌等

    async def cut_tiles(self, player_id: str, room_id: str, cutClass: bool, TileId: str):
        try:
            # 获取游戏状态
            game_state = self.game_manager.get_game(room_id)
            if not game_state:
                return
            
            # 获取玩家
            player_conn = self.players[player_id]
            username = player_conn.username
            
            # 查找对应的玩家和索引
            current_player = None
            player_index = -1
            for i, p in enumerate(game_state.player_list):
                if p.username == username:
                    current_player = p
                    player_index = i
                    break
            
            if current_player is None:
                return
            
            # 检查是否是当前玩家的回合
            if game_state.current_player_index != player_index:
                return
            
            # 将操作数据放入队列
            await game_state.action_queues[player_index].put({
                "cutClass": cutClass,  # 布尔值
                "TileId": TileId            # 字符串
            })
            
            game_state.action_events[player_index].set()
        except Exception as e:
            print(f"处理切牌操作时发生错误: {e}")

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