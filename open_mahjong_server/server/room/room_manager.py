from typing import Dict, Any, Optional
from .room_validators import GBRoomValidator, MMCValidator, RiichiRoomValidator
from ..response import Response
from ..game_gb.game_state import ChineseGameState
from ..game_calculation.game_calculation_service import Chinese_Hepai_Check
from ..game_calculation.game_calculation_service import Chinese_Tingpai_Check
import json
import asyncio
import logging

logger = logging.getLogger(__name__)

class RoomManager:
    def __init__(self, game_server):
        # 游戏服务器
        self.game_server = game_server
        # 存储房间信息和房间密码
        self.rooms: Dict[str, dict] = {}
        self.room_passwords: Dict[str, str] = {}
        # 房间的合法性验证器
        self.room_validators = {
            "guobiao": GBRoomValidator,
            "mmc": MMCValidator,
            "riichi": RiichiRoomValidator
        }
        # 不同规则挂载的游戏验证器
        self.Chinese_Hepai_Check = Chinese_Hepai_Check()
        self.Chinese_Tingpai_Check = Chinese_Tingpai_Check()

    async def create_GB_room(self, player_id: str, room_name: str, gameround: int, 
                           password: str, roundTimerValue: int, stepTimerValue: int, tips: bool, random_seed: int = 0, open_cuohe: bool = False) -> Response:
        try:
            # 检查玩家是否存在
            if player_id not in self.game_server.players:
                return Response(
                    type="tips",
                    success=False,
                    message="请先登录"
                )

            # 获取玩家信息
            player = self.game_server.players[player_id] # 拿取 PlayerConnection
            if not player.user_id:
                return Response(
                    type="tips",
                    success=False,
                    message="请先登录"
                )
            host_user_id = player.user_id  # 获取房主ID
            host_name = player.username  # 获取房主名（用于显示）

            # 获取房主的设置信息
            host_settings = self.game_server.db_manager.get_user_settings(host_user_id)
            if not host_settings:
                return Response(
                    type="tips",
                    success=False,
                    message="获取用户设置失败"
                )

            # 构建房间配置
            has_password = False
            if password == "":
                has_password = False
            else:
                has_password = True
            
            # 传参配置 传入的参数
            room_config = {
                "room_name": room_name, # 房间名
                "game_round": gameround, # 最大局数
                "round_timer": roundTimerValue, # 局时
                "step_timer": stepTimerValue, # 步时
                "random_seed": random_seed, # 随机种子
                "open_cuohe": open_cuohe, # 是否开启错和
            }

            # 拿取国标麻将验证器 使用验证器验证room_config
            try:
                validator_class = self.room_validators["guobiao"]
                validated_config = validator_class(**room_config) # 解包room_config 调用验证器方法
            except ValueError as e:
                return Response(
                    type="tips",
                    success=False,
                    message=f"房间配置无效: {str(e)}"
                )

            # 生成房间ID
            room_id = self._generate_room_id()

            # 创建房间数据头
            room_data = {
                "room_id": room_id, # 房间ID
                "room_type": "guobiao", # 房间类型
                "max_player": 4, # 最大玩家数
                "player_list": [host_user_id], # 玩家列表（使用 user_id）
                "player_settings": {
                    host_user_id: {
                        "user_id": host_user_id,
                        "username": host_settings.get('username', host_name),
                        "title_id": host_settings.get('title_id', 1),
                        "profile_image_id": host_settings.get('profile_image_id', 1),
                        "character_id": host_settings.get('character_id', 1),
                        "voice_id": host_settings.get('voice_id', 1)
                    }
                },  # 玩家ID到设置信息的映射
                "has_password": has_password, # 是否有密码
                "tips": tips, # 是否开启提示
                "host_user_id": host_user_id, # 房主ID
                "host_name": host_name, # 房主名（用于显示）
                "is_game_running": False, # 游戏是否正在运行
            }

            # 将房间数据尾 添加到room_data中
            room_data.update(validated_config.dict())

            # 存储room_data到房间字典中 如果有密码保存密码
            self.rooms[room_id] = room_data
            if has_password:
                self.room_passwords[room_id] = password

            # 更新玩家信息
            player.current_room_id = room_id

            # 广播房间信息
            await self._broadcast_room_info(room_id)

            return Response(
                type = "create_room",
                success = True,
                message = "房间创建成功",
                room_info = room_data
            )

        except Exception as e:
            return Response(
                type="error_message",
                success=False,
                message=f"创建房间失败: {str(e)}"
            )

    def get_room_list(self) -> Response:
        try:
            room_list = []
            for room_id, room_data in self.rooms.items():
                room_list.append(room_data)
            return Response(
                type="get_room_list",
                success=True,
                message="获取房间列表成功",
                room_list=room_list
            )
        except Exception as e:
            return Response(
                type="error_message",
                success=False,
                message=f"获取房间列表失败: {str(e)}"
            )

    async def join_room(self, player_id: str, room_id: str, password: str) -> Response:
        try:
            # 检查房间是否存在
            if room_id not in self.rooms:
                return Response(
                    type="error_message",
                    success=False,
                    message="房间不存在"
                )

            room_data = self.rooms[room_id]
            
            # 检查游戏是否正在运行
            if room_data.get("is_game_running", False):
                return Response(
                    type="error_message",
                    success=False,
                    message="游戏正在进行中，无法加入"
                )
            
            # 检查密码
            if room_data["has_password"] and self.room_passwords.get(room_id) != password:
                return Response(
                    type="error_message",
                    success=False,
                    message="密码错误"
                )

            # 检查房间是否满员
            if len(room_data["player_list"]) >= room_data["max_player"]:
                return Response(
                    type="error_message",
                    success=False,
                    message="房间已满"
                )

            # 获取玩家信息
            player = self.game_server.players[player_id]
            if not player.user_id:
                return Response(
                    type="error_message",
                    success=False,
                    message="请先登录"
                )
            
            # 检查玩家是否已经在房间中
            if player.user_id in room_data["player_list"]:
                return Response(
                    type="error_message",
                    success=False,
                    message="玩家已在房间中"
                )
            
            # 检查玩家是否在其他房间中
            if player.current_room_id and player.current_room_id != room_id:
                return Response(
                    type="error_message",
                    success=False,
                    message="玩家已在其他房间中，请先离开当前房间"
                )
            
            # 更新房间信息
            room_data["player_list"].append(player.user_id)
            # 更新玩家设置映射
            if "player_settings" not in room_data:
                room_data["player_settings"] = {}
            
            # 获取玩家的设置信息
            player_settings = self.game_server.db_manager.get_user_settings(player.user_id)
            if player_settings:
                room_data["player_settings"][player.user_id] = {
                    "user_id": player.user_id,
                    "username": player_settings.get('username', player.username),
                    "title_id": player_settings.get('title_id', 1),
                    "profile_image_id": player_settings.get('profile_image_id', 1),
                    "character_id": player_settings.get('character_id', 1),
                    "voice_id": player_settings.get('voice_id', 1)
                }
            else:
                # 如果获取失败，使用默认值
                room_data["player_settings"][player.user_id] = {
                    "user_id": player.user_id,
                    "username": player.username,
                    "title_id": 1,
                    "profile_image_id": 1,
                    "character_id": 1,
                    "voice_id": 1
                }

            # 更新玩家信息
            player.current_room_id = room_id

            # 广播房间信息
            await self._broadcast_room_info(room_id)

            return Response(
                type="join_room",
                success=True,
                message="加入房间成功"
            )

        except Exception as e:
            return Response(
                type="error_message",
                success=False,
                message=f"加入房间失败: {str(e)}"
            )

    async def leave_room(self, Connect_id: str, room_id: str) -> Response:
        try:
            # 检查房间是否存在
            if room_id not in self.rooms:
                return Response(
                    type="error_message",
                    success=False,
                    message="房间不存在"
                )

            room_data = self.rooms[room_id]
            player = self.game_server.players[Connect_id]
            
            if not player.user_id:
                return Response(
                    type="error_message",
                    success=False,
                    message="请先登录"
                )

            # 检查玩家是否在房间中
            if player.user_id not in room_data["player_list"]:
                return Response(
                    type="error_message",
                    success=False,
                    message="玩家不在房间中"
                )

            # 更新房间信息
            room_data["player_list"].remove(player.user_id)

            # 更新玩家信息
            player.current_room_id = None
            
            # 更新玩家设置映射
            if "player_settings" in room_data and player.user_id in room_data["player_settings"]:
                del room_data["player_settings"][player.user_id]

            # 如果房间空了就删除
            if len(room_data["player_list"]) == 0:
                # 调用 destroy_room 方法进行房间清理
                await self.destroy_room(room_id)
                return Response(
                    type="leave_room",
                    success=True,
                    message="房间已解散"
                )
            else:
                # 广播房间信息
                await self._broadcast_room_info(room_id)
                return Response(
                    type="leave_room",
                    success=True,
                    message="离开房间成功"
                )

        except Exception as e:
            return Response(
                type="error_message",
                success=False,
                message=f"离开房间失败: {str(e)}"
            )

    def _generate_room_id(self) -> str:
        """生成房间ID"""
        for i in range(1, 9999):
            if str(i) not in self.rooms:
                return str(i)
        raise ValueError("无法创建更多房间")

    async def _broadcast_room_info(self, room_id: str):
        """广播房间信息给所有房间内的玩家"""
        room_data = self.rooms[room_id]
        
        response = Response(
            type = "get_room_info",
            success = True,
            message = "房间信息更新",
            room_info = room_data
        )

        for user_id in room_data["player_list"]:
            if user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[user_id]
                try:
                    player_setting = room_data.get("player_settings", {}).get(user_id, {})
                    username = player_setting.get("username", f"用户{user_id}")
                    logger.debug(f"正在广播给玩家 user_id={user_id}, username={username}")
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    logger.debug(f"广播成功")
                except Exception as e:
                    logger.error(f"广播给玩家 user_id={user_id} 失败: {e}")

    async def destroy_room(self, room_id: str):
        """销毁房间并广播离开房间消息给所有玩家"""
        if room_id not in self.rooms:
            logger.warning(f"房间 {room_id} 不存在，无需销毁")
            return
        
        room_data = self.rooms[room_id]
        
        # 如果游戏正在运行，清理gamestate
        if room_data.get("is_game_running", False):
            # 从 game_state 中获取所有玩家的 user_id（因为 player_list 可能已经为空）
            game_state = None
            if room_id in self.game_server.room_id_to_ChineseGameState:
                game_state = self.game_server.room_id_to_ChineseGameState[room_id]
                # 调用游戏状态的清理方法
                await game_state.cleanup_game_state()
        
        # 向所有房间内的玩家广播离开房间消息
        leave_response = Response(
            type="leave_room",
            success=True,
            message="房间已解散"
        )
        
        # 获取所有玩家ID的副本，因为后面会删除房间
        player_list_copy = room_data["player_list"].copy()
        
        # 向所有玩家广播离开房间消息并清除他们的房间ID
        for user_id in player_list_copy:
            if user_id in self.game_server.user_id_to_connection:
                player_conn = self.game_server.user_id_to_connection[user_id]
                
                # 清除玩家的房间ID
                player_conn.current_room_id = None
                
                # 广播离开房间消息
                try:
                    player_setting = room_data.get("player_settings", {}).get(user_id, {})
                    username = player_setting.get("username", f"用户{user_id}")
                    logger.debug(f"正在向玩家 user_id={user_id}, username={username} 广播房间解散消息")
                    await player_conn.websocket.send_json(leave_response.dict(exclude_none=True))
                    logger.debug(f"房间解散消息广播成功")
                except Exception as e:
                    logger.error(f"向玩家 user_id={user_id} 广播房间解散消息失败: {e}")
        
        # 删除房间和密码
        del self.rooms[room_id]
        if room_id in self.room_passwords:
            del self.room_passwords[room_id]
        
        logger.info(f"房间 {room_id} 已销毁") 


