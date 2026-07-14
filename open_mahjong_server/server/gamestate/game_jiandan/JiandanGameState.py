from __future__ import annotations

import asyncio
import logging
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional

from .action_check import JiandanActionPolicy
from .init_tiles import init_jiandan_tiles
from .settlement import JiandanSettlementPolicy
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
class JiandanPlayer:
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


class JiandanGameState:
    """Live Jiandan game state for the first-win rule.

    A confirmed win ends the hand immediately.  Multi-stage winner exit and
    continuation are intentionally outside this rule integration.
    """

    def __init__(
        self,
        game_server: Any = None,
        room_data: Optional[Dict[str, Any]] = None,
        calculation_service: Any = None,
        db_manager: Any = None,
        gamestate_id: str = "jiandan-test",
    ):
        room_data = room_data or self._default_room_data()
        self.game_server = game_server
        self.calculation_service = calculation_service or _default_calculation_service()
        self.db_manager = db_manager
        self.gamestate_id = gamestate_id
        self.room_id = room_data["room_id"]
        self.tips = room_data.get("tips", False)
        self.max_round = room_data.get("game_round", 1)
        self.step_time = room_data.get("step_timer", 0)
        self.round_time = room_data.get("round_timer", 0)
        self.room_rule = room_data.get("room_rule", "jiandan")
        self.room_type = room_data.get("room_type", "custom")
        self.sub_rule = room_data.get("sub_rule", "jiandan/standard")
        self.action_policy = JiandanActionPolicy()
        self.settlement_policy = JiandanSettlementPolicy()
        self.room_random_seed = room_data.get("random_seed", 0)
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(
            self.room_random_seed if self.room_random_seed else None
        )
        self.open_cuohe = False
        self.show_moqie_hint = False
        self.hepai_limit = 0
        self.tactical_call = False
        self.claim_protection = room_data.get("claim_protection", True)
        self.claim_protect_delay = room_data.get("claim_protect_delay", 1.3)
        self.claim_meld_followup_gap = room_data.get("claim_meld_followup_gap", 0.8)
        self.allow_spectator_config = room_data.get("allow_spectator", True)

        self.player_list: List[JiandanPlayer] = []
        player_settings = room_data.get("player_settings", {})
        for index, user_id in enumerate(room_data["player_list"]):
            settings = player_settings.get(user_id, {})
            username = settings.get("username", f"用户{user_id}")
            player = JiandanPlayer(user_id, username, room_data.get("round_timer", 0))
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
        self.dead_wall_count = 0
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
        """Draft local live loop.

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

    async def _send_claim_protection_payload(self, viewer_index: int, payload: dict) -> None:
        await self.send_payload_to_player(viewer_index, payload, record_fallback=False)
        await self.send_to_realtime_spectators(viewer_index, payload)

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

    async def player_reconnect(self, user_id: int) -> None:
        """Clear offline marker and restore the player's table with standard game messages."""
        for player in self.player_list:
            if player.user_id == user_id:
                if "offline" in player.tag_list:
                    player.tag_list.remove("offline")
                await self.send_payload_to_player(
                    player.player_index,
                    self.build_game_start_payload(player.player_index),
                )
                pending_payload = self.build_pending_action_payload(player.player_index)
                if pending_payload is not None:
                    await self.send_payload_to_player(player.player_index, pending_payload)
                elif self.game_status == "END":
                    await self.send_payload_to_player(
                        player.player_index,
                        self.build_final_settlement_payload(player.player_index),
                    )
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
        return 12.0

    def schedule_bot_actions(self) -> None:
        from .bot import jiandan_bot_action

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
            for idx in range(4):
                payloads.extend(final_settlement_payloads(self, idx))
        else:
            cut_tile = window.get("tile") if window.get("status") == "waiting_action_after_cut" else None
            rob_kong_tile = window.get("tile") if window.get("status") == "waiting_action_qianggang" else None
            action_player_index = window.get("player")
            if action_player_index is None:
                action_player_index = self.current_player_index
            # 与青雀一致：行动窗口通知所有视角。只有实际需要决策的玩家
            # 携带非空 action_list；其他真人客户端仍可由通用 ask 路径同步
            # 当前行动者与服务端权威余牌数，无需规则专用客户端刷新逻辑。
            for idx in range(4):
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

    def start_game_recording(self) -> None:
        from ..public.game_record_manager import init_game_record

        init_game_record(self)
        self.game_record["game_title"]["sub_rule"] = self.sub_rule
        self.game_record["game_title"]["hepai_limit"] = self.hepai_limit
        self.spectator_manager.record_game_title()

    def start_round_recording(self) -> None:
        from ..public.game_record_manager import init_game_round

        if not self.game_record:
            self.start_game_recording()
        init_game_round(self)
        self.round_record_finalized = False
        self.spectator_manager.record_round_start()

    def _has_active_round_record(self) -> bool:
        return bool(
            self.game_record.get("game_round", {}).get(f"round_index_{self.round_index}")
        ) and not self.round_record_finalized

    def record_visible_action(self, action_info: dict) -> None:
        if not self._has_active_round_record():
            return

        from ..public.game_record_manager import (
            player_action_record_angang,
            player_action_record_chipenggang,
            player_action_record_cut,
            player_action_record_deal,
        )

        action = action_info.get("action")
        tile = action_info.get("tile")
        if action == "cut":
            player_action_record_cut(self, cut_tile=tile, is_moqie=bool(action_info.get("cutClass", False)))
        elif action == "deal_tile":
            player_action_record_deal(self, deal_tile=tile, deal_type="d")
        elif action == "deal_gang_tile":
            player_action_record_deal(self, deal_tile=tile, deal_type="gd")
        elif action == "angang":
            player_action_record_angang(
                self,
                angang_tile=tile,
                is_mo_gang=bool(action_info.get("is_mo_gang", False)),
                combination_mask=action_info.get("combination_mask"),
            )
        elif action in {"chi_left", "chi_mid", "chi_right", "peng", "gang"}:
            player_action_record_chipenggang(
                self,
                action_type=action,
                mingpai_tile=tile,
                action_player=action_info.get("player"),
                combination_mask=action_info.get("combination_mask"),
            )

    def finalize_round_recording(self) -> None:
        if not self._has_active_round_record():
            return

        from ..public.game_record_manager import (
            player_action_record_hu,
            player_action_record_liuju,
            player_action_record_round_end,
        )
        from .boardcast import _settlement_hu_class

        for player in self.player_list:
            if any(meld and meld[0] in {"s", "k", "g"} for meld in player.combination_tiles):
                player.record_counter.fulu_times += 1

        self.apply_deferred_score_changes()
        if self.deferred_hu_settlements:
            for settlement in self.deferred_hu_settlements:
                winner_index = settlement.get("winner", -1)
                hu_class = _settlement_hu_class(settlement)
                hu_fan = list(settlement.get("fan_names") or settlement.get("fan_ids") or [])
                fan_ids = list(settlement.get("fan_ids") or hu_fan)
                score_changes = list(settlement.get("score_changes") or [0, 0, 0, 0])
                player_action_record_hu(
                    self,
                    hu_class=hu_class,
                    hu_score=settlement.get("points", 0),
                    hu_fan=hu_fan,
                    hepai_player_index=winner_index,
                    score_changes=score_changes,
                    hepai_tile=settlement.get("tile"),
                    ron_discarder_index=self._settlement_payer_index(settlement),
                )
                self._update_record_counter_for_settlement(settlement, fan_ids)
        else:
            player_action_record_liuju(self)

        player_action_record_round_end(self)
        self.round_record_finalized = True

    def finalize_game_recording(self) -> None:
        if not self.game_record or "game_title" not in self.game_record:
            return
        from ..public.game_record_manager import end_game_record

        end_game_record(self)

    def _update_record_counter_for_settlement(self, settlement: dict, fan_ids: list[str]) -> None:
        winner_index = settlement.get("winner")
        if winner_index not in range(len(self.player_list)):
            return
        winner = self.player_list[winner_index]
        points = int(settlement.get("points", 0) or 0)
        winner.record_counter.recorded_fans.append(fan_ids)
        winner.record_counter.win_score += points
        winner.record_counter.win_turn += len(winner.discard_tiles) + 1
        if settlement.get("source") == "self_draw":
            winner.record_counter.zimo_times += 1
        else:
            winner.record_counter.dianhe_times += 1
            payer_index = self._settlement_payer_index(settlement)
            if payer_index in range(len(self.player_list)):
                payer = self.player_list[payer_index]
                payer.record_counter.fangchong_times += 1
                payer.record_counter.fangchong_score += points

    @staticmethod
    def _settlement_payer_index(settlement: dict) -> Optional[int]:
        payer_index = settlement.get("discarder")
        if payer_index is not None:
            return payer_index
        return settlement.get("kong_player")

    def build_game_start_payload(self, player_index: int) -> dict:
        from .boardcast import game_start_payload

        return game_start_payload(self, player_index, reveal_final=self.game_status == "END")

    def emit_game_start_payloads(self) -> list[dict]:
        payloads = [self.build_game_start_payload(idx) for idx in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self, player_index: int) -> Optional[dict]:
        from .boardcast import pending_action_payload

        return pending_action_payload(self, player_index)

    def build_final_settlement_payload(self, player_index: int) -> dict:
        from .boardcast import final_settlement_payload

        return final_settlement_payload(self, player_index)

    def emit_ready_status_payloads(self) -> list[dict]:
        from .boardcast import ready_status_payload

        payloads = [ready_status_payload(self, idx) for idx in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def emit_game_end_payloads(self) -> list[dict]:
        from .boardcast import game_end_payload
        from ..public.logic_common import assign_strict_final_ranks

        assign_strict_final_ranks(self.player_list)
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

        self.emit_game_end_payloads()
        await self.flush_outbound_payloads()

        await self.spectator_manager.send_final_record_and_close()
        self.persist_game_record()

        if self.game_server is None:
            return
        gamestate_manager = getattr(self.game_server, "gamestate_manager", None)
        if gamestate_manager is not None:
            await gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
        room_manager = getattr(self.game_server, "room_manager", None)
        if room_manager is not None:
            if self.room_type == "match" and hasattr(room_manager, "destroy_room"):
                await room_manager.destroy_room(self.room_id)
            elif hasattr(room_manager, "finish_custom_game_room"):
                await room_manager.finish_custom_game_room(self.room_id)

    def persist_game_record(self) -> Optional[str]:
        """Persist the finalized replay and rule-local statistics."""
        if self.db_manager is None or not self.game_record:
            return None
        try:
            game_id = self.db_manager.store_jiandan_game_record(
                self.game_record,
                self.player_list,
                self.room_type,
                f"{self.max_round}/4",
            )
            has_ai_player = any(player.user_id <= 10 for player in self.player_list)
            if self.room_type == "events":
                logger.info("Jiandan event game skips player statistics: game_id=%s", game_id)
            elif game_id and not has_ai_player:
                total_rounds = len(self.game_record.get("game_round", {}))
                self.db_manager.store_jiandan_game_stats(
                    game_id,
                    self.player_list,
                    self.room_type,
                    self.max_round,
                    total_rounds,
                )
                self.db_manager.store_jiandan_fan_stats(
                    game_id,
                    self.player_list,
                    self.room_type,
                    self.max_round,
                )
            elif has_ai_player:
                logger.info("Jiandan game contains an AI player; statistics skipped")
            return game_id
        except Exception as exc:
            logger.warning(
                "Jiandan replay persistence failed, room_id=%s: %s",
                self.room_id,
                exc,
                exc_info=True,
            )
            return None

    async def flush_outbound_payloads(self) -> None:
        """Send newly recorded payloads to connected players where possible."""
        while self.outbound_send_cursor < len(self.outbound_payloads):
            payload = self.outbound_payloads[self.outbound_send_cursor]
            self.outbound_send_cursor += 1
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
                    stash_protected_cut_payload(self, player_index, payload)
                    arm_claim_protection_timer(self, self._claim_protection_send_fn)
                    continue
            await self._send_claim_protection_payload(player_index, payload)

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
            await connection.websocket.send_json(payload)
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
            had_claim_protection = bool(getattr(self, "_cp_active", False))
            await self.finish_claim_protection()
            has_claim = any(
                result.get("action_type") not in {None, "", "none", "pass"}
                for result in results.values()
            )
            if had_claim_protection and has_claim:
                from ..public.claim_protection import compute_protected_meld_delay

                delay = compute_protected_meld_delay(self)
                if delay > 0:
                    await asyncio.sleep(delay)
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
            }
            result = next_window.get("result") or {}
            for key in ("meld_code", "combination_mask", "is_mo_gang"):
                if key in result:
                    action_payload[key] = result[key]
            if action == "cut":
                self.begin_claim_protection(next_window.get("actions") or {}, player_index)
            self.emit_visible_action_payloads(action_payload, reveal_final=next_window.get("status") == "END")
            if action in {"angang", "jiagang"} and next_window.get("drawn_tile") is not None:
                self.emit_visible_action_payloads(
                    {
                        "action": "deal_gang_tile",
                        "player": player_index,
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
            if not rob_kong_result.get("robbed") and rob_kong_result.get("meld_code"):
                self.emit_visible_action_payloads(
                    {
                        "action": "jiagang",
                        "player": kong_player_index,
                        "tile": tile,
                        "meld_code": rob_kong_result.get("previous_meld_code", f"k{tile}"),
                        "combination_mask": rob_kong_result.get("combination_mask"),
                        "is_mo_gang": rob_kong_result.get("is_mo_gang", False),
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
            "room_rule": "jiandan",
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
        self.round_random_seed = self._derive_round_seed()

    def initialize_round(self) -> None:
        self.reset_round_state()
        init_jiandan_tiles(self)
        self.current_player_index = self.dealer_index
        self.game_status = "waiting_hand_action"

    def begin_hand_action(self, player_index: Optional[int] = None, *, is_get_gang_tile: bool = False) -> dict:
        """Refresh waiting cache and return actions after a draw or supplement draw."""
        player_index = self.current_player_index if player_index is None else player_index
        self.current_player_index = player_index
        self.hand_action_is_gang_draw[player_index] = bool(is_get_gang_tile)
        self.action_policy.refresh_waiting_tiles(self, player_index, exclude_last_tile=True)
        self.game_status = "waiting_hand_action"
        return {
            "status": self.game_status,
            "player": player_index,
            "actions": self.action_policy.check_hand_action(self, player_index, is_get_gang_tile=is_get_gang_tile),
        }

    def begin_only_cut(self, player_index: int) -> dict:
        """Return the forced-discard action window after chi or peng."""
        self.current_player_index = player_index
        self.hand_action_is_gang_draw[player_index] = False
        self.game_status = "onlycut_after_action"
        return {
            "status": self.game_status,
            "player": player_index,
            "actions": self.action_policy.check_only_cut(self, player_index),
        }

    def apply_turn_action(
        self,
        player_index: int,
        action: str,
        *,
        tile: Optional[int] = None,
        settlement: Optional[dict] = None,
    ) -> dict:
        """Apply one selected self/turn action and return the next response window."""
        if action == "cut":
            if tile is None:
                raise ValueError("Discard action requires a tile.")
            self.hand_action_is_gang_draw[player_index] = False
            cut_tile = self.record_discard(player_index, tile)
            for other in self.player_list:
                if other.player_index != player_index and not other.is_hu:
                    self.action_policy.refresh_waiting_tiles(self, other.player_index)
            self.game_status = "waiting_action_after_cut"
            return {
                "status": self.game_status,
                "player": player_index,
                "tile": cut_tile,
                "actions": self.action_policy.check_after_cut(self, cut_tile),
            }

        if action == "hu_self":
            win_tile = tile if tile is not None else self.player_list[player_index].hand_tiles[-1]
            if win_tile is not None and win_tile <= 0:
                win_tile = self.player_list[player_index].hand_tiles[-1]
            self.record_self_draw_win(player_index, win_tile, settlement)
            if self.game_status == "END":
                return {"status": self.game_status, "player": player_index, "tile": win_tile, "actions": None}
            drawn_tile = self.draw_after_discard_resolution(player_index)
            if drawn_tile is None:
                return {"status": self.game_status, "player": None, "tile": None, "actions": None}
            next_window = self.begin_hand_action(self.current_player_index)
            next_window["drawn_tile"] = drawn_tile
            return next_window

        if action == "angang":
            if tile is None:
                raise ValueError("Concealed-kong action requires a tile.")
            result = self.declare_concealed_kong(player_index, tile)
            next_window = self.begin_hand_action(player_index, is_get_gang_tile=True)
            next_window["result"] = result
            next_window["drawn_tile"] = result.get("drawn_tile")
            return next_window

        if action == "jiagang":
            if tile is None:
                raise ValueError("Added-kong action requires a tile.")
            result = self.attempt_added_kong(player_index, tile)
            for other in self.player_list:
                if other.player_index != player_index and not other.is_hu:
                    self.action_policy.refresh_waiting_tiles(self, other.player_index)
            return {
                "status": self.game_status,
                "player": player_index,
                "tile": tile,
                "result": result,
                "actions": self.action_policy.check_jiagang(self, tile),
            }

        raise ValueError(f"Unsupported turn action: {action}")

    def continue_after_discard_responses(
        self,
        discarder_index: int,
        tile: int,
        win_responses: Dict[int, str],
        claim_responses: Optional[Dict[int, str]] = None,
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Resolve a discard response window and return a consistent next window."""
        win_result = self.resolve_discard_win_responses(discarder_index, tile, win_responses, settlements)
        if win_result["winners"]:
            return self._window_after_draw_result(win_result, "discard_win", {"win_result": win_result})
        if self.game_status == "END":
            return self._end_window({"win_result": win_result})

        claim_result = self.resolve_discard_claim_responses(discarder_index, tile, claim_responses or {})
        if not claim_result["claimed"]:
            return self._window_after_draw_result(
                claim_result,
                "discard_no_claim",
                {"win_result": win_result, "claim_result": claim_result},
            )
        if claim_result["action"] == "gang":
            window = self.begin_hand_action(claim_result["claimant"], is_get_gang_tile=True)
            window["reason"] = "discard_gang"
            window["claim_result"] = claim_result
            window["drawn_tile"] = claim_result.get("drawn_tile")
            return window
        window = self.begin_only_cut(claim_result["claimant"])
        window["reason"] = "discard_claim"
        window["claim_result"] = claim_result
        return window

    def continue_after_rob_kong_responses(
        self,
        kong_player_index: int,
        tile: int,
        responses: Dict[int, str],
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Resolve a robbing-kong response window and return a consistent next window."""
        result = self.resolve_added_kong_responses(kong_player_index, tile, responses, settlements)
        if result["robbed"]:
            return self._window_after_draw_result(result, "rob_kong", {"rob_kong_result": result})
        if self.game_status == "END":
            return self._end_window({"rob_kong_result": result})
        window = self.begin_hand_action(kong_player_index, is_get_gang_tile=True)
        window["reason"] = "added_kong"
        window["drawn_tile"] = result.get("drawn_tile")
        window["rob_kong_result"] = result
        return window

    def next_active_index(self, from_index: int) -> Optional[int]:
        if len(self.tiles_list) <= self.dead_wall_count:
            return None
        for offset in range(1, len(self.player_list) + 1):
            player_index = (from_index + offset) % len(self.player_list)
            if not self.player_list[player_index].is_hu:
                return player_index
        return None

    def mark_player_hu(self, player_index: int, settlement: Optional[dict] = None) -> None:
        player = self.player_list[player_index]
        # first_win is terminal. Ignore any stale/late action that reaches this
        # method after the first winner has already closed the hand.
        if player.is_hu or self.game_status == "END" or self.hu_count >= 1:
            return
        self.hu_order_counter += 1
        player.is_hu = True
        player.hu_order = self.hu_order_counter
        self._mark_opening_action(interrupt=True)
        if settlement is not None:
            self.deferred_hu_settlements.append({**settlement, "winner": player_index, "hu_order": player.hu_order})
        if self.should_end_hand():
            self.ended_by = "win"
            self.game_status = "END"

    def apply_deferred_score_changes(self) -> None:
        """Apply one hand's settlements and persist one scoreboard row exactly once."""
        if self.deferred_scores_applied:
            return
        round_score_changes = [0 for _ in self.player_list]
        for settlement in self.deferred_hu_settlements:
            changes = settlement.get("score_changes")
            if not changes:
                continue
            for player_index, change in enumerate(changes):
                if player_index < len(self.player_list):
                    self.player_list[player_index].score += change
                    round_score_changes[player_index] += change
        for player in self.player_list:
            score_change = round_score_changes[player.player_index]
            if score_change > 0:
                score_change_text = f"+{score_change:02d}"
            elif score_change < 0:
                score_change_text = f"-{abs(score_change):02d}"
            else:
                score_change_text = "0"
            player.score_history.append(score_change_text)
            player.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True

    def should_end_hand(self) -> bool:
        return self.hu_count >= 1 or len(self.tiles_list) <= self.dead_wall_count

    @property
    def hu_count(self) -> int:
        return sum(1 for player in self.player_list if player.is_hu)

    def advance_dealer_after_round(self) -> int:
        # Follow the established Qingque seat model: rotate every identity's
        # wind and sort by the new seat, so the next dealer remains East (0).
        for player in self.player_list:
            player.player_index = back_current_num(player.player_index)
        self.player_list.sort(key=lambda player: player.player_index)
        self.dealer_index = 0
        self.current_round += 1
        self.round_index += 1
        return self.dealer_index

    def clear_lockout_on_discard(self, player_index: int, discarded_tile: int) -> None:
        self.player_list[player_index].discard_win_lockout_tiles = {discarded_tile}

    def add_discard_win_lockout(self, player_index: int, tile: int) -> None:
        self.player_list[player_index].discard_win_lockout_tiles.add(tile)

    def can_win_by_discard(self, player_index: int, tile: int) -> bool:
        return tile not in self.player_list[player_index].discard_win_lockout_tiles

    def record_discard(self, player_index: int, tile: int) -> int:
        """Apply a discard and start the next same-tile discard-win lockout window."""
        player = self.player_list[player_index]
        if player.is_hu:
            raise ValueError("A player who has already won cannot discard.")
        if tile not in player.hand_tiles:
            raise ValueError(f"Player {player_index} cannot discard tile {tile}; it is not in hand.")
        player.hand_tiles.remove(tile)
        player.discard_tiles.append(tile)
        player.discard_origin_tiles.append(tile)
        player.has_draw_slot = False
        self.current_player_index = player_index
        self._mark_opening_action()
        self.clear_lockout_on_discard(player_index, tile)
        return tile

    def record_discard_win_pass(self, player_index: int, tile: int) -> None:
        """Record that a player skipped a currently available discard win."""
        player = self.player_list[player_index]
        if player.is_hu:
            return
        if tile in player.waiting_tiles and self.can_win_by_discard(player_index, tile):
            self.add_discard_win_lockout(player_index, tile)

    def record_discard_win(
        self,
        winner_index: int,
        discarder_index: int,
        tile: int,
        settlement: Optional[dict] = None,
    ) -> None:
        """Record a discard win while deferring fan details until final settlement."""
        if not self.can_win_by_discard(winner_index, tile):
            raise ValueError(f"Player {winner_index} is locked out from winning on tile {tile}.")
        if settlement is None:
            settlement = self.settlement_policy.build(
                self,
                winner_index,
                "discard",
                tile,
                payer_index=discarder_index,
            )
        self.mark_player_hu(
            winner_index,
            {
                "source": "discard",
                "discarder": discarder_index,
                "tile": tile,
                **settlement,
            },
        )

    def record_self_draw_win(self, winner_index: int, tile: int, settlement: Optional[dict] = None) -> None:
        """Record a self-draw win while deferring fan details until final settlement."""
        player = self.player_list[winner_index]
        if player.is_hu:
            return
        if tile not in player.hand_tiles:
            raise ValueError(f"Player {winner_index} cannot self-draw win on tile {tile}; it is not in hand.")
        if settlement is None:
            settlement = self.settlement_policy.build(self, winner_index, "self_draw", tile)
        self.mark_player_hu(
            winner_index,
            {
                "source": "self_draw",
                "tile": tile,
                **settlement,
            },
        )

    def draw_after_discard_resolution(self, from_index: int) -> Optional[int]:
        """Draw for the next active player after a discard-response branch resolves."""
        next_index = self.next_active_index(from_index)
        if next_index is None:
            return None
        self.current_player_index = next_index
        if self.should_end_hand():
            self.ended_by = self.ended_by or "wall"
            self.game_status = "END"
            return None
        drawn_tile = self.player_list[next_index].get_tile(self.tiles_list)
        self.natural_draw_count[next_index] = self.natural_draw_count.get(next_index, 0) + 1
        return drawn_tile

    def resolve_discard_win_responses(
        self,
        discarder_index: int,
        tile: int,
        responses: Dict[int, str],
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Resolve discard-win/pass choices before lower-priority claims."""
        settlements = settlements or {}
        winners: list[int] = []
        passed: list[int] = []
        for player_index in self._response_order_after(discarder_index, responses):
            action = responses[player_index]
            if player_index == discarder_index or self.player_list[player_index].is_hu:
                continue
            if action == "pass":
                self.record_discard_win_pass(player_index, tile)
                passed.append(player_index)
            elif action == "hu":
                if tile not in self.player_list[player_index].waiting_tiles:
                    raise ValueError(f"Player {player_index} is not waiting on tile {tile}.")
                self.record_discard_win(
                    player_index,
                    discarder_index,
                    tile,
                    settlements.get(player_index),
                )
                winners.append(player_index)
                # first_win is head-bump: the nearest legal winner in response
                # order ends the hand, so later responses are not resolved.
                break
            elif action in {"", "none"}:
                continue
            else:
                raise ValueError(f"Unsupported discard-win response: {action}")

        drawn_tile = None
        draw_player = None
        if self.game_status != "END" and winners:
            drawn_tile = self.draw_after_discard_resolution(winners[-1])
            draw_player = self.current_player_index if drawn_tile is not None else None

        return {
            "winners": winners,
            "passed": passed,
            "draw_player": draw_player,
            "drawn_tile": drawn_tile,
            "ended": self.game_status == "END",
        }

    def resolve_discard_claim_responses(
        self,
        discarder_index: int,
        tile: int,
        responses: Dict[int, str],
    ) -> dict:
        """Resolve chi/peng/gang claims after discard wins are declined."""
        claim = self._select_discard_claim(discarder_index, responses)
        if claim is None:
            drawn_tile = self.draw_after_discard_resolution(discarder_index)
            return {
                "claimed": False,
                "claimant": None,
                "action": None,
                "meld_code": None,
                "draw_player": self.current_player_index if drawn_tile is not None else None,
                "drawn_tile": drawn_tile,
                "next_status": self.game_status,
            }

        claimant_index, action = claim
        player = self.player_list[claimant_index]
        discarder = self.player_list[discarder_index]
        # 与其它规则一致：鸣牌后从打牌者河牌移除被认走张，并记入 discard_origin_tiles
        if discarder.discard_tiles:
            if discarder.discard_tiles[-1] == tile:
                discarder.discard_tiles.pop(-1)
            elif tile in discarder.discard_tiles:
                for i in range(len(discarder.discard_tiles) - 1, -1, -1):
                    if discarder.discard_tiles[i] == tile:
                        discarder.discard_tiles.pop(i)
                        break
        discarder.discard_origin_tiles.append(tile)
        meld_code, combination_mask = self._apply_discard_claim(player, tile, action)
        self.current_player_index = claimant_index

        drawn_tile = None
        if action == "gang":
            if not self.tiles_list:
                raise ValueError("Cannot claim a discard kong when the wall is empty.")
            drawn_tile = player.get_tile(self.tiles_list)
            self.game_status = "waiting_hand_action"
        else:
            player.has_draw_slot = False
            self.game_status = "onlycut_after_action"

        return {
            "claimed": True,
            "claimant": claimant_index,
            "action": action,
            "meld_code": meld_code,
            "combination_mask": combination_mask,
            "draw_player": claimant_index if drawn_tile is not None else None,
            "drawn_tile": drawn_tile,
            "next_status": self.game_status,
        }

    def declare_concealed_kong(self, player_index: int, tile: int) -> dict:
        """Declare a concealed kong, store true tile server-side, and draw a supplement."""
        player = self.player_list[player_index]
        is_mo_gang = bool(player.has_draw_slot and player.hand_tiles and player.hand_tiles[-1] == tile)
        self._remove_tiles(player.hand_tiles, [tile, tile, tile, tile])
        player.has_draw_slot = False
        player.combination_tiles.append(f"G{tile}")
        combination_mask = [2, tile, 2, tile, 2, tile, 2, tile]
        player.combination_mask.append(combination_mask)
        self._mark_opening_action(interrupt=True)
        drawn_tile = self._draw_supplement_tile(player_index)
        return {
            "player": player_index,
            "action": "angang",
            "meld_code": f"G{tile}",
            "public_meld_code": "G0",
            "combination_mask": combination_mask,
            "is_mo_gang": is_mo_gang,
            "drawn_tile": drawn_tile,
            "next_status": self.game_status,
        }

    def attempt_added_kong(self, player_index: int, tile: int) -> dict:
        """Validate an added-kong attempt without mutating state before robbing responses."""
        player = self.player_list[player_index]
        if tile not in player.hand_tiles:
            raise ValueError(f"Player {player_index} cannot add kong tile {tile}; it is not in hand.")
        if f"k{tile}" not in player.combination_tiles:
            raise ValueError(f"Player {player_index} has no exposed triplet k{tile} to upgrade.")
        self.current_player_index = player_index
        self._mark_opening_action(interrupt=True)
        self.game_status = "waiting_action_qianggang"
        is_mo_gang = bool(player.has_draw_slot and player.hand_tiles and player.hand_tiles[-1] == tile)
        try:
            triplet_index = player.combination_tiles.index(f"k{tile}")
            combination_mask = list(player.combination_mask[triplet_index])
        except (ValueError, IndexError):
            combination_mask = [1, tile, 0, tile, 0, tile]
        insert_index = next((idx for idx in range(0, len(combination_mask), 2) if combination_mask[idx] == 1), 0)
        preview_mask = list(combination_mask)
        preview_mask[insert_index:insert_index] = [3, tile]
        return {
            "player": player_index,
            "tile": tile,
            "action": "jiagang",
            "meld_code": f"k{tile}",
            "combination_mask": preview_mask,
            "is_mo_gang": is_mo_gang,
            "next_status": self.game_status,
        }

    def resolve_added_kong_responses(
        self,
        kong_player_index: int,
        tile: int,
        responses: Dict[int, str],
        settlements: Optional[Dict[int, dict]] = None,
    ) -> dict:
        """Resolve robbing-kong responses, or finalize the added kong and supplement draw."""
        settlements = settlements or {}
        winners: list[int] = []
        passed: list[int] = []
        for player_index in self._response_order_after(kong_player_index, responses):
            action = responses[player_index]
            if player_index == kong_player_index or self.player_list[player_index].is_hu:
                continue
            if action == "pass":
                if tile in self.player_list[player_index].waiting_tiles and self.can_win_by_discard(player_index, tile):
                    self.add_discard_win_lockout(player_index, tile)
                passed.append(player_index)
            elif action == "hu":
                if tile not in self.player_list[player_index].waiting_tiles:
                    raise ValueError(f"Player {player_index} is not waiting on added-kong tile {tile}.")
                if not self.can_win_by_discard(player_index, tile):
                    raise ValueError(f"Player {player_index} is locked out from robbing kong tile {tile}.")
                self.mark_player_hu(
                    player_index,
                    {
                        "source": "rob_kong",
                        "kong_player": kong_player_index,
                        "tile": tile,
                        **(
                            settlements.get(player_index)
                            or self.settlement_policy.build(
                                self,
                                player_index,
                                "rob_kong",
                                tile,
                                payer_index=kong_player_index,
                            )
                        ),
                    },
                )
                winners.append(player_index)
                # Robbing a kong follows the same first-win priority rule.
                break
            elif action in {"", "none"}:
                continue
            else:
                raise ValueError(f"Unsupported robbing-kong response: {action}")

        if winners:
            self._consume_robbed_added_kong_tile(kong_player_index, tile)
            drawn_tile = None
            draw_player = None
            if self.game_status != "END":
                drawn_tile = self.draw_after_discard_resolution(winners[-1])
                draw_player = self.current_player_index if drawn_tile is not None else None
            return {
                "robbed": True,
                "winners": winners,
                "passed": passed,
                "draw_player": draw_player,
                "drawn_tile": drawn_tile,
                "ended": self.game_status == "END",
            }

        kong_result = self._finalize_added_kong(kong_player_index, tile)
        drawn_tile = self._draw_supplement_tile(kong_player_index)
        return {
            "robbed": False,
            "winners": [],
            "passed": passed,
            **kong_result,
            "draw_player": kong_player_index,
            "drawn_tile": drawn_tile,
            "ended": self.game_status == "END",
        }

    def _select_discard_claim(self, discarder_index: int, responses: Dict[int, str]) -> Optional[tuple[int, str]]:
        candidates: list[tuple[int, int, int, str]] = []
        fixed_next_player = (discarder_index + 1) % 4
        priority = {
            "gang": 2,
            "peng": 2,
            "chi_left": 1,
            "chi_mid": 1,
            "chi_right": 1,
        }
        for player_index, action in responses.items():
            if action not in priority:
                continue
            if player_index == discarder_index or self.player_list[player_index].is_hu:
                continue
            if action.startswith("chi") and player_index != fixed_next_player:
                continue
            distance = (player_index - discarder_index) % 4
            candidates.append((-priority[action], distance, player_index, action))
        if not candidates:
            return None
        _, _, player_index, action = min(candidates)
        return player_index, action

    @staticmethod
    def _response_order_after(from_index: int, responses: Dict[int, str]) -> list[int]:
        return sorted(responses, key=lambda player_index: (player_index - from_index) % 4)

    def _apply_discard_claim(self, player: JiandanPlayer, tile: int, action: str) -> tuple[str, list[int]]:
        if action == "peng":
            hand_tiles = [tile, tile]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"k{tile}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1]]
        elif action == "gang":
            hand_tiles = [tile, tile, tile]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"g{tile}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1], 0, hand_tiles[2]]
        elif action == "chi_left":
            hand_tiles = [tile - 2, tile - 1]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"s{tile - 2}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1]]
        elif action == "chi_mid":
            hand_tiles = [tile - 1, tile + 1]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"s{tile - 1}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1]]
        elif action == "chi_right":
            hand_tiles = [tile + 1, tile + 2]
            self._remove_tiles(player.hand_tiles, hand_tiles)
            meld_code = f"s{tile}"
            combination_mask = [1, tile, 0, hand_tiles[0], 0, hand_tiles[1]]
        else:
            raise ValueError(f"Unsupported discard claim action: {action}")
        player.combination_tiles.append(meld_code)
        player.combination_mask.append(combination_mask)
        self._mark_opening_action(interrupt=True)
        return meld_code, combination_mask

    def _finalize_added_kong(self, player_index: int, tile: int) -> dict:
        player = self.player_list[player_index]
        is_mo_gang = bool(player.has_draw_slot and player.hand_tiles and player.hand_tiles[-1] == tile)
        self._remove_tiles(player.hand_tiles, [tile])
        player.has_draw_slot = False
        try:
            triplet_index = player.combination_tiles.index(f"k{tile}")
        except ValueError as exc:
            raise ValueError(f"Player {player_index} has no exposed triplet k{tile} to upgrade.") from exc
        if triplet_index < len(player.combination_mask):
            combination_mask = list(player.combination_mask[triplet_index])
        else:
            combination_mask = [1, tile, 0, tile, 0, tile]
        insert_index = next((idx for idx in range(0, len(combination_mask), 2) if combination_mask[idx] == 1), 0)
        combination_mask[insert_index:insert_index] = [3, tile]
        player.combination_mask[triplet_index:triplet_index + 1] = [combination_mask]
        player.combination_tiles[triplet_index] = f"g{tile}"
        return {
            "meld_code": f"g{tile}",
            "previous_meld_code": f"k{tile}",
            "combination_mask": combination_mask,
            "is_mo_gang": is_mo_gang,
        }

    def _consume_robbed_added_kong_tile(self, player_index: int, tile: int) -> None:
        """Robbed added-kong tile leaves the kong player's hand, but the meld stays a triplet."""
        player = self.player_list[player_index]
        self._remove_tiles(player.hand_tiles, [tile])
        player.has_draw_slot = False

    def _window_after_draw_result(self, result: dict, reason: str, extra: dict) -> dict:
        if result.get("ended") or self.game_status == "END":
            return self._end_window(extra)
        draw_player = result.get("draw_player")
        if draw_player is None:
            return self._end_window(extra)
        window = self.begin_hand_action(draw_player)
        window["reason"] = reason
        window["drawn_tile"] = result.get("drawn_tile")
        window.update(extra)
        return window

    def _end_window(self, extra: Optional[dict] = None) -> dict:
        payload = {"status": "END", "player": None, "actions": None, "ended_by": self.ended_by}
        if extra:
            payload.update(extra)
        return payload

    def _mark_opening_action(self, *, interrupt: bool = False) -> None:
        self.opening_action_taken = True
        if interrupt:
            self.opening_flow_interrupted = True

    def _pre_win_tiles_for_context(self, source: str, hand_tiles: list[int], winning_tile: int) -> list[int]:
        pre_win_tiles = list(hand_tiles)
        if winning_tile in pre_win_tiles:
            pre_win_tiles.remove(winning_tile)
        return pre_win_tiles

    def _settlement_context(self, winner_index: int, source: str, hand_tiles: list[int], tile: int) -> dict:
        rinshan = source == "self_draw" and bool(self.hand_action_is_gang_draw.get(winner_index))
        return {
            "win_source": source,
            "pre_win_tiles": self._pre_win_tiles_for_context(source, hand_tiles, tile),
            "heavenly_win": (
                source == "self_draw"
                and winner_index == self.dealer_index
                and not self.opening_action_taken
                and not rinshan
            ),
            "earthly_win": (
                source == "self_draw"
                and winner_index != self.dealer_index
                and self.natural_draw_count.get(winner_index, 0) == 1
                and not self.opening_flow_interrupted
                and not rinshan
            ),
            "haitei": source == "self_draw" and len(self.tiles_list) <= self.dead_wall_count,
            "houtei": source == "discard" and len(self.tiles_list) <= self.dead_wall_count,
            "rinshan": rinshan,
            "chankan": source == "rob_kong",
        }

    def _draw_supplement_tile(self, player_index: int) -> int:
        if not self.tiles_list:
            self.ended_by = self.ended_by or "wall"
            self.game_status = "END"
            raise ValueError("Cannot draw a supplement tile when the wall is empty.")
        self.current_player_index = player_index
        drawn_tile = self.player_list[player_index].get_tile(self.tiles_list)
        self.game_status = "waiting_hand_action"
        return drawn_tile

    @staticmethod
    def _remove_tiles(hand_tiles: list[int], tiles: list[int]) -> None:
        for tile in tiles:
            if tile not in hand_tiles:
                raise ValueError(f"Cannot remove tile {tile}; it is not in hand.")
        for tile in tiles:
            hand_tiles.remove(tile)
