# 观战系统管理器 - 基于增量牌谱的延迟观战
import asyncio
import json
import logging
import time
from typing import Any, Dict, Optional

from .connection_delivery import send_bounded

logger = logging.getLogger(__name__)


class SpectatorManager:
    """基于牌谱增量的延迟观战管理器。

    action_ticks 与落库牌谱共用同一格式，由 game_record_manager.append_action_tick 写入；
    本类仅负责时间戳缓冲与观战者推送。ask_hand / ask_other 仍经 record_tick 单独记录。
    """

    def __init__(self, gamestate, delay: float = 180.0, enabled: bool = True):
        self.gamestate = gamestate
        self.spectator_delay = delay
        from ..public.spectator_rules import too_many_ai_for_spectator
        self.enabled = enabled and not too_many_ai_for_spectator(gamestate.player_list)
        self.spectator_connections: Dict[int, Any] = {}
        self.spectator_send_tasks: Dict[int, asyncio.Task] = {}
        self._joining_tasks: Dict[int, asyncio.Task] = {}
        self._admission_lock = asyncio.Lock()
        self._phase = "open"
        self.send_timeout = 5.0

        self.game_title: dict = {}
        self.players_settings: list = []
        self.round_headers: Dict[int, dict] = {}
        self.round_ticks: Dict[int, list] = {}
        self.current_round_index: int = 0
        self.spectator_progress: Dict[int, dict] = {}

    def record_game_title(self):
        if not self.enabled or self.game_title:
            return
        from ..public.game_record_manager import build_game_title_data

        gs = self.gamestate
        self.game_title = build_game_title_data(gs)
        self.players_settings = [
            {
                "user_id": player.user_id,
                "username": player.username,
                "title_used": player.title_used,
                "profile_used": player.profile_used,
                "avatar_frame_used": getattr(player, "avatar_frame_used", 0),
                "character_used": player.character_used,
                "voice_used": player.voice_used,
            }
            for player in gs.player_list
        ]

    def record_round_start(self):
        if not self.enabled:
            return
        from ..public.game_record_manager import build_round_header_data

        gs = self.gamestate
        round_idx = gs.round_index
        self.current_round_index = round_idx
        header = build_round_header_data(gs)
        header_filter = getattr(gs, "spectator_round_header", None)
        self.round_headers[round_idx] = {
            "timestamp": time.time(),
            "data": header_filter(header) if callable(header_filter) else header,
        }
        self.round_ticks[round_idx] = []

    def record_tick(self, tick):
        """记录一条带时间戳的观战 tick（由 append_action_tick 或 ask 事件调用）。"""
        if not self.enabled:
            return
        round_idx = self.current_round_index
        if round_idx not in self.round_ticks:
            self.round_ticks[round_idx] = []
        self.round_ticks[round_idx].append({
            "timestamp": time.time(),
            "tick": tick,
        })

    def record_ask_hand(self, player_index: int, action_list: list):
        if not self.enabled:
            return
        self.record_tick(["ask_hand", player_index, ",".join(action_list)])

    def record_ask_other(self, player_action_map: dict, cut_tile: int):
        if not self.enabled:
            return
        info = ";".join(f"{idx}:{','.join(actions)}" for idx, actions in player_action_map.items())
        self.record_tick(["ask_other", cut_tile, info])

    async def add_spectator(self, user_id: int, connection: Any):
        """Own initial delivery as well as the incremental stream until closure."""
        from ...response import Response

        # Replacing a subscription includes awaiting its cancelled writer. Keep
        # that admission step atomic so simultaneous reconnects cannot orphan a
        # second stream. Initial network delivery runs after releasing the lock.
        async with self._admission_lock:
            await self.remove_spectator(user_id)
            accepting = self._phase == "open"
            if accepting:
                task = asyncio.create_task(self._initialize_spectator(user_id, connection))
                self._joining_tasks[user_id] = task
        if not accepting:
            try:
                await self._send(connection, Response(type="spectator/add_spectator",
                    success=False, message="对局已结束，无法加入观战"))
            except Exception:
                logger.debug("发送观战结束提示失败 user_id=%s", user_id, exc_info=True)
            return
        try:
            await task
        except asyncio.CancelledError:
            # Removing a subscription cancels its delivery, not the websocket
            # request handler that happened to await it. Preserve caller cancellation.
            if asyncio.current_task().cancelling():
                if self._joining_tasks.get(user_id) is task:
                    await self.remove_spectator(user_id)
                raise
        finally:
            if self._joining_tasks.get(user_id) is task:
                self._joining_tasks.pop(user_id, None)

    async def _send(self, connection, response):
        await send_bounded(connection.websocket, response.dict(exclude_none=True), timeout=self.send_timeout)

    async def _initialize_spectator(self, user_id: int, connection: Any):
        from ...response import Response, MessageInfo

        if not self.enabled:
            msg = "当前对局包含机器人，禁用观战"
            response = Response(
                type="spectator/add_spectator",
                success=False,
                message=msg,
                message_info=MessageInfo(title="观战不可用", content=msg),
            )
            try:
                await self._send(connection, response)
            except Exception as e:
                logger.error(f"发送观战禁用提示失败: {e}")
            return

        target_time = time.time() - self.spectator_delay
        record = self._build_spectator_record(target_time)

        if record is None:
            earliest_ts = min((h["timestamp"] for h in self.round_headers.values()), default=None)
            if earliest_ts:
                wait_sec = max(0, int(earliest_ts + self.spectator_delay - time.time()))
                msg = f"需要等待 {wait_sec} 秒后才能开始观战"
            else:
                msg = "对局尚未开始，无法观战"
            response = Response(
                type="spectator/add_spectator",
                success=False,
                message=msg,
                message_info=MessageInfo(title="观战延迟", content=msg),
            )
            try:
                await self._send(connection, response)
            except Exception as e:
                logger.error(f"发送观战等待提示失败: {e}")
            return

        self.spectator_connections[user_id] = connection

        last_round_idx = 0
        last_tick_count = 0
        for ri in sorted(self.round_headers.keys()):
            if self.round_headers[ri]["timestamp"] <= target_time:
                last_round_idx = ri
                ticks = self.round_ticks.get(ri, [])
                last_tick_count = sum(1 for t in ticks if t["timestamp"] <= target_time)
        self.spectator_progress[user_id] = {
            "round_index": last_round_idx,
            "tick_index": last_tick_count,
        }

        try:
            resp_ok = Response(type="spectator/add_spectator", success=True, message="已成功加入观战")
            await self._send(connection, resp_ok)
        except Exception as e:
            logger.error(f"发送观战成功响应失败: {e}")
            await self.remove_spectator(user_id)
            return

        try:
            record_json = json.dumps(record, ensure_ascii=False, default=str)
            init_resp = Response(
                type="spectator/record_init",
                success=True,
                message="观战初始数据",
                message_info=MessageInfo(
                    title=self.gamestate.gamestate_id, content=record_json
                ),
            )
            await self._send(connection, init_resp)
            logger.info(f"已向观战玩家 {user_id} 发送初始牌谱数据")
        except Exception as e:
            logger.error(f"发送观战初始数据失败: {e}")
            await self.remove_spectator(user_id)
            return

        if self._phase == "open":
            self.spectator_send_tasks[user_id] = asyncio.create_task(self._delivery_loop(user_id))

    @staticmethod
    async def _stop_tasks(tasks):
        pending = [task for task in tasks if task is not asyncio.current_task() and not task.done()]
        for task in pending:
            task.cancel()
        if pending:
            await asyncio.gather(*pending, return_exceptions=True)

    async def remove_spectator(self, user_id: int):
        self.spectator_connections.pop(user_id, None)
        self.spectator_progress.pop(user_id, None)
        tasks = [group.pop(user_id, None) for group in (self._joining_tasks, self.spectator_send_tasks)]
        await self._stop_tasks(task for task in tasks if task is not None)
        logger.info(f"移除观战玩家 {user_id}")

    async def cleanup(self):
        self._phase = "closed"
        tasks = list(self._joining_tasks.values()) + list(self.spectator_send_tasks.values())
        self._joining_tasks.clear()
        self.spectator_send_tasks.clear()
        self.spectator_connections.clear()
        self.spectator_progress.clear()
        await self._stop_tasks(tasks)
        logger.info(f"已清理观战管理器，room_id: {self.gamestate.room_id}")

    async def send_final_record_and_close(self):
        from ...response import Response, MessageInfo

        if self._phase != "open":
            return
        self._phase = "finishing"
        try:
            # A final record is meaningful only after record_init. Finish the
            # bounded initial sends, then stop every incremental writer first.
            if self._joining_tasks:
                await asyncio.gather(*list(self._joining_tasks.values()), return_exceptions=True)
            await self._stop_tasks(list(self.spectator_send_tasks.values()))
            self.spectator_send_tasks.clear()
            if self._phase != "finishing" or not self.enabled:
                return
            final_record = self._build_full_record()
            if final_record is None:
                return
            resp = Response(type="spectator/record_complete", success=True,
                message="游戏对局结束，已获取全部对局记录",
                message_info=MessageInfo(title=self.gamestate.gamestate_id,
                    content=json.dumps(final_record, ensure_ascii=False, default=str)))

            async def send_final(user_id, connection):
                try:
                    await self._send(connection, resp)
                except Exception as exc:
                    logger.warning("发送完整牌谱失败 user_id=%s: %s", user_id, exc)

            # One slow viewer must not hold up other viewers or match release.
            await asyncio.gather(*(send_final(uid, conn)
                for uid, conn in list(self.spectator_connections.items())))
        finally:
            await self.cleanup()

    def _build_spectator_record(self, target_time: float) -> Optional[dict]:
        if not self.game_title or not self.round_headers:
            return None

        record = {
            "game_title": {**self.game_title},
            "players_settings": self.players_settings,
            "game_round": {},
        }

        found_any = False
        for ri in sorted(self.round_headers.keys()):
            header = self.round_headers[ri]
            if header["timestamp"] > target_time:
                break

            round_data = {**header["data"], "action_ticks": []}
            for entry in self.round_ticks.get(ri, []):
                if entry["timestamp"] <= target_time:
                    round_data["action_ticks"].append(entry["tick"])
                else:
                    break
            record["game_round"][f"round_index_{ri}"] = round_data
            found_any = True

        return record if found_any else None

    def _build_full_record(self) -> Optional[dict]:
        if not self.game_title or not self.round_headers:
            return None

        from ..public.game_record_manager import apply_game_title_end_fields

        record = {
            "game_title": {**self.game_title},
            "players_settings": self.players_settings,
            "game_round": {},
        }
        apply_game_title_end_fields(self.gamestate, record["game_title"])
        for ri in sorted(self.round_headers.keys()):
            header = self.round_headers[ri]
            round_data = {**header["data"], "action_ticks": []}
            for entry in self.round_ticks.get(ri, []):
                round_data["action_ticks"].append(entry["tick"])
            record["game_round"][f"round_index_{ri}"] = round_data
        complete = getattr(self.gamestate, "complete_spectator_record", None)
        return complete(record) if callable(complete) else record

    def _get_new_updates(self, user_id: int, target_time: float) -> Optional[list]:
        progress = self.spectator_progress.get(user_id)
        if not progress:
            return None

        updates = []
        for ri in sorted(self.round_headers.keys()):
            header = self.round_headers[ri]

            if ri > progress["round_index"] and header["timestamp"] <= target_time:
                round_data = {**header["data"], "action_ticks": []}
                for entry in self.round_ticks.get(ri, []):
                    if entry["timestamp"] <= target_time:
                        round_data["action_ticks"].append(entry["tick"])
                updates.append({"type": "new_round", "round_index": ri, "round_data": round_data})
                progress["round_index"] = ri
                progress["tick_index"] = len(round_data["action_ticks"])

            elif ri == progress["round_index"]:
                ticks = self.round_ticks.get(ri, [])
                start = progress["tick_index"]
                new_ticks = [t["tick"] for t in ticks[start:] if t["timestamp"] <= target_time]
                if new_ticks:
                    updates.append({"type": "ticks", "round_index": ri, "new_ticks": new_ticks})
                    progress["tick_index"] = start + len(new_ticks)

        return updates or None

    async def _delivery_loop(self, user_id: int):
        from ...response import Response, MessageInfo

        try:
            while user_id in self.spectator_connections:
                target_time = time.time() - self.spectator_delay
                updates = self._get_new_updates(user_id, target_time)

                if updates:
                    try:
                        update_json = json.dumps(updates, ensure_ascii=False, default=str)
                        resp = Response(
                            type="spectator/record_update",
                            success=True,
                            message="观战增量数据",
                            message_info=MessageInfo(
                                title=self.gamestate.gamestate_id, content=update_json
                            ),
                        )
                        conn = self.spectator_connections.get(user_id)
                        if conn:
                            await self._send(conn, resp)
                    except Exception as e:
                        logger.error(f"向观战玩家 {user_id} 发送增量数据失败: {e}")
                        await self.remove_spectator(user_id)
                        return

                await asyncio.sleep(1.0)
        except asyncio.CancelledError:
            logger.info(f"观战玩家 {user_id} 的发送任务已取消")
        except Exception as e:
            logger.error(f"观战玩家 {user_id} 的发送任务出错: {e}")
        finally:
            if self.spectator_send_tasks.get(user_id) is asyncio.current_task():
                self.spectator_send_tasks.pop(user_id, None)
