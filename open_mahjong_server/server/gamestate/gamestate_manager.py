# 游戏状态管理器
import logging
import asyncio
import uuid
from copy import deepcopy
from .public.lifecycle import all_humans_offline, cancel_auxiliary_tasks, cancel_game_task
from typing import Dict, Any, Optional
from .game_guobiao.GuobiaoGameState import GuobiaoGameState
from ..response import Response, MessageInfo
from ..room.room_seats import get_seats
from ..database.inventory_router import attach_game_frames
from .public.duplicate_wall import configure_duplicate_state
from .game_mmcr.QingqueGameState import QingqueGameState
from .game_changsha.ChangshaGameState import ChangshaGameState
from .game_classical.ClassicalGameState import ClassicalGameState
from .game_riichi.RiichiGameState import RiichiGameState
from .game_sichuan.SichuanGameState import SichuanGameState
from .game_sichuan.XueliuGameState import XueliuGameState
from .game_jiandan.JiandanGameState import JiandanGameState
from .game_zhongyong import ZhongyongGameState, NanqueGameState
from .game_taiwan.TaiwanGameState import TaiwanGameState
from .game_hongkong import HongKongGameState
from .game_guizhou import GuizhouGameState
from .game_hangzhou import HangzhouGameState
from .game_yixing import YixingGameState
from .game_wenzhou.WenzhouGameState import WenzhouGameState
from .game_hongzhong.HongzhongGameState import HongzhongGameState
from .game_tuidao import TuidaoGameState
from .game_guangdong import GuangdongGameState
from .game_shanxi import ShanxiGameState
from .game_changchun import ChangchunGameState
from .game_shanghai.ShanghaiGameState import ShanghaiGameState
from .game_shanghai.QinghunpengGameState import QinghunpengGameState
from .game_hongque.HongqueGameState import HongqueGameState
from .game_free.FreeGameState import FreeGameState
logger = logging.getLogger(__name__)

class GameStateManager:
    """管理所有游戏状态实例"""
    
    def __init__(self, game_server):
        """
        初始化游戏状态管理器
        
        Args:
            game_server: 游戏服务器实例
        """
        self.game_server = game_server
        # 房间来源号的辅助索引；对局存续由 gamestate_id 管理。
        self.room_id_to_GuobiaoGameState: Dict[str, GuobiaoGameState] = {}
        self.room_id_to_QingqueGameState: Dict[str, QingqueGameState] = {}
        self.room_id_to_ChangshaGameState: Dict[str, ChangshaGameState] = {}
        self.room_id_to_ClassicalGameState: Dict[str, ClassicalGameState] = {}
        self.room_id_to_RiichiGameState: Dict[str, RiichiGameState] = {}
        self.room_id_to_SichuanGameState: Dict[str, SichuanGameState] = {}
        self.room_id_to_JiandanGameState: Dict[str, JiandanGameState] = {}
        self.room_id_to_ZhongyongGameState: Dict[str, ZhongyongGameState] = {}
        self.room_id_to_TaiwanGameState: Dict[str, TaiwanGameState] = {}
        self.room_id_to_HongKongGameState: Dict[str, HongKongGameState] = {}
        self.room_id_to_TuidaoGameState: Dict[str, TuidaoGameState] = {}
        self.room_id_to_ShanghaiGameState: Dict[str, ShanghaiGameState] = {}
        self.room_id_to_HongqueGameState: Dict[str, HongqueGameState] = {}
        self.room_id_to_FreeGameState: Dict[str, FreeGameState] = {}
        self.room_id_to_GuizhouGameState: Dict[str, GuizhouGameState] = {}
        self.room_id_to_YixingGameState: Dict[str, YixingGameState] = {}
        self.room_id_to_WenzhouGameState: Dict[str, WenzhouGameState] = {}
        self.room_id_to_HangzhouGameState: Dict[str, HangzhouGameState] = {}
        self.room_id_to_HongzhongGameState: Dict[str, HongzhongGameState] = {}
        self.room_id_to_ShanxiGameState: Dict[str, ShanxiGameState] = {}
        self.room_id_to_ChangchunGameState: Dict[str, ChangchunGameState] = {}
        # gamestate_id 到游戏状态的映射（主要管理方式）
        self.gamestate_id_to_game_state: Dict[str, Any] = {}
        # 用户ID到游戏状态的映射（用于快速查找玩家所在的活跃游戏）
        self.user_id_to_game_state: Dict[int, Any] = {}
        self._closing_games = {}
    
    async def start_game(self, Connect_id: str, room_id: str, *, event_auto_start: bool = False) -> Optional[Response]:
        """
        开始游戏
        
        Args:
            Connect_id: 连接ID
            room_id: 房间ID
            
        Returns:
            Response对象，如果成功返回None
        """
        # 检查房间是否存在
        if room_id not in self.game_server.room_manager.rooms:
            return Response(type="error_message", success=False, message="房间不存在")
            
        room_data = self.game_server.room_manager.rooms[room_id]
        if room_data.get("duplicate_key") and room_data.get("room_rule") != "guobiao":
            return Response(type="error_message", success=False, message="复式牌墙仅支持国标麻将（含蓝十改）")
        if room_data.get("event_seating_pending") and not event_auto_start:
            return Response(type="error_message", success=False, message="自动匹配正在安排入座，请稍候")
        
        # 检查是否是房主
        player = self.game_server.players[Connect_id]
        self.game_server.room_manager._sync_room_host(room_data)
        if player.user_id != room_data.get("host_user_id"):
            return Response(type="error_message", success=False, message="只有房主能开始游戏")
            
        # 检查人数是否满足：默认满 4 人；房间可声明 min_players_to_start（自由模式为 1）
        try:
            min_players = int(room_data.get("min_players_to_start", 4) or 4)
        except (TypeError, ValueError):
            min_players = 4
        if min_players < 1:
            min_players = 1
        if len(room_data["player_list"]) < min_players:
            return Response(type="error_message", success=False, message="人数不足")

        # 检查除房主外的所有玩家是否都已准备（机器人默认已准备）
        if not self.game_server.room_manager.all_players_ready(room_data):
            return Response(type="error_message", success=False, message="有玩家尚未准备")
        
        # 检查游戏是否已经在运行
        if room_data.get("is_game_running", False):
            return Response(type="error_message", success=False, message="游戏已在进行中")

        # 任一在房玩家若仍在其他对局中，禁止重复开局
        for player_id in room_data["player_list"]:
            if player_id <= 10:
                continue
            if self.is_user_in_active_game(player_id):
                return Response(
                    type="error_message",
                    success=False,
                    message="有玩家正在对局中，无法开始游戏",
                )
            
        # Re-read equipment at start, including rooms created before an admin revoked a grant.
        # Matchmaking already reads get_user_settings when constructing its room data.
        for player_id in room_data["player_list"]:
            if player_id > 10:
                settings = self.game_server.db_manager.get_user_settings(player_id)
                profiles = room_data.get("player_settings", {})
                for key in (player_id, str(player_id)):
                    if key in profiles:
                        for field, default in (("title_id",1),("character_id",1),("voice_id",1),("profile_image_id",1),("avatar_frame_id",0)):
                            profiles[key][field] = settings.get(field,default) if settings else default

        classes = {
            "guobiao": GuobiaoGameState, "qingque": QingqueGameState,
            "changsha": ChangshaGameState, "classical": ClassicalGameState,
            "riichi": RiichiGameState, "sichuan": SichuanGameState,
            "jiandan": JiandanGameState, "zhongyong": ZhongyongGameState, "taiwan": TaiwanGameState,
            "shanghai": ShanghaiGameState, "hongque": HongqueGameState, "hongkong": HongKongGameState, "guangdong": TuidaoGameState,
            "shanxi": ShanxiGameState,
            "changchun": ChangchunGameState,
            "free": FreeGameState, "guizhou": GuizhouGameState, "yixing": YixingGameState, "wenzhou": WenzhouGameState, "hangzhou": HangzhouGameState, "hongzhong": HongzhongGameState,
        }
        state_class = classes.get(room_data["room_rule"])
        if room_data["room_rule"] == "guangdong" and room_data.get("sub_rule") == "guangdong/mil2023":
            state_class = GuangdongGameState
        if state_class is None:
            return Response(type="error_message", success=False, message="房间类型不支持")
        if room_data["room_rule"] == "sichuan" and room_data.get("sub_rule") in ("sichuan/xueliu", "sichuan/xueliu_exchange"):
            state_class = XueliuGameState
        if room_data["room_rule"] == "shanghai" and room_data.get("sub_rule") == "shanghai/qinghunpeng":
            state_class = QinghunpengGameState
        if room_data["room_rule"] == "zhongyong" and room_data.get("sub_rule") == "zhongyong/nanque":
            state_class = NanqueGameState
        room_data.setdefault("instance_id", str(uuid.uuid4()))
        game_state = None
        try:
            snapshot = deepcopy(room_data)
            snapshot["player_list"] = [user_id for user_id in get_seats(room_data) if user_id >= 0]
            game_state = state_class(
                self.game_server, snapshot, self.game_server.calculation_service,
                self.game_server.db_manager, str(uuid.uuid4()),
            )
            from .public.ai.pacing import configure_bot_pacing, DEFAULT_BOT_SPEED
            snapshot.setdefault("bot_speed", DEFAULT_BOT_SPEED)
            configure_bot_pacing(game_state, snapshot)
            self.register_game(game_state, room_data)
            configure_duplicate_state(game_state, snapshot)
            attach_game_frames(game_state, snapshot)
            game_state.game_task = asyncio.create_task(self.run_game(game_state, game_state.run_game_loop))
            logger.info("对局已启动 room_id=%s gamestate_id=%s", room_id, game_state.gamestate_id)
        except Exception as exc:
            logger.exception("启动对局失败 room_id=%s", room_id)
            if game_state is not None and self.gamestate_id_to_game_state.get(game_state.gamestate_id) is game_state:
                await self.cleanup_game_state_complete(gamestate_id=game_state.gamestate_id, reason="start_failed")
            return Response(type="error_message", success=False, message=f"启动游戏失败: {exc}")
        return None

    def _room_indexes(self):
        return (
            self.room_id_to_GuobiaoGameState, self.room_id_to_QingqueGameState,
            self.room_id_to_ChangshaGameState, self.room_id_to_ClassicalGameState,
            self.room_id_to_RiichiGameState, self.room_id_to_SichuanGameState,
            self.room_id_to_JiandanGameState, self.room_id_to_ZhongyongGameState, self.room_id_to_TaiwanGameState,
            self.room_id_to_ShanghaiGameState, self.room_id_to_HongqueGameState, self.room_id_to_HongKongGameState, self.room_id_to_TuidaoGameState,
            self.room_id_to_ShanxiGameState, self.room_id_to_ChangchunGameState, self.room_id_to_FreeGameState, self.room_id_to_GuizhouGameState, self.room_id_to_YixingGameState, self.room_id_to_WenzhouGameState, self.room_id_to_HangzhouGameState, self.room_id_to_HongzhongGameState,
        )

    def register_game(self, state, room_data):
        """Register one independent game and reserve its display/chat room number."""
        indexes = dict(zip(
            ("guobiao", "qingque", "changsha", "classical", "riichi", "sichuan",
             "jiandan", "zhongyong", "taiwan", "shanghai", "hongque", "hongkong", "guangdong", "shanxi", "changchun", "free", "guizhou", "yixing", "wenzhou", "hangzhou", "hongzhong"), self._room_indexes()
        ))
        index = indexes[state.room_rule]
        if self.get_game_state_by_room_id(state.room_id) is not None:
            raise ValueError("房间已有活跃对局")
        for player in state.player_list:
            if player.user_id > 10 and self.is_user_in_active_game(player.user_id):
                raise ValueError("玩家已有活跃对局")
        state.origin_room_instance_id = room_data.get("instance_id")
        state.lifecycle_state = "running"
        state.close_reason = None
        state.abandoned_users = set()
        state._presence_lock = asyncio.Lock()
        self.gamestate_id_to_game_state[state.gamestate_id] = state
        index[state.room_id] = state
        for player in state.player_list:
            if player.user_id > 10:
                self.user_id_to_game_state[player.user_id] = state
        room_manager = self.game_server.room_manager
        room_manager.active_game_room_ids[state.gamestate_id] = state.room_id
        if room_manager.rooms.get(state.room_id) is room_data:
            room_data["active_gamestate_id"] = state.gamestate_id
            room_data["is_game_running"] = True

    async def run_game(self, state, loop):
        """Catch rule-loop failures/early returns that would otherwise leak indexes."""
        try:
            await loop()
        except asyncio.CancelledError:
            raise
        except Exception:
            logger.exception("对局任务异常 gamestate_id=%s", state.gamestate_id)
        finally:
            if getattr(state, "lifecycle_state", None) == "running":
                await self.cleanup_game_state_complete(gamestate_id=state.gamestate_id, reason="runtime_error")

    async def check_player_reconnect(self, Connect_id: str, user_id: int):
        """
        检查玩家是否需要重连并发送提示
        
        Args:
            Connect_id: 连接ID
            user_id: 用户ID
        """
        if user_id in self.user_id_to_game_state:
            game_state = self.user_id_to_game_state[user_id]
            if getattr(game_state, "lifecycle_state", "running") != "running" or user_id in getattr(game_state, "abandoned_users", ()):
                return
            reconnect_message = Response(
                type="message",
                success=True,
                message="reconnect_ask",
                message_info=MessageInfo(
                    title="对局重连",
                    content=f"检测到您有一场正在进行的游戏，是否返回游戏？"
                )
            )
            
            # 在响应中添加额外字段方便客户端处理逻辑
            response_dict = reconnect_message.dict(exclude_none=True)
            
            if Connect_id in self.game_server.players:
                await self.game_server.players[Connect_id].websocket.send_json(response_dict)
                
                room_id = game_state.room_id
                logger.info(f"已向玩家 {user_id} 发送重连请求，房间 ID: {room_id}")
    
    async def player_disconnect(self, user_id: int, connection=None):
        state = self.user_id_to_game_state.get(user_id)
        if state is not None:
            async with state._presence_lock:
                if connection is not None and self.game_server.user_id_to_connection.get(user_id) is not connection:
                    return
                if getattr(state, "lifecycle_state", "running") == "running":
                    try:
                        await state.player_disconnect(user_id)
                    finally:
                        if all_humans_offline(state):
                            await self.cleanup_game_state_complete(gamestate_id=state.gamestate_id, reason="all_humans_offline")
        await self.remove_spectator_from_all_games(user_id)
    
    async def remove_spectator_from_all_games(self, user_id: int):
        """从所有进行中对局的延时观战列表移除用户（退出观战、加入匹配等场景）。"""
        for game_state in list(self.gamestate_id_to_game_state.values()):
            if hasattr(game_state, "remove_spectator"):
                await game_state.remove_spectator(user_id)
    
    async def player_reconnect(self, user_id: int):
        state = self.user_id_to_game_state.get(user_id)
        if state is None:
            return False
        async with state._presence_lock:
            if (getattr(state, "lifecycle_state", "running") != "running"
                    or user_id in getattr(state, "abandoned_users", ())):
                return False
            await state.player_reconnect(user_id)
            vm = getattr(state, "vote_manager", None)
            if vm is not None:
                await vm.sync_to_user(user_id)
            return True
    
    def remove_player_from_game_state(self, user_id: int):
        """
        从游戏状态映射中移除玩家
        
        Args:
            user_id: 用户ID
        """
        if user_id in self.user_id_to_game_state:
            del self.user_id_to_game_state[user_id]
            logger.info(f"已从游戏状态映射中移除玩家 {user_id}")

    async def abandon_player_game(self, Connect_id: str, user_id: int):
        """Forfeit reconnect eligibility, while retaining occupancy until game end."""
        state = self.user_id_to_game_state.get(user_id)
        if state is None:
            return
        async with state._presence_lock:
            state.abandoned_users.add(user_id)
            if getattr(state, "lifecycle_state", "running") == "running":
                try:
                    await state.player_disconnect(user_id)
                finally:
                    if all_humans_offline(state):
                        await self.cleanup_game_state_complete(gamestate_id=state.gamestate_id, reason="all_humans_offline")
        room_manager = self.game_server.room_manager
        room = room_manager.rooms.get(state.room_id)
        if (room is not None and room.get("instance_id") == state.origin_room_instance_id
                and user_id in room.get("player_list", [])):
            await room_manager.leave_room(Connect_id, state.room_id)
    
    def get_game_state_by_room_id(self, room_id: str) -> Optional[Any]:
        """
        根据房间ID获取游戏状态（仅用于开始游戏时检查，之后不再使用）
        
        Args:
            room_id: 房间ID
            
        Returns:
            游戏状态对象，如果不存在返回None
        """
        if room_id in self.room_id_to_GuobiaoGameState:
            return self.room_id_to_GuobiaoGameState.get(room_id)
        elif room_id in self.room_id_to_QingqueGameState:
            return self.room_id_to_QingqueGameState.get(room_id)
        elif room_id in self.room_id_to_ChangshaGameState:
            return self.room_id_to_ChangshaGameState.get(room_id)
        elif room_id in self.room_id_to_ClassicalGameState:
            return self.room_id_to_ClassicalGameState.get(room_id)
        elif room_id in self.room_id_to_RiichiGameState:
            return self.room_id_to_RiichiGameState.get(room_id)
        elif room_id in self.room_id_to_SichuanGameState:
            return self.room_id_to_SichuanGameState.get(room_id)
        elif room_id in self.room_id_to_ZhongyongGameState:
            return self.room_id_to_ZhongyongGameState.get(room_id)
        elif room_id in self.room_id_to_JiandanGameState:
            return self.room_id_to_JiandanGameState.get(room_id)
        elif room_id in self.room_id_to_TaiwanGameState:
            return self.room_id_to_TaiwanGameState.get(room_id)
        elif room_id in self.room_id_to_ShanghaiGameState:
            return self.room_id_to_ShanghaiGameState.get(room_id)
        elif room_id in self.room_id_to_HongqueGameState:
            return self.room_id_to_HongqueGameState.get(room_id)
        elif room_id in self.room_id_to_HongKongGameState:
            return self.room_id_to_HongKongGameState.get(room_id)
        elif room_id in self.room_id_to_TuidaoGameState:
            return self.room_id_to_TuidaoGameState.get(room_id)
        elif room_id in self.room_id_to_YixingGameState:
            return self.room_id_to_YixingGameState.get(room_id)
        elif room_id in self.room_id_to_GuizhouGameState:
            return self.room_id_to_GuizhouGameState.get(room_id)
        elif room_id in self.room_id_to_ShanxiGameState:
            return self.room_id_to_ShanxiGameState.get(room_id)
        elif room_id in self.room_id_to_FreeGameState:
            return self.room_id_to_FreeGameState.get(room_id)
        # New rule families use the same registered indexes for lookup and cleanup.
        for index in self._room_indexes():
            if room_id in index:
                return index[room_id]
        return None
    
    def get_game_state_by_gamestate_id(self, gamestate_id: str) -> Optional[Any]:
        """
        根据 gamestate_id 获取游戏状态
        
        Args:
            gamestate_id: 游戏状态ID
            
        Returns:
            游戏状态对象，如果不存在返回None
        """
        state = self.gamestate_id_to_game_state.get(gamestate_id)
        return state if state is not None and getattr(state, "lifecycle_state", "running") == "running" else None
    
    def get_game_state_by_user_id(self, user_id: int) -> Optional[Any]:
        """
        根据用户ID获取游戏状态
        
        Args:
            user_id: 用户ID
            
        Returns:
            游戏状态对象，如果不存在返回None
        """
        return self.user_id_to_game_state.get(user_id)

    def is_user_in_active_game(self, user_id: int) -> bool:
        """
        玩家是否处于尚未结束的对局（以 user_id_to_game_state 及座位名单为准）。
        与 current_room_id 无关：退房后仍可处于对局中。
        """
        game_state = self.user_id_to_game_state.get(user_id)
        if game_state is None:
            return False
        player_list = getattr(game_state, "player_list", None)
        if player_list is not None:
            return any(p.user_id == user_id for p in player_list)
        return True
    
    def get_playing_rooms_count(self) -> int:
        """
        获取正在进行的游戏房间数量
        
        Returns:
            正在进行的游戏房间数量
        """
        return len(self.gamestate_id_to_game_state)

    def get_match_playing_games_count(self) -> int:
        """获取正在进行的排位匹配对局数量（不写入 room_manager.rooms）。"""
        return sum(
            1 for game_state in self.gamestate_id_to_game_state.values()
            if getattr(game_state, "room_type", None) == "match"
        )
    
    def get_spectator_list(self) -> list:
        """
        获取所有正在进行的游戏的观战列表
        
        Returns:
            观战信息列表，每个元素包含规则、4个玩家ID和gamestate_id
        """
        from ..response import SpectatorInfo
        from .public.spectator_rules import too_many_ai_for_spectator
        spectator_list = []
        
        # 遍历所有游戏状态
        for gamestate_id, game_state in self.gamestate_id_to_game_state.items():
            if getattr(game_state, "lifecycle_state", "running") != "running":
                continue
            # 确保游戏状态有玩家列表且至少有4个玩家
            if hasattr(game_state, 'player_list') and len(game_state.player_list) >= 4:
                # 含 3 个及以上 AI(uid<=10) 的对局不开放观战
                if too_many_ai_for_spectator(game_state.player_list):
                    continue
                # 若游戏状态显式关闭观战，也不返回
                if hasattr(game_state, "spectator_enabled") and not game_state.spectator_enabled:
                    continue
                # 获取规则类型
                rule = game_state.room_rule or ""
                sub_rule = getattr(game_state, "sub_rule", None) or rule
                
                # 获取4个玩家的用户名（用于显示）
                player1_name = game_state.player_list[0].username or str(game_state.player_list[0].user_id)
                player2_name = game_state.player_list[1].username or str(game_state.player_list[1].user_id)
                player3_name = game_state.player_list[2].username or str(game_state.player_list[2].user_id)
                player4_name = game_state.player_list[3].username or str(game_state.player_list[3].user_id)
                
                # 创建观战信息
                event_id = getattr(game_state, "event_id", None)
                if not event_id:
                    room_id = getattr(game_state, "room_id", None)
                    room = self.game_server.room_manager.rooms.get(room_id) if room_id else None
                    if room:
                        event_id = room.get("event_id")
                spectator_info = SpectatorInfo(
                    rule=rule,
                    sub_rule=sub_rule,
                    player1_name=player1_name,
                    player2_name=player2_name,
                    player3_name=player3_name,
                    player4_name=player4_name,
                    gamestate_id=gamestate_id,
                    event_id=event_id,
                )
                spectator_list.append(spectator_info)
        
        return spectator_list
    
    async def cleanup_game_state_complete(self, gamestate_id: str = None, room_id: str = None, *, reason=None):
        state = self.gamestate_id_to_game_state.get(gamestate_id) if gamestate_id else self.get_game_state_by_room_id(room_id)
        if state is None:
            return
        gid = state.gamestate_id
        current = asyncio.current_task()
        closing = self._closing_games.get(gid)
        if closing is not None:
            # The closer may be waiting for game_task; never make that task wait back.
            if current is not closing[0] and current is not getattr(state, "game_task", None):
                await asyncio.shield(closing[1])
            return
        finished = asyncio.get_running_loop().create_future()
        self._closing_games[gid] = (current, finished)
        state.lifecycle_state = "closing"
        state.close_reason = reason or getattr(state, "close_reason", None) or "completed"
        room_manager = self.game_server.room_manager
        try:
            try:
                await cancel_auxiliary_tasks(state)
                await state.cleanup_game_state()
            except Exception:
                logger.exception("清理对局任务失败 gamestate_id=%s", gid)
            finally:
                # Even a rule-specific cleanup failure must stop the main loop.
                await cancel_game_task(state)
            if getattr(state, "_duplicate_game_id", None):
                from ..database.duplicate_walls import mark_duplicate_game_ended
                try:
                    mark_duplicate_game_ended(state.db_manager, state._duplicate_game_id)
                except Exception:
                    logger.exception("结束复式比赛关联失败 gamestate_id=%s", gid)
            if getattr(self.game_server, "friend_manager", None):
                try:
                    await self.game_server.friend_manager.on_game_end(state)
                except Exception:
                    logger.exception("结束实时观战失败 gamestate_id=%s", gid)
            if state.close_reason not in ("completed", "start_failed"):
                payload = {"type": "gamestate/closed", "success": True, "gamestate_id": gid,
                           "reason": state.close_reason, "message": "对局已结束"}
                for player in state.player_list:
                    conn = self.game_server.user_id_to_connection.get(player.user_id)
                    if conn is not None and player.user_id > 10:
                        try:
                            await asyncio.wait_for(conn.websocket.send_json(payload), timeout=3)
                        except Exception:
                            logger.debug("对局结束通知发送失败 user_id=%s", player.user_id, exc_info=True)
        finally:
            # Every removal must still belong to this game, even after an await.
            for player in state.player_list:
                if self.user_id_to_game_state.get(player.user_id) is state:
                    self.user_id_to_game_state.pop(player.user_id)
            for index in self._room_indexes():
                if index.get(state.room_id) is state:
                    index.pop(state.room_id)
            if self.gamestate_id_to_game_state.get(gid) is state:
                self.gamestate_id_to_game_state.pop(gid)
            try:
                if getattr(state, "room_type", None) == "match" and getattr(self.game_server, "match_manager", None):
                    self.game_server.match_manager.release_match(gid)
                else:
                    await room_manager.finish_custom_game_room(
                        state.room_id, expected_gamestate_id=gid,
                        expected_instance_id=getattr(state, "origin_room_instance_id", None),
                    )
            finally:
                room_manager.active_game_room_ids.pop(gid, None)
                state.lifecycle_state = "closed"
                self._closing_games.pop(gid, None)
                finished.set_result(None)
                logger.info("对局已清理 gamestate_id=%s reason=%s", gid, state.close_reason)
