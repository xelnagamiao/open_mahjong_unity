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
        # 匹配承诺锁：凡是已匹配成功（match_found）的玩家立即进入此集合，直到其所在的
        # 匹配对局被彻底清理（正常结束或全员掉线）才释放。掉线、放弃重连都不会解锁，
        # 以此保证“匹配成功后必须打完当前一局，才能进入下一局”。
        self.committed_users: set = set()
        # gamestate_id -> {"queue_type", "user_ids", "room_id"}，用于对局结束时统一释放
        self.gamestate_to_match: Dict[str, dict] = {}

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

        # 已在房间中（自定义房等）不允许排位匹配，需先退出房间
        if getattr(player, "current_room_id", None):
            return Response(type="tips", success=False, message="请先退出当前房间再进行排位匹配")
        if self._is_user_in_custom_room(user_id):
            return Response(type="tips", success=False, message="请先退出当前房间再进行排位匹配")

        # 已在队列中
        if user_id in self.user_to_queue:
            return Response(type="tips", success=False, message="您已在匹配队列中")

        # 已匹配成功但对局尚未结束（覆盖 match_found 到开局之间的空窗，以及掉线/放弃重连
        # 导致 is_user_in_active_game 失效但对局仍在进行的情况）
        if user_id in self.committed_users:
            return Response(type="tips", success=False, message="您已匹配到对局，请完成当前对局后再匹配")

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
        """玩家断线时从“等待队列”移除。

        注意：仅移除尚在排队等待的玩家。已匹配成功（committed_users）的玩家不在此解锁，
        其匹配承诺会保留到所在对局彻底结束（由 release_match 释放），以避免断线后被再次匹配。
        """
        if user_id in self.user_to_queue:
            queue_type = self.user_to_queue[user_id]
            if user_id in self.queues.get(queue_type, []):
                self.queues[queue_type].remove(user_id)
            del self.user_to_queue[user_id]
            logger.info(f"断线玩家 {user_id} 已从匹配队列 {queue_type} 移除")

    def is_user_committed(self, user_id: int) -> bool:
        """玩家是否已匹配成功且对局尚未结束。"""
        return user_id in self.committed_users

    def is_user_in_queue(self, user_id: int) -> bool:
        """玩家是否仍在匹配等待队列中。"""
        return user_id in self.user_to_queue

    def blocks_spectator(self, user_id: int) -> bool:
        """匹配排队中或已匹配成功（对局尚未结束）时不允许进入观战。"""
        return user_id in self.user_to_queue or user_id in self.committed_users

    def _is_user_in_custom_room(self, user_id: int) -> bool:
        """兜底：current_room_id 未同步时，仍按房间成员表判断是否已在自定义房。"""
        for room_data in self.game_server.room_manager.rooms.values():
            if user_id in room_data.get("player_list", []):
                return True
        return False

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
            # 立即上锁：从匹配成功这一刻起，玩家被绑定到本局，关闭“开局前 5 秒空窗”被再次匹配的可能
            self.committed_users.add(uid)

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
        """延迟 5 秒后直接创建对局并启动（不依赖房间系统）。

        匹配对局不再写入 room_manager.rooms，因此不会出现在房间列表、也无法被加入；
        仅向 RoomManager 申请一个唯一房间号用于对局内部映射键与客户端聊天频道。
        """
        try:
            await asyncio.sleep(5)

            room_config = queue_type_to_room_config(queue_type)
            # 申请一个不与自定义房间冲突、且不进入房间列表的匹配专用房间号
            room_id = self.game_server.room_manager.allocate_match_room_id()

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

            # 对局所需的配置数据头（仅用于构造 GameState，不注册到 room_manager.rooms）
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

            # 通过 gamestate_manager 创建 GuobiaoGameState（匹配对局不设置 current_room_id，
            # 玩家“是否忙碌”由对局映射 + committed 锁共同保证）
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

            # 登记匹配会话，并累加游戏中人数。释放统一在对局清理时通过 release_match 完成。
            self.gamestate_to_match[gamestate_id] = {
                "queue_type": queue_type,
                "user_ids": list(user_ids),
                "room_id": room_id,
            }
            self.playing_counts[queue_type] = self.playing_counts.get(queue_type, 0) + len(user_ids)

            game_state.game_task = asyncio.create_task(game_state.run_game_loop())
            logger.info(f"排位匹配对局已创建，room_id={room_id}, queue_type={queue_type}, gamestate_id={gamestate_id}")
        except Exception as e:
            logger.error(f"创建排位匹配对局失败，queue_type={queue_type}, 玩家={user_ids}, 错误: {e}", exc_info=True)
            # 开局失败：释放承诺锁与已分配的房间号，避免玩家被永久锁定
            for uid in user_ids:
                self.committed_users.discard(uid)
            try:
                if "room_id" in locals():
                    self.game_server.room_manager.release_match_room_id(room_id)
            except Exception:
                pass
            # 通知玩家匹配失败，让其可重新匹配
            fail_response = Response(type="tips", success=False, message="匹配开局失败，请重新匹配")
            for uid in user_ids:
                conn = self.game_server.user_id_to_connection.get(uid)
                if conn:
                    try:
                        await conn.websocket.send_json(fail_response.dict(exclude_none=True))
                    except Exception:
                        pass

    def release_match(self, gamestate_id: str):
        """匹配对局彻底结束（正常结束或全员掉线清理）时，统一释放该局的匹配状态。

        - 解除参与玩家的承诺锁（committed_users），使其可重新匹配
        - 回收游戏中人数（playing_counts）
        - 释放占用的匹配房间号

        通过 gamestate_id 定位，幂等：重复调用安全。
        """
        entry = self.gamestate_to_match.pop(gamestate_id, None)
        if not entry:
            return
        queue_type = entry.get("queue_type")
        user_ids = entry.get("user_ids", [])
        room_id = entry.get("room_id")
        for uid in user_ids:
            self.committed_users.discard(uid)
        if queue_type in self.playing_counts:
            self.playing_counts[queue_type] = max(0, self.playing_counts[queue_type] - len(user_ids))
        if room_id is not None:
            self.game_server.room_manager.release_match_room_id(room_id)
        logger.info(f"匹配对局结束，已释放匹配状态：gamestate_id={gamestate_id}, 玩家={user_ids}")
