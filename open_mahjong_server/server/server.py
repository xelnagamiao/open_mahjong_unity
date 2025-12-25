from fastapi import FastAPI, WebSocket
from typing import Dict, Optional, List, Any
from pydantic import BaseModel
import json
import asyncio
import logging
from logging.handlers import RotatingFileHandler
from contextlib import asynccontextmanager
from datetime import datetime
from .response import Response
from .game_gb.game_state import ChineseGameState
from .room.room_manager import RoomManager
from .database.db_manager import DatabaseManager
from .chat_server.chat_server import ChatServer
from .game_calculation.game_calculation_service import GameCalculationService
import secrets,hashlib
import subprocess,os,signal,sys
import time

Debug = True

# 根据 Debug 值决定使用哪个配置
if Debug:
    from .test_config import Config
else:
    from .local_config import Config

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
    handlers=[
        RotatingFileHandler("app.log", maxBytes=5*1024*1024, backupCount=25, encoding='utf-8'),
        logging.StreamHandler() if Config.logging_do_stream_handler == True else None
    ]
)

logger = logging.getLogger(__name__)


# 创建数据库实例
db_manager = DatabaseManager(
    host = Config.host,
    user = Config.user,
    password = Config.password,
    database = Config.database,
    port = Config.port
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
    if Config.auto_create_chatserver:
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
        self.current_room_id: Optional[str] = None # 所在房间ID
        self.is_tourist: bool = False  # 是否为游客

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
        logging.info(f"玩家 {Connect_id} 已连接")

    # 玩家断开连接：
    async def disconnect(self, Connect_id: str):
        if Connect_id in self.players:
            player = self.players[Connect_id]
            # 帮助玩家自动离开房间（异步执行）
            if player.current_room_id:
                await self.room_manager.leave_room(Connect_id, player.current_room_id)
                logging.info(f"玩家 {Connect_id} 已离开房间 {player.current_room_id}")
            

            # 如果是游客账户，尝试删除（只有在用户名包含"游客"且 is_tourist 为 True 时才删除）
            if player.user_id and player.is_tourist:
                if db_manager.delete_tourist_user(player.user_id, player.username):
                    logging.info(f"已删除游客账户 user_id={player.user_id}, username={player.username}")
                else:
                    logging.warning(f"删除游客账户失败 user_id={player.user_id}, username={player.username}")
            
            # 删除用户ID到玩家连接的映射
            if player.user_id:
                self.user_id_to_connection.pop(player.user_id, None)
            # 删除玩家连接并更新玩家信息
            del self.players[Connect_id]
            logging.info(f"玩家 {Connect_id} 已断开连接")

    # 玩家登录：存储用户ID和用户名到玩家连接的映射
    def store_player_session(self, Connect_id: str, user_id: int, username: str, is_tourist: bool = False):
        if Connect_id in self.players:
            player = self.players[Connect_id]
            player.user_id = user_id
            player.username = username
            player.is_tourist = is_tourist
            self.user_id_to_connection[user_id] = player # 存储用户ID到玩家连接的映射
            logging.info(f"已存储{'游客' if is_tourist else '玩家'} user_id={user_id}, username={username} 的会话数据")


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
        
        # 检查游戏是否已经在运行
        if room_data.get("is_game_running", False):
            return Response(type="error_message", success=False, message="游戏已在进行中")
            
        # 设置游戏运行状态
        room_data["is_game_running"] = True
        
        # 创建游戏任务
        if room_data["room_type"] == "guobiao":
            self.room_id_to_ChineseGameState[room_id] = ChineseGameState(self, room_data, self.calculation_service,self.db_manager)
            asyncio.create_task(self.room_id_to_ChineseGameState[room_id].game_loop_chinese())

    def get_server_stats(self) -> Dict[str, int]:
        """
        获取服务器统计数据
        返回：在线人数、等待房间数、进行房间数
        """
        # 在线人数：所有已连接的玩家
        online_players = len(self.players)
        
        # 进行房间数：正在运行游戏的房间
        playing_rooms = len(self.room_id_to_ChineseGameState)
        
        # 等待房间数：总房间数 - 进行房间数
        total_rooms = len(self.room_manager.rooms)
        waiting_rooms = total_rooms - playing_rooms
        
        return {
            "online_players": online_players,
            "waiting_rooms": waiting_rooms,
            "playing_rooms": playing_rooms
        }

game_server = GameServer()

@app.websocket("/game/{Connect_id}")
async def message_input(websocket: WebSocket, Connect_id: str):
    logging.info(f"收到新的连接请求: {Connect_id}")
    await game_server.connect(websocket, Connect_id)
    logging.info(f"连接建立成功: {Connect_id}")

    try:
        while True:
            message = await websocket.receive_json()
            logging.info(f"收到消息: {message}")

            if message["type"] == "send_release_version":
                release_version = message["release_version"]
                if release_version != Config.release_version:
                    from .response import MessageInfo
                    response = Response(
                        type="message",
                        success=False,
                        message="版本不匹配",
                        message_info=MessageInfo(
                            title="版本检查失败",
                            content="您的客户端版本与服务器版本不匹配，请更新客户端到最新版本后再试。"
                        )
                    )
                    await websocket.send_json(response.dict(exclude_none=True))
                    # 断开连接
                    await websocket.close()
                    break

            if message["type"] == "login":
                is_tourist = message.get("is_tourist", False)
                
                if is_tourist:
                    # 游客登录：创建新账户，不需要密码验证
                    logging.info(f"游客登录请求 - Connect_id: {Connect_id}")
                    response = await player_login("", "", is_tourist=True)
                else:
                    # 普通用户登录：需要用户名和密码
                    username = message.get("username", "")
                    password = message.get("password", "")
                    logging.info(f"登录请求 - 用户名: {username}, 密码: {password}")
                    response = await player_login(username, password, is_tourist=False)
                
                if response.success and response.login_info:
                    user_id = response.login_info.user_id
                    # 检查该账户是否已经在其他地方登录
                    if user_id in game_server.user_id_to_connection:
                        old_player = game_server.user_id_to_connection[user_id]
                        old_connect_id = old_player.Connect_id
                        
                        # 获取当前时间
                        current_time = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                        
                        # 给之前的连接发送提示消息
                        try:
                            from .response import MessageInfo
                            kickout_message = Response(
                                type="message",
                                success=False,
                                message="账户被顶替",
                                message_info=MessageInfo(
                                    title="账户登录提醒",
                                    content=f"您的账户已于{current_time}在其他地方登录。如果不是您的行为，可能账户已被他人使用，请及时修改密码以确保账户安全。"
                                )
                            )
                            await old_player.websocket.send_json(kickout_message.dict(exclude_none=True))
                            logging.info(f"已向旧连接 {old_connect_id} 发送账户被顶替提示")
                        except Exception as e:
                            logging.error(f"向旧连接发送提示消息失败: {e}")
                        
                        # 断开之前的连接
                        await game_server.disconnect(old_connect_id)
                        logging.info(f"已断开旧连接 {old_connect_id}，用户ID: {user_id}")
                    
                    # 存储新登录的会话
                    game_server.store_player_session(Connect_id, user_id, response.login_info.username, is_tourist=is_tourist)
                await websocket.send_json(response.dict(exclude_none=True))

            elif message["type"] == "create_GB_room":
                logging.info(f"创建房间请求 - 用户名: {Connect_id}")
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

            elif message["type"] == "get_server_stats":
                # 获取服务器统计数据
                from .response import ServerStatsInfo
                stats = game_server.get_server_stats()
                server_stats = ServerStatsInfo(
                    online_players=stats["online_players"],
                    waiting_rooms=stats["waiting_rooms"],
                    playing_rooms=stats["playing_rooms"]
                )
                response = Response(
                    type="get_server_stats",
                    success=True,
                    message="获取服务器统计成功",
                    server_stats=server_stats
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
                    continue
                
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
                    continue
                
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
    
    except Exception as e:
        # WebSocket 连接断开或其他异常
        logging.error(f"WebSocket 连接异常: {Connect_id}, 错误: {e}")
    finally:
        # 确保在连接断开时调用 disconnect 方法
        asyncio.create_task(game_server.disconnect(Connect_id))

def validate_username(username: str) -> Optional[str]:
    """
    验证用户名：中文=2，数字=1，英文=1，总长度>=4，不超过32字节
    Returns:
        如果验证失败返回错误消息，否则返回None
    """
    if not username or not username.strip():
        return "用户名不能为空"
    
    username = username.strip()
    
    if len(username.encode('utf-8')) > 32:
        return "用户名不能超过32个字节"
    
    length = 0
    for char in username:
        if '\u4e00' <= char <= '\u9fff':
            length += 2  # 中文=2
        elif char.isalpha() and char.isascii():
            length += 1  # 英文=1
        elif char.isdigit():
            length += 1  # 数字=1
    
    if length < 2:
        return "用户名长度至少需要2（中文=2，数字=1，英文=1）"
    
    return None

def validate_password(password: str) -> Optional[str]:
    """
    验证密码：6-32个字符，只能包含英文、数字或特殊字符
    Returns:
        如果验证失败返回错误消息，否则返回None
    """
    if not password:
        return "密码不能为空"
    
    if len(password) < 6 or len(password) > 32:
        return "密码至少需要6个字符" if len(password) < 6 else "密码不能超过32个字符"
    
    for char in password:
        is_letter = char.isalpha() and char.isascii()
        is_digit = char.isdigit()
        is_special = 33 <= ord(char) <= 126 and not (char.isalpha() or char.isdigit())
        
        if not (is_letter or is_digit or is_special):
            return "密码只能包含英文、数字或特殊字符"
    
    return None

async def player_login(username: str, password: str, is_tourist: bool = False) -> Response:
    """
    玩家登录/注册功能
    如果用户不存在则自动注册，存在则验证密码
    游客登录时使用随机用户名和空密码
    Args:
        username: 用户名（游客登录时会被忽略，会自动生成）
        password: 密码（游客登录时使用空密码）
        is_tourist: 是否为游客登录
    Returns:
        Response 对象，包含登录结果和用户信息
    """
    # 如果是游客登录，生成随机用户名并使用空密码
    if is_tourist:
        max_attempts = 100  # 最大尝试次数，避免无限循环
        tourist_username = None
        
        for attempt in range(max_attempts):
            # 生成随机后缀
            random_suffix = secrets.token_urlsafe(8)
            candidate_username = f"游客_{random_suffix}"
            
            # 检查用户名是否已存在
            if db_manager.get_user_by_username(candidate_username) is None:
                tourist_username = candidate_username
                break
        
        # 如果所有尝试都失败
        if tourist_username is None:
            logging.error("无法生成唯一的游客用户名，已达到最大尝试次数")
            return Response(
                type="tips",
                success=False,
                message="游客登录失败，请稍后重试"
            )
        
        username = tourist_username
        password = ""
    
    # 验证用户名和密码（游客不需要验证，因为已经生成）
    if not is_tourist:
        username_error = validate_username(username)
        if username_error:
            return Response(
                type="tips",
                success=False,
                message=username_error
            )
        
        password_error = validate_password(password)
        if password_error:
            return Response(
                type="tips",
                success=False,
                message=password_error
            )
    
    # 检查用户是否存在
    player: Optional[Dict[str, Any]] = db_manager.get_user_by_username(username)
    
    if player is not None:
        # 用户存在，验证密码
        stored_password_hash = player.get('password')
        if stored_password_hash and db_manager.verify_password(password, stored_password_hash):
            user_id = player.get('user_id')
        else:
            return Response(
                type="login",
                success=False,
                message="密码错误"
            )
    else:
        # 用户不存在，创建新用户
        user_id = db_manager.create_user(username, password, is_tourist=is_tourist)
        if not user_id:
            return Response(
                type="login",
                success=False,
                message="注册失败",
                username=username
            )
    
    # 生成用户秘钥
    user_key = await chat_server.hash_username(username)
    logging.info(f" 生成用户秘钥{user_key} ")
    
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
        message="游客登录成功" if is_tourist else ("登录成功" if player is not None else "注册并登录成功"),
        login_info=login_info,
        user_settings=user_settings,
        user_config=user_config
    )

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "server:app",
        host="localhost",
        port=8081,
        reload= True
    ) 