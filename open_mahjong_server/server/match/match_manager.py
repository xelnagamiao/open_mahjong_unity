"""
匹配队列管理器
管理四种规则的 20 个段位 / Elo 匹配队列（配置见 rating_rules.py）。
"""
import asyncio
import uuid
import logging
from collections import defaultdict
from typing import Dict, List, Optional
from .rank_calculator import (
    can_play_tier, queue_type_to_room_config,
    queue_type_to_display_name, parse_queue_type,
)
from ..response import Response
from .rating_rules import QUEUES, default_rating

logger = logging.getLogger(__name__)

ALL_QUEUE_TYPES = list(QUEUES)


class MatchManager:
    MAX_QUEUES = 4

    def __init__(self, game_server):
        self.game_server = game_server
        # 匹配等待队列 {queue_type: [user_id, ...]}
        self.queues: Dict[str, List[int]] = {qt: [] for qt in ALL_QUEUE_TYPES}
        self.user_to_queues: Dict[int, List[str]] = {}
        self.user_revisions: Dict[int, int] = {}
        self.user_locks = defaultdict(asyncio.Lock)
        self.disconnect_epochs: Dict[int, int] = {}
        self.winning_queues: Dict[int, str] = {}
        # 各队列正在游戏中的人数
        self.playing_counts: Dict[str, int] = {qt: 0 for qt in ALL_QUEUE_TYPES}
        # 匹配承诺锁：凡是已匹配成功（match_found）的玩家立即进入此集合，直到其所在的
        # 匹配对局被彻底清理（正常结束或全员掉线）才释放。掉线、放弃重连都不会解锁，
        # 以此保证“匹配成功后必须打完当前一局，才能进入下一局”。
        self.committed_users: set = set()
        # gamestate_id -> {"queue_type", "user_ids", "room_id"}，用于对局结束时统一释放
        self.gamestate_to_match: Dict[str, dict] = {}

    # ==================== 队列操作 ====================

    def snapshot(self, user_id: Optional[int]) -> dict:
        queues = list(self.user_to_queues.get(user_id, []))
        return dict(my_queue=queues[0] if queues else None, my_queues=queues,
                    match_revision=self.user_revisions.get(user_id, 0),
                    match_committed=user_id in self.committed_users,
                    match_queue_type=self.winning_queues.get(user_id))

    def _changed(self, user_id: int):
        self.user_revisions[user_id] = self.user_revisions.get(user_id, 0) + 1

    def _reply(self, operation, user_id, success, message):
        return Response(type=f"match/{operation}_queue_done", success=success,
                        message=message, **self.snapshot(user_id))

    def _join_block_reason(self, player, user_id):
        if getattr(player, "is_tourist", False):
            return "游客无法进行排位匹配，请先注册账号"
        if getattr(player, "current_room_id", None) or self._is_user_in_custom_room(user_id):
            return "请先退出当前房间再进行排位匹配"
        if user_id in getattr(self.game_server.room_manager, "event_seating_users", ()):
            return "赛事正在安排入座，无法加入匹配"
        if user_id in self.committed_users:
            return "您已匹配到对局，请完成当前对局后再匹配"
        if self.game_server.gamestate_manager.is_user_in_active_game(user_id):
            return "您正在游戏中，无法匹配"
        return None

    async def join_queue(self, connect_id: str, queue_type: str) -> Response:
        player = self.game_server.players.get(connect_id)
        user_id = getattr(player, "user_id", None)
        if not user_id:
            return self._reply("join", user_id, False, "请先登录")
        epoch = self.disconnect_epochs.get(user_id, 0)
        async with self.user_locks[user_id]:
            try:
                return await self._join_queue(connect_id, player, user_id, queue_type, epoch)
            except Exception:
                logger.exception("加入匹配失败: %s", user_id)
                return self._reply("join", user_id, False, "加入匹配失败，请稍后重试")

    async def _join_queue(self, connect_id, player, user_id, queue_type, epoch):
        if (self.game_server.players.get(connect_id) is not player
                or self.disconnect_epochs.get(user_id, 0) != epoch):
            return self._reply("join", user_id, False, "连接已断开，请重新匹配")
        if not isinstance(queue_type, str) or queue_type not in self.queues:
            return self._reply("join", user_id, False, "无效的匹配类型")
        blocked = self._join_block_reason(player, user_id)
        if blocked:
            return self._reply("join", user_id, False, blocked)
        existing = self.user_to_queues.get(user_id, [])
        if queue_type in existing:
            return self._reply("join", user_id, True, "已在此场次匹配中")
        if len(existing) >= self.MAX_QUEUES:
            return self._reply("join", user_id, False, "最多同时匹配 4 个场次")

        await self.game_server.gamestate_manager.remove_spectator_from_all_games(user_id)

        # Recheck exclusivity and disconnects after spectator cleanup yields.
        if (self.game_server.players.get(connect_id) is not player
                or self.disconnect_epochs.get(user_id, 0) != epoch):
            return self._reply("join", user_id, False, "连接已断开，请重新匹配")
        blocked = self._join_block_reason(player, user_id)
        if blocked:
            return self._reply("join", user_id, False, blocked)
        spec = QUEUES[queue_type]
        tier = spec.tier
        rank_data = self.game_server.db_manager.get_rank_data(user_id)
        if rank_data is None:
            return self._reply("join", user_id, False, "无法读取评分，请稍后重试")
        rating = (rank_data or {}).get('ratings', {}).get(spec.rule, default_rating(spec.rule))
        privileges = self.game_server.db_manager.get_user_sponsor_mcrpl(user_id) or {}
        rank_name = (rank_data or {}).get("guobiao_rank", "10级") if spec.rule == 'guobiao' else rating['rank_name']
        if spec.graded and not can_play_tier(
            rank_name, tier,
            is_mcrpl_qualified=privileges.get("is_mcrpl_qualified", False),
            is_beginner_qualified=privileges.get("is_beginner_qualified", False),
            is_intermediate_qualified=privileges.get("is_intermediate_qualified", False),
            is_advanced_qualified=privileges.get("is_advanced_qualified", False),
        ):
            return self._reply("join", user_id, False, "段位不足，无法进入该场次")
        self.queues[queue_type].append(user_id)
        self.user_to_queues.setdefault(user_id, []).append(queue_type)
        self._changed(user_id)
        logger.info("玩家 %s 加入匹配队列 %s", user_id, queue_type)
        await self._try_start_match(queue_type)
        return self._reply("join", user_id, True, f"已加入 {queue_type_to_display_name(queue_type)} 匹配队列")

    def _remove_queues(self, user_id, queue_type=None):
        existing = self.user_to_queues.get(user_id, [])
        removed = [q for q in existing if queue_type is None or q == queue_type]
        for q in removed:
            if user_id in self.queues[q]:
                self.queues[q].remove(user_id)
        remaining = [q for q in existing if q not in removed]
        if remaining:
            self.user_to_queues[user_id] = remaining
        else:
            self.user_to_queues.pop(user_id, None)
        if removed:
            self._changed(user_id)

    async def leave_queue(self, connect_id: str, queue_type: Optional[str] = None) -> Response:
        player = self.game_server.players.get(connect_id)
        user_id = getattr(player, "user_id", None)
        if not user_id:
            return self._reply("leave", user_id, False, "请先登录")
        epoch = self.disconnect_epochs.get(user_id, 0)
        async with self.user_locks[user_id]:
            if (self.game_server.players.get(connect_id) is not player
                    or self.disconnect_epochs.get(user_id, 0) != epoch):
                return self._reply("leave", user_id, False, "连接已断开")
            if queue_type is not None and (not isinstance(queue_type, str) or queue_type not in self.queues):
                return self._reply("leave", user_id, False, "无效的匹配类型")
            if user_id in self.committed_users:
                return self._reply("leave", user_id, False, "已匹配到对局，无法取消")
            self._remove_queues(user_id, queue_type)
            return self._reply("leave", user_id, True, "已取消匹配")

    def player_disconnect(self, user_id: int):
        self.disconnect_epochs[user_id] = self.disconnect_epochs.get(user_id, 0) + 1
        self._remove_queues(user_id)

    def is_user_committed(self, user_id: int) -> bool:
        """玩家是否已匹配成功且对局尚未结束。"""
        return user_id in self.committed_users

    def is_user_in_queue(self, user_id: int) -> bool:
        """玩家是否仍在匹配等待队列中。"""
        return user_id in self.user_to_queues

    def blocks_spectator(self, user_id: int) -> bool:
        """An event seating reservation and any active game also block spectating."""
        room_manager = getattr(self.game_server, "room_manager", None)
        game_manager = getattr(self.game_server, "gamestate_manager", None)
        return (
            user_id in self.user_to_queues or user_id in self.committed_users
            or user_id in getattr(room_manager, "event_seating_users", ())
            or bool(game_manager and game_manager.is_user_in_active_game(user_id))
        )

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

    def get_my_match_state(self, user_id: Optional[int]) -> tuple[Optional[str], bool]:
        """当前玩家所在等待队列，以及是否已匹配成功且对局未结束。"""
        if not user_id:
            return None, False
        queues = self.user_to_queues.get(user_id, [])
        return (queues[0] if queues else None), user_id in self.committed_users

    # ==================== 匹配与开局 ====================

    async def _try_start_match(self, queue_type: str):
        """检查队列是否凑满 4 人，凑满则创建对局"""
        queue = self.queues[queue_type]
        if len(queue) < 4:
            return

        matched_users = queue[:4]
        # Commit all four and remove every competing queue before the first await.
        for uid in matched_users:
            self._remove_queues(uid)
            self.committed_users.add(uid)
            self.winning_queues[uid] = queue_type
            self._changed(uid)
        for uid in matched_users:
            try:
                await self.game_server.gamestate_manager.remove_spectator_from_all_games(uid)
            except Exception:
                logger.exception("匹配后清理观战失败: %s", uid)

        logger.info(f"匹配成功: {queue_type}, 玩家: {matched_users}")

        # 通知客户端匹配成功，随后立即创建对局。
        display_name = queue_type_to_display_name(queue_type)
        for uid in matched_users:
            match_found_response = Response(
                type="match/match_found", success=True, message=display_name,
                **self.snapshot(uid))
            conn = self.game_server.user_id_to_connection.get(uid)
            if conn:
                try:
                    await conn.websocket.send_json(match_found_response.dict(exclude_none=True))
                except Exception as e:
                    logger.error(f"通知玩家 {uid} 匹配成功失败: {e}")

        asyncio.create_task(self._start_game(queue_type, matched_users))

    async def _start_game(self, queue_type: str, user_ids: List[int]):
        """直接创建对局并启动（不依赖房间系统）。

        匹配对局不再写入 room_manager.rooms，因此不会出现在房间列表、也无法被加入；
        仅向 RoomManager 申请一个唯一房间号用于对局内部映射键与客户端聊天频道。
        """
        try:
            # A disconnect while notifying players has no GameState to notify yet.
            # Apply the same all-offline cleanup policy before creating the game.
            if not any(uid in self.game_server.user_id_to_connection for uid in user_ids):
                for uid in user_ids:
                    self.committed_users.discard(uid)
                    self.winning_queues.pop(uid, None)
                    self._changed(uid)
                logger.info("匹配开局前全员掉线，已取消开局: %s", user_ids)
                return

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
                **room_config,
                "room_id": room_id,
                "room_type": "match",
                "room_rule": QUEUES[queue_type].rule,
                "sub_rule": room_config["sub_rule"],
                "hepai_limit": room_config["hepai_limit"],
                "tourist_limit": True,
                "allow_spectator": True,
                "max_player": 4,
                "player_list": list(user_ids),
                "player_settings": player_settings,
                "has_password": False,
                "tips": room_config["tips"],
                "count_tips": room_config["count_tips"],
                "pointer_tips": room_config["pointer_tips"],
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
                "claim_protection": room_config.get("claim_protection", True),
                "match_queue_type": queue_type,
                "match_tier": QUEUES[queue_type].tier,
            }

            # 通过 gamestate_manager 创建 GuobiaoGameState（匹配对局不设置 current_room_id，
            # 玩家“是否忙碌”由对局映射 + committed 锁共同保证）
            from ..gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState
            from ..gamestate.game_riichi.RiichiGameState import RiichiGameState
            from ..gamestate.game_mmcr.QingqueGameState import QingqueGameState
            from ..gamestate.game_sichuan.SichuanGameState import SichuanGameState
            state_class = dict(guobiao=GuobiaoGameState, riichi=RiichiGameState,
                               qingque=QingqueGameState, sichuan=SichuanGameState)[room_data['room_rule']]
            gamestate_id = str(uuid.uuid4())
            game_state = state_class(
                self.game_server,
                room_data,
                self.game_server.calculation_service,
                self.game_server.db_manager,
                gamestate_id,
            )
            game_state.match_queue_type = queue_type
            for player in game_state.player_list:
                if player.user_id not in self.game_server.user_id_to_connection:
                    player.tag_list.append("offline")

            gsm = self.game_server.gamestate_manager
            gsm.register_game(game_state, room_data)

            # 登记匹配会话，并累加游戏中人数。释放统一在对局清理时通过 release_match 完成。
            self.gamestate_to_match[gamestate_id] = {
                "queue_type": queue_type,
                "user_ids": list(user_ids),
                "room_id": room_id,
            }
            self.playing_counts[queue_type] = self.playing_counts.get(queue_type, 0) + len(user_ids)

            game_state.game_task = asyncio.create_task(gsm.run_game(game_state, game_state.run_game_loop))
            logger.info(f"排位匹配对局已创建，room_id={room_id}, queue_type={queue_type}, gamestate_id={gamestate_id}")
        except Exception as e:
            logger.error(f"创建排位匹配对局失败，queue_type={queue_type}, 玩家={user_ids}, 错误: {e}", exc_info=True)
            if "game_state" in locals() and self.game_server.gamestate_manager.gamestate_id_to_game_state.get(game_state.gamestate_id) is game_state:
                await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=game_state.gamestate_id, reason="start_failed")
            # 开局失败：释放承诺锁与已分配的房间号，避免玩家被永久锁定
            for uid in user_ids:
                self.committed_users.discard(uid)
                self.winning_queues.pop(uid, None)
                self._changed(uid)
            try:
                if "room_id" in locals():
                    self.game_server.room_manager.release_match_room_id(room_id)
            except Exception:
                pass
            # 通知玩家匹配失败，让其可重新匹配
            for uid in user_ids:
                fail_response = Response(type="match/failed", success=False, message="匹配开局失败，请重新匹配", **self.snapshot(uid))
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
            self.winning_queues.pop(uid, None)
            self._changed(uid)
        if queue_type in self.playing_counts:
            self.playing_counts[queue_type] = max(0, self.playing_counts[queue_type] - len(user_ids))
        if room_id is not None:
            self.game_server.room_manager.release_match_room_id(room_id)
        logger.info(f"匹配对局结束，已释放匹配状态：gamestate_id={gamestate_id}, 玩家={user_ids}")
