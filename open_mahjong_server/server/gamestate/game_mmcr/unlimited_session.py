"""Qingque custom games may continue until a player requests an end after a hand."""
import logging
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

    @property
    def enabled(self):
        return (self.gs.room_rule == "qingque" and self.gs.room_type == "custom"
                and self.gs.unlimited_rounds)

    def snapshot(self):
        countdown = max(0.0, self.return_notice_until - monotonic())
        return {
            "revision": self.revision,
            "enabled": self.enabled,
            "withdrawn_players": [dict(user_id=uid, username=name)
                                  for uid, name in self.withdrawn.items()],
            "returned_player": self.returned_player if countdown > 0 else None,
            "countdown": countdown,
        }

    def should_end_after_round(self):
        if self.enabled:
            return bool(self.withdrawn)
        return self.gs.current_round >= self.gs.max_round * 4

    async def set_withdrawn(self, user_id, withdrawn):
        if not self.enabled:
            return False, "仅青雀无固定时限房间支持此操作"
        if type(withdrawn) is not bool:
            return False, "结束游戏状态必须为布尔值"
        player = next((p for p in self.gs.player_list if p.user_id == user_id and p.user_id > 10), None)
        if player is None:
            return False, "只有本局玩家可以结束或回归游戏"
        if (getattr(self.gs, "lifecycle_state", "running") != "running"
                or self.gs.game_status in ("waiting", "END", "waiting_ready", "finished")
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
