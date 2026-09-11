"""Server-owned event/base matchmaking; the management page need not stay open."""
import asyncio
import logging
from datetime import datetime, timezone

logger = logging.getLogger(__name__)


def event_auto_match_config(event: dict) -> dict:
    """Only use the independently saved snapshot, never the manual room settings."""
    import json

    settings = (event or {}).get("room_settings") or {}
    if isinstance(settings, str):
        try:
            settings = json.loads(settings)
        except (TypeError, ValueError):
            return {}
    if not isinstance(settings, dict):
        return {}
    config = settings.get("auto_match")
    return config if isinstance(config, dict) else {}


class EventAutoMatcher:
    def __init__(self, game_server, poll_seconds: float = 2.0):
        self.game_server = game_server
        self.poll_seconds = poll_seconds
        self._wake = asyncio.Event()
        self._task = None
        self._status = {}

    def start(self):
        if self._task is None or self._task.done():
            self._task = asyncio.create_task(self._run(), name="event-auto-match")

    async def stop(self):
        if self._task is not None:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass
            self._task = None

    def wake(self):
        self._wake.set()

    def status(self, event_id: str) -> dict:
        event = self.game_server.db_manager.get_event(event_id)
        config = event_auto_match_config(event)
        rows = self.game_server.db_manager.list_event_ready_players(event_id)
        manager = self.game_server.room_manager
        return {
            "worker_running": self._task is not None and not self._task.done(),
            "enabled": bool(event and event.get("status") == "active" and config.get("enabled") is True),
            "waiting_count": len(rows),
            "eligible_count": sum(manager.event_auto_player_eligible(event, row["user_id"]) for row in rows) if event else 0,
            "matched_tables": 0,
            "last_match_at": None,
            "last_error": "",
            **self._status.get(event_id, {}),
        }

    async def run_once(self):
        for event_id in self.game_server.db_manager.list_auto_match_event_ids():
            state = self._status.setdefault(event_id, {"matched_tables": 0, "last_error": ""})
            # Bound each pass so a large venue cannot starve the others.
            for _ in range(16):
                try:
                    result = await self.game_server.room_manager.match_event_ready_players(event_id)
                except asyncio.CancelledError:
                    raise
                except Exception:
                    logger.exception("场馆 %s 自动匹配失败", event_id)
                    state["last_error"] = "自动匹配失败，服务器将重试"
                    break
                if result is None:
                    state["last_error"] = ""
                    break
                if not result.success:
                    state["last_error"] = result.message or "自动组桌失败"
                    break
                state["matched_tables"] += 1
                state["last_match_at"] = datetime.now(timezone.utc).isoformat()
                state["last_error"] = ""

    async def _run(self):
        while True:
            self._wake.clear()
            try:
                await self.run_once()
            except asyncio.CancelledError:
                raise
            except Exception:
                logger.exception("赛事自动匹配执行失败，将在下一轮重试")
            try:
                await asyncio.wait_for(self._wake.wait(), timeout=self.poll_seconds)
            except asyncio.TimeoutError:
                pass
