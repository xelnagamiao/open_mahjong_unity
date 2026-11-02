"""Qingque custom games may continue until a player requests an end after a hand."""
import logging
import asyncio
from time import monotonic

from ...response import Response

logger = logging.getLogger(__name__)


class UnlimitedSession:
    RETURN_NOTICE_SECONDS = 5.0

    def __init__(self, game_state):
        self.gs = game_state
        self.withdrawn = {}  # user_id -> username; seats may change between hands.
        self.returned_player = None
        self.return_notice_until = 0.0
        self.revision = 0
        self.frozen = False
        self.forced_end = False
        self.end_requested = asyncio.Event()

    @property
    def enabled(self):
        return (self.gs.room_rule == "qingque" and self.gs.unlimited_rounds
                and (self.gs.room_type == "custom" or
                     getattr(self.gs, 'match_queue_type', None) == 'qingque_elo_unlimited'))

    def snapshot(self):
        countdown = max(0.0, self.return_notice_until - monotonic())
        return {
            "revision": self.revision,
            "enabled": self.enabled,
            "finishing": self.frozen,
            "current_round": self.gs.current_round,
            "withdrawn_players": [dict(user_id=uid, username=name)
                                  for uid, name in self.withdrawn.items()],
            "returned_player": self.returned_player if countdown > 0 else None,
            "countdown": countdown,
        }

    def should_end_after_round(self):
        if self.forced_end:
            return True
        if self.enabled:
            return bool(self.withdrawn)
        return self.gs.current_round >= self.gs.max_round * 4

    def freeze(self):
        if not self.frozen:
            self.frozen = True
            self.revision += 1

    def stop_after_disconnect(self):
        self.forced_end = True
        self.end_requested.set()

    async def wait_for_continuation(self, awaitable):
        """A stop request also interrupts ready/animation waits, never a hand."""
        if not self.enabled and not self.forced_end:
            return await awaitable
        ready = asyncio.create_task(awaitable)
        stopped = asyncio.create_task(self.end_requested.wait())
        try:
            done, _ = await asyncio.wait((ready, stopped), return_when=asyncio.FIRST_COMPLETED)
            if ready in done:
                return await ready
        finally:
            for task in (ready, stopped):
                if not task.done():
                    task.cancel()
            await asyncio.gather(ready, stopped, return_exceptions=True)

    async def set_withdrawn(self, user_id, withdrawn):
        if not self.enabled:
            return False, "仅青雀无固定时限房间支持此操作"
        if type(withdrawn) is not bool:
            return False, "结束游戏状态必须为布尔值"
        player = next((p for p in self.gs.player_list if p.user_id == user_id and p.user_id > 10), None)
        if player is None:
            return False, "只有本局玩家可以结束或回归游戏"
        blocked_statuses = ("waiting", "finished") if self.gs.room_type == 'match' else ("waiting", "END", "waiting_ready", "finished")
        if (getattr(self.gs, "lifecycle_state", "running") != "running" or self.frozen
                or self.gs.game_status in blocked_statuses
                or getattr(self.gs, "next_status", None) == "match_end"):
            return False, "本局正在结算或尚未开始，请在行牌期间操作"

        # Set an explicit desired state so retries cannot toggle the player back.
        if withdrawn != (user_id in self.withdrawn):
            self.revision += 1
            if withdrawn:
                self.withdrawn[user_id] = player.username
                self.returned_player = None
                self.return_notice_until = 0.0
            else:
                del self.withdrawn[user_id]
                self.returned_player = dict(user_id=user_id, username=player.username)
                self.return_notice_until = monotonic() + self.RETURN_NOTICE_SECONDS
        if self.withdrawn or self.forced_end:
            self.end_requested.set()
        else:
            self.end_requested.clear()
        await self.broadcast()
        return True, ""

    async def broadcast(self):
        response = Response(type="gamestate/unlimited_update", success=True, message="无固定时限状态更新",
                            gamestate_id=self.gs.gamestate_id,
                            unlimited_info=self.snapshot())
        payload = response.dict(exclude_none=True)
        for player in self.gs.player_list:
            connection = self.gs.game_server.user_id_to_connection.get(player.user_id)
            if player.user_id <= 10 or connection is None:
                continue
            try:
                await connection.websocket.send_json(payload)
            except Exception:
                logger.exception("同步无固定时限状态失败 user_id=%s", player.user_id)
            await self.gs.send_to_realtime_spectators(player.player_index, response)
