from typing import Dict, Any, Optional
from .room_validators import GBRoomValidator, MMCValidator, RiichiRoomValidator, SichuanRoomValidator, ChangshaRoomValidator, JiandanRoomValidator
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
        # 已分配给排位匹配对局的房间号集合。匹配对局不依赖房间系统（不进入 self.rooms、
        # 不出现在房间列表、不可被加入），但仍占用一个唯一房间号用于对局内映射与聊天频道，
        # 需在此登记以避免与自定义房间号发生冲突。
        self.match_room_ids: set = set()
        # 房间的合法性验证器
        self.room_validators = {
            "guobiao": GBRoomValidator,
            "changsha": ChangshaRoomValidator,
            "jiandan": JiandanRoomValidator,
            "mmc": MMCValidator,
            "riichi": RiichiRoomValidator,
            "sichuan": SichuanRoomValidator
        }
        # 不同规则挂载的游戏验证器
        self.Chinese_Hepai_Check = Chinese_Hepai_Check()
        self.Chinese_Tingpai_Check = Chinese_Tingpai_Check()

    def _reject_if_in_active_game(self, user_id: int, action: str = "进入或创建房间") -> Optional[Response]:
        """对局中的玩家不可创建/加入房间；退房、被踢等其它操作不受此限制。"""
        if self.game_server.gamestate_manager.is_user_in_active_game(user_id):
            return Response(
                type="tips",
                success=False,
                message=f"您正在对局中，无法{action}",
            )
        return None

    def _reject_if_in_match_queue(self, user_id: int, action: str = "进入或创建房间") -> Optional[Response]:
        """匹配等待队列中的玩家不可创建/加入房间。"""
        match_manager = getattr(self.game_server, "match_manager", None)
        if match_manager and match_manager.is_user_in_queue(user_id):
            return Response(
                type="tips",
                success=False,
                message="您正在匹配队列中，请先取消匹配再进入或创建房间",
            )
        return None

    def _reject_room_entry_conflicts(self, user_id: int, action: str = "进入或创建房间") -> Optional[Response]:
        blocked = self._reject_if_in_active_game(user_id, action)
        if blocked:
            return blocked
        blocked = self._reject_if_in_match_queue(user_id, action)
        if blocked:
            return blocked
        match_manager = getattr(self.game_server, "match_manager", None)
        if match_manager and match_manager.is_user_committed(user_id):
            return Response(
                type="tips",
                success=False,
                message="您已匹配到对局，请完成当前对局后再进入或创建房间",
            )
        return None

    def _normalize_event_id(self, event_id) -> Optional[str]:
        if event_id is None:
            return None
        text = str(event_id).strip()
        return text or None

    def _validate_event_for_room(self, event_id: Optional[str], user_id: Optional[int] = None) -> Optional[Response]:
        """校验赛事可建房。user_id 有值时额外校验该用户为赛事管理员。"""
        if not event_id:
            return None
        event = self.game_server.db_manager.get_event(event_id)
        if not event:
            return Response(type="tips", success=False, message="赛事不存在")
        if event.get("status") != "active":
            status = event.get("status") or ""
            if status == "registered":
                msg = "赛事尚未开启，无法创建比赛房间"
            elif status == "closed":
                msg = "赛事已关闭，无法创建比赛房间"
            else:
                msg = "赛事未激活，无法创建比赛房间"
            return Response(type="tips", success=False, message=msg)
        if user_id is not None:
            role = self.game_server.db_manager.get_event_admin_role(event_id, user_id)
            if not role:
                return Response(type="tips", success=False, message="您没有该赛事的管理权限")
        return None

    def _apply_event_fields(self, room_data: dict, event_id: Optional[str]) -> None:
        if not event_id:
            return
        room_data["room_type"] = "events"
        room_data["event_id"] = event_id
        room_data["persist_empty"] = True

    def _is_persist_empty_room(self, room_data: dict) -> bool:
        return bool(
            room_data.get("persist_empty")
            or room_data.get("room_type") == "events"
        )

    def _clear_empty_host(self, room_data: dict) -> None:
        """空房间无房主；第一个加入的真人由 _sync_room_host 指定。
        host_user_id 用 0 而非 null，避免 Unity 端 int 反序列化失败。
        """
        if not room_data.get("player_list"):
            room_data["host_user_id"] = 0
            room_data["host_name"] = ""

    def _normalize_host_user_id(self, room_data: dict) -> None:
        """保证下发给客户端的 host_user_id 为 int（空房为 0）。"""
        if room_data.get("host_user_id") is None:
            room_data["host_user_id"] = 0
        if not room_data.get("player_list"):
            room_data["host_user_id"] = 0
            room_data.setdefault("host_name", "")

    def get_room_list(self, show_tip: bool = False) -> Response:
        try:
            room_list = []
            for room_id, room_data in self.rooms.items():
                self._normalize_host_user_id(room_data)
                room_list.append(room_data)
            return Response(
                type="room/get_room_list",
                success=True,
                message="获取房间列表成功",
                room_list=room_list,
                show_tip=show_tip
            )
        except Exception as e:
            return Response(
                type="error_message",
                success=False,
                message=f"获取房间列表失败: {str(e)}"
            )

    async def create_GB_room(self, player_id: str, room_name: str, gameround: int, 
                           password: str, roundTimerValue: int, stepTimerValue: int, tips: bool, random_seed: int = 0, open_cuohe: bool = False, sub_rule: str = "guobiao/standard", hepai_limit: int = 8, tourist_limit: bool = False, allow_spectator: bool = True, tactical_call: bool = False, claim_protection: bool = True, cuohe_type: int = 0, event_id: Optional[str] = None) -> Response:
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
            blocked = self._reject_room_entry_conflicts(host_user_id, "创建房间")
            if blocked:
                return blocked
            event_id = self._normalize_event_id(event_id)
            event_blocked = self._validate_event_for_room(event_id, host_user_id)
            if event_blocked:
                return event_blocked
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
            
            # 校验起和番限制（1-64）
            hepai_limit = max(1, min(64, hepai_limit))
            cuohe_type = 0 if cuohe_type not in (0, 1) else cuohe_type

            # 传参配置 传入的参数
            room_config = {
                "room_name": room_name, # 房间名
                "game_round": gameround, # 最大局数
                "round_timer": roundTimerValue, # 局时
                "step_timer": stepTimerValue, # 步时
                "random_seed": random_seed, # 随机种子
                "open_cuohe": open_cuohe, # 是否开启错和
                "cuohe_type": cuohe_type, # 错和形式
                "tactical_call": tactical_call, # 战术鸣牌
                "claim_protection": claim_protection, # 鸣牌保护
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
                "room_type": "custom", # 房间类型（自定义对局）
                "room_rule": "guobiao", # 房间规则（用于游戏与统计）
                "sub_rule": sub_rule, # 子规则
                "hepai_limit": hepai_limit, # 起和番限制
                "tourist_limit": tourist_limit, # 游客限制
                "allow_spectator": allow_spectator, # 允许观战
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
                "show_moqie_hint": False, # 手摸切灰显（创建房间 UI 后续可改）
                "host_user_id": host_user_id, # 房主ID
                "host_name": host_name, # 房主名（用于显示）
                "is_game_running": False, # 游戏是否正在运行
            }
            self._apply_event_fields(room_data, event_id)

            # 将房间数据尾 添加到room_data中
            room_data.update(validated_config.dict())
            room_data["is_player_set_random_seed"] = validated_config.random_seed != 0

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
                                  tips: bool, random_seed: int = 0, open_cuohe: bool = False, sub_rule: str = "qingque/standard", tourist_limit: bool = False, allow_spectator: bool = True, tactical_call: bool = False, claim_protection: bool = True, event_id: Optional[str] = None) -> Response:
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
            blocked = self._reject_room_entry_conflicts(host_user_id, "创建房间")
            if blocked:
                return blocked
            event_id = self._normalize_event_id(event_id)
            event_blocked = self._validate_event_for_room(event_id, host_user_id)
            if event_blocked:
                return event_blocked
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
                "tactical_call": tactical_call, # 战术鸣牌
                "claim_protection": claim_protection, # 鸣牌保护
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
                "room_type": "custom", # 房间类型（自定义对局）
                "room_rule": "qingque", # 房间规则（用于游戏与统计）
                "sub_rule": sub_rule, # 子规则
                "hepai_limit": 1, # 青雀起和限制固定为1
                "tourist_limit": tourist_limit, # 游客限制
                "allow_spectator": allow_spectator, # 允许观战
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
                "show_moqie_hint": False, # 手摸切灰显（创建房间 UI 后续可改）
                "host_user_id": host_user_id, # 房主ID
                "host_name": host_name, # 房主名（用于显示）
                "is_game_running": False, # 游戏是否正在运行
            }
            self._apply_event_fields(room_data, event_id)

            # 将房间数据尾 添加到room_data中
            room_data.update(validated_config.dict())
            room_data["is_player_set_random_seed"] = validated_config.random_seed != 0

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

    async def create_Changsha_room(self, player_id: str, room_name: str, gameround: int,
                                   password: str, roundTimerValue: int, stepTimerValue: int,
                                   tips: bool, random_seed: int = 0, sub_rule: str = "changsha/classic_double_bird",
                                   tourist_limit: bool = False, allow_spectator: bool = True,
                                   tactical_call: bool = False, claim_protection: bool = True,
                                   open_kong_replacement_count: int = 2,
                                   initial_hu_si_xi: bool = True,
                                   initial_hu_ban_ban_hu: bool = True,
                                   initial_hu_que_yi_se: bool = True,
                                   initial_hu_liu_liu_shun: bool = True,
                                   initial_hu_san_tong: bool = True,
                                   bird_count: int = 2,
                                   dealer_bird: bool = True,
                                   event_id: Optional[str] = None) -> Response:
        """创建长沙麻将房间。当前接入经典双鸟规则。"""
        try:
            if player_id not in self.game_server.players:
                return Response(type="tips", success=False, message="请先登录")

            player = self.game_server.players[player_id]
            if not player.user_id:
                return Response(type="tips", success=False, message="请先登录")
            host_user_id = player.user_id
            blocked = self._reject_room_entry_conflicts(host_user_id, "创建房间")
            if blocked:
                return blocked
            event_id = self._normalize_event_id(event_id)
            event_blocked = self._validate_event_for_room(event_id, host_user_id)
            if event_blocked:
                return event_blocked
            host_name = player.username

            host_settings = self.game_server.db_manager.get_user_settings(host_user_id)
            if not host_settings:
                return Response(type="tips", success=False, message="获取用户设置失败")

            has_password = password != ""
            room_config = {
                "room_name": room_name,
                "game_round": gameround,
                "round_timer": roundTimerValue,
                "step_timer": stepTimerValue,
                "random_seed": random_seed,
                "open_cuohe": False,
                "tactical_call": tactical_call,
                "claim_protection": claim_protection,
                "open_kong_replacement_count": open_kong_replacement_count,
                "initial_hu_si_xi": initial_hu_si_xi,
                "initial_hu_ban_ban_hu": initial_hu_ban_ban_hu,
                "initial_hu_que_yi_se": initial_hu_que_yi_se,
                "initial_hu_liu_liu_shun": initial_hu_liu_liu_shun,
                "initial_hu_san_tong": initial_hu_san_tong,
                "bird_count": bird_count,
                "dealer_bird": dealer_bird,
            }

            try:
                validator_class = self.room_validators["changsha"]
                validated_config = validator_class(**room_config)
            except ValueError as e:
                return Response(type="tips", success=False, message=f"房间配置无效: {str(e)}")

            room_id = self._generate_room_id()
            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "changsha",
                "sub_rule": sub_rule,
                "hepai_limit": 1,
                "tourist_limit": tourist_limit,
                "allow_spectator": allow_spectator,
                "max_player": 4,
                "player_list": [host_user_id],
                "player_settings": {
                    host_user_id: {
                        "user_id": host_user_id,
                        "username": host_settings.get('username', host_name),
                        "title_id": host_settings.get('title_id', 1),
                        "profile_image_id": host_settings.get('profile_image_id', 1),
                        "character_id": host_settings.get('character_id', 1),
                        "voice_id": host_settings.get('voice_id', 1)
                    }
                },
                "has_password": has_password,
                "tips": tips,
                "show_moqie_hint": False,
                "host_user_id": host_user_id,
                "host_name": host_name,
                "is_game_running": False,
            }
            self._apply_event_fields(room_data, event_id)

            room_data.update(validated_config.dict())
            room_data["is_player_set_random_seed"] = validated_config.random_seed != 0

            self.rooms[room_id] = room_data
            if has_password:
                self.room_passwords[room_id] = password

            player.current_room_id = room_id
            await self._broadcast_room_info(room_id)

            return Response(
                type="room/create_room_done",
                success=True,
                message="房间创建成功",
                room_info=room_data
            )

        except Exception as e:
            return Response(type="error_message", success=False, message=f"创建房间失败: {str(e)}")

    async def create_Jiandan_room(
        self,
        player_id: str,
        room_name: str,
        gameround: int,
        password: str,
        roundTimerValue: int,
        stepTimerValue: int,
        tips: bool,
        random_seed: int = 0,
        sub_rule: str = "jiandan/standard",
        tourist_limit: bool = False,
        allow_spectator: bool = True,
        tactical_call: bool = False,
        claim_protection: bool = True,
        event_id: Optional[str] = None,
    ) -> Response:
        """Create a first-win Jiandan room.

        The room deliberately exposes no hand-end option: every confirmed win
        ends the hand, and multi-stage continuation belongs to a separate PR.
        """
        try:
            if player_id not in self.game_server.players:
                return Response(type="tips", success=False, message="请先登录")

            player = self.game_server.players[player_id]
            if not player.user_id:
                return Response(type="tips", success=False, message="请先登录")
            host_user_id = player.user_id
            blocked = self._reject_room_entry_conflicts(host_user_id, "创建房间")
            if blocked:
                return blocked
            event_id = self._normalize_event_id(event_id)
            event_blocked = self._validate_event_for_room(event_id, host_user_id)
            if event_blocked:
                return event_blocked

            host_settings = self.game_server.db_manager.get_user_settings(host_user_id)
            if not host_settings:
                return Response(type="tips", success=False, message="获取用户设置失败")

            room_config = {
                "room_name": room_name,
                "game_round": gameround,
                "round_timer": roundTimerValue,
                "step_timer": stepTimerValue,
                "random_seed": random_seed,
                "tactical_call": tactical_call,
                "claim_protection": claim_protection,
            }
            try:
                validated_config = self.room_validators["jiandan"](**room_config)
            except ValueError as e:
                return Response(type="tips", success=False, message=f"房间配置无效: {str(e)}")

            room_id = self._generate_room_id()
            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "jiandan",
                "sub_rule": sub_rule,
                "hepai_limit": 0,
                "open_cuohe": False,
                "tourist_limit": tourist_limit,
                "allow_spectator": allow_spectator,
                "max_player": 4,
                "player_list": [host_user_id],
                "player_settings": {
                    host_user_id: {
                        "user_id": host_user_id,
                        "username": host_settings.get("username", player.username),
                        "title_id": host_settings.get("title_id", 1),
                        "profile_image_id": host_settings.get("profile_image_id", 1),
                        "character_id": host_settings.get("character_id", 1),
                        "voice_id": host_settings.get("voice_id", 1),
                    }
                },
                "has_password": password != "",
                "tips": tips,
                "show_moqie_hint": False,
                "host_user_id": host_user_id,
                "host_name": player.username,
                "is_game_running": False,
            }
            self._apply_event_fields(room_data, event_id)
            room_data.update(validated_config.dict())
            room_data["is_player_set_random_seed"] = validated_config.random_seed != 0

            self.rooms[room_id] = room_data
            if password:
                self.room_passwords[room_id] = password
            player.current_room_id = room_id
            await self._broadcast_room_info(room_id)
            return Response(
                type="room/create_room_done",
                success=True,
                message="房间创建成功",
                room_info=room_data,
            )
        except Exception as e:
            logger.error("创建简单麻将房间失败: %s", e, exc_info=True)
            return Response(type="error_message", success=False, message=f"创建房间失败: {str(e)}")

    async def create_Classical_room(self, player_id: str, room_name: str, gameround: int,
                                    password: str, roundTimerValue: int, stepTimerValue: int,
                                    tips: bool, random_seed: int = 0, sub_rule: str = "classical/standard", tourist_limit: bool = False, allow_spectator: bool = True, event_id: Optional[str] = None) -> Response:
        """创建古典麻将房间"""
        try:
            if player_id not in self.game_server.players:
                return Response(type="tips", success=False, message="请先登录")

            player = self.game_server.players[player_id]
            if not player.user_id:
                return Response(type="tips", success=False, message="请先登录")
            host_user_id = player.user_id
            blocked = self._reject_room_entry_conflicts(host_user_id, "创建房间")
            if blocked:
                return blocked
            event_id = self._normalize_event_id(event_id)
            event_blocked = self._validate_event_for_room(event_id, host_user_id)
            if event_blocked:
                return event_blocked
            host_name = player.username

            host_settings = self.game_server.db_manager.get_user_settings(host_user_id)
            if not host_settings:
                return Response(type="tips", success=False, message="获取用户设置失败")

            has_password = password != ""

            room_config = {
                "room_name": room_name,
                "game_round": gameround,
                "round_timer": roundTimerValue,
                "step_timer": stepTimerValue,
                "random_seed": random_seed,
                "open_cuohe": False,
            }

            try:
                validator_class = self.room_validators["guobiao"]
                validated_config = validator_class(**room_config)
            except ValueError as e:
                return Response(type="tips", success=False, message=f"房间配置无效: {str(e)}")

            room_id = self._generate_room_id()

            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "classical",
                "sub_rule": sub_rule,
                "hepai_limit": 1,
                "tourist_limit": tourist_limit,
                "allow_spectator": allow_spectator,
                "max_player": 4,
                "player_list": [host_user_id],
                "player_settings": {
                    host_user_id: {
                        "user_id": host_user_id,
                        "username": host_settings.get('username', host_name),
                        "title_id": host_settings.get('title_id', 1),
                        "profile_image_id": host_settings.get('profile_image_id', 1),
                        "character_id": host_settings.get('character_id', 1),
                        "voice_id": host_settings.get('voice_id', 1)
                    }
                },
                "has_password": has_password,
                "tips": tips,
                "show_moqie_hint": False,
                "host_user_id": host_user_id,
                "host_name": host_name,
                "is_game_running": False,
            }
            self._apply_event_fields(room_data, event_id)

            room_data.update(validated_config.dict())
            room_data["is_player_set_random_seed"] = validated_config.random_seed != 0

            self.rooms[room_id] = room_data
            if has_password:
                self.room_passwords[room_id] = password

            player.current_room_id = room_id

            await self._broadcast_room_info(room_id)

            return Response(
                type="room/create_room_done",
                success=True,
                message="房间创建成功",
                room_info=room_data
            )

        except Exception as e:
            return Response(type="error_message", success=False, message=f"创建房间失败: {str(e)}")

    async def create_Sichuan_room(self, player_id: str, room_name: str, gameround: int,
                                  password: str, roundTimerValue: int, stepTimerValue: int,
                                  tips: bool, random_seed: int = 0, sub_rule: str = "sichuan/standard",
                                  tourist_limit: bool = False, allow_spectator: bool = True,
                                  tactical_call: bool = False, blood_battle: bool = True, claim_protection: bool = True, event_id: Optional[str] = None) -> Response:
        """创建四川麻将（血战到底）房间。blood_battle 为可选开关。"""
        try:
            if player_id not in self.game_server.players:
                return Response(type="tips", success=False, message="请先登录")

            player = self.game_server.players[player_id]
            if not player.user_id:
                return Response(type="tips", success=False, message="请先登录")
            host_user_id = player.user_id
            blocked = self._reject_room_entry_conflicts(host_user_id, "创建房间")
            if blocked:
                return blocked
            event_id = self._normalize_event_id(event_id)
            event_blocked = self._validate_event_for_room(event_id, host_user_id)
            if event_blocked:
                return event_blocked
            host_name = player.username

            host_settings = self.game_server.db_manager.get_user_settings(host_user_id)
            if not host_settings:
                return Response(type="tips", success=False, message="获取用户设置失败")

            has_password = password != ""

            room_config = {
                "room_name": room_name,
                "game_round": gameround,
                "round_timer": roundTimerValue,
                "step_timer": stepTimerValue,
                "random_seed": random_seed,
                "tactical_call": tactical_call,
                "blood_battle": blood_battle,
                "claim_protection": claim_protection,
            }

            try:
                validator_class = self.room_validators["sichuan"]
                validated_config = validator_class(**room_config)
            except ValueError as e:
                return Response(type="tips", success=False, message=f"房间配置无效: {str(e)}")

            room_id = self._generate_room_id()

            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "sichuan",
                "sub_rule": sub_rule,
                "hepai_limit": 1,
                "tourist_limit": tourist_limit,
                "allow_spectator": allow_spectator,
                "max_player": 4,
                "player_list": [host_user_id],
                "player_settings": {
                    host_user_id: {
                        "user_id": host_user_id,
                        "username": host_settings.get('username', host_name),
                        "title_id": host_settings.get('title_id', 1),
                        "profile_image_id": host_settings.get('profile_image_id', 1),
                        "character_id": host_settings.get('character_id', 1),
                        "voice_id": host_settings.get('voice_id', 1)
                    }
                },
                "has_password": has_password,
                "tips": tips,
                "show_moqie_hint": False,
                "host_user_id": host_user_id,
                "host_name": host_name,
                "is_game_running": False,
            }
            self._apply_event_fields(room_data, event_id)

            room_data.update(validated_config.dict())
            room_data["is_player_set_random_seed"] = validated_config.random_seed != 0

            self.rooms[room_id] = room_data
            if has_password:
                self.room_passwords[room_id] = password

            player.current_room_id = room_id

            await self._broadcast_room_info(room_id)

            return Response(
                type="room/create_room_done",
                success=True,
                message="房间创建成功",
                room_info=room_data
            )

        except Exception as e:
            return Response(type="error_message", success=False, message=f"创建房间失败: {str(e)}")

    async def create_Riichi_room(self, player_id: str, room_name: str, gameround: int,
                                 password: str, roundTimerValue: int, stepTimerValue: int,
                                 tips: bool, random_seed: int = 0,
                                 sub_rule: str = "riichi/standard",
                                 open_cuohe: bool = False,
                                 hepai_limit: int = 1,
                                 red_dora: bool = True,
                                 allow_kuikae: bool = False,
                                 open_xiru: bool = True,
                                 open_tobi: bool = True,
                                 hepai_way: str = "head_bump",
                                 tourist_limit: bool = False,
                                 allow_spectator: bool = True,
                                 event_id: Optional[str] = None) -> Response:
        """创建立直麻将房间"""
        try:
            if player_id not in self.game_server.players:
                return Response(type="tips", success=False, message="请先登录")

            player = self.game_server.players[player_id]
            if not player.user_id:
                return Response(type="tips", success=False, message="请先登录")
            host_user_id = player.user_id
            blocked = self._reject_room_entry_conflicts(host_user_id, "创建房间")
            if blocked:
                return blocked
            event_id = self._normalize_event_id(event_id)
            event_blocked = self._validate_event_for_room(event_id, host_user_id)
            if event_blocked:
                return event_blocked
            host_name = player.username

            host_settings = self.game_server.db_manager.get_user_settings(host_user_id)
            if not host_settings:
                return Response(type="tips", success=False, message="获取用户设置失败")

            has_password = password != ""

            hepai_limit = max(1, min(64, hepai_limit))

            room_config = {
                "room_name": room_name,
                "game_round": gameround,
                "round_timer": roundTimerValue,
                "step_timer": stepTimerValue,
                "random_seed": random_seed,
                "open_cuohe": open_cuohe,
                "hepai_limit": hepai_limit,
                "red_dora": red_dora,
                "allow_kuikae": allow_kuikae,
                "open_xiru": open_xiru,
                "open_tobi": open_tobi,
                "hepai_way": hepai_way,
            }

            try:
                validator_class = self.room_validators["riichi"]
                validated_config = validator_class(**room_config)
            except ValueError as e:
                return Response(type="tips", success=False, message=f"房间配置无效: {str(e)}")

            room_id = self._generate_room_id()

            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "riichi",
                "sub_rule": sub_rule,
                "tourist_limit": tourist_limit,
                "allow_spectator": allow_spectator,
                "max_player": 4,
                "player_list": [host_user_id],
                "player_settings": {
                    host_user_id: {
                        "user_id": host_user_id,
                        "username": host_settings.get('username', host_name),
                        "title_id": host_settings.get('title_id', 1),
                        "profile_image_id": host_settings.get('profile_image_id', 1),
                        "character_id": host_settings.get('character_id', 1),
                        "voice_id": host_settings.get('voice_id', 1)
                    }
                },
                "has_password": has_password,
                "tips": tips,
                "show_moqie_hint": False,
                "host_user_id": host_user_id,
                "host_name": host_name,
                "is_game_running": False,
            }
            self._apply_event_fields(room_data, event_id)

            room_data.update(validated_config.dict())
            room_data["is_player_set_random_seed"] = validated_config.random_seed != 0

            self.rooms[room_id] = room_data
            if has_password:
                self.room_passwords[room_id] = password

            player.current_room_id = room_id

            await self._broadcast_room_info(room_id)

            return Response(
                type="room/create_room_done",
                success=True,
                message="房间创建成功",
                room_info=room_data
            )

        except Exception as e:
            return Response(type="error_message", success=False, message=f"创建房间失败: {str(e)}")

    def get_room_list(self, show_tip: bool = False) -> Response:
        try:
            room_list = []
            for room_id, room_data in self.rooms.items():
                room_list.append(room_data)
            return Response(
                type="room/get_room_list",
                success=True,
                message="获取房间列表成功",
                room_list=room_list,
                show_tip=show_tip
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

            blocked = self._reject_room_entry_conflicts(player.user_id, "加入房间")
            if blocked:
                return blocked
            
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

            # 若房间开启了游客限制，则不允许游客加入
            if room_data.get("tourist_limit", False) and getattr(player, "is_tourist", False):
                return Response(
                    type="error_message",
                    success=False,
                    message="该房间不允许游客加入"
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

            # 空比赛房：第一个加入的真人成为房主
            self._sync_room_host(room_data)

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
            # 同步移除其准备状态
            if player.user_id in room_data.get("ready_list", []):
                room_data["ready_list"].remove(player.user_id)

            # 更新玩家信息
            player.current_room_id = None
            
            # 更新玩家设置映射
            if "player_settings" in room_data and player.user_id in room_data["player_settings"]:
                del room_data["player_settings"][player.user_id]

            # 比赛场空房保留；普通房空了或仅剩机器人则销毁
            if len(room_data["player_list"]) == 0:
                if self._is_persist_empty_room(room_data):
                    self._clear_empty_host(room_data)
                    await self._broadcast_room_info(room_id)
                    return Response(
                        type="room/leave_room_done",
                        success=True,
                        message="离开房间成功"
                    )
                await self.destroy_room(room_id)
                return Response(
                    type="room/leave_room_done",
                    success=True,
                    message="房间已解散"
                )

            # 检查剩余玩家是否都是机器人（user_id <= 10）
            all_bots = all(user_id <= 10 for user_id in room_data["player_list"])
            if all_bots:
                if self._is_persist_empty_room(room_data):
                    self._remove_all_bots_from_room(room_data)
                    self._clear_empty_host(room_data)
                    await self._broadcast_room_info(room_id)
                    return Response(
                        type="room/leave_room_done",
                        success=True,
                        message="离开房间成功"
                    )
                await self.destroy_room(room_id)
                return Response(
                    type="room/leave_room_done",
                    success=True,
                    message="房间已解散（仅剩机器人）"
                )

            # 有人退出后清理全部机器人，再同步新房主，避免机器人排在 player_list 首位
            self._remove_all_bots_from_room(room_data)
            if len(room_data["player_list"]) == 0:
                if self._is_persist_empty_room(room_data):
                    self._clear_empty_host(room_data)
                    await self._broadcast_room_info(room_id)
                    return Response(
                        type="room/leave_room_done",
                        success=True,
                        message="离开房间成功"
                    )
                await self.destroy_room(room_id)
                return Response(
                    type="room/leave_room_done",
                    success=True,
                    message="房间已解散"
                )

            self._sync_room_host(room_data)
            
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

    async def add_smart_bot_to_room(self, Connect_id: str, room_id: str) -> Response:
        """添加牌效 AI 机器人（user_id=2）到房间"""
        try:
            if room_id not in self.rooms:
                return Response(type="error_message", success=False, message="房间不存在")

            room_data = self.rooms[room_id]

            if room_data.get("is_game_running", False):
                return Response(type="error_message", success=False, message="游戏正在进行中，无法添加机器人")

            if len(room_data["player_list"]) >= room_data["max_player"]:
                return Response(type="error_message", success=False, message="房间已满")

            bot_user_id = 2

            room_data["player_list"].append(bot_user_id)

            if "player_settings" not in room_data:
                room_data["player_settings"] = {}

            room_data["player_settings"][bot_user_id] = {
                "user_id": bot_user_id,
                "username": "牌效罗伯特",
                "title_id": 1,
                "profile_image_id": 1,
                "character_id": 1,
                "voice_id": 1
            }

            await self._broadcast_room_info(room_id)

            return Response(type="tips", success=True, message="牌效罗伯特已添加到房间")

        except Exception as e:
            logger.error(f"添加牌效机器人到房间失败: {e}", exc_info=True)
            return Response(type="tips", success=False, message=f"添加机器人失败: {str(e)}")

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
            # 同步移除其准备状态
            if target_user_id in room_data.get("ready_list", []):
                room_data["ready_list"].remove(target_user_id)

            # 更新房间中的玩家设置信息
            # 普通玩家：直接删除其设置信息
            # 机器人（user_id <= 10）：只有当房间中已经没有该 user_id 时才删除设置，避免同类机器人共享配置被提前删掉
            if "player_settings" in room_data and target_user_id in room_data["player_settings"]:
                if target_user_id <= 10:
                    # 如果 player_list 中已经没有该机器人类型，再删除其设置
                    if target_user_id not in room_data["player_list"]:
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

            # 如果房间空了：比赛场保留，普通房销毁
            if len(room_data["player_list"]) == 0:
                if self._is_persist_empty_room(room_data):
                    self._clear_empty_host(room_data)
                    await self._broadcast_room_info(room_id)
                    return Response(
                        type="tips",
                        success=True,
                        message="玩家已移除"
                    )
                await self.destroy_room(room_id)
                return Response(
                    type="tips",
                    success=True,
                    message="玩家已移除，房间已解散"
                )

            self._sync_room_host(room_data)

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

    async def set_player_ready(self, Connect_id: str, room_id: str, ready: bool) -> Optional[Response]:
        """设置玩家准备状态。房主无需准备；机器人默认视为已准备，不进入 ready_list。
        成功时返回 None（状态通过 refresh_room_info 广播刷新），失败时返回错误 Response。"""
        try:
            if room_id not in self.rooms:
                return Response(type="error_message", success=False, message="房间不存在")

            room_data = self.rooms[room_id]

            if room_data.get("is_game_running", False):
                return Response(type="error_message", success=False, message="游戏进行中，无法更改准备状态")

            player = self.game_server.players.get(Connect_id)
            if not player or not player.user_id:
                return Response(type="error_message", success=False, message="请先登录")

            if player.user_id not in room_data["player_list"]:
                return Response(type="error_message", success=False, message="玩家不在房间中")

            # 房主无需准备
            if player.user_id == room_data["player_list"][0]:
                return Response(type="tips", success=False, message="房主无需准备")

            ready_list = room_data.setdefault("ready_list", [])
            if ready:
                if player.user_id not in ready_list:
                    ready_list.append(player.user_id)
            else:
                if player.user_id in ready_list:
                    ready_list.remove(player.user_id)

            await self._broadcast_room_info(room_id)
            return None

        except Exception as e:
            logger.error(f"设置准备状态失败: {e}", exc_info=True)
            return Response(type="error_message", success=False, message=f"更改准备状态失败: {str(e)}")

    def all_players_ready(self, room_data: dict) -> bool:
        """除房主外，所有真人玩家是否都已准备（机器人 user_id<=10 默认视为已准备）。"""
        player_list = room_data.get("player_list", [])
        ready_list = room_data.get("ready_list", [])
        for idx, user_id in enumerate(player_list):
            if idx == 0:
                continue  # 房主无需准备
            if user_id <= 10:
                continue  # 机器人默认已准备
            if user_id not in ready_list:
                return False
        return True

    def _generate_room_id(self) -> str:
        """生成房间ID（同时避开已被匹配对局占用的房间号）"""
        for i in range(1, 9999):
            rid = str(i)
            if rid not in self.rooms and rid not in self.match_room_ids:
                return rid
        raise ValueError("无法创建更多房间")

    def allocate_match_room_id(self) -> str:
        """为排位匹配对局分配一个唯一房间号（不写入 self.rooms，仅登记到 match_room_ids）。"""
        for i in range(1, 9999):
            rid = str(i)
            if rid not in self.rooms and rid not in self.match_room_ids:
                self.match_room_ids.add(rid)
                return rid
        raise ValueError("无法创建更多匹配房间号")

    def release_match_room_id(self, room_id: str):
        """匹配对局结束后释放其占用的房间号。"""
        self.match_room_ids.discard(room_id)

    def _remove_all_bots_from_room(self, room_data: dict):
        """移除房间内所有机器人（user_id <= 10），并清理其准备状态与设置。"""
        player_list = room_data.get("player_list") or []
        if not any(user_id <= 10 for user_id in player_list):
            return

        room_data["player_list"] = [user_id for user_id in player_list if user_id > 10]

        ready_list = room_data.get("ready_list", [])
        room_data["ready_list"] = [user_id for user_id in ready_list if user_id > 10]

        if "player_settings" in room_data:
            for bot_id in list(room_data["player_settings"].keys()):
                if bot_id <= 10 and bot_id not in room_data["player_list"]:
                    del room_data["player_settings"][bot_id]

    def _sync_room_host(self, room_data: dict):
        """player_list 首位为在房最久的玩家，同步 host 字段供客户端与权限校验使用。"""
        player_list = room_data.get("player_list") or []
        if not player_list:
            return
        host_user_id = player_list[0]
        room_data["host_user_id"] = host_user_id
        host_settings = room_data.get("player_settings", {}).get(host_user_id, {})
        room_data["host_name"] = host_settings.get("username", f"用户{host_user_id}")

    async def finish_custom_game_room(self, room_id: str):
        """自定义房对局结束后恢复等待态，保留房间供继续开局。"""
        if room_id not in self.rooms:
            logger.warning(f"房间 {room_id} 不存在，无法恢复等待态")
            return

        room_data = self.rooms[room_id]
        room_data["is_game_running"] = False
        # 对局结束后清空准备状态，要求重新准备才能再次开局
        room_data["ready_list"] = []
        self._sync_room_host(room_data)
        await self._broadcast_room_info(room_id)
        logger.info(f"自定义房 {room_id} 对局结束，已恢复等待态")

    async def sync_my_room(self, Connect_id: str) -> Response:
        """按服务端权威数据同步当前玩家所在房间；不在任何房间时返回 sync_not_in_room。"""
        if Connect_id not in self.game_server.players:
            return Response(type="tips", success=False, message="连接无效")
        player = self.game_server.players[Connect_id]
        if not player.user_id:
            return Response(type="tips", success=False, message="请先登录")

        user_id = player.user_id
        for room_id, room_data in self.rooms.items():
            if user_id in room_data.get("player_list", []):
                player.current_room_id = room_id
                room_data.setdefault("ready_list", [])
                return Response(
                    type="room/refresh_room_info",
                    success=True,
                    message="房间信息更新",
                    room_info=room_data,
                )

        player.current_room_id = None
        return Response(
            type="room/sync_not_in_room",
            success=True,
            message="不在任何房间",
        )

    async def _broadcast_room_info(self, room_id: str):
        """广播房间信息给所有房间内的玩家"""
        room_data = self.rooms[room_id]
        # 确保准备列表存在，使客户端始终能收到该字段
        room_data.setdefault("ready_list", [])
        self._normalize_host_user_id(room_data)        
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


    async def create_empty_event_room(self, event_id: str, room_rule: str, room_config: dict,
                                      password: str = "", created_by: Optional[int] = None) -> Response:
        """管理端创建比赛场空房间（无房主，第一个加入的真人成为房主）。"""
        event_id = self._normalize_event_id(event_id)
        event_blocked = self._validate_event_for_room(event_id, None)
        if event_blocked:
            return event_blocked

        rule = (room_rule or "").strip()
        rule_defaults = {
            "guobiao": ("guobiao/standard", "guobiao"),
            "qingque": ("qingque/standard", "guobiao"),
            "classical": ("classical/standard", "guobiao"),
            "riichi": ("riichi/standard", "riichi"),
            "sichuan": ("sichuan/standard", "sichuan"),
            "changsha": ("changsha/classic_double_bird", "changsha"),
        }
        if rule not in rule_defaults:
            return Response(type="tips", success=False, message=f"不支持的规则: {rule}")

        default_sub, _validator_key = rule_defaults[rule]
        sub_rule = room_config.get("sub_rule") or default_sub
        tips = bool(room_config.get("tips", False))
        tourist_limit = bool(room_config.get("tourist_limit", False))
        allow_spectator = bool(room_config.get("allow_spectator", True))
        has_password = bool(password)

        base_config = {
            "room_name": room_config.get("room_name") or f"赛事房间-{event_id[-6:]}",
            "game_round": int(room_config.get("game_round", 4)),
            "round_timer": int(room_config.get("round_timer", 20)),
            "step_timer": int(room_config.get("step_timer", 5)),
            "random_seed": int(room_config.get("random_seed", 0) or 0),
        }
        try:
            if rule == "guobiao":
                validated = self.room_validators["guobiao"](
                    **base_config,
                    open_cuohe=bool(room_config.get("open_cuohe", False)),
                    cuohe_type=int(room_config.get("cuohe_type", 0) or 0),
                    tactical_call=bool(room_config.get("tactical_call", False)),
                    claim_protection=bool(room_config.get("claim_protection", True)),
                )
                hepai_limit = max(1, min(64, int(room_config.get("hepai_limit", 8))))
            elif rule == "qingque":
                validated = self.room_validators["guobiao"](
                    **base_config,
                    open_cuohe=False,
                    tactical_call=bool(room_config.get("tactical_call", False)),
                    claim_protection=bool(room_config.get("claim_protection", True)),
                )
                hepai_limit = 1
            elif rule == "classical":
                validated = self.room_validators["guobiao"](
                    **base_config,
                    open_cuohe=False,
                )
                hepai_limit = 1
            elif rule == "riichi":
                validated = self.room_validators["riichi"](
                    **base_config,
                    open_cuohe=bool(room_config.get("open_cuohe", False)),
                    hepai_limit=max(1, min(64, int(room_config.get("hepai_limit", 1)))),
                    red_dora=bool(room_config.get("red_dora", True)),
                    allow_kuikae=bool(room_config.get("allow_kuikae", False)),
                    open_xiru=bool(room_config.get("open_xiru", True)),
                    open_tobi=bool(room_config.get("open_tobi", True)),
                    hepai_way=room_config.get("hepai_way") or "multi_ron",
                )
                hepai_limit = None
            elif rule == "sichuan":
                validated = self.room_validators["sichuan"](
                    **base_config,
                    tactical_call=bool(room_config.get("tactical_call", False)),
                    blood_battle=bool(room_config.get("blood_battle", True)),
                    claim_protection=bool(room_config.get("claim_protection", True)),
                )
                hepai_limit = 1
            else:
                validated = self.room_validators["changsha"](
                    **base_config,
                    open_cuohe=False,
                    tactical_call=bool(room_config.get("tactical_call", False)),
                    claim_protection=bool(room_config.get("claim_protection", True)),
                    open_kong_replacement_count=int(room_config.get("open_kong_replacement_count", 2)),
                    initial_hu_si_xi=bool(room_config.get("initial_hu_si_xi", True)),
                    initial_hu_ban_ban_hu=bool(room_config.get("initial_hu_ban_ban_hu", True)),
                    initial_hu_que_yi_se=bool(room_config.get("initial_hu_que_yi_se", True)),
                    initial_hu_liu_liu_shun=bool(room_config.get("initial_hu_liu_liu_shun", True)),
                    initial_hu_san_tong=bool(room_config.get("initial_hu_san_tong", True)),
                    bird_count=int(room_config.get("bird_count", 2)),
                    dealer_bird=bool(room_config.get("dealer_bird", True)),
                )
                hepai_limit = 1
        except Exception as e:
            return Response(type="tips", success=False, message=f"房间配置无效: {e}")

        room_id = self._generate_room_id()
        room_data = {
            "room_id": room_id,
            "room_type": "custom",
            "room_rule": rule,
            "sub_rule": sub_rule,
            "tourist_limit": tourist_limit,
            "allow_spectator": allow_spectator,
            "max_player": 4,
            "player_list": [],
            "player_settings": {},
            "ready_list": [],
            "has_password": has_password,
            "tips": tips,
            "show_moqie_hint": False,
            "host_user_id": 0,
            "host_name": "",
            "is_game_running": False,
            "created_by_admin": created_by,
        }
        if hepai_limit is not None:
            room_data["hepai_limit"] = hepai_limit
        self._apply_event_fields(room_data, event_id)
        room_data.update(validated.dict())
        room_data["is_player_set_random_seed"] = validated.random_seed != 0

        self.rooms[room_id] = room_data
        if has_password:
            self.room_passwords[room_id] = password
        await self._broadcast_room_info(room_id)
        return Response(
            type="room/create_room_done",
            success=True,
            message="空房间创建成功",
            room_info=room_data,
        )

    def list_event_rooms(self, event_id: str) -> list:
        event_id = self._normalize_event_id(event_id)
        if not event_id:
            return []
        items = []
        for room_data in self.rooms.values():
            if room_data.get("event_id") == event_id:
                items.append(room_data)
        return items

    async def admin_destroy_event_room(self, room_id: str, event_id: Optional[str] = None) -> Response:
        if room_id not in self.rooms:
            return Response(type="tips", success=False, message="房间不存在")
        room_data = self.rooms[room_id]
        if room_data.get("room_type") != "events":
            return Response(type="tips", success=False, message="不是比赛场房间")
        if event_id and room_data.get("event_id") != event_id:
            return Response(type="tips", success=False, message="房间不属于该赛事")
        if room_data.get("is_game_running"):
            return Response(type="tips", success=False, message="对局进行中，请先结束对局再删除房间")
        await self.destroy_room(room_id)
        return Response(type="tips", success=True, message="房间已删除")

    async def destroy_room(self, room_id: str):
        """销毁房间并广播离开房间消息给所有玩家"""
        if room_id not in self.rooms:
            logger.warning(f"房间 {room_id} 不存在，无需销毁")
            return
        
        room_data = self.rooms[room_id]
        
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


