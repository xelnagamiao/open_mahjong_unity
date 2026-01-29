from typing import Dict, Any, Optional
from .room_validators import GBRoomValidator, MMCValidator, RiichiRoomValidator
from ..response import Response
from ..gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState
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
                type = "room/create_room_done",
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

    async def create_Qingque_room(self, player_id: str, room_name: str, gameround: int,
                                  password: str, roundTimerValue: int, stepTimerValue: int,
                                  tips: bool, random_seed: int = 0, open_cuohe: bool = False) -> Response:
        """
        创建青雀房间。
        青雀规则不支持错和，open_cuohe 参数会被忽略，统一按 False 处理。
        """
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
            
            # 传参配置 传入的参数（青雀规则不支持错和，强制为 False）
            room_config = {
                "room_name": room_name, # 房间名
                "game_round": gameround, # 最大局数
                "round_timer": roundTimerValue, # 局时
                "step_timer": stepTimerValue, # 步时
                "random_seed": random_seed, # 随机种子
                "open_cuohe": False, # 青雀规则不支持错和，固定为 False
            }

            # 拿取国标麻将验证器（青雀规则与国标类似，复用验证器）
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
                "room_type": "qingque", # 房间类型（青雀）
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
                type = "room/create_room_done",
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
                type="room/get_room_list",
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
                type="room/join_room_done",
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
                    type="room/leave_room_done",
                    success=True,
                    message="房间已解散"
                )
            
            # 检查剩余玩家是否都是机器人（user_id <= 10）
            all_bots = all(user_id <= 10 for user_id in room_data["player_list"])
            if all_bots:
                # 如果剩下的玩家都是机器人，也销毁房间
                await self.destroy_room(room_id)
                return Response(
                    type="room/leave_room_done",
                    success=True,
                    message="房间已解散（仅剩机器人）"
                )
            
            # 广播房间信息
            await self._broadcast_room_info(room_id)
            return Response(
                type="room/leave_room_done",
                success=True,
                    message="离开房间成功"
                )

        except Exception as e:
            return Response(
                type="error_message",
                success=False,
                message=f"离开房间失败: {str(e)}"
            )

    async def add_bot_to_room(self, Connect_id: str, room_id: str) -> Response:
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
                    message="游戏正在进行中，无法添加机器人"
                )
            
            # 检查房间是否满员
            if len(room_data["player_list"]) >= room_data["max_player"]:
                return Response(
                    type="error_message",
                    success=False,
                    message="房间已满"
                )
            
            # 机器人 user_id 为 0
            bot_user_id = 0
            
            # 添加机器人到房间（允许重复添加）
            room_data["player_list"].append(bot_user_id)
            
            # 更新玩家设置映射
            if "player_settings" not in room_data:
                room_data["player_settings"] = {}
            
            # 设置机器人信息
            room_data["player_settings"][bot_user_id] = {
                "user_id": bot_user_id,
                "username": "麻雀罗伯特",
                "title_id": 1,
                "profile_image_id": 1,
                "character_id": 1,
                "voice_id": 1
            }
            
            # 广播房间信息更新
            await self._broadcast_room_info(room_id)
            
            return Response(
                type="tips",
                success=True,
                message="罗伯特已添加到房间"
            )
            
        except Exception as e:
            logger.error(f"添加机器人到房间失败: {e}", exc_info=True)
            return Response(
                type="tips",
                success=False,
                message=f"添加机器人失败: {str(e)}"
            )

    async def kick_player_from_room(self, Connect_id: str, room_id: str, target_user_id: int) -> Response:
        """
        房主移除房间中的指定玩家
        """
        try:
            # 检查房间是否存在
            if room_id not in self.rooms:
                return Response(
                    type="tips",
                    success=False,
                    message="房间不存在"
                )

            room_data = self.rooms[room_id]

            # 检查请求者是否为房主
            host_user_id = room_data.get("host_user_id")
            requester = self.game_server.players.get(Connect_id)
            if not requester or requester.user_id != host_user_id:
                return Response(
                    type="tips",
                    success=False,
                    message="只有房主可以移除玩家"
                )

            # 不能移除房主自己
            if target_user_id == host_user_id:
                return Response(
                    type="tips",
                    success=False,
                    message="不能移除房主自己"
                )

            # 检查目标玩家是否在房间中
            if target_user_id not in room_data["player_list"]:
                return Response(
                    type="tips",
                    success=False,
                    message="目标玩家不在房间中"
                )

            # 从房间玩家列表中移除
            room_data["player_list"].remove(target_user_id)

            # 更新房间中的玩家设置信息
            # 普通玩家：直接删除其设置信息
            # 机器人（user_id == 0）：只有当房间中已经没有任何 0 时才删除设置，避免多机器人共享同一配置被提前删掉
            if "player_settings" in room_data and target_user_id in room_data["player_settings"]:
                if target_user_id == 0:
                    # 如果 player_list 中已经没有机器人了，再删设置
                    if 0 not in room_data["player_list"]:
                        del room_data["player_settings"][target_user_id]
                else:
                    del room_data["player_settings"][target_user_id]

            # 更新目标玩家的房间信息并通知其被移除
            target_conn = self.game_server.user_id_to_connection.get(target_user_id)
            if target_conn:
                target_conn.current_room_id = None
                kick_response = Response(
                    type="room/leave_room_done",
                    success=True,
                    message="您已被房主移出房间"
                )
                try:
                    await target_conn.websocket.send_json(kick_response.dict(exclude_none=True))
                except Exception as e:
                    logger.error(f"向被移除玩家 user_id={target_user_id} 发送消息失败: {e}")

            # 如果房间空了就销毁
            if len(room_data["player_list"]) == 0:
                await self.destroy_room(room_id)
                return Response(
                    type="tips",
                    success=True,
                    message="玩家已移除，房间已解散"
                )

            # 广播房间信息更新
            await self._broadcast_room_info(room_id)

            return Response(
                type="tips",
                success=True,
                message="玩家已被移出房间"
            )

        except Exception as e:
            logger.error(f"移除玩家失败: {e}", exc_info=True)
            return Response(
                type="tips",
                success=False,
                message=f"移除玩家失败: {str(e)}"
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
            type = "room/refresh_room_info",
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
            await self.game_server.gamestate_manager.cleanup_game_state_by_room_id(room_id)
        
        # 向所有房间内的玩家广播离开房间消息
        leave_response = Response(
            type="room/leave_room_done",
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


