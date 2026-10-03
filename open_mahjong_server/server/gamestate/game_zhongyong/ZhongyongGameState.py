from __future__ import annotations

import asyncio
import logging
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional

from .action_check import ZhongyongActionPolicy
from ..game_jiandan.init_tiles import init_jiandan_tiles
from .settlement import ZhongyongSettlementPolicy
from ..public.hand_slot_utils import has_draw_slot, pick_timeout_discard_tile
from ..public.logic_common import back_current_num
from ..public.random_seed_manager import derive_round_seed, setup_random_seed_system
from ..public.round_end_timing import liuju_ready_wait_seconds
from ..public.vote_manager import vote_checkpoint

logger = logging.getLogger(__name__)

_DEFAULT_CALCULATION_SERVICE: Any = None


def _default_calculation_service() -> Any:
    global _DEFAULT_CALCULATION_SERVICE
    if _DEFAULT_CALCULATION_SERVICE is None:
        from ...game_calculation.game_calculation_service import GameCalculationService

        _DEFAULT_CALCULATION_SERVICE = GameCalculationService()
    return _DEFAULT_CALCULATION_SERVICE


class RecordCounter:
    def __init__(self) -> None:
        self.fulu_times = 0
        self.recorded_fans = []
        self.rank_result = 0
        self.zimo_times = 0
        self.dianhe_times = 0
        self.fangchong_times = 0
        self.fangchong_score = 0
        self.win_turn = 0
        self.win_score = 0


@dataclass
class ZhongyongPlayer:
    user_id: int
    username: str
    remaining_time: int = 0
    hand_tiles: list[int] = field(default_factory=list)
    discard_tiles: list[int] = field(default_factory=list)
    discard_origin_tiles: list[int] = field(default_factory=list)
    combination_tiles: list[str] = field(default_factory=list)
    combination_mask: list = field(default_factory=list)
    score: int = 0
    player_index: int = 0
    original_player_index: int = 0
    tag_list: list[str] = field(default_factory=list)
    waiting_tiles: set[int] = field(default_factory=set)
    is_hu: bool = False
    hu_order: int = 0
    has_draw_slot: bool = False
    discard_win_lockout_tiles: set[int] = field(default_factory=set)
    record_counter: RecordCounter = field(default_factory=RecordCounter)
    score_history: list[str] = field(default_factory=list)
    round_number_history: list[int] = field(default_factory=list)
    title_used: int = 1
    profile_used: int = 1
    character_used: int = 1
    voice_used: int = 1

    @property
    def is_bot(self) -> bool:
        return self.user_id <= 10

    def get_tile(self, tiles_list: list[int], *, mark_draw_slot: bool = True) -> int:
        tile = tiles_list.pop(0)
        self.hand_tiles.append(tile)
        if mark_draw_slot:
            self.has_draw_slot = True
        return tile

    def reset_for_round(self, round_time: int) -> None:
        self.hand_tiles = []
        self.discard_tiles = []
        self.discard_origin_tiles = []
        self.combination_tiles = []
        self.combination_mask = []
        self.tag_list = []
        self.waiting_tiles = set()
        self.is_hu = False
        self.hu_order = 0
        self.has_draw_slot = False
        self.discard_win_lockout_tiles = set()
        self.remaining_time = round_time


from .hand_flow import HandFlow
from .recording import RoundRecording


class ZhongyongGameState(HandFlow, RoundRecording):
    """Zhongyong family: authoritative action windows with separate scoring and hand flow.

    Standard Zhongyong ends at the first winner; Nanque retires winners until
    three have won. The room profile selects this behavior before play starts.
    """

    def __init__(
        self,
        game_server: Any = None,
        room_data: Optional[Dict[str, Any]] = None,
        calculation_service: Any = None,
        db_manager: Any = None,
        gamestate_id: str = "zhongyong-test",
    ):
        room_data = room_data or self._default_room_data()
        self.game_server = game_server
        self.calculation_service = calculation_service or _default_calculation_service()
        self.db_manager = db_manager
        self.gamestate_id = gamestate_id
        self.room_id = room_data["room_id"]
        self.tips = room_data.get("tips", False)
        self.count_tips = bool(room_data.get("count_tips", False))
        self.pointer_tips = bool(room_data.get("pointer_tips", True))
        self.max_round = room_data.get("game_round", 1)
        self.step_time = room_data.get("step_timer", 0)
        self.round_time = room_data.get("round_timer", 0)
        self.room_rule = room_data.get("room_rule", "zhongyong")
        self.room_type = room_data.get("room_type", "custom")
        self.sub_rule = room_data.get("sub_rule", "zhongyong/standard")
        self.is_nanque = self.sub_rule == "zhongyong/nanque"
        self.winner_limit = 3 if self.is_nanque else 1
        self.discard_log = []
        self.last_discard_offsets = [-1] * 4
        self._final_result_pending = False
        self.action_policy = ZhongyongActionPolicy()
        self.settlement_policy = ZhongyongSettlementPolicy()
        self.room_random_seed = room_data.get("random_seed", 0)
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(
            self.room_random_seed if self.room_random_seed else None
        )
        self.open_cuohe = False
        self.show_moqie_hint = False
        self.hepai_limit = 0
        self.tactical_call = False
        from ..public.claim_protection import room_claim_protection_enabled
        self.claim_protection = room_claim_protection_enabled(room_data, self.room_rule)
        self.claim_protect_delay = room_data.get("claim_protect_delay", 1.3)
        self.claim_meld_followup_gap = room_data.get("claim_meld_followup_gap", 0.7)
        self.claim_meld_post_gap = room_data.get("claim_meld_post_gap", 0.5)
        self.allow_spectator_config = room_data.get("allow_spectator", True)

        self.player_list: List[ZhongyongPlayer] = []
        player_settings = room_data.get("player_settings", {})
        for index, user_id in enumerate(room_data["player_list"]):
            settings = player_settings.get(user_id, {})
            username = settings.get("username", f"用户{user_id}")
            player = ZhongyongPlayer(user_id, username, room_data.get("round_timer", 0))
            player.player_index = index
            player.original_player_index = index
            player.title_used = settings.get("title_id", 1)
            player.profile_used = settings.get("profile_image_id", 1)
            player.character_used = settings.get("character_id", 1)
            player.voice_used = settings.get("voice_id", 1)
            self.player_list.append(player)

        from ..public.claim_protection import init_claim_protection_state
        from ..public.spectator_manager import SpectatorManager
        from ..public.spectator_rules import too_many_ai_for_spectator

        init_claim_protection_state(self)
        self.spectator_enabled = self.allow_spectator_config and not too_many_ai_for_spectator(self.player_list)
        self.spectator_manager = SpectatorManager(self, delay=180.0, enabled=self.spectator_enabled)
        self.realtime_spectators = []

        self.tiles_list: list[int] = []
        self.current_player_index = 0
        self.dealer_index = 0
        self.current_round = 1
        self.round_index = 1
        self.round_random_seed = self._derive_round_seed()
        self.hu_order_counter = 0
        self.deferred_hu_settlements: list[dict] = []
        self.deferred_scores_applied = False
        self.ended_by: str | None = None
        self.game_status = "waiting"
        self.dead_wall_count = 0 if self.is_nanque else 14
        self.server_action_tick = 0
        self.game_task: Optional[asyncio.Task] = None
        self.lifecycle_completed = False
        self.game_record: dict = {}
        self.player_action_tick = 0
        self.round_record_finalized = False
        self.bot_tasks: set[asyncio.Task] = set()
        self.bot_action_ticks: dict[int, int] = {}
        self.action_events: Dict[int, asyncio.Event] = {i: asyncio.Event() for i in range(4)}
        self.action_queues: Dict[int, asyncio.Queue] = {i: asyncio.Queue() for i in range(4)}
        self.waiting_players_list: list[int] = []
        self.action_dict: Dict[int, list[str]] = {i: [] for i in range(4)}
        self.hand_action_is_gang_draw: Dict[int, bool] = {i: False for i in range(4)}
        self.natural_draw_count: Dict[int, int] = {i: 0 for i in range(4)}
        self.opening_action_taken = False
        self.opening_flow_interrupted = False
        self.live_pending_window: Optional[dict] = None
        self.outbound_payloads: list[dict] = []
        self.outbound_send_cursor = 0
        self.websocket_sent_payloads: list[dict] = []
        self.action_priority: Dict[str, int] = {
            "hu_self": 6,
            "hu": 5,
            "angang": 3,
            "jiagang": 3,
            "peng": 2,
            "gang": 2,
            "chi_left": 1,
            "chi_mid": 1,
            "chi_right": 1,
            "pass": 0,
            "cut": 0,
        }

    async def run_game_loop(self) -> None:
        """Authoritative per-hand loop.

        This loop wires action windows to hidden-information-safe payload
        builders and sends them to connected players when a lightweight
        game_server connection map is available.
        """
        try:
            self.start_game_recording()
            while self.current_round <= self.max_round * 4:
                self.initialize_round()
                self.start_round_recording()
                self.emit_game_start_payloads()
                await self.flush_outbound_payloads()
                self.open_action_window(self.begin_hand_action(self.current_player_index))
                await self.flush_outbound_payloads()
                while self.game_status != "END":
                    await vote_checkpoint(self)
                    await self.resolve_action_window(timeout=self.estimated_action_window_timeout())
                    await self.flush_outbound_payloads()
                self.finalize_round_recording()
                await self.run_round_ready_phase(timeout=self.estimated_round_result_ready_timeout())
                if self.current_round >= self.max_round * 4:
                    break
                self.advance_round_after_ready()
                await self.flush_outbound_payloads()
            self.finalize_game_recording()
            await self.complete_game_lifecycle()
        except asyncio.CancelledError:
            logger.info("Jiandan game loop cancelled, room_id=%s", self.room_id)
            raise
        except Exception as exc:
            logger.error("Jiandan game loop failed, room_id=%s: %s", self.room_id, exc, exc_info=True)
            raise

    async def cleanup_game_state(self) -> None:
        """Cancel the live loop task and outstanding bot work."""
        from ..public.outbound_pipe import close_outbound_pipes
        close_outbound_pipes(self)
        from ..public.claim_protection import end_claim_protection_interval

        end_claim_protection_interval(self)
        await self.spectator_manager.cleanup()
        current_task = asyncio.current_task()
        if self.game_task and not self.game_task.done() and self.game_task is not current_task:
            self.game_task.cancel()
            try:
                await self.game_task
            except asyncio.CancelledError:
                logger.info("Jiandan game loop cancelled during cleanup, room_id=%s", self.room_id)
            except Exception as exc:
                logger.error("Error while cancelling jiandan game loop, room_id=%s: %s", self.room_id, exc)
        for task in list(self.bot_tasks):
            if not task.done():
                task.cancel()
        if self.bot_tasks:
            await asyncio.gather(*self.bot_tasks, return_exceptions=True)
            self.bot_tasks.clear()
        self.bot_action_ticks.clear()

    async def add_spectator(self, user_id: int, connection: Any) -> None:
        await self.spectator_manager.add_spectator(user_id, connection)

    async def remove_spectator(self, user_id: int) -> None:
        await self.spectator_manager.remove_spectator(user_id)

    async def send_to_realtime_spectators(self, player_index: int, payload: dict) -> None:
        from ..public.spectator_rules import deliver_realtime_spectator_message

        await deliver_realtime_spectator_message(self, player_index, payload)

    async def send_realtime_spectator_snapshot(self, spectator_user_id: int, view_player_index: int) -> None:
        """Restore the viewed seat, including a result panel while between hands."""
        if view_player_index < 0 or view_player_index >= len(self.player_list):
            return
        conn = None
        if self.game_server is not None:
            conn = getattr(self.game_server, "user_id_to_connection", {}).get(spectator_user_id)
        if conn is None or getattr(conn, "websocket", None) is None:
            return
        from ..public.outbound_pipe import drain_viewer
        await drain_viewer(self, view_player_index)
        payload = self.build_game_start_payload(view_player_index)
        await conn.websocket.send_json(payload)
        if self.game_status in {"END", "waiting_ready"}:
            await conn.websocket.send_json(self.build_final_settlement_payload(view_player_index))
            if self.game_status == "waiting_ready":
                from .boardcast import ready_status_payload
                await conn.websocket.send_json(ready_status_payload(self, view_player_index))
        else:
            pending = self.build_pending_action_payload(view_player_index)
            if pending is not None:
                await conn.websocket.send_json(pending)

    async def _deliver_claim_payload(self, viewer_index, payload):
        player = self.player_list[viewer_index]
        connection = (getattr(self.game_server, "user_id_to_connection", {}) or {}).get(player.user_id)
        if connection is not None and getattr(connection, "websocket", None) is not None:
            await connection.websocket.send_json(payload)
            self.websocket_sent_payloads.append(payload)
        await self.send_to_realtime_spectators(viewer_index, payload)

    async def _send_claim_protection_payload(self, viewer_index: int, payload: dict) -> None:
        from ..public.claim_protection import send_cut
        from ..public.outbound_pipe import send_to_viewer, schedule_viewer_send
        from ..public.claim_protection import take_post_meld_gap_delay
        info = payload.get("do_action_info") or {}
        async def deliver():
            await self._deliver_claim_payload(viewer_index, payload)
        if info.get("action_list") == ["cut"] and await send_cut(self, viewer_index, info, deliver):
            return
        gap = take_post_meld_gap_delay(self, viewer_index)
        hand_ask = payload.get("ask_hand_action_info") is not None
        if hand_ask and self.claim_protection and viewer_index != self.current_player_index:
            schedule_viewer_send(self, viewer_index, deliver, delay_before=gap)
        else:
            await send_to_viewer(self, viewer_index, deliver, delay_before=gap)

    @staticmethod
    async def _claim_protection_send_fn(game_state: Any, viewer_index: int, payload: dict) -> None:
        await game_state._send_claim_protection_payload(viewer_index, payload)

    def begin_claim_protection(self, action_dict: Dict[int, list[str]], action_player: int) -> None:
        from ..public.claim_protection import begin_claim_protection_interval

        begin_claim_protection_interval(self, action_dict, action_player)

    async def finish_claim_protection(self) -> None:
        from ..public.claim_protection import finalize_claim_protection

        await finalize_claim_protection(self, self._claim_protection_send_fn)

    async def player_disconnect(self, user_id: int) -> None:
        """Mark a player offline while preserving state for reconnect."""
        for player in self.player_list:
            if player.user_id == user_id:
                if "offline" not in player.tag_list:
                    player.tag_list.append("offline")
                break
        from ..public.lifecycle import close_if_all_humans_offline
        await close_if_all_humans_offline(self)

    async def player_reconnect(self, user_id: int) -> None:
        """Clear offline marker and restore the player's table with standard game messages."""
        for player in self.player_list:
            if player.user_id == user_id:
                from ..public.outbound_pipe import drain_viewer
                await drain_viewer(self, player.player_index)
                if "offline" in player.tag_list:
                    player.tag_list.remove("offline")
                await self.send_payload_to_player(
                    player.player_index,
                    self.build_game_start_payload(player.player_index),
                )
                if self.game_status in {"END", "waiting_ready"}:
                    await self.send_payload_to_player(
                        player.player_index,
                        self.build_final_settlement_payload(player.player_index),
                    )
                    if self.game_status == "waiting_ready":
                        from .boardcast import ready_status_payload
                        await self.send_payload_to_player(
                            player.player_index, ready_status_payload(self, player.player_index)
                        )
                else:
                    pending_payload = self.build_pending_action_payload(player.player_index)
                    if pending_payload is not None:
                        await self.send_payload_to_player(player.player_index, pending_payload)
                break

    async def submit_action(
        self,
        player_index: int,
        action_type: str,
        *,
        target_tile: Optional[int] = None,
        TileId: Optional[int] = None,
        cutIndex: int = -1,
        cutClass: bool = False,
    ) -> None:
        """Queue a client-style action for the currently waiting player."""
        if player_index not in self.waiting_players_list:
            raise ValueError(f"Player {player_index} is not waiting for action.")
        if action_type not in self.action_dict.get(player_index, []):
            raise ValueError(
                f"Action {action_type} is not legal for player {player_index}: "
                f"{self.action_dict.get(player_index, [])}"
            )
        await self.action_queues[player_index].put(
            {
                "player_index": player_index,
                "action_type": action_type,
                "target_tile": target_tile,
                "TileId": TileId,
                "cutIndex": cutIndex,
                "cutClass": cutClass,
            }
        )
        self.action_events[player_index].set()

    def _consume_time_bank(self, player_index: int, elapsed_seconds: float) -> None:
        """Deduct only the whole seconds used after the configured step time."""
        overtime = max(0, int(elapsed_seconds) - int(self.step_time or 0))
        if overtime <= 0:
            return
        player = self.player_list[player_index]
        player.remaining_time = max(0, int(player.remaining_time) - overtime)

    def _build_timeout_action(self, player_index: int) -> Optional[dict]:
        """Build the ordinary automatic action when one player's clock expires."""
        actions = self.action_dict.get(player_index, [])
        action_type = None
        tile = None
        cut_index = -1
        cut_class = False
        if "pass" in actions:
            action_type = "pass"
        elif "cut" in actions:
            action_type = "cut"
            player = self.player_list[player_index]
            tile = pick_timeout_discard_tile(player.hand_tiles) if player.hand_tiles else None
            cut_index = len(player.hand_tiles) - 1
            if tile is not None:
                for hand_index in range(len(player.hand_tiles) - 1, -1, -1):
                    if player.hand_tiles[hand_index] == tile:
                        cut_index = hand_index
                        break
            cut_class = bool(has_draw_slot(player))
        elif "ready" in actions:
            action_type = "ready"
        if action_type is None:
            return None
        if action_type != "ready":
            self.player_list[player_index].remaining_time = 0
        self.action_dict[player_index] = []
        if player_index in self.waiting_players_list:
            self.waiting_players_list.remove(player_index)
        return {
            "player_index": player_index,
            "action_type": action_type,
            "target_tile": None,
            "TileId": tile,
            "cutIndex": cut_index if action_type == "cut" else -1,
            "cutClass": cut_class,
            "is_timeout_action": action_type == "cut",
        }

    async def wait_action(self, timeout: Optional[float] = None) -> Dict[int, dict]:
        """Collect queued actions for the current action_dict.

        This adapter does
        not resolve priority or mutate hand state; the public driver helpers do
        that after the selected responses are known.
        """
        self.waiting_players_list = [idx for idx, actions in self.action_dict.items() if actions]
        for idx in range(4):
            self.action_events[idx].clear()
            while not self.action_queues[idx].empty():
                self.action_queues[idx].get_nowait()

        self.schedule_bot_actions()
        results: Dict[int, dict] = {}
        loop = asyncio.get_running_loop()
        started_at = loop.time()
        deadline = None if timeout is None else started_at + timeout
        is_ready_phase = self.game_status == "waiting_ready"
        player_deadlines = {
            idx: started_at + int(self.step_time or 0) + max(0, int(self.player_list[idx].remaining_time))
            for idx in self.waiting_players_list
        } if not is_ready_phase else {}
        while self.waiting_players_list:
            wait_tasks = {
                asyncio.create_task(self.action_events[idx].wait()): idx
                for idx in self.waiting_players_list
            }
            now = loop.time()
            next_deadlines = []
            if deadline is not None:
                next_deadlines.append(deadline)
            if not is_ready_phase:
                next_deadlines.extend(player_deadlines[idx] for idx in self.waiting_players_list)
            wait_timeout = None if not next_deadlines else max(0.0, min(next_deadlines) - now)
            try:
                done, pending = await asyncio.wait(
                    wait_tasks.keys(),
                    timeout=wait_timeout,
                    return_when=asyncio.FIRST_COMPLETED,
                )
            except asyncio.CancelledError:
                for task in wait_tasks:
                    task.cancel()
                await asyncio.gather(*wait_tasks, return_exceptions=True)
                raise
            for task in pending:
                task.cancel()
            if pending:
                await asyncio.gather(*pending, return_exceptions=True)
            for task in done:
                idx = wait_tasks[task]
                if not self.action_queues[idx].empty():
                    results[idx] = await self.action_queues[idx].get()
                    if not is_ready_phase:
                        self._consume_time_bank(idx, loop.time() - started_at)
                    self.action_dict[idx] = []
                    self.action_events[idx].clear()
                    self.waiting_players_list.remove(idx)
            now = loop.time()
            expired = []
            if not is_ready_phase:
                expired.extend(
                    idx for idx in self.waiting_players_list
                    if now >= player_deadlines[idx]
                )
            if deadline is not None and now >= deadline:
                expired.extend(idx for idx in self.waiting_players_list if idx not in expired)
            for idx in expired:
                timeout_action = self._build_timeout_action(idx)
                if timeout_action is not None:
                    results[idx] = timeout_action

        for idx in list(self.waiting_players_list):
            timeout_action = self._build_timeout_action(idx)
            if timeout_action is not None:
                results[idx] = timeout_action
        return results

    async def run_round_ready_phase(self, timeout: Optional[float] = None) -> Dict[int, dict]:
        """Wait for non-bot players to acknowledge the round result before the next hand."""
        self.game_status = "waiting_ready"
        self.action_dict = {}
        for player in self.player_list:
            if player.is_bot:
                self.action_dict[player.player_index] = []
            else:
                self.action_dict[player.player_index] = ["ready"]
        self.waiting_players_list = [idx for idx, actions in self.action_dict.items() if actions]
        self.server_action_tick += 1
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()
        if timeout is None:
            timeout = 8.0
        results = await self.wait_action(timeout=timeout)
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()
        return results

    def estimated_action_window_timeout(self) -> Optional[float]:
        """Use the same step-time plus round-time window as the established rules."""
        if not self.waiting_players_list:
            return None
        max_wait = 0
        for idx in self.waiting_players_list:
            player = self.player_list[idx]
            max_wait = max(max_wait, int(getattr(player, "remaining_time", 0)) + int(self.step_time or 0))
        return float(max_wait) if max_wait > 0 else None

    def estimated_round_result_ready_timeout(self) -> float:
        """Keep the ordinary single result panel open long enough to confirm."""
        if not self.deferred_hu_settlements:
            return liuju_ready_wait_seconds()
        from ..public.round_end_timing import hu_result_ready_wait_seconds
        count = len(self.deferred_hu_settlements[-1].get("fan_ids") or [])
        return hu_result_ready_wait_seconds(count, pre_panel_delay_sec=0 if self.is_nanque else None)

    def schedule_bot_actions(self) -> None:
        from ..game_jiandan.bot import jiandan_bot_action

        if self.game_status == "END":
            return
        for player_index in list(self.waiting_players_list):
            actions = self.action_dict.get(player_index, [])
            if not actions or not self.player_list[player_index].is_bot:
                continue
            if self.bot_action_ticks.get(player_index) == self.server_action_tick:
                continue
            task = asyncio.create_task(
                jiandan_bot_action(
                    self,
                    player_index,
                    list(actions),
                    self.game_status,
                    self.server_action_tick,
                )
            )
            self.bot_action_ticks[player_index] = self.server_action_tick
            self.bot_tasks.add(task)
            task.add_done_callback(self.bot_tasks.discard)

    def open_action_window(self, window: dict) -> dict:
        """Expose a driver window through live-loop action fields."""
        self.live_pending_window = window
        self.game_status = window.get("status", self.game_status)
        actions = window.get("actions") or {}
        self.action_dict = {idx: list(actions.get(idx, [])) for idx in range(4)}
        self.waiting_players_list = [idx for idx, items in self.action_dict.items() if items]
        self.server_action_tick += 1
        window["action_tick"] = self.server_action_tick
        self.emit_window_payloads(window)
        return window

    def emit_window_payloads(self, window: dict) -> list[dict]:
        """Build and queue payloads for the active action window."""
        from .boardcast import ask_action_payload, final_settlement_payloads

        payloads: list[dict] = []
        if window.get("status") == "END":
            self._final_result_pending = True
        else:
            cut_tile = window.get("tile") if window.get("status") == "waiting_action_after_cut" else None
            rob_kong_tile = window.get("tile") if window.get("status") == "waiting_action_qianggang" else None
            action_player_index = window.get("player")
            if action_player_index is None:
                action_player_index = self.current_player_index
            # 与青雀一致：行动窗口通知所有视角。只有实际需要决策的玩家
            # 携带非空 action_list；其他真人客户端仍可由通用 ask 路径同步
            # 当前行动者与服务端权威余牌数，无需规则专用客户端刷新逻辑。
            for idx in [action_player_index] + [i for i in range(4) if i != action_player_index]:
                # An empty response ask would disclose the still-hidden cut.
                if cut_tile is not None and not self.action_dict.get(idx):
                    continue
                payloads.append(
                    ask_action_payload(
                        self,
                        idx,
                        self.action_dict.get(idx, []),
                        action_player_index=action_player_index,
                        cut_tile=cut_tile,
                        rob_kong_tile=rob_kong_tile,
                    )
                )
        self.outbound_payloads.extend(payloads)
        return payloads

    def emit_visible_action_payloads(self, action_info: dict, *, reveal_final: bool = False) -> list[dict]:
        from .boardcast import visible_action_payload

        self.record_visible_action(action_info)
        if action_info.get("action") == "cut":
            from ..public.claim_protection import begin_discard
            begin_discard(self, action_info["player"])
        if action_info.get("action", "").startswith("hu"):
            if self.is_nanque:
                from .boardcast import mid_win_payload
                payloads = [mid_win_payload(self, idx, action_info["player"]) for idx in range(4)]
                self.outbound_payloads.extend(payloads)
                self.outbound_payloads.append({"_pause": 1.7})
                return payloads
            return []
        payloads = [
            {
                **visible_action_payload(self, idx, action_info, reveal_final=reveal_final),
                "player_index": idx,
            }
            for idx in range(4)
        ]
        self.outbound_payloads.extend(payloads)
        return payloads

    def _latest_hu_settlement_for(self, winner_index: int) -> Optional[dict]:
        for settlement in reversed(self.deferred_hu_settlements):
            if settlement.get("winner") == winner_index:
                return settlement
        return None









    def build_game_start_payload(self, player_index: int) -> dict:
        from .boardcast import game_start_payload

        return game_start_payload(self, player_index, reveal_final=self.game_status in {"END", "waiting_ready"})

    def emit_game_start_payloads(self) -> list[dict]:
        payloads = [self.build_game_start_payload(idx) for idx in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self, player_index: int) -> Optional[dict]:
        from .boardcast import pending_action_payload

        return pending_action_payload(self, player_index)

    def build_final_settlement_payload(self, player_index: int) -> dict:
        from .boardcast import final_settlement_payloads

        return final_settlement_payloads(self, player_index)[-1]

    def emit_ready_status_payloads(self) -> list[dict]:
        from .boardcast import ready_status_payload

        payloads = [ready_status_payload(self, idx) for idx in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def emit_game_end_payloads(self) -> list[dict]:
        from .boardcast import game_end_payload
        from ..public.logic_common import assign_strict_final_ranks

        assign_strict_final_ranks(list(self.player_list))
        payloads = [game_end_payload(self, idx) for idx in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    async def complete_game_lifecycle(self) -> None:
        if self.lifecycle_completed:
            return
        self.lifecycle_completed = True
        self.action_dict = {idx: [] for idx in range(4)}
        self.waiting_players_list = []
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()

        from ..public.logic_common import assign_strict_final_ranks
        from ..public.game_record_manager import remember_local_record_detail

        assign_strict_final_ranks(list(self.player_list))
        game_id = self.persist_game_record()
        remember_local_record_detail(self, game_id, f"{self.max_round}/4")

        self.emit_game_end_payloads()
        await self.flush_outbound_payloads()

        await self.spectator_manager.send_final_record_and_close()

        if self.game_server is None:
            return
        gamestate_manager = getattr(self.game_server, "gamestate_manager", None)
        if gamestate_manager is not None:
            await gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)


    async def flush_outbound_payloads(self) -> None:
        """Send newly recorded payloads to connected players where possible."""
        while self.outbound_send_cursor < len(self.outbound_payloads):
            payload = self.outbound_payloads[self.outbound_send_cursor]
            self.outbound_send_cursor += 1
            if "_pause" in payload:
                await asyncio.sleep(payload["_pause"])
                continue
            player_index = payload.get("player_index")
            if player_index is None:
                continue
            action_list = (payload.get("do_action_info") or {}).get("action_list") or []
            is_cut = bool(action_list) and action_list[0] == "cut"
            if is_cut and getattr(self, "_cp_active", False):
                from ..public.claim_protection import (
                    arm_claim_protection_timer,
                    is_protected_viewer,
                    stash_protected_cut_payload,
                )

                if is_protected_viewer(self, player_index):
                    from ..public.claim_protection import stage_protected_cut
                    async def cut_send(vi=player_index, p=payload):
                        await self._deliver_claim_payload(vi, p)
                    stage_protected_cut(self, player_index, payload.get("do_action_info") or {}, cut_send)
                    stash_protected_cut_payload(self, player_index, payload)
                    arm_claim_protection_timer(self, self._claim_protection_send_fn)
                    continue
            delay = 0.0
            from ..public.claim_protection import (
                is_protected_viewer as _ipv,
                compute_protected_meld_delay,
                mark_post_meld_gap,
                REAL_MELD_ACTIONS,
            )
            is_real_meld = bool(action_list) and action_list[0] in REAL_MELD_ACTIONS
            if is_real_meld:
                from ..public.claim_protection import schedule_meld
                async def meld_send(vi=player_index, p=payload):
                    await self._deliver_claim_payload(vi, p)
                if schedule_meld(self, player_index, meld_send, protected=_ipv(self, player_index)):
                    if _ipv(self, player_index):
                        mark_post_meld_gap(self, player_index)
                    continue
            if (not is_cut) and _ipv(self, player_index) and getattr(self, "_cp_cut_flush_time", None):
                delay = compute_protected_meld_delay(self)
            if delay > 0:
                from ..public.outbound_pipe import schedule_viewer_send

                # raw deliver without nesting another pipe wait: call websocket path via schedule
                async def _raw(vi=player_index, p=payload):
                    player = self.player_list[vi]
                    connection = None
                    if self.game_server is not None:
                        connection = getattr(self.game_server, "user_id_to_connection", {}).get(player.user_id)
                    if connection is not None and getattr(connection, "websocket", None) is not None:
                        await connection.websocket.send_json(p)
                        self.websocket_sent_payloads.append(p)
                    await self.send_to_realtime_spectators(vi, p)

                schedule_viewer_send(self, player_index, _raw, delay_before=delay)
                if is_real_meld and _ipv(self, player_index):
                    mark_post_meld_gap(self, player_index)
            else:
                await self._send_claim_protection_payload(player_index, payload)
                if is_real_meld and _ipv(self, player_index):
                    mark_post_meld_gap(self, player_index)

        if self._final_result_pending:
            self._final_result_pending = False
            await self.present_final_settlements()

    async def present_final_settlements(self):
        from .boardcast import final_settlement_payloads
        from ..public.round_end_timing import hu_result_ready_pre_panel_seconds, sichuan_settle_hu_panel_wait_seconds
        self.apply_deferred_score_changes()
        views = [final_settlement_payloads(self, idx) for idx in range(4)]
        for step in range(len(views[0])):
            for idx in range(4):
                await self._send_claim_protection_payload(idx, views[idx][step])
            info = views[0][step]["show_result_info"]
            if self.is_nanque and step < len(views[0]) - 1:
                delay = hu_result_ready_pre_panel_seconds() if info.get("blood_battle_step") == "reveal_hu" else sichuan_settle_hu_panel_wait_seconds(len(info.get("hu_fan") or []))
                await asyncio.sleep(delay)

    async def send_payload_to_player(
        self,
        player_index: int,
        payload: dict,
        *,
        record_fallback: bool = True,
    ) -> bool:
        """Send a payload to one player, or keep it in the local outbox when offline."""
        if player_index not in range(len(self.player_list)):
            raise ValueError(f"Invalid player index for payload send: {player_index}")
        payload.setdefault("player_index", player_index)
        player = self.player_list[player_index]
        connection = None
        if self.game_server is not None:
            connection = getattr(self.game_server, "user_id_to_connection", {}).get(player.user_id)
        if connection is not None and getattr(connection, "websocket", None) is not None:
            from ..public.outbound_pipe import send_to_viewer
            from ..public.claim_protection import take_post_meld_gap_delay

            async def _do(conn=connection, p=payload):
                await conn.websocket.send_json(p)

            await send_to_viewer(
                self,
                player_index,
                _do,
                delay_before=take_post_meld_gap_delay(self, player_index),
            )
            self.websocket_sent_payloads.append(payload)
            return True
        if record_fallback:
            self.outbound_payloads.append(payload)
        return False

    async def resolve_action_window(
        self,
        timeout: Optional[float] = None,
        *,
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Wait for the current live window and apply the queued actions."""
        if self.live_pending_window is None:
            raise ValueError("No live action window is open.")
        window = self.live_pending_window
        results = await self.wait_action(timeout)
        if window.get("status") == "waiting_action_after_cut":
            await self.finish_claim_protection()
            # 受保护观众后续帧走 outbound_pipe，此处不再全局 sleep
        return self.apply_action_results(window, results, settlements=settlements)

    def apply_action_results(
        self,
        window: dict,
        action_results: Dict[int, dict],
        *,
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Apply collected live-loop actions using the tested driver helpers."""
        status = window.get("status")
        settlements = settlements or {}

        if status in {"waiting_hand_action", "onlycut_after_action"}:
            player_index = window["player"]
            action_data = action_results.get(player_index)
            if action_data is None:
                raise ValueError(f"Missing action for player {player_index}.")
            action = action_data["action_type"]
            tile = action_data.get("TileId") if action == "cut" else action_data.get("target_tile")
            if tile is not None and tile <= 0:
                tile = None
            if action == "hu_self":
                tile = tile if tile is not None else self.player_list[player_index].hand_tiles[-1]
            next_window = self.apply_turn_action(
                player_index,
                action,
                tile=tile,
                settlement=settlements.get(player_index),
            )
            action_payload = {
                "action": action,
                "player": player_index,
                "tile": tile,
                "cutIndex": action_data.get("cutIndex"),
                "cutClass": action_data.get("cutClass", False),
                "is_timeout_action": action_data.get("is_timeout_action", False),
            }
            result = next_window.get("result") or {}
            for key in ("meld_code", "combination_mask", "is_mo_gang"):
                if key in result:
                    action_payload[key] = result[key]
            if action == "cut":
                self.begin_claim_protection(next_window.get("actions") or {}, player_index)
            self.emit_visible_action_payloads(action_payload, reveal_final=next_window.get("status") == "END")
            if action in {"angang", "hu_self"} and next_window.get("drawn_tile") is not None:
                self.emit_visible_action_payloads(
                    {
                        "action": "deal_tile" if action == "hu_self" else "deal_gang_tile",
                        "player": next_window.get("player", player_index),
                        "tile": next_window.get("drawn_tile"),
                    },
                    reveal_final=next_window.get("status") == "END",
                )
            return self.open_action_window(next_window)

        if status == "waiting_action_after_cut":
            discarder_index = window["player"]
            tile = window["tile"]
            win_responses: Dict[int, str] = {}
            claim_responses: Dict[int, str] = {}
            for player_index, action_data in action_results.items():
                action = action_data["action_type"]
                if action in {"hu", "pass", "", "none"}:
                    win_responses[player_index] = action
                if action in {"chi_left", "chi_mid", "chi_right", "peng", "gang", "pass", "", "none"}:
                    claim_responses[player_index] = action
            next_window = self.continue_after_discard_responses(
                discarder_index,
                tile,
                win_responses,
                claim_responses,
                settlements=settlements,
            )
            win_result = next_window.get("win_result") or {}
            winners = list(win_result.get("winners", []))
            for winner_index in winners:
                settlement = self._latest_hu_settlement_for(winner_index)
                self.emit_visible_action_payloads(
                    {
                        "action": self._hu_action_for_settlement(settlement),
                        "player": winner_index,
                        "tile": tile,
                    },
                    reveal_final=next_window.get("status") == "END",
                )
            claim_result = next_window.get("claim_result") or next_window.get("result") or {}
            if claim_result.get("claimed"):
                self.emit_visible_action_payloads(
                    {
                        "action": claim_result.get("action"),
                        "player": claim_result.get("claimant"),
                        "tile": tile,
                        "meld_code": claim_result.get("meld_code"),
                        "combination_mask": claim_result.get("combination_mask"),
                        "cut_from_player": discarder_index,
                    },
                )
            if next_window.get("drawn_tile") is not None and next_window.get("player") is not None:
                deal_action = "deal_gang_tile" if next_window.get("reason") == "discard_gang" else "deal_tile"
                self.emit_visible_action_payloads(
                    {
                        "action": deal_action,
                        "player": next_window.get("player"),
                        "tile": next_window.get("drawn_tile"),
                    },
                )
            return self.open_action_window(next_window)

        if status == "waiting_action_qianggang":
            kong_player_index = window["player"]
            tile = window["tile"]
            responses = {player_index: data["action_type"] for player_index, data in action_results.items()}
            next_window = self.continue_after_rob_kong_responses(
                kong_player_index,
                tile,
                responses,
                settlements=settlements,
            )
            rob_kong_result = next_window.get("rob_kong_result") or {}
            if rob_kong_result.get("robbed"):
                winners = list(rob_kong_result.get("winners", []))
                for winner_index in winners:
                    settlement = self._latest_hu_settlement_for(winner_index)
                    self.emit_visible_action_payloads(
                        {
                            "action": self._hu_action_for_settlement(settlement),
                            "player": winner_index,
                            "tile": tile,
                        },
                        reveal_final=next_window.get("status") == "END",
                    )
            if next_window.get("drawn_tile") is not None and next_window.get("player") is not None:
                deal_action = "deal_tile" if rob_kong_result.get("robbed") else "deal_gang_tile"
                self.emit_visible_action_payloads(
                    {
                        "action": deal_action,
                        "player": next_window.get("player"),
                        "tile": next_window.get("drawn_tile"),
                    },
                )
            return self.open_action_window(next_window)

        if status == "END":
            return window
        raise ValueError(f"Unsupported live action window status: {status}")

    @staticmethod
    def _hu_action_for_settlement(settlement: Optional[dict]) -> str:
        if settlement is None:
            return "hu_first"
        from .boardcast import _settlement_hu_class

        return _settlement_hu_class(settlement)

    @staticmethod
    def _default_room_data() -> Dict[str, Any]:
        return {
            "room_id": "jiandan-test-room",
            "player_list": [101, 102, 103, 104],
            "player_settings": {},
            "round_timer": 0,
            "step_timer": 0,
            "game_round": 1,
            "room_rule": "zhongyong",
            "room_type": "custom",
            "tips": False,
            "random_seed": 1,
        }

    def _derive_round_seed(self) -> int:
        return derive_round_seed(self.master_seed, self.current_round)

    def advance_round_after_ready(self) -> None:
        """Advance to the next hand with fixed dealer rotation and fresh action state."""
        self.advance_dealer_after_round()
        self.current_player_index = self.dealer_index
        self.live_pending_window = None
        self.action_dict = {idx: [] for idx in range(4)}
        self.waiting_players_list = []
        self.hand_action_is_gang_draw = {idx: False for idx in range(4)}
        self.natural_draw_count = {idx: 0 for idx in range(4)}
        self.opening_action_taken = False
        self.opening_flow_interrupted = False
        self.bot_action_ticks = {}
        self.game_status = "waiting"

    def reset_round_state(self) -> None:
        for player in self.player_list:
            player.reset_for_round(self.round_time)
        self.tiles_list = []
        self.current_player_index = self.dealer_index
        self.hu_order_counter = 0
        self.deferred_hu_settlements = []
        self.deferred_scores_applied = False
        self.ended_by = None
        self.game_status = "waiting"
        self.live_pending_window = None
        self.action_dict = {idx: [] for idx in range(4)}
        self.waiting_players_list = []
        for idx in range(4):
            self.action_events[idx].clear()
            while not self.action_queues[idx].empty():
                self.action_queues[idx].get_nowait()
        self.hand_action_is_gang_draw = {idx: False for idx in range(4)}
        self.natural_draw_count = {idx: 0 for idx in range(4)}
        self.opening_action_taken = False
        self.opening_flow_interrupted = False
        self.bot_action_ticks = {}
        self.discard_log = []
        self.last_discard_offsets = [-1] * 4
        self._final_result_pending = False
        self.round_random_seed = self._derive_round_seed()

    def initialize_round(self) -> None:
        self.reset_round_state()
        init_jiandan_tiles(self)
        self.current_player_index = self.dealer_index
        self.game_status = "waiting_hand_action"
































