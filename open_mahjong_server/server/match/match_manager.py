"""
匹配队列管理器
管理 12 个排位匹配队列（4 场次 x 3 局制）
"""
import asyncio
import uuid
import logging
from typing import Dict, List, Optional
from .rank_calculator import (
    TIER_BASE_SCORE, GAME_TYPE_MULTIPLIER,
    can_play_tier, queue_type_to_room_config,
    queue_type_to_display_name, parse_queue_type,
)
from ..response import Response

logger = logging.getLogger(__name__)

ALL_QUEUE_TYPES = [
    f"{tier}_{game_type}"
    for tier in ["beginner", "intermediate", "advanced", "mcrpl"]
    for game_type in ["dongfeng", "banzhuang", "quanzhuang"]
]


class MatchManager:
    def __init__(self, game_server):
        self.game_server = game_server
        # 匹配等待队列 {queue_type: [user_id, ...]}
        self.queues: Dict[str, List[int]] = {qt: [] for qt in ALL_QUEUE_TYPES}
        # user_id -> queue_type（防止重复排队）
        self.user_to_queue: Dict[int, str] = {}
        # 各队列正在游戏中的人数
        self.playing_counts: Dict[str, int] = {qt: 0 for qt in ALL_QUEUE_TYPES}

    # ==================== 队列操作 ====================

    async def join_queue(self, connect_id: str, queue_type: str) -> Response:
        """玩家加入匹配队列"""
        if queue_type not in self.queues:
            return Response(type="tips", success=False, message="无效的匹配类型")

        player = self.game_server.players.get(connect_id)
        if not player or not player.user_id:
            return Response(type="tips", success=False, message="请先登录")

        if getattr(player, "is_tourist", False):
            return Response(type="tips", success=False, message="游客无法进行排位匹配，请先注册账号")

        user_id = player.user_id

        # 已在队列中
        if user_id in self.user_to_queue:
            return Response(type="tips", success=False, message="您已在匹配队列中")

        # 正在游戏中
        if self.game_server.gamestate_manager.is_user_in_active_game(user_id):
            return Response(type="tips", success=False, message="您正在游戏中，无法匹配")

        # 资格校验
        parsed = parse_queue_type(queue_type)
        if not parsed:
            return Response(type="tips", success=False, message="无效的匹配类型")
        tier, _ = parsed

        rank_data = self.game_server.db_manager.get_rank_data(user_id)
        sponsor_mcrpl = self.game_server.db_manager.get_user_sponsor_mcrpl(user_id)
        rank_name = rank_data["guobiao_rank"] if rank_data else "10级"
        is_mcrpl = sponsor_mcrpl.get("is_mcrpl_qualified", False) if sponsor_mcrpl else False

        if not can_play_tier(rank_name, tier, is_mcrpl):
            return Response(type="tips", success=False, message="段位不足，无法进入该场次")

        # 加入队列
        self.queues[queue_type].append(user_id)
        self.user_to_queue[user_id] = queue_type
        logger.info(f"玩家 {user_id} 加入匹配队列 {queue_type}，当前等待: {len(self.queues[queue_type])}")

        # 尝试凑满开始
        await self._try_start_match(queue_type)

        return Response(
            type="match/join_queue_done",
            success=True,
            message=f"已加入 {queue_type_to_display_name(queue_type)} 匹配队列"
        )

    async def leave_queue(self, connect_id: str) -> Response:
        """玩家离开匹配队列"""
        player = self.game_server.players.get(connect_id)
        if not player or not player.user_id:
            return Response(type="tips", success=False, message="请先登录")

        user_id = player.user_id
        if user_id not in self.user_to_queue:
            return Response(type="tips", success=False, message="您不在匹配队列中")

        queue_type = self.user_to_queue[user_id]
        if user_id in self.queues.get(queue_type, []):
            self.queues[queue_type].remove(user_id)
        del self.user_to_queue[user_id]
        logger.info(f"玩家 {user_id} 离开匹配队列 {queue_type}")

        return Response(type="match/leave_queue_done", success=True, message="已取消匹配")

    def player_disconnect(self, user_id: int):
        """玩家断线时从队列移除"""
        if user_id in self.user_to_queue:
            queue_type = self.user_to_queue[user_id]
            if user_id in self.queues.get(queue_type, []):
                self.queues[queue_type].remove(user_id)
            del self.user_to_queue[user_id]
            logger.info(f"断线玩家 {user_id} 已从匹配队列 {queue_type} 移除")

    def get_queue_status(self) -> dict:
        """获取所有队列的等待人数和游戏中人数"""
        status = {}
        for qt in ALL_QUEUE_TYPES:
            status[qt] = {
                "waiting": len(self.queues[qt]),
                "playing": self.playing_counts.get(qt, 0),
            }
        return status

    # ==================== 匹配与开局 ====================

    async def _try_start_match(self, queue_type: str):
        """检查队列是否凑满 4 人，凑满则创建对局"""
        queue = self.queues[queue_type]
        if len(queue) < 4:
            return

        # 取出前 4 名玩家
        matched_users = queue[:4]
        self.queues[queue_type] = queue[4:]
        for uid in matched_users:
            if uid in self.user_to_queue:
                del self.user_to_queue[uid]

        logger.info(f"匹配成功: {queue_type}, 玩家: {matched_users}")

        # 通知客户端匹配成功（5 秒倒计时）
        display_name = queue_type_to_display_name(queue_type)
        match_found_response = Response(
            type="match/match_found",
            success=True,
            message=display_name,
        )
        for uid in matched_users:
            conn = self.game_server.user_id_to_connection.get(uid)
            if conn:
                try:
                    await conn.websocket.send_json(match_found_response.dict(exclude_none=True))
                except Exception as e:
                    logger.error(f"通知玩家 {uid} 匹配成功失败: {e}")

        # 5 秒后创建房间并开始游戏
        asyncio.create_task(self._delayed_start_game(queue_type, matched_users))

    async def _delayed_start_game(self, queue_type: str, user_ids: List[int]):
        """延迟 5 秒后创建房间并启动游戏"""
        await asyncio.sleep(5)

        room_config = queue_type_to_room_config(queue_type)
        room_id = self.game_server.room_manager._generate_room_id()

        # 收集玩家设置
        player_settings = {}
        for uid in user_ids:
            settings = self.game_server.db_manager.get_user_settings(uid)
            conn = self.game_server.user_id_to_connection.get(uid)
            username = conn.username if conn else f"用户{uid}"
            player_settings[uid] = {
                "user_id": uid,
                "username": settings.get("username", username) if settings else username,
                "title_id": settings.get("title_id", 1) if settings else 1,
                "profile_image_id": settings.get("profile_image_id", 1) if settings else 1,
                "character_id": settings.get("character_id", 1) if settings else 1,
                "voice_id": settings.get("voice_id", 1) if settings else 1,
            }

        room_data = {
            "room_id": room_id,
            "room_type": "match",
            "room_rule": "guobiao",
            "sub_rule": room_config["sub_rule"],
            "hepai_limit": room_config["hepai_limit"],
            "tourist_limit": True,
            "allow_spectator": True,
            "max_player": 4,
            "player_list": list(user_ids),
            "player_settings": player_settings,
            "has_password": False,
            "tips": room_config["tips"],
            "host_user_id": user_ids[0],
            "host_name": player_settings[user_ids[0]]["username"],
            "is_game_running": True,
            "room_name": queue_type_to_display_name(queue_type),
            "game_round": room_config["game_round"],
            "round_timer": room_config["round_timer"],
            "step_timer": room_config["step_timer"],
            "random_seed": 0,
            "open_cuohe": room_config["open_cuohe"],
            "show_moqie_hint": room_config.get("show_moqie_hint", False),
            "tactical_call": room_config.get("tactical_call", False),
            "match_queue_type": queue_type,
        }

        # 注册房间
        self.game_server.room_manager.rooms[room_id] = room_data

        # 更新玩家状态
        for uid in user_ids:
            conn = self.game_server.user_id_to_connection.get(uid)
            if conn:
                conn.current_room_id = room_id

        # 更新游戏中人数
        self.playing_counts[queue_type] = self.playing_counts.get(queue_type, 0) + 4

        # 通过 gamestate_manager 创建 GuobiaoGameState
        from ..gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState
        gamestate_id = str(uuid.uuid4())
        game_state = GuobiaoGameState(
            self.game_server,
            room_data,
            self.game_server.calculation_service,
            self.game_server.db_manager,
            gamestate_id,
        )

        gsm = self.game_server.gamestate_manager
        gsm.room_id_to_GuobiaoGameState[room_id] = game_state
        gsm.gamestate_id_to_game_state[gamestate_id] = game_state
        for uid in user_ids:
            gsm.user_id_to_game_state[uid] = game_state

        game_state.game_task = asyncio.create_task(game_state.run_game_loop())
        logger.info(f"排位匹配房间 {room_id} 已创建，queue_type={queue_type}, gamestate_id={gamestate_id}")

    def on_match_game_end(self, queue_type: str):
        """匹配对局结束时减少游戏中人数"""
        if queue_type in self.playing_counts:
            self.playing_counts[queue_type] = max(0, self.playing_counts[queue_type] - 4)
