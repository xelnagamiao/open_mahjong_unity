"""
FriendManager —— 管理实时观战请求、限频与游戏内的实时观战者注册表。

并发模型：所有方法都跑在主事件循环里，靠 asyncio.create_task 起超时计时器。
观战者本身存放在每个 GameState 的 game_state.realtime_spectators（List[RealtimeSpectator]）。
"""
from __future__ import annotations

import asyncio
import logging
import time
import uuid
from dataclasses import dataclass, field
from typing import Dict, List, Optional, TYPE_CHECKING

from ..response import Response, RealtimeSpectatorEntry

if TYPE_CHECKING:
    from ..server import GameServer  # noqa: F401

logger = logging.getLogger(__name__)


REALTIME_REQUEST_TIMEOUT_SECONDS = 10.0
REALTIME_REQUEST_COOLDOWN_SECONDS = 60.0


@dataclass
class RealtimeSpectator:
    """挂在 game_state.realtime_spectators 上的一个实时观战者条目。"""
    user_id: int
    username: str
    player_index: int  # 实时观战者"挂在"哪个座位（接收该座位的全部广播）


@dataclass
class PendingRealtimeRequest:
    """A 发起的尚未响应的实时观战请求。"""
    request_id: str
    from_user_id: int
    from_username: str
    to_user_id: int
    to_username: str
    gamestate_id: str
    player_index: int      # B 在该 game 中的座位 index
    created_at: float
    timer_task: Optional[asyncio.Task] = field(default=None, repr=False)


class FriendManager:
    """好友/关注的运行时状态管理（持久化部分在 DatabaseManager）。"""

    def __init__(self, game_server: "GameServer"):
        self.game_server = game_server
        # 每用户最近一次发起实时观战请求的时间戳
        self.last_request_at: Dict[int, float] = {}
        # request_id -> PendingRealtimeRequest
        self.pending_requests: Dict[str, PendingRealtimeRequest] = {}
        # from_user_id -> request_id（同一时刻每个用户只允许一个未响应请求）
        self.outgoing_by_from: Dict[int, str] = {}

    # ----------------- 工具 -----------------

    def _now(self) -> float:
        return time.time()

    async def _send_to_user(self, user_id: int, response: Response) -> bool:
        """通过 user_id 找连接发送一条 Response，找不到返回 False。"""
        conn = self.game_server.user_id_to_connection.get(user_id)
        if not conn:
            return False
        try:
            await conn.websocket.send_json(response.dict(exclude_none=True))
            return True
        except Exception as exc:  # pragma: no cover
            logger.warning(f"FriendManager._send_to_user 发送失败 user_id={user_id}: {exc}")
            return False

    # ----------------- 好友列表 -----------------

    def _compute_state(self, friend_user_id: int) -> Dict[str, Optional[str]]:
        """返回 {state, gamestate_id} ：'in_game' / 'online' / 'offline'。"""
        conn = self.game_server.user_id_to_connection.get(friend_user_id)
        if conn is None:
            return {"state": "offline", "gamestate_id": None}
        game_state = self.game_server.gamestate_manager.get_game_state_by_user_id(friend_user_id)
        if game_state is None:
            return {"state": "online", "gamestate_id": None}
        # 含 bot/未开启观战的对局也不让观战，参考 get_spectator_list
        if hasattr(game_state, "spectator_enabled") and not game_state.spectator_enabled:
            return {"state": "online", "gamestate_id": None}
        if hasattr(game_state, "player_list") and any(
            p.user_id <= 10 for p in game_state.player_list
        ):
            return {"state": "online", "gamestate_id": None}
        return {"state": "in_game", "gamestate_id": getattr(game_state, "gamestate_id", None)}

    def build_friend_list_payload(self, user_id: int) -> List[Dict]:
        """读取数据库 + 在线状态，组装 FriendInfo 列表。"""
        rows = self.game_server.db_manager.list_friends(user_id)
        result: List[Dict] = []
        for row in rows:
            state_info = self._compute_state(int(row["user_id"]))
            result.append(
                {
                    "user_id": int(row["user_id"]),
                    "username": row.get("username", ""),
                    "profile_image_id": int(row.get("profile_image_id") or 1),
                    "state": state_info["state"],
                    "gamestate_id": state_info["gamestate_id"],
                }
            )
        return result

    # ----------------- 实时观战请求生命周期 -----------------

    def _find_player_index(self, game_state, target_user_id: int) -> Optional[int]:
        if not hasattr(game_state, "player_list"):
            return None
        for p in game_state.player_list:
            if getattr(p, "user_id", None) == target_user_id:
                return getattr(p, "player_index", None)
        return None

    async def request_realtime(self, from_user_id: int, from_username: str, target_user_id: int) -> Response:
        """A 发起请求：校验后给 B 推送询问、起 10s 超时。"""
        if from_user_id == target_user_id:
            return Response(
                type="friend/realtime_request_result",
                success=False,
                message="不能实时观战自己",
            )

        # 限频
        last = self.last_request_at.get(from_user_id, 0.0)
        elapsed = self._now() - last
        if elapsed < REALTIME_REQUEST_COOLDOWN_SECONDS:
            return Response(
                type="friend/realtime_request_result",
                success=False,
                message=f"请求过于频繁，每分钟仅可发起一次（剩余 {int(REALTIME_REQUEST_COOLDOWN_SECONDS - elapsed)} 秒）",
            )

        # 目标必须在线且在游戏中
        target_conn = self.game_server.user_id_to_connection.get(target_user_id)
        if target_conn is None:
            return Response(
                type="friend/realtime_request_result",
                success=False,
                message="对方当前不在线",
            )
        game_state = self.game_server.gamestate_manager.get_game_state_by_user_id(target_user_id)
        if game_state is None:
            return Response(
                type="friend/realtime_request_result",
                success=False,
                message="对方当前不在游戏中",
            )
        if hasattr(game_state, "spectator_enabled") and not game_state.spectator_enabled:
            return Response(
                type="friend/realtime_request_result",
                success=False,
                message="该对局不允许观战",
            )

        player_index = self._find_player_index(game_state, target_user_id)
        if player_index is None:
            return Response(
                type="friend/realtime_request_result",
                success=False,
                message="未找到对方在对局中的座位",
            )

        # 避免重复请求：如果已有未响应的请求，先撤销旧的
        old_request_id = self.outgoing_by_from.get(from_user_id)
        if old_request_id and old_request_id in self.pending_requests:
            await self._cancel_pending(old_request_id, notify_target=True)

        # 已经在该对局做实时观战者了？
        if any(
            sp.user_id == from_user_id
            for sp in getattr(game_state, "realtime_spectators", [])
        ):
            return Response(
                type="friend/realtime_request_result",
                success=False,
                message="您已在实时观战该对局",
            )

        # 标记限频
        self.last_request_at[from_user_id] = self._now()

        request_id = str(uuid.uuid4())
        target_username = getattr(target_conn, "username", "") or ""
        req = PendingRealtimeRequest(
            request_id=request_id,
            from_user_id=from_user_id,
            from_username=from_username,
            to_user_id=target_user_id,
            to_username=target_username,
            gamestate_id=getattr(game_state, "gamestate_id", ""),
            player_index=player_index,
            created_at=self._now(),
        )
        self.pending_requests[request_id] = req
        self.outgoing_by_from[from_user_id] = request_id

        # 推送给 B
        incoming = Response(
            type="friend/realtime_request_incoming",
            success=True,
            message="收到实时观战请求",
            realtime_request_id=request_id,
            realtime_from_user_id=from_user_id,
            realtime_from_username=from_username,
            realtime_gamestate_id=req.gamestate_id,
        )
        await self._send_to_user(target_user_id, incoming)

        # 起 10s 超时
        req.timer_task = asyncio.create_task(self._timeout_request(request_id))

        return Response(
            type="friend/realtime_request_result",
            success=True,
            message="已发起实时观战请求，等待对方回应",
            realtime_request_id=request_id,
            realtime_to_user_id=target_user_id,
            realtime_to_username=target_username,
        )

    async def _timeout_request(self, request_id: str):
        try:
            await asyncio.sleep(REALTIME_REQUEST_TIMEOUT_SECONDS)
        except asyncio.CancelledError:
            return
        req = self.pending_requests.pop(request_id, None)
        if req is None:
            return
        self.outgoing_by_from.pop(req.from_user_id, None)
        # 通知 A 超时
        await self._send_to_user(
            req.from_user_id,
            Response(
                type="friend/realtime_request_timeout",
                success=False,
                message="请求已超时，对方忽略了该请求",
                realtime_request_id=request_id,
            ),
        )
        # 也通知 B 收回弹窗（万一 B 还没操作）
        await self._send_to_user(
            req.to_user_id,
            Response(
                type="friend/realtime_request_revoked",
                success=False,
                message="请求已失效",
                realtime_request_id=request_id,
            ),
        )

    async def _cancel_pending(self, request_id: str, notify_target: bool):
        req = self.pending_requests.pop(request_id, None)
        if req is None:
            return
        self.outgoing_by_from.pop(req.from_user_id, None)
        if req.timer_task and not req.timer_task.done():
            req.timer_task.cancel()
        if notify_target:
            await self._send_to_user(
                req.to_user_id,
                Response(
                    type="friend/realtime_request_revoked",
                    success=False,
                    message="对方撤回了实时观战请求",
                    realtime_request_id=request_id,
                ),
            )

    async def cancel_request(self, from_user_id: int, request_id: str) -> Response:
        """A 主动撤回。"""
        req = self.pending_requests.get(request_id)
        if req is None:
            return Response(
                type="friend/realtime_request_cancel_result",
                success=False,
                message="请求不存在或已结束",
                realtime_request_id=request_id,
            )
        if req.from_user_id != from_user_id:
            return Response(
                type="friend/realtime_request_cancel_result",
                success=False,
                message="无权撤回该请求",
                realtime_request_id=request_id,
            )
        await self._cancel_pending(request_id, notify_target=True)
        return Response(
            type="friend/realtime_request_cancel_result",
            success=True,
            message="已撤回实时观战请求",
            realtime_request_id=request_id,
        )

    async def respond_request(self, responder_user_id: int, request_id: str, accept: bool) -> Response:
        """B 端响应。"""
        req = self.pending_requests.get(request_id)
        if req is None:
            return Response(
                type="friend/realtime_request_respond_result",
                success=False,
                message="请求不存在或已结束",
                realtime_request_id=request_id,
            )
        if req.to_user_id != responder_user_id:
            return Response(
                type="friend/realtime_request_respond_result",
                success=False,
                message="无权响应该请求",
                realtime_request_id=request_id,
            )

        # 取消计时器并出表
        if req.timer_task and not req.timer_task.done():
            req.timer_task.cancel()
        self.pending_requests.pop(request_id, None)
        self.outgoing_by_from.pop(req.from_user_id, None)

        if not accept:
            await self._send_to_user(
                req.from_user_id,
                Response(
                    type="friend/realtime_request_declined",
                    success=False,
                    message="对方拒绝了实时观战申请",
                    realtime_request_id=request_id,
                ),
            )
            return Response(
                type="friend/realtime_request_respond_result",
                success=True,
                message="已拒绝实时观战请求",
                realtime_request_id=request_id,
            )

        # 接受：把 A 加到 game_state.realtime_spectators
        game_state = self.game_server.gamestate_manager.get_game_state_by_gamestate_id(req.gamestate_id)
        if game_state is None:
            await self._send_to_user(
                req.from_user_id,
                Response(
                    type="friend/realtime_request_declined",
                    success=False,
                    message="对局已结束，无法开始实时观战",
                    realtime_request_id=request_id,
                ),
            )
            return Response(
                type="friend/realtime_request_respond_result",
                success=False,
                message="对局已结束",
                realtime_request_id=request_id,
            )

        spectators_list: List[RealtimeSpectator] = getattr(
            game_state, "realtime_spectators", []
        )
        # 防止重复加入
        if not any(sp.user_id == req.from_user_id for sp in spectators_list):
            spectators_list.append(
                RealtimeSpectator(
                    user_id=req.from_user_id,
                    username=req.from_username,
                    player_index=req.player_index,
                )
            )

        # 通知 A 开始
        await self._send_to_user(
            req.from_user_id,
            Response(
                type="friend/realtime_started",
                success=True,
                message="对方同意了实时观战申请",
                realtime_request_id=request_id,
                realtime_gamestate_id=req.gamestate_id,
                realtime_to_user_id=req.to_user_id,
                realtime_to_username=req.to_username,
            ),
        )

        # 通知所有座位玩家：刷新观战者列表
        await self.broadcast_realtime_spectators_changed(game_state)

        return Response(
            type="friend/realtime_request_respond_result",
            success=True,
            message="已开启实时观战",
            realtime_request_id=request_id,
        )

    # ----------------- 退出 / 踢出 / 清理 -----------------

    async def exit_realtime(self, user_id: int) -> Response:
        """A 主动退出当前所有实时观战。"""
        removed_any = False
        for game_state in list(self.game_server.gamestate_manager.gamestate_id_to_game_state.values()):
            spectators = getattr(game_state, "realtime_spectators", None)
            if not spectators:
                continue
            before = len(spectators)
            spectators[:] = [sp for sp in spectators if sp.user_id != user_id]
            if len(spectators) != before:
                removed_any = True
                await self.broadcast_realtime_spectators_changed(game_state)
        return Response(
            type="friend/realtime_exit_result",
            success=True,
            message="已退出实时观战" if removed_any else "您当前没有进行中的实时观战",
        )

    async def kick_realtime(self, host_user_id: int, spectator_user_id: int) -> Response:
        """B 端踢出一个实时观战者；仅可踢出挂在自己座位的观战者。"""
        game_state = self.game_server.gamestate_manager.get_game_state_by_user_id(host_user_id)
        if game_state is None:
            return Response(
                type="friend/realtime_kick_result",
                success=False,
                message="您当前不在游戏中",
            )
        host_player_index = self._find_player_index(game_state, host_user_id)
        if host_player_index is None:
            return Response(
                type="friend/realtime_kick_result",
                success=False,
                message="未找到您的座位",
            )

        spectators: List[RealtimeSpectator] = getattr(game_state, "realtime_spectators", [])
        target = next(
            (sp for sp in spectators if sp.user_id == spectator_user_id and sp.player_index == host_player_index),
            None,
        )
        if target is None:
            return Response(
                type="friend/realtime_kick_result",
                success=False,
                message="未找到该实时观战者",
            )
        spectators.remove(target)

        # 通知被踢出者
        await self._send_to_user(
            spectator_user_id,
            Response(
                type="friend/realtime_kicked",
                success=False,
                message="对方已结束您的实时观战",
            ),
        )
        # 刷新列表
        await self.broadcast_realtime_spectators_changed(game_state)
        return Response(
            type="friend/realtime_kick_result",
            success=True,
            message="已踢出实时观战者",
        )

    async def on_player_disconnect(self, user_id: int):
        """玩家断开时清理：作为实时观战者从所有游戏移除；作为对局玩家时整桌实时观战者也散场。"""
        for game_state in list(
            self.game_server.gamestate_manager.gamestate_id_to_game_state.values()
        ):
            spectators: List[RealtimeSpectator] = getattr(game_state, "realtime_spectators", [])
            if not spectators:
                continue
            # 1) 该 user_id 自己是观战者
            before_len = len(spectators)
            spectators[:] = [sp for sp in spectators if sp.user_id != user_id]
            removed_as_spectator = len(spectators) != before_len

            # 2) 该 user_id 是该对局的座位玩家 → 挂在其座位的所有观战者下机
            host_player_index = self._find_player_index(game_state, user_id)
            removed_as_host = False
            if host_player_index is not None:
                to_kick = [sp for sp in spectators if sp.player_index == host_player_index]
                if to_kick:
                    spectators[:] = [sp for sp in spectators if sp.player_index != host_player_index]
                    removed_as_host = True
                    for sp in to_kick:
                        await self._send_to_user(
                            sp.user_id,
                            Response(
                                type="friend/realtime_kicked",
                                success=False,
                                message="被观战玩家已离线，实时观战结束",
                            ),
                        )

            if removed_as_spectator or removed_as_host:
                await self.broadcast_realtime_spectators_changed(game_state)

        # 撤销 user_id 名下的待响应请求
        out_id = self.outgoing_by_from.get(user_id)
        if out_id:
            await self._cancel_pending(out_id, notify_target=True)
        # 撤销发往该 user 的待响应请求
        to_cancel = [
            rid for rid, r in list(self.pending_requests.items())
            if r.to_user_id == user_id
        ]
        for rid in to_cancel:
            req = self.pending_requests.pop(rid, None)
            if req is None:
                continue
            self.outgoing_by_from.pop(req.from_user_id, None)
            if req.timer_task and not req.timer_task.done():
                req.timer_task.cancel()
            await self._send_to_user(
                req.from_user_id,
                Response(
                    type="friend/realtime_request_declined",
                    success=False,
                    message="对方已离线",
                    realtime_request_id=rid,
                ),
            )

    async def on_game_end(self, game_state):
        """游戏结束时：通知挂着的实时观战者下机。"""
        spectators: List[RealtimeSpectator] = getattr(game_state, "realtime_spectators", [])
        if not spectators:
            return
        snapshot = list(spectators)
        spectators.clear()
        for sp in snapshot:
            await self._send_to_user(
                sp.user_id,
                Response(
                    type="friend/realtime_ended",
                    success=True,
                    message="实时观战的对局已结束",
                ),
            )

    # ----------------- 状态推送 -----------------

    async def broadcast_realtime_spectators_changed(self, game_state):
        """把当前实时观战者列表推给桌上所有座位玩家。"""
        spectators: List[RealtimeSpectator] = getattr(game_state, "realtime_spectators", [])
        entries = [RealtimeSpectatorEntry(user_id=sp.user_id, username=sp.username) for sp in spectators]
        payload = Response(
            type="friend/realtime_spectators_changed",
            success=True,
            message="实时观战者列表更新",
            realtime_spectators=entries,
            realtime_gamestate_id=getattr(game_state, "gamestate_id", None),
        )
        if not hasattr(game_state, "player_list"):
            return
        for p in game_state.player_list:
            uid = getattr(p, "user_id", None)
            if uid is None or uid <= 10:
                continue
            await self._send_to_user(uid, payload)
