from typing import Dict, Any, Optional
from room_validators import GBRoomValidator, MMCValidator, RiichiRoomValidator
from response import Response
from open_mahjong_server.server.GB_Game.GameState_GB import ChineseGameState
from open_mahjong_server.server.GB_Game.Hepai_Check_GB import Chinese_Hepai_Check
from open_mahjong_server.server.GB_Game.Tingpai_Check_GB import Chinese_Tingpai_Check
import json
import asyncio

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
        # 房间游戏状态
        self.room_game_states: Dict[str, ChineseGameState] = {}

    async def create_GB_room(self, player_id: str, room_name: str, gameround: int, 
                           password: str, roundTimerValue: int, stepTimerValue: int, tips: bool) -> Response:
        try:
            # 检查玩家是否存在
            if player_id not in self.game_server.players:
                return Response(
                    type="error_message",
                    success=False,
                    message="请先登录"
                )

            # 获取玩家信息
            player = self.game_server.players[player_id] # 拿取 PlayerConnection
            host_name = player.username # 获取房主名

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
            }

            # 拿取国标麻将验证器 使用验证器验证room_config
            try:
                validator_class = self.room_validators["guobiao"]
                validated_config = validator_class(**room_config) # 解包room_config 调用验证器方法
            except ValueError as e:
                return Response(
                    type="error_message",
                    success=False,
                    message=f"房间配置无效: {str(e)}"
                )

            # 生成房间ID
            room_id = self._generate_room_id()

            # 创建房间数据头 固定的参数
            room_data = {
                "room_id": room_id, # 房间ID
                "room_type": "guobiao", # 房间类型
                "max_player": 4, # 最大玩家数
                "player_list": [host_name], # 玩家列表
                "has_password": has_password, # 是否有密码
                "tips": tips, # 是否开启提示
                "host_name": host_name, # 房主名
            }

            # 将房间数据尾 添加到room_data中
            room_data.update(validated_config.dict())

            # 存储room_data到房间字典中 如果有密码保存密码
            self.rooms[room_id] = room_data
            if has_password:
                self.room_passwords[room_id] = password

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
            
            # 检查玩家是否已经在房间中
            if player.username in room_data["player_list"]:
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
            room_data["player_list"].append(player.username)

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

    async def leave_room(self, player_id: str, room_id: str) -> Response:
        try:
            # 检查房间是否存在
            if room_id not in self.rooms:
                return Response(
                    type="error_message",
                    success=False,
                    message="房间不存在"
                )

            room_data = self.rooms[room_id]
            player = self.game_server.players[player_id]

            # 检查玩家是否在房间中
            if player.username not in room_data["player_list"]:
                return Response(
                    type="error_message",
                    success=False,
                    message="玩家不在房间中"
                )

            # 更新房间信息
            room_data["player_list"].remove(player.username)

            # 更新玩家信息
            player.current_room_id = None

            # 如果房间空了就删除
            if len(room_data["player_list"]) == 0:
                del self.rooms[room_id]
                if room_id in self.room_passwords:
                    del self.room_passwords[room_id]
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

        for username in room_data["player_list"]:
            if username in self.game_server.username_to_connection:
                player_conn = self.game_server.username_to_connection[username]
                try:
                    print(f"正在广播给玩家 {username}")
                    await player_conn.websocket.send_json(response.dict(exclude_none=True))
                    print(f"广播成功")
                except Exception as e:
                    print(f"广播给玩家 {username} 失败: {e}") 