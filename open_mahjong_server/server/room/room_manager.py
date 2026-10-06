from ..database.duplicate_walls import apply_duplicate_room_config, load_duplicate_wall
from typing import Dict, Optional
from .room_validators import GBRoomValidator, MMCValidator, RiichiRoomValidator, SichuanRoomValidator, ChangshaRoomValidator, JiandanRoomValidator, TaiwanRoomValidator, FreeRoomValidator, ShanghaiRoomValidator
from .hongkong_room import HongKongRoomValidator, create_hongkong_room
from .guizhou_room import GuizhouRoomValidator, create_guizhou_room
from .hangzhou_room import HangzhouRoomValidator, create_hangzhou_room, enforce_hangzhou_tactical_room
from .yixing_room import YixingRoomValidator, create_yixing_room
from .wenzhou_room import WenzhouRoomValidator, create_wenzhou_room
from .changchun_room import ChangchunRoomValidator
from .hongzhong_room import HongzhongRoomValidator, create_hongzhong_room
from .guangdong_room import GuangdongRoomValidator
from .shanxi_room import ShanxiRoomValidator
from ..response import Response, public_room_data
from .room_seats import get_seats, seat_player, unseat_player
from ..gamestate.public.ai.guobiao_heuristic_gate import guobiao_heuristic_bot_reject_reason
from ..game_calculation.game_calculation_service import Chinese_Hepai_Check
from ..game_calculation.game_calculation_service import Chinese_Tingpai_Check
from ..gamestate.public.player_count import player_count_for_sub_rule
import asyncio
import logging
import uuid

logger = logging.getLogger(__name__)

class RoomManager:
    def __init__(self, game_server):
        # 游戏服务器
        self.game_server = game_server
        # 存储房间信息和房间密码
        self.rooms: Dict[str, dict] = {}
        self.room_passwords: Dict[str, str] = {}
        # All event seating shares one lock, including players waiting in two venues.
        self.event_seating_lock = asyncio.Lock()
        self.event_seating_users: set = set()
        # 已分配给排位匹配对局的房间号集合。匹配对局不依赖房间系统（不进入 self.rooms、
        # 不出现在房间列表、不可被加入），但仍占用一个唯一房间号用于对局内映射与聊天频道，
        # 需在此登记以避免与自定义房间号发生冲突。
        self.match_room_ids: set = set()
        self.active_game_room_ids: Dict[str, str] = {}
        # 房间的合法性验证器
        self.room_validators = {
            "guobiao": GBRoomValidator,
            "changsha": ChangshaRoomValidator,
            "jiandan": JiandanRoomValidator,
            "hongque": JiandanRoomValidator,
            "free": FreeRoomValidator,
            "mmc": MMCValidator,
            "riichi": RiichiRoomValidator,
            "sichuan": SichuanRoomValidator,
            "taiwan": TaiwanRoomValidator,
            "hongkong": HongKongRoomValidator,
            "guizhou": GuizhouRoomValidator,
            "yixing": YixingRoomValidator,
            "wenzhou": WenzhouRoomValidator,
            "hangzhou": HangzhouRoomValidator,
            "hongzhong": HongzhongRoomValidator,
            "changchun": ChangchunRoomValidator,
            "guangdong": GuangdongRoomValidator,
            "shanxi": ShanxiRoomValidator,
            "shanghai": ShanghaiRoomValidator,
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
        if user_id in getattr(self, "event_seating_users", ()):
            return Response(type="tips", success=False, message="正在为您组桌，请稍候")
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
        """校验赛事可建房。user_id 有值时按创建房间权限（所有 / 已报名 / 管理员）校验。"""
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
        if user_id is not None and not self._can_create_venue_room(event, user_id):
            return Response(
                type="tips",
                success=False,
                message=self._venue_create_denied_message(event),
            )
        return None

    def _can_create_venue_room(self, event: dict, user_id: int) -> bool:
        event_id = event.get("event_id")
        role = self.game_server.db_manager.get_event_admin_role(event_id, user_id)
        if role:
            return True
        cfg = self.game_server.db_manager.parse_entry_config(event.get("entry_config"))
        perm = cfg.get("create_room_permission") or "admin"
        if perm == "all":
            return True
        if perm != "registered":
            return False
        registration = self.game_server.db_manager.get_event_registration(event_id, user_id)
        return bool(registration and registration.get("status") == "approved")

    def _venue_create_denied_message(self, event: dict) -> str:
        cfg = self.game_server.db_manager.parse_entry_config(event.get("entry_config"))
        perm = cfg.get("create_room_permission") or "admin"
        if perm == "admin":
            kind = event.get("kind") or "event"
            return "本基地仅限管理员创建房间" if kind == "base" else "本赛事仅限管理员创建房间"
        if perm == "registered":
            return "报名后可以创建房间"
        return "您没有该场馆的建房权限"

    def _can_join_venue_room(self, event_id: Optional[str], user_id: int) -> Optional[str]:
        event_id = self._normalize_event_id(event_id)
        if not event_id:
            return None
        event = self.game_server.db_manager.get_event(event_id)
        if not event:
            return "场馆不存在"
        if event.get("status") != "active":
            return "场馆未开启，无法加入房间"
        role = self.game_server.db_manager.get_event_admin_role(event_id, user_id)
        if role:
            return None
        registration = self.game_server.db_manager.get_event_registration(event_id, user_id)
        if registration and registration.get("status") == "approved":
            return None
        cfg = self.game_server.db_manager.parse_entry_config(event.get("entry_config"))
        # 创建房间权限为「所有」时，未报名玩家也可以进入该场馆的房间。
        if (cfg.get("create_room_permission") or "admin") == "all":
            return None
        return "请先在赛事/基地页报名并通过后再加入房间"

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

    def get_room_list(self, show_tip: bool = False, event_id: Optional[str] = None) -> Response:
        try:
            event_id = self._normalize_event_id(event_id)
            room_list = []
            for _room_id, room_data in self.rooms.items():
                get_seats(room_data)
                self._normalize_host_user_id(room_data)
                room_event_id = room_data.get("event_id")
                is_event_room = room_data.get("room_type") == "events" or bool(room_event_id)
                if event_id:
                    if room_event_id != event_id:
                        continue
                elif is_event_room:
                    continue
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
                           password: str, roundTimerValue: int, stepTimerValue: int, tips: bool, random_seed: int = 0, open_cuohe: bool = False, sub_rule: str = "guobiao/standard", hepai_limit: int = 8, tourist_limit: bool = False, allow_spectator: bool = True, tactical_call: bool = False, claim_protection: bool = True, cuohe_type: int = 0, event_id: Optional[str] = None, count_tips: bool = False, use_flowers: bool = True, pointer_tips: bool = True, tian_di_ren_he: bool = False) -> Response:
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
            
            if sub_rule == "guobiao/blood_battle":
                open_cuohe, tactical_call, hepai_limit = False, False, 8

            if sub_rule == "guobiao/lanshi":
                hepai_limit, open_cuohe, cuohe_type = 5, True, 1

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
                "use_flowers": False if sub_rule == "guobiao/lanshi" else use_flowers,
                "tian_di_ren_he": tian_di_ren_he if sub_rule in ("guobiao/standard", "guobiao/sanma", "guobiao/blood_battle") else False,
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
                "max_player": player_count_for_sub_rule(sub_rule), # 最大玩家数
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
            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
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
                                  tips: bool, random_seed: int = 0, open_cuohe: bool = False, sub_rule: str = "qingque/standard", tourist_limit: bool = False, allow_spectator: bool = True, tactical_call: bool = False, claim_protection: bool = True, event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True) -> Response:
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
            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
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
                                   base_score_no_dealer: bool = False,
                                   small_hu_score: int = 2,
                                   big_hu_score: int = 8,
                                   event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True) -> Response:
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
                "base_score_no_dealer": base_score_no_dealer,
                "small_hu_score": small_hu_score,
                "big_hu_score": big_hu_score,
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

            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
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
        count_tips: bool = False,
        pointer_tips: bool = True,
    ) -> Response:
        """Create a standard Zhongyong or Nanque room; the subrule fixes hand flow."""
        try:
            # The old creation endpoint is only an alias; every new record uses the current family.
            if sub_rule == "jiandan/standard":
                sub_rule = "zhongyong/nanque"
            if sub_rule not in {"zhongyong/standard", "zhongyong/nanque"}:
                return Response(type="tips", success=False, message="不支持的中庸子规则")
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
                "room_rule": "zhongyong",
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

            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
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
            logger.error("创建中庸／南雀房间失败: %s", e, exc_info=True)
            return Response(type="error_message", success=False, message=f"创建房间失败: {str(e)}")

    async def create_Hongque_room(
        self, player_id: str, room_name: str, gameround: int, password: str,
        roundTimerValue: int, stepTimerValue: int, tips: bool,
        random_seed: int = 0, sub_rule: str = "hongque/v1.6",
        tourist_limit: bool = False, allow_spectator: bool = False,
        hepai_way: str = "multi_ron",
        count_tips: bool = False,
        pointer_tips: bool = True,
    ) -> Response:
        """Create a memory-only Hongque prototype room."""
        try:
            if player_id not in self.game_server.players:
                return Response(type="tips", success=False, message="请先登录")
            player = self.game_server.players[player_id]
            if not player.user_id:
                return Response(type="tips", success=False, message="请先登录")
            blocked = self._reject_room_entry_conflicts(player.user_id, "创建房间")
            if blocked:
                return blocked
            settings = self.game_server.db_manager.get_user_settings(player.user_id)
            if not settings:
                return Response(type="tips", success=False, message="获取用户设置失败")
            validated = self.room_validators["hongque"](
                room_name=room_name, game_round=gameround,
                round_timer=roundTimerValue, step_timer=stepTimerValue,
                random_seed=random_seed,
            )
            room_id = self._generate_room_id()
            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "hongque",
                "sub_rule": sub_rule,
                "hepai_limit": 0,
                "open_cuohe": False,
                "tourist_limit": tourist_limit,
                "allow_spectator": False,
                "hepai_way": hepai_way if hepai_way in ("head_bump", "multi_ron") else "multi_ron",
                "max_player": 4,
                "player_list": [player.user_id],
                "player_settings": {player.user_id: {
                    "user_id": player.user_id,
                    "username": settings.get("username", player.username),
                    "title_id": settings.get("title_id", 1),
                    "profile_image_id": settings.get("profile_image_id", 1),
                    "character_id": settings.get("character_id", 1),
                    "voice_id": settings.get("voice_id", 1),
                }},
                "has_password": bool(password),
                "tips": tips,
                "show_moqie_hint": False,
                "host_user_id": player.user_id,
                "host_name": player.username,
                "is_game_running": False,
            }
            room_data.update(validated.dict())
            # Hongque exposes actual hand count to clients (4/8/12/16), unlike
            # wind-based rules whose game_round remains a 1-4 wind count.
            room_data["game_round"] = validated.game_round * 4
            room_data["is_player_set_random_seed"] = validated.random_seed != 0
            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
            self.rooms[room_id] = room_data
            if password:
                self.room_passwords[room_id] = password
            player.current_room_id = room_id
            await self._broadcast_room_info(room_id)
            return Response(type="room/create_room_done", success=True,
                            message="虹雀原型房间创建成功", room_info=room_data)
        except ValueError as exc:
            return Response(type="tips", success=False, message=f"房间配置无效: {exc}")
        except Exception as exc:
            logger.error("创建虹雀原型房间失败: %s", exc, exc_info=True)
            return Response(type="error_message", success=False, message=f"创建房间失败: {exc}")

    async def create_Free_room(
        self, player_id: str, room_name: str, password: str,
        random_seed: int = 0, sub_rule: str = "free/standard",
        tourist_limit: bool = False,
        wall_wan: bool = True, wall_tong: bool = True, wall_suo: bool = True,
        wall_winds: bool = True, wall_dragons: bool = True, wall_flowers: bool = True,
        pointer_tips: bool = True,
    ) -> Response:
        """创建自由模式房间：无观战、无计时、不入库牌谱。"""
        try:
            if player_id not in self.game_server.players:
                return Response(type="tips", success=False, message="请先登录")
            player = self.game_server.players[player_id]
            if not player.user_id:
                return Response(type="tips", success=False, message="请先登录")
            blocked = self._reject_room_entry_conflicts(player.user_id, "创建房间")
            if blocked:
                return blocked
            settings = self.game_server.db_manager.get_user_settings(player.user_id)
            if not settings:
                return Response(type="tips", success=False, message="获取用户设置失败")
            validated = self.room_validators["free"](
                room_name=room_name,
                random_seed=random_seed,
                wall_wan=wall_wan,
                wall_tong=wall_tong,
                wall_suo=wall_suo,
                wall_winds=wall_winds,
                wall_dragons=wall_dragons,
                wall_flowers=wall_flowers,
            )
            room_id = self._generate_room_id()
            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "free",
                "sub_rule": sub_rule or "free/standard",
                "hepai_limit": 0,
                "open_cuohe": False,
                "tourist_limit": tourist_limit,
                "allow_spectator": False,
                "min_players_to_start": 1,
                "max_player": 4,
                "player_list": [player.user_id],
                "player_settings": {player.user_id: {
                    "user_id": player.user_id,
                    "username": settings.get("username", player.username),
                    "title_id": settings.get("title_id", 1),
                    "profile_image_id": settings.get("profile_image_id", 1),
                    "character_id": settings.get("character_id", 1),
                    "voice_id": settings.get("voice_id", 1),
                }},
                "has_password": bool(password),
                "tips": False,
                "show_moqie_hint": False,
                "host_user_id": player.user_id,
                "host_name": player.username,
                "is_game_running": False,
                "game_round": 1,
                "round_timer": 0,
                "step_timer": 0,
            }
            room_data.update(validated.dict())
            room_data["min_players_to_start"] = 1
            room_data["is_player_set_random_seed"] = validated.random_seed != 0
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
            self.rooms[room_id] = room_data
            if password:
                self.room_passwords[room_id] = password
            player.current_room_id = room_id
            await self._broadcast_room_info(room_id)
            return Response(type="room/create_room_done", success=True,
                            message="自由模式房间创建成功", room_info=room_data)
        except ValueError as exc:
            return Response(type="tips", success=False, message=f"房间配置无效: {exc}")
        except Exception as exc:
            logger.error("创建自由模式房间失败: %s", exc, exc_info=True)
            return Response(type="error_message", success=False, message=f"创建房间失败: {exc}")

    async def create_Classical_room(self, player_id: str, room_name: str, gameround: int,
                                    password: str, roundTimerValue: int, stepTimerValue: int,
                                    tips: bool, random_seed: int = 0, sub_rule: str = "classical/standard", tourist_limit: bool = False, allow_spectator: bool = True, event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True) -> Response:
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

            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
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

    async def create_Shanghai_room(self, player_id: str, room_name: str, gameround: int,
                                    password: str, roundTimerValue: int, stepTimerValue: int,
                                    tips: bool, random_seed: int = 0, sub_rule: str = "shanghai/qiaoma", tourist_limit: bool = False, allow_spectator: bool = True, event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True, hepai_limit: int = 0) -> Response:
        """创建上海敲麻麻将房间"""
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
                "sub_rule": sub_rule,
                "hepai_limit": hepai_limit,
            }

            try:
                validator_class = self.room_validators["shanghai"]
                validated_config = validator_class(**room_config)
            except ValueError as e:
                return Response(type="tips", success=False, message=f"房间配置无效: {str(e)}")

            room_id = self._generate_room_id()

            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "shanghai",
                "sub_rule": sub_rule,
                "hepai_limit": validated_config.hepai_limit,
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

            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
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
                                  tactical_call: bool = False, blood_battle: bool = True, claim_protection: bool = True, event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True, hepai_limit: int = 0) -> Response:
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
                "sub_rule": sub_rule,
                "tactical_call": tactical_call,
                # 血流有自己的“和后留桌直到牌墙结束”状态机，
                # 不接受标准四川血战开关覆盖。
                "blood_battle": False if sub_rule in ("sichuan/xueliu", "sichuan/xueliu_exchange") else blood_battle,
                "hepai_limit": hepai_limit,
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
                "hepai_limit": validated_config.hepai_limit,
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

            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
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

    async def create_Hangzhou_room(self, player_id, **config):
        return await create_hangzhou_room(self, player_id, **config)

    async def create_Hongzhong_room(self, player_id, **config):
        return await create_hongzhong_room(self, player_id, **config)

    async def create_Wenzhou_room(self, player_id, **config):
        return await create_wenzhou_room(self, player_id, **config)

    async def create_Yixing_room(self, player_id, **config):
        return await create_yixing_room(self, player_id, **config)

    async def create_Guizhou_room(self, player_id, **config):
        return await create_guizhou_room(self, player_id, **config)

    async def create_HongKong_room(self, player_id, **config):
        return await create_hongkong_room(self, player_id, **config)

    async def create_Taiwan_room(self, player_id: str, room_name: str, gameround: int,
                                  password: str, roundTimerValue: int, stepTimerValue: int,
                                  tips: bool, random_seed: int = 0,
                                  sub_rule: str = "taiwan/standard",
                                  tourist_limit: bool = False,
                                  allow_spectator: bool = True,
                                  open_cuohe: bool = False,
                                  cuohe_type: int = 0,
                                  detailed_config: Optional[dict] = None,
                                  event_id: Optional[str] = None, count_tips: bool = False, pointer_tips: bool = True) -> Response:
        """创建台湾麻将标准规则房间。"""
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

            cuohe_type = 0 if cuohe_type not in (0, 1) else cuohe_type
            room_config = {
                "room_name": room_name,
                "game_round": gameround,
                "round_timer": roundTimerValue,
                "step_timer": stepTimerValue,
                "random_seed": random_seed,
                "sub_rule": sub_rule or "taiwan/standard",
                "tips": tips,
                "open_cuohe": open_cuohe,
                "cuohe_type": cuohe_type,
                "detailed_config": detailed_config,
            }
            try:
                validated_config = self.room_validators["taiwan"](**room_config)
            except ValueError as e:
                return Response(type="tips", success=False, message=f"房间配置无效: {str(e)}")

            room_id = self._generate_room_id()
            has_password = password != ""
            room_data = {
                "room_id": room_id,
                "room_type": "custom",
                "room_rule": "taiwan",
                "sub_rule": validated_config.sub_rule,
                "hepai_limit": validated_config.detailed_config["minimum_tai"],
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
                        "voice_id": host_settings.get('voice_id', 1),
                    }
                },
                "has_password": has_password,
                "tips": validated_config.tips,
                "show_moqie_hint": False,
                "open_cuohe": validated_config.open_cuohe,
                "cuohe_type": validated_config.cuohe_type,
                "tactical_call": False,
                "claim_protection": False,
                "host_user_id": host_user_id,
                "host_name": host_name,
                "is_game_running": False,
            }
            self._apply_event_fields(room_data, event_id)
            room_data.update(validated_config.dict())
            room_data["is_player_set_random_seed"] = validated_config.random_seed != 0

            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
            self.rooms[room_id] = room_data
            if has_password:
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
                                 event_id: Optional[str] = None, count_tips: bool = False, starting_score: Optional[int] = None, pointer_tips: bool = True, detailed_config: Optional[dict] = None, claim_protection: bool = False) -> Response:
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
                "claim_protection": claim_protection,
                "game_round": gameround,
                "round_timer": roundTimerValue,
                "step_timer": stepTimerValue,
                "random_seed": random_seed,
                "open_cuohe": open_cuohe,
                "hepai_limit": hepai_limit,
                "red_dora": red_dora,
                "starting_score": starting_score if starting_score is not None else (35000 if sub_rule == "riichi/sanma" else 50000 if sub_rule == "riichi/langyong" else 25000),
                "sub_rule": sub_rule,
                "detailed_config": detailed_config,
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
                "max_player": 3 if sub_rule == "riichi/sanma" else 4,
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

            room_data["count_tips"] = count_tips
            apply_duplicate_room_config(room_data)
            room_data["pointer_tips"] = bool(pointer_tips)
            room_data.setdefault("instance_id", str(uuid.uuid4()))
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

            venue_blocked = self._can_join_venue_room(room_data.get("event_id"), player.user_id)
            if venue_blocked:
                return Response(
                    type="error_message",
                    success=False,
                    message=venue_blocked
                )
            
            # 更新房间信息
            seat_player(room_data, player.user_id)
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
            if room_data.get("event_seating_pending"):
                room_data.setdefault("event_seating_withdrawn", []).append(player.user_id)
            unseat_player(room_data, player.user_id)
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
                        type="room/leave_room_done", room_id=room_id, room_instance_id=room_data.get("instance_id"),
                        success=True,
                        message="离开房间成功"
                    )
                await self.destroy_room(room_id)
                return Response(
                    type="room/leave_room_done", room_id=room_id, room_instance_id=room_data.get("instance_id"),
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
                        type="room/leave_room_done", room_id=room_id, room_instance_id=room_data.get("instance_id"),
                        success=True,
                        message="离开房间成功"
                    )
                await self.destroy_room(room_id)
                return Response(
                    type="room/leave_room_done", room_id=room_id, room_instance_id=room_data.get("instance_id"),
                    success=True,
                    message="房间已解散（仅剩机器人）"
                )

            self._sync_room_host(room_data)
            
            # 广播房间信息
            await self._broadcast_room_info(room_id)
            return Response(
                type="room/leave_room_done", room_id=room_id, room_instance_id=room_data.get("instance_id"),
                success=True,
                    message="离开房间成功"
                )

        except Exception as e:
            return Response(
                type="error_message",
                success=False,
                message=f"离开房间失败: {str(e)}"
            )

    async def add_bot_to_room(self, Connect_id: str, room_id: str, seat_index=None) -> Response:
        return await self._add_room_bot(Connect_id, room_id, 0, seat_index)

    async def add_smart_bot_to_room(self, Connect_id: str, room_id: str, seat_index=None) -> Response:
        return await self._add_room_bot(Connect_id, room_id, 2, seat_index)

    async def add_guobiao_heuristic_bot_to_room(self, Connect_id: str, room_id: str, seat_index=None) -> Response:
        return await self._add_room_bot(Connect_id, room_id, 3, seat_index)

    async def _add_room_bot(self, Connect_id: str, room_id: str, bot_user_id: int, seat_index) -> Response:
        room = self.rooms.get(room_id)
        if room is None:
            return Response(type="error_message", success=False, message="房间不存在")
        self._sync_room_host(room)
        player = self.game_server.players.get(Connect_id)
        if (not player or player.user_id != room.get("host_user_id")
                or player.current_room_id != room_id):
            return Response(type="error_message", success=False, message="只有房主可以添加机器人")
        if room.get("is_game_running", False):
            return Response(type="error_message", success=False, message="游戏正在进行中，无法添加机器人")
        if room.get("room_rule") == "free":
            return Response(type="error_message", success=False, message="自由模式不支持添加机器人")
        if bot_user_id == 3:
            reject = guobiao_heuristic_bot_reject_reason(room)
            if reject:
                return Response(type="error_message", success=False, message=reject)
        try:
            seat_player(room, bot_user_id, seat_index)
        except ValueError as error:
            return Response(type="error_message", success=False, message=str(error))

        enforce_hangzhou_tactical_room(room)
        username = {0: "麻雀罗伯特", 2: "牌效罗伯特", 3: "高性能罗伯特"}[bot_user_id]
        room.setdefault("player_settings", {})[bot_user_id] = {
            "user_id": bot_user_id, "username": username,
            "title_id": 1, "profile_image_id": 1, "character_id": 1, "voice_id": 1,
        }
        await self._broadcast_room_info(room_id)
        return Response(type="tips", success=True, message=f"{username}已添加到房间")

    async def kick_player_from_room(self, Connect_id: str, room_id: str, target_user_id: int, seat_index=None) -> Response:
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
            self._sync_room_host(room_data)
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
            try:
                unseat_player(room_data, target_user_id, seat_index)
            except ValueError as error:
                return Response(type="tips", success=False, message=str(error))
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
                    type="room/leave_room_done", room_id=room_id, room_instance_id=room_data.get("instance_id"),
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

    async def set_bot_speed(self, Connect_id: str, room_id: str, speed) -> Optional[Response]:
        from ..gamestate.public.ai.pacing import BOT_SPEEDS

        room = self.rooms.get(room_id)
        player = self.game_server.players.get(Connect_id)
        if room is None:
            return Response(type="error_message", success=False, message="房间不存在")
        self._sync_room_host(room)
        if (not player or not room.get("player_list")
                or player.user_id != room.get("host_user_id")
                or player.current_room_id != room_id):
            return Response(type="error_message", success=False, message="只有房主可以调整机器人速度")
        if room.get("is_game_running", False):
            return Response(type="error_message", success=False, message="请在开局前调整机器人速度")
        if speed == "standard":
            speed = "fast"
        if not isinstance(speed, str) or speed not in BOT_SPEEDS:
            return Response(type="error_message", success=False, message="机器人速度无效")
        room["bot_speed"] = speed
        await self._broadcast_room_info(room_id)
        return None

    async def set_claim_protection(self, Connect_id: str, room_id: str, enabled) -> Optional[Response]:
        from ..gamestate.public.claim_protection import supports_claim_protection
        room = self.rooms.get(room_id)
        player = self.game_server.players.get(Connect_id)
        if room is None:
            return Response(type="error_message", success=False, message="房间不存在")
        self._sync_room_host(room)
        if (not player or not room.get("player_list") or player.user_id != room.get("host_user_id")
                or player.current_room_id != room_id):
            return Response(type="error_message", success=False, message="只有房主可以调整鸣牌保护")
        if room.get("is_game_running", False):
            return Response(type="error_message", success=False, message="请在开局前调整鸣牌保护")
        if not supports_claim_protection(room.get("room_rule")):
            return Response(type="error_message", success=False, message="此规则不支持鸣牌保护")
        if type(enabled) is not bool:
            return Response(type="error_message", success=False, message="鸣牌保护开关无效")
        room["claim_protection"] = enabled
        await self._broadcast_room_info(room_id)
        return None

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
            self._sync_room_host(room_data)
            if player.user_id == room_data.get("host_user_id"):
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
        self._sync_room_host(room_data)
        for user_id in player_list:
            if user_id == room_data.get("host_user_id"):
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
            if rid not in self.rooms and rid not in self.match_room_ids and rid not in self.active_game_room_ids.values():
                return rid
        raise ValueError("无法创建更多房间")

    def allocate_match_room_id(self) -> str:
        """为排位匹配对局分配一个唯一房间号（不写入 self.rooms，仅登记到 match_room_ids）。"""
        for i in range(1, 9999):
            rid = str(i)
            if rid not in self.rooms and rid not in self.match_room_ids and rid not in self.active_game_room_ids.values():
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

        seats = get_seats(room_data)
        room_data["seat_list"] = [user_id if user_id > 10 else -1 for user_id in seats]
        room_data["player_list"] = [user_id for user_id in player_list if user_id > 10]

        ready_list = room_data.get("ready_list", [])
        room_data["ready_list"] = [user_id for user_id in ready_list if user_id > 10]

        if "player_settings" in room_data:
            for bot_id in list(room_data["player_settings"].keys()):
                if bot_id <= 10 and bot_id not in room_data["player_list"]:
                    del room_data["player_settings"][bot_id]

    def _sync_room_host(self, room_data: dict):
        """按入房顺序选择仍在房内的第一位真人，座位位置不影响房主。"""
        if room_data.get("room_rule") == "guizhou":
            from .guizhou_room import apply_bot_tactical_policy
            apply_bot_tactical_policy(room_data)
        player_list = room_data.get("player_list") or []
        host_user_id = next((user_id for user_id in player_list if user_id > 10), 0)
        room_data["host_user_id"] = host_user_id
        host_settings = room_data.get("player_settings", {}).get(host_user_id, {})
        room_data["host_name"] = host_settings.get("username", f"用户{host_user_id}") if host_user_id else ""

    async def finish_custom_game_room(self, room_id: str, *, expected_gamestate_id: str, expected_instance_id=None):
        """Release only the lobby association owned by this exact game."""
        room = self.rooms.get(room_id)
        if (room is None or room.get("active_gamestate_id") != expected_gamestate_id
                or room.get("instance_id") != expected_instance_id):
            return
        room.pop("active_gamestate_id", None)
        room["is_game_running"] = False
        room["ready_list"] = []
        self._sync_room_host(room)
        await self._broadcast_room_info(room_id)

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
                from .tuidao_room import enforce_tuidao_tactical_room
                enforce_tuidao_tactical_room(room_data)
                player.current_room_id = room_id
                room_data.setdefault("ready_list", [])
                get_seats(room_data)
                self._sync_room_host(room_data)
                from .guangdong_room import enforce_guangdong_tactical
                enforce_guangdong_tactical(room_data)
                enforce_hangzhou_tactical_room(room_data)
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
        from .changchun_room import enforce_changchun_tactical_room
        enforce_changchun_tactical_room(room_data)
        enforce_hangzhou_tactical_room(room_data)
        from .guangdong_room import enforce_guangdong_tactical
        enforce_guangdong_tactical(room_data)
        from .tuidao_room import enforce_tuidao_tactical_room
        enforce_tuidao_tactical_room(room_data)
        from ..gamestate.public.ai.pacing import normalize_bot_speed
        room_data["bot_speed"] = normalize_bot_speed(room_data.get("bot_speed"))
        # 确保准备列表存在，使客户端始终能收到该字段
        room_data.setdefault("ready_list", [])
        get_seats(room_data)
        self._sync_room_host(room_data)
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
                                      password: str = "", created_by: Optional[int] = None,
                                      broadcast: bool = True) -> Response:
        """管理端创建比赛场空房间（无房主，第一个加入的真人成为房主）。"""
        event_id = self._normalize_event_id(event_id)
        event_blocked = self._validate_event_for_room(event_id, None)
        if event_blocked:
            return event_blocked

        rule = (room_rule or "").strip()
        if room_config.get("duplicate_key") and room_config.get("sub_rule") == "guobiao/blood_battle":
            raise ValueError("国标血战暂不支持复式牌墙")
        if room_config.get("duplicate_key") and rule != "guobiao":
            return Response(type="tips", success=False, message="复式牌墙仅支持国标麻将（含蓝十改）")
        rule_defaults = {
            "guobiao": ("guobiao/standard", "guobiao"),
            "qingque": ("qingque/standard", "guobiao"),
            "classical": ("classical/standard", "guobiao"),
            "riichi": ("riichi/standard", "riichi"),
            "sichuan": ("sichuan/standard", "sichuan"),
            "changsha": ("changsha/classic_double_bird", "changsha"),
            "taiwan": ("taiwan/standard", "taiwan"),
            "hongkong": ("hongkong/qingzhang", "hongkong"),
            "guizhou": ("guizhou/standard", "guizhou"),
            "yixing": ("yixing/standard", "yixing"),
            "wenzhou": ("wenzhou/mil2024", "wenzhou"),
            "hangzhou": ("hangzhou/mil2025", "hangzhou"),
            "hongzhong": ("hongzhong/mil2024", "hongzhong"),
            "changchun": ("changchun/mil2024", "changchun"),
            "guangdong": ("guangdong/tuidao_mil2024", "guangdong"),
            "shanxi": ("shanxi/mil2023", "shanxi"),
        }
        if rule not in rule_defaults:
            return Response(type="tips", success=False, message=f"不支持的规则: {rule}")

        default_sub, _validator_key = rule_defaults[rule]
        sub_rule = room_config.get("sub_rule") or default_sub
        tips = bool(room_config.get("tips", False))
        tourist_limit = bool(room_config.get("tourist_limit", False))
        allow_spectator = bool(room_config.get("allow_spectator", True))
        has_password = bool(password)

        try:
            base_config = {
                "room_name": room_config.get("room_name") or f"赛事房间-{event_id[-6:]}",
                "game_round": int(room_config.get("game_round", 4)),
                "round_timer": int(room_config.get("round_timer", 20)),
                "step_timer": int(room_config.get("step_timer", 5)),
                "random_seed": room_config.get("random_seed", 0) if rule == "guangdong" else int(room_config.get("random_seed", 0) or 0),
            }
        except (TypeError, ValueError) as error:
            return Response(type="tips", success=False, message=f"房间配置无效: {error}")
        try:
            if rule == "guobiao":
                validated = self.room_validators["guobiao"](
                    **base_config,
                    use_flowers=False if sub_rule == "guobiao/lanshi" else room_config.get("use_flowers", True),
                    tian_di_ren_he=room_config.get("tian_di_ren_he", False) if sub_rule in ("guobiao/standard", "guobiao/sanma", "guobiao/blood_battle") else False,
                    open_cuohe=True if sub_rule == "guobiao/lanshi" else bool(room_config.get("open_cuohe", False)),
                    cuohe_type=1 if sub_rule == "guobiao/lanshi" else int(room_config.get("cuohe_type", 0) or 0),
                    tactical_call=bool(room_config.get("tactical_call", False)),
                    claim_protection=room_config.get("claim_protection", True),
                )
                hepai_limit = 5 if sub_rule == "guobiao/lanshi" else max(1, min(64, int(room_config.get("hepai_limit", 8))))
            elif rule == "qingque":
                validated = self.room_validators["guobiao"](
                    **base_config,
                    open_cuohe=False,
                    tactical_call=bool(room_config.get("tactical_call", False)),
                    claim_protection=room_config.get("claim_protection", True),
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
                    claim_protection=room_config.get("claim_protection", False),
                    **base_config,
                    sub_rule=sub_rule,
                    detailed_config=room_config.get("detailed_config"),
                    open_cuohe=bool(room_config.get("open_cuohe", False)),
                    hepai_limit=max(1, min(64, int(room_config.get("hepai_limit", 1)))),
                    red_dora=bool(room_config.get("red_dora", True)),
                    starting_score=room_config.get("starting_score", 35000 if sub_rule == "riichi/sanma" else 50000 if sub_rule == "riichi/langyong" else 25000),
                    allow_kuikae=bool(room_config.get("allow_kuikae", False)),
                    open_xiru=bool(room_config.get("open_xiru", True)),
                    open_tobi=bool(room_config.get("open_tobi", True)),
                    hepai_way=room_config.get("hepai_way") or "multi_ron",
                )
                hepai_limit = None
            elif rule == "sichuan":
                validated = self.room_validators["sichuan"](
                    **base_config,
                    sub_rule=sub_rule,
                    hepai_limit=room_config.get("hepai_limit", 0),
                    tactical_call=bool(room_config.get("tactical_call", False)),
                    blood_battle=bool(room_config.get("blood_battle", True)),
                    claim_protection=room_config.get("claim_protection", True),
                )
                hepai_limit = validated.hepai_limit
            elif rule == "shanxi":
                validated = self.room_validators["shanxi"](
                    **base_config, sub_rule=sub_rule, tips=room_config.get("tips", False),
                    detailed_config=room_config.get("detailed_config"),
                    **{key: room_config[key] for key in ("use_flowers", "open_cuohe", "tactical_call", "claim_protection", "tian_di_ren_he", "allow_spectator", "tourist_limit", "count_tips", "pointer_tips") if key in room_config},
                )
                hepai_limit = 0
            elif rule == "guangdong":
                validated = self.room_validators["guangdong"](
                    **{**base_config, **{key: room_config[key] for key in ("game_round", "round_timer", "step_timer") if key in room_config}},
                    sub_rule=sub_rule, tips=room_config.get("tips", False),
                    detailed_config=room_config.get("detailed_config"),
                    **{key: room_config[key] for key in ("use_flowers", "tian_di_ren_he", "open_cuohe", "tactical_call", "claim_protection", "allow_spectator", "tourist_limit", "count_tips", "pointer_tips", "hepai_limit") if key in room_config},
                )
                hepai_limit = 0
            elif rule in ("guizhou", "yixing", "wenzhou", "hongzhong", "hangzhou", "changchun"):
                validated = self.room_validators[rule](
                    **{**base_config, **{key: room_config[key] for key in ("game_round", "round_timer", "step_timer") if key in room_config}},
                    sub_rule=sub_rule, tips=room_config.get("tips", True),
                    detailed_config=room_config.get("detailed_config"),
                    **({"hepai_limit": room_config["hepai_limit"]} if rule == "hongzhong" and "hepai_limit" in room_config else {}),
                    **{key: room_config[key] for key in ("use_flowers", "open_cuohe", "tactical_call", "claim_protection", "tian_di_ren_he", "allow_spectator", "tourist_limit", "count_tips", "pointer_tips") if key in room_config},
                )
                hepai_limit = 0
            elif rule == "hongkong":
                validated = self.room_validators["hongkong"](
                    **base_config, sub_rule=sub_rule, tips=tips,
                    detailed_config=room_config.get("detailed_config"),
                )
                from ..game_calculation.hongkong.models import HongKongRules
                hepai_limit = HongKongRules.from_room(validated.model_dump()).minimum_fan
            elif rule == "taiwan":
                validated = self.room_validators["taiwan"](
                    **base_config,
                    sub_rule=sub_rule,
                    tips=tips,
                    open_cuohe=bool(room_config.get("open_cuohe", False)),
                    cuohe_type=int(room_config.get("cuohe_type", 0) or 0),
                    detailed_config=room_config.get("detailed_config"),
                )
                hepai_limit = validated.detailed_config["minimum_tai"]
            else:
                validated = self.room_validators["changsha"](
                    **base_config,
                    open_cuohe=False,
                    tactical_call=bool(room_config.get("tactical_call", False)),
                    claim_protection=room_config.get("claim_protection", True),
                    open_kong_replacement_count=int(room_config.get("open_kong_replacement_count", 2)),
                    initial_hu_si_xi=bool(room_config.get("initial_hu_si_xi", True)),
                    initial_hu_ban_ban_hu=bool(room_config.get("initial_hu_ban_ban_hu", True)),
                    initial_hu_que_yi_se=bool(room_config.get("initial_hu_que_yi_se", True)),
                    initial_hu_liu_liu_shun=bool(room_config.get("initial_hu_liu_liu_shun", True)),
                    initial_hu_san_tong=bool(room_config.get("initial_hu_san_tong", True)),
                    bird_count=int(room_config.get("bird_count", 2)),
                    dealer_bird=bool(room_config.get("dealer_bird", True)),
                    base_score_no_dealer=bool(room_config.get("base_score_no_dealer", False)),
                    small_hu_score=int(room_config.get("small_hu_score", 2)),
                    big_hu_score=int(room_config.get("big_hu_score", 8)),
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
            "max_player": player_count_for_sub_rule(sub_rule),
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

        try:
            wall = load_duplicate_wall(self.game_server.db_manager, room_config["duplicate_key"]) if room_config.get("duplicate_key") else None
            apply_duplicate_room_config(room_data, wall)
        except ValueError as error:
            return Response(type="tips", success=False, message=str(error))
        self.rooms[room_id] = room_data
        if has_password:
            self.room_passwords[room_id] = password
        if broadcast:
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
                items.append(public_room_data(room_data))
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
        if room_data.get("event_seating_pending"):
            return Response(type="tips", success=False, message="自动匹配正在组桌，请稍候")
        await self.destroy_room(room_id)
        return Response(type="tips", success=True, message="房间已删除")

    def _fill_player_settings(self, room_data: dict, user_id: int, fallback_username: str = "") -> None:
        if "player_settings" not in room_data:
            room_data["player_settings"] = {}
        player_settings = self.game_server.db_manager.get_user_settings(user_id)
        if player_settings:
            room_data["player_settings"][user_id] = {
                "user_id": user_id,
                "username": player_settings.get("username", fallback_username or f"用户{user_id}"),
                "title_id": player_settings.get("title_id", 1),
                "profile_image_id": player_settings.get("profile_image_id", 1),
                "character_id": player_settings.get("character_id", 1),
                "voice_id": player_settings.get("voice_id", 1),
            }
        else:
            room_data["player_settings"][user_id] = {
                "user_id": user_id,
                "username": fallback_username or f"用户{user_id}",
                "title_id": 1,
                "profile_image_id": 1,
                "character_id": 1,
                "voice_id": 1,
            }

    async def seat_event_table(
        self,
        admin_user_id: int,
        event_id: str,
        user_ids: list,
        room_rule: str = "guobiao",
        room_config: Optional[dict] = None,
    ) -> Response:
        async with self.event_seating_lock:
            return await self._seat_event_table_locked(
                admin_user_id, event_id, user_ids, room_rule, room_config or {}, automatic=False,
            )

    def _event_player_connection(self, user_id: int):
        conn = self.game_server.user_id_to_connection.get(user_id)
        if not conn or getattr(conn, "event_disconnecting", False):
            return None
        if self.game_server.players.get(getattr(conn, "Connect_id", None)) is not conn:
            return None
        state = getattr(getattr(conn, "websocket", None), "client_state", None)
        if getattr(state, "name", "") == "DISCONNECTED":
            return None
        return conn

    def event_auto_player_eligible(self, event: dict, user_id: int) -> bool:
        if not event or event.get("status") != "active":
            return False
        conn = self._event_player_connection(user_id)
        if not conn or getattr(conn, "current_room_id", None):
            return False
        if self._reject_room_entry_conflicts(user_id, "自动组桌"):
            return False
        if any(user_id in room.get("player_list", []) for room in self.rooms.values()):
            return False
        return self._event_auto_player_allowed(event, user_id, conn)

    def _event_auto_player_allowed(self, event, user_id, conn) -> bool:
        db = self.game_server.db_manager
        cfg = db.parse_entry_config(event.get("entry_config"))
        from ..event.auto_match import event_auto_match_config
        room_config = event_auto_match_config(event).get("room_config") or {}
        if not isinstance(room_config, dict):
            return False
        if (cfg.get("forbid_tourist") or room_config.get("tourist_limit")) and getattr(conn, "is_tourist", False):
            return False
        if cfg.get("unregistered_can_ready") or db.get_event_admin_role(event["event_id"], user_id):
            return True
        registration = db.get_event_registration(event["event_id"], user_id)
        return bool(registration and registration.get("status") == "approved")

    async def match_event_ready_players(self, event_id: str) -> Optional[Response]:
        from ..event.auto_match import event_auto_match_config
        from ..game_calculation.riichi.sanma import player_count
        async with self.event_seating_lock:
            event = self.game_server.db_manager.get_event(event_id)
            cfg = event_auto_match_config(event)
            if not event or event.get("status") != "active" or cfg.get("enabled") is not True:
                return None
            count = player_count((cfg.get("room_config") or {}).get("sub_rule")) if cfg.get("room_rule") == "riichi" else 4
            ids = []
            for row in self.game_server.db_manager.list_event_ready_players(event_id):
                uid = int(row["user_id"])
                if uid not in ids and self.event_auto_player_eligible(event, uid):
                    ids.append(uid)
                if len(ids) == count:
                    break
            if len(ids) < count:
                return None
            if not isinstance(cfg.get("room_config"), dict) or not cfg.get("room_rule"):
                return Response(type="event/seat_table", success=False, message="请先保存自动匹配的对局设置")
            return await self._seat_event_table_locked(
                cfg.get("updated_by"), event_id, ids, cfg["room_rule"], cfg["room_config"], automatic=True,
            )

    async def _seat_event_table_locked(
        self, admin_user_id, event_id, user_ids, room_rule, room_config, *, automatic,
    ) -> Response:
        from ..game_calculation.riichi.sanma import player_count
        event_id = self._normalize_event_id(event_id)
        if not event_id:
            return Response(type="event/seat_table", success=False, message="场馆无效")
        if not automatic and not self.game_server.db_manager.get_event_admin_role(event_id, admin_user_id):
            return Response(type="event/seat_table", success=False, message="没有管理权限")
        ids = []
        for uid in user_ids or []:
            try:
                parsed = int(uid)
            except (TypeError, ValueError):
                return Response(type="event/seat_table", success=False, message="玩家 ID 无效")
            if parsed in ids:
                return Response(type="event/seat_table", success=False, message="不能重复选择同一玩家")
            ids.append(parsed)
        count = player_count((room_config or {}).get("sub_rule")) if room_rule == "riichi" else 4
        if len(ids) != count:
            return Response(type="event/seat_table", success=False, message=f"请恰好选择 {count} 名准备中的玩家")

        ready_rows = self.game_server.db_manager.list_event_ready_players(event_id)
        ready_ids = {int(row["user_id"]) for row in ready_rows}
        missing = [uid for uid in ids if uid not in ready_ids]
        if missing:
            return Response(type="event/seat_table", success=False, message="所选玩家不都在准备池中")

        for uid in ids:
            blocked = self._reject_room_entry_conflicts(uid, "组桌")
            if blocked:
                return Response(type="event/seat_table", success=False, message=f"玩家 {uid} 无法组桌：{blocked.message}")
            conn = self.game_server.user_id_to_connection.get(uid)
            if (conn and getattr(conn, "current_room_id", None)) or any(uid in room.get("player_list", []) for room in self.rooms.values()):
                return Response(type="event/seat_table", success=False, message=f"玩家 {uid} 已在其他房间")

        claimed = []
        room_id = None
        room_data = None
        committed = False
        self.event_seating_users.update(ids)
        try:
            # Reserve every seat before yielding to networking.
            created = await self.create_empty_event_room(
                event_id=event_id, room_rule=room_rule or "guobiao", room_config=room_config or {},
                created_by=admin_user_id, broadcast=False,
            )
            if not created.success or not created.room_info:
                return Response(type="event/seat_table", success=False, message=created.message or "创建房间失败")
            room_id = created.room_info["room_id"]
            room_data = self.rooms[room_id]
            claimed = self.game_server.db_manager.claim_event_ready_players(event_id, ids, automatic=automatic)
            if len(claimed) != count:
                raise ValueError("准备状态或场馆设置已变化，请重新组桌")
            for uid in ids:
                seat_player(room_data, uid)
                conn = self.game_server.user_id_to_connection.get(uid)
                self._fill_player_settings(room_data, uid, conn.username if conn else "")
                if conn:
                    conn.current_room_id = room_id
            self._sync_room_host(room_data)
            if automatic:
                room_data["event_seating_pending"] = True
                room_data["ready_list"] = ids.copy()
                for uid in ids:
                    await self.game_server.gamestate_manager.remove_spectator_from_all_games(uid)
                    await self.game_server.friend_manager.leave_spectating_for_event(uid)
                # Spectator cleanup can yield; do not force a departed or newly busy player in.
                match_manager = self.game_server.match_manager
                if room_data["player_list"] != ids or any(
                    not self._event_player_connection(uid)
                    or self._event_player_connection(uid).current_room_id != room_id
                    or self.game_server.gamestate_manager.is_user_in_active_game(uid)
                    or match_manager.is_user_in_queue(uid)
                    or match_manager.is_user_committed(uid)
                    for uid in ids
                ):
                    raise ValueError("有玩家的状态已变化，等待其他玩家后重新匹配")
            seated = Response(
                type="event/seated", success=True,
                message="自动匹配成功，即将开始对局" if automatic else "管理员已为您组桌", room_info=room_data,
            )
            # The Unity client must receive event/seated before game_start.
            for uid in ids:
                conn = self._event_player_connection(uid)
                if not conn:
                    if automatic:
                        raise ValueError("有玩家已离线，等待其他玩家后重新匹配")
                    continue
                try:
                    await asyncio.wait_for(conn.websocket.send_json(seated.model_dump(mode="json", exclude_none=True)), timeout=3)
                except Exception as exc:
                    if automatic:
                        raise ValueError("组桌通知未送达，已保留其他玩家的等待状态") from exc
                    logger.warning("组桌通知玩家 %s 失败: %s", uid, exc)

            event = self.game_server.db_manager.get_event(event_id)
            if not event or event.get("status") != "active":
                raise ValueError("场馆已关闭，已停止组桌")
            if automatic:
                from ..event.auto_match import event_auto_match_config
                cfg = event_auto_match_config(event)
                if cfg.get("enabled") is not True or cfg.get("room_rule") != room_rule or cfg.get("room_config") != room_config:
                    raise ValueError("自动匹配设置已变化，已停止本次组桌")
                if room_data["player_list"] != ids or any(
                    not self._event_player_connection(uid)
                    or self._event_player_connection(uid).current_room_id != room_id
                    or not self._event_auto_player_allowed(event, uid, self._event_player_connection(uid))
                    for uid in ids
                ):
                    raise ValueError("有玩家已离开，等待其他玩家后重新匹配")
                # start_game installs all active-game indexes before its first yield.
                host = self._event_player_connection(ids[0])
                response = await self.game_server.gamestate_manager.start_game(host.Connect_id, room_id, event_auto_start=True)
                if response is not None and not response.success:
                    raise ValueError(response.message or "自动开局失败")
                if not room_data.get("is_game_running"):
                    raise ValueError("自动开局失败")
            committed = True
            for uid in ids:
                try:
                    self.game_server.db_manager.clear_user_event_ready(uid)
                except Exception:
                    logger.exception("组桌成功后清理玩家 %s 的其他等待记录失败", uid)
            if not automatic:
                await self._broadcast_room_info(room_id)
            return Response(type="event/seat_table", success=True, message="自动匹配成功，对局已开始" if automatic else "组桌成功", room_info=room_data)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            logger.warning("场馆 %s 组桌失败: %s", event_id, exc, exc_info=not isinstance(exc, ValueError))
            return Response(type="event/seat_table", success=False, message=str(exc) if isinstance(exc, ValueError) else "组桌失败，准备状态已保留")
        finally:
            if committed and self.rooms.get(room_id) is room_data and room_data is not None:
                room_data.pop("event_seating_pending", None)
            try:
                if not committed:
                    await self._rollback_event_seating(room_id, claimed, automatic=automatic, original_room=room_data)
            finally:
                self.event_seating_users.difference_update(ids)

    async def _rollback_event_seating(self, room_id, claimed, *, automatic, original_room):
        """Remove an unstarted table and restore the original queue positions."""
        withdrawn = set()
        notify = []
        room = original_room
        if room:
            game_manager = self.game_server.gamestate_manager
            try:
                state = game_manager.get_game_state_by_gamestate_id(room.get("active_gamestate_id"))
                if state is not None and state.origin_room_instance_id == room.get("instance_id"):
                    await game_manager.cleanup_game_state_complete(gamestate_id=state.gamestate_id, reason="start_failed")
            except Exception:
                logger.exception("清理未开局的场馆房间 %s 失败，将继续恢复等待状态", room_id)
            withdrawn = set(room.get("event_seating_withdrawn", []))
            if self.rooms.get(room_id) is room:
                self.rooms.pop(room_id)
                self.room_passwords.pop(room_id, None)
        for uid in set((room or {}).get("player_list", [])) | {row["user_id"] for row in claimed}:
            conn = self.game_server.user_id_to_connection.get(uid)
            if conn and getattr(conn, "current_room_id", None) == room_id and room_id not in self.rooms:
                conn.current_room_id = None
                notify.append(conn)
        # A disconnect or voluntary room exit must not silently re-enqueue that player.
        rows = [row for row in claimed if row["user_id"] not in withdrawn and (not automatic or self._event_player_connection(row["user_id"]))]
        if rows:
            try:
                self.game_server.db_manager.restore_event_ready_players(rows)
            except Exception:
                logger.exception("组桌失败后恢复准备池失败: %s", [row["user_id"] for row in rows])
        response = Response(type="room/leave_room_done", success=True, message="组桌未完成，已返回等待",
                            room_id=room_id, room_instance_id=(room or {}).get("instance_id"))
        for conn in notify:
            try:
                await asyncio.wait_for(conn.websocket.send_json(response.model_dump(mode="json", exclude_none=True)), timeout=3)
            except Exception:
                pass

    async def destroy_room(self, room_id: str):
        """Remove a lobby only. Active game seats, indexes and tasks are independent."""
        room = self.rooms.pop(room_id, None)
        if room is None:
            return
        self.room_passwords.pop(room_id, None)
        response = Response(
            type="room/leave_room_done", success=True, message="房间已解散",
            room_id=room_id, room_instance_id=room.get("instance_id"),
        )
        recipients = []
        for uid in list(room["player_list"]):
            conn = self.game_server.user_id_to_connection.get(uid)
            if conn is not None and conn.current_room_id == room_id:
                conn.current_room_id = None
                recipients.append(conn)
        for conn in recipients:
            try:
                await conn.websocket.send_json(response.model_dump(exclude_none=True))
            except Exception:
                logger.debug("发送房间解散通知失败 user_id=%s", conn.user_id, exc_info=True)
        logger.info("大厅房间已销毁 room_id=%s", room_id)
