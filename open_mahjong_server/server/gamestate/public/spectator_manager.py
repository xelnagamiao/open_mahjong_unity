# 观战系统管理器 - 基于增量牌谱的延迟观战
import asyncio
import json
import logging
import time
from typing import Any, Dict, Optional

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
        self.round_headers[round_idx] = {
            "timestamp": time.time(),
            "data": build_round_header_data(gs),
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

    def record_do_action_ticks(self, action_list, action_player, **kwargs):
        """已废弃：行动 tick 改由 player_action_record_* → append_action_tick 统一写入。"""
        return

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
                await connection.websocket.send_json(response.dict(exclude_none=True))
            except Exception as e:
                logger.error(f"发送观战禁用提示失败: {e}")
            return

        if user_id in self.spectator_connections:
            await self.remove_spectator(user_id)

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
                await connection.websocket.send_json(response.dict(exclude_none=True))
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
            await connection.websocket.send_json(resp_ok.dict(exclude_none=True))
        except Exception as e:
            logger.error(f"发送观战成功响应失败: {e}")
            return

        try:
            record_json = json.dumps(record, ensure_ascii=False, default=str)
            init_resp = Response(
                type="spectator/record_init",
                success=True,
                message="观战初始数据",
                message_info=MessageInfo(title="spectator_record", content=record_json),
            )
            await connection.websocket.send_json(init_resp.dict(exclude_none=True))
            logger.info(f"已向观战玩家 {user_id} 发送初始牌谱数据")
        except Exception as e:
            logger.error(f"发送观战初始数据失败: {e}")
            await self.remove_spectator(user_id)
            return

        task = asyncio.create_task(self._delivery_loop(user_id))
        self.spectator_send_tasks[user_id] = task

    async def remove_spectator(self, user_id: int):
        self.spectator_connections.pop(user_id, None)
        self.spectator_progress.pop(user_id, None)
        task = self.spectator_send_tasks.pop(user_id, None)
        if task and not task.done():
            task.cancel()
            try:
                await task
            except asyncio.CancelledError:
                pass
        logger.info(f"移除观战玩家 {user_id}")

    async def cleanup(self):
        for task in list(self.spectator_send_tasks.values()):
            if not task.done():
                task.cancel()
                try:
                    await task
                except asyncio.CancelledError:
                    pass
        self.spectator_send_tasks.clear()
        self.spectator_connections.clear()
        self.spectator_progress.clear()
        logger.info(f"已清理观战管理器，room_id: {self.gamestate.room_id}")

    async def send_final_record_and_close(self):
        from ...response import Response, MessageInfo

        if not self.enabled or not self.spectator_connections:
            return

        final_record = self._build_full_record()
        if final_record is None:
            return

        record_json = json.dumps(final_record, ensure_ascii=False, default=str)
        msg = "游戏对局结束，已获取全部对局记录"

        for user_id, conn in list(self.spectator_connections.items()):
            try:
                resp = Response(
                    type="spectator/record_complete",
                    success=True,
                    message=msg,
                    message_info=MessageInfo(title="spectator_record_final", content=record_json),
                )
                await conn.websocket.send_json(resp.dict(exclude_none=True))
            except Exception as e:
                logger.error(f"向观战玩家 {user_id} 发送完整牌谱失败: {e}")

        for task in list(self.spectator_send_tasks.values()):
            if not task.done():
                task.cancel()
                try:
                    await task
                except asyncio.CancelledError:
                    pass
        self.spectator_send_tasks.clear()
        self.spectator_progress.clear()
        self.spectator_connections.clear()

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
        return record

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
                            message_info=MessageInfo(title="spectator_update", content=update_json),
                        )
                        conn = self.spectator_connections.get(user_id)
                        if conn:
                            await conn.websocket.send_json(resp.dict(exclude_none=True))
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
            self.spectator_send_tasks.pop(user_id, None)
