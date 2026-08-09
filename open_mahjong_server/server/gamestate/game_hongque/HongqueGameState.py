from __future__ import annotations

import asyncio
import logging
import random
import time
from dataclasses import dataclass, field
from typing import Any, Optional, Sequence

from .efficiency_bot import choose_claim_plan, choose_turn_plan
from .heuristic_bot import (
    OpponentView,
    choose_claim_plan as choose_claim_plan_v3,
    choose_turn_plan as choose_turn_plan_v3,
)
from .rules import (
    call_candidates,
    classify_meld,
    kong_candidates,
    kong_win_candidates,
)
from .scoring import best_win_result
from .tile import HongqueTile, full_deck
from .win_check import is_winning_hand
from ..public.ai.bot_executor import run_room_bot_cpu
from ..public.vote_manager import vote_checkpoint
from ..public.round_end_timing import (
    ROUND_END_PRESENTATION_FADE_SEC,
    SICHUAN_MID_PANEL_CONFIRM_SEC,
    hu_result_ready_pre_panel_seconds,
    hu_result_ready_wait_seconds,
    liuju_ready_wait_seconds,
    sichuan_settle_hu_panel_wait_seconds,
)
from .hongque_debug import (
    HONGQUE_DEBUG_SCENARIO,
    apply_debug_player_seating,
    apply_hongque_debug_hands,
    get_debug_dealer_index,
    get_debug_forced_discard,
    resolve_debug_scenario,
)

logger = logging.getLogger(__name__)
BOT_ACTION_DELAY = 0.5


@dataclass
class HongquePlayer:
    user_id: int
    username: str
    index: int
    hand: list[str] = field(default_factory=list)
    discards: list[str] = field(default_factory=list)
    melds: list[dict] = field(default_factory=list)
    score: int = 0
    supplements: int = 0
    online: bool = True
    title_used: int = 1
    profile_used: int = 1
    character_used: int = 1
    voice_used: int = 1
    drawn_tile: Optional[str] = None
    last_draw_was_supplement: bool = False
    remaining_time: int = 20
    score_history: list[str] = field(default_factory=list)
    round_number_history: list[int] = field(default_factory=list)

    @property
    def is_bot(self) -> bool:
        return self.user_id <= 10


class HongqueGameState:
    """Authoritative, memory-only Hongque 2 game state.

    The wire format uses HQv3.1 resource keys (``AX1`` ... ``GY9``).  All
    actions are checked server-side.  This rule intentionally has no
    database, statistics, replay, spectator, or match integration.
    """

    def __init__(self, game_server: Any, room_data: dict, calculation_service: Any = None,
                 db_manager: Any = None, gamestate_id: str = "hongque-test",
                 debug: Optional[bool] = None) -> None:
        self.game_server = game_server
        self.room_id = room_data["room_id"]
        self.gamestate_id = gamestate_id
        self.room_rule = "hongque"
        self.sub_rule = room_data.get("sub_rule", "hongque/v1.6")
        self.room_type = room_data.get("room_type", "custom")
        self.allow_spectator_config = False
        self.spectator_enabled = False
        self.realtime_spectators: list = []
        self.max_round = max(1, int(room_data.get("game_round", 1))) * 4
        round_timer = max(0, int(room_data.get("round_timer", 20) or 0))
        step_timer = max(0, int(room_data.get("step_timer", 5) or 0))
        self.round_time = round_timer
        self.step_time = step_timer
        self.turn_seconds = max(1, self.round_time + self.step_time)
        # Claim windows use the same round-time bank plus step time as hand
        # actions.  The early implementation only used step_time here (normally 5s),
        # which made chi/peng/win prompts disappear far too quickly.
        self.claim_seconds = self.turn_seconds
        self.tips = bool(room_data.get("tips", True))
        # 战术鸣牌与国标一致：申请广播后停顿再开打断窗口，窗口按 grace 秒数计时。
        self.tactical_pre_grace_delay = float(
            room_data.get("tactical_pre_grace_delay", 0.5)
        )
        self.tactical_grace_seconds = float(
            room_data.get("tactical_grace_seconds", 5.0)
        )
        # 如果您在管理自己规则内的分支，请不要将 Debug = True 的配置上传到公共代码仓库。
        self.Debug = False if debug is None else bool(debug)
        self.debug_scenario = HONGQUE_DEBUG_SCENARIO
        # 和牌方式：multi_ron = 多家可同时荣和（依次展示结算面板）；
        # head_bump = 头跳，只由距出牌者最近的荣和者截和。
        self.hepai_way = room_data.get("hepai_way", "multi_ron")
        self.turn_deadline: Optional[float] = None
        self.claim_deadline: Optional[float] = None
        self.turn_started_at: Optional[float] = None
        self.claim_started_at: Optional[float] = None
        self.claim_deadlines: dict[int, float] = {}
        self.current_round = 1
        self.dealer_index = 0
        self.current_player_index = 0
        self.phase = "starting"
        self.wall: list[str] = []
        self.last_discard: Optional[dict] = None
        self.action_tick = 0
        self.message = ""
        self.round_result: Optional[dict] = None
        self.events: list[dict] = []
        self.event_sequence = 0
        self.game_task: Optional[asyncio.Task] = None
        self._lock = asyncio.Lock()
        self._bot_task: Optional[asyncio.Task] = None
        self._bot_claim_tasks: dict[int, asyncio.Task] = {}
        self._round_task: Optional[asyncio.Task] = None
        self._ready_phase_active = False
        self._ready_players: set[int] = set()
        self._ready_event = asyncio.Event()
        self._rng = random.Random(str(room_data.get("random_seed") or gamestate_id))
        self.claim_options: dict[int, list[dict]] = {}
        self.claim_responses: dict[int, dict] = {}
        # 战术鸣牌：记录本张弃牌区间内已广播「亮牌申请」的 (玩家 -> candidate id)。
        # 申请帧只发声/显示动画，不改变牌面；最终执行帧据此标记 silent，避免重复发声。
        self._claim_apply_broadcast: dict[int, str] = {}
        # 当前生效的亮牌申请（打断窗口期间的最终候选）。
        self._claim_applied: Optional[tuple[int, dict]] = None
        self._claim_grace_active = False
        self._claim_grace_task: Optional[asyncio.Task] = None
        self._claim_grace_timeout_task: Optional[asyncio.Task] = None
        self._claim_grace_deadline: Optional[float] = None
        # 打断窗口内已 pass 的玩家：本轮申请不再重复询问；新申请替换时清空。
        self._grace_passed_players: set[int] = set()
        # 川麻模式：已提交中/低优先级亮牌的玩家，若仍有更高优先级选项，可在窗口内升级亮牌。
        self._claim_upgrade_players: set[int] = set()
        self._claim_timeout_task: Optional[asyncio.Task] = None
        self._turn_timeout_task: Optional[asyncio.Task] = None
        self.players: list[HongquePlayer] = []
        settings = room_data.get("player_settings", {})
        for index, user_id in enumerate(room_data["player_list"]):
            player_settings = settings.get(user_id, settings.get(str(user_id), {}))
            self.players.append(HongquePlayer(
                user_id=user_id,
                username=player_settings.get("username", f"玩家{user_id}"),
                index=index,
                title_used=int(player_settings.get("title_id", 1)),
                profile_used=int(player_settings.get("profile_image_id", 1)),
                character_used=int(player_settings.get("character_id", 1)),
                voice_used=int(player_settings.get("voice_id", 1)),
                remaining_time=self.round_time,
            ))
        self.player_list = self.players

    async def run_game_loop(self) -> None:
        try:
            await self._start_round()
            # 虹雀是事件驱动状态机，没有其它规则的 while game_status 主循环。
            # 在暂停期间持有状态锁，玩家、超时和机器人任务都无法推进牌局。
            while True:
                vote_manager = getattr(self, "vote_manager", None)
                if vote_manager is not None and vote_manager.phase == "pause_pending":
                    async with self._lock:
                        if vote_manager.phase == "pause_pending":
                            paused_at = time.monotonic()
                            await vote_checkpoint(self)
                            self._shift_clocks_after_vote_pause(
                                time.monotonic() - paused_at
                            )
                await asyncio.sleep(0.05)
        except asyncio.CancelledError:
            raise

    def _shift_clocks_after_vote_pause(self, paused_seconds: float) -> None:
        """暂停不消耗虹雀的步时、储备时间或战术鸣牌窗口。"""
        if paused_seconds <= 0:
            return
        if self.turn_deadline is not None:
            self.turn_deadline += paused_seconds
        if self.claim_deadline is not None:
            self.claim_deadline += paused_seconds
        if self.turn_started_at is not None:
            self.turn_started_at += paused_seconds
        if self.claim_started_at is not None:
            self.claim_started_at += paused_seconds
        if self._claim_grace_deadline is not None:
            self._claim_grace_deadline += paused_seconds
        self.claim_deadlines = {
            index: deadline + paused_seconds
            for index, deadline in self.claim_deadlines.items()
        }

    async def _start_round(self) -> None:
        self._cancel_bot_claim_tasks()
        self._ready_phase_active = False
        self._ready_players.clear()
        self._ready_event.clear()
        if self.Debug:
            # 调试：真人固定自家(0)，庄家/首家固定为场景指定座位（上家=3）。
            apply_debug_player_seating(self)
            self.dealer_index = get_debug_dealer_index(self)
        self.wall = full_deck()
        self._rng.shuffle(self.wall)
        for player in self.players:
            player.hand.clear()
            player.discards.clear()
            player.melds.clear()
            player.supplements = 0
            player.drawn_tile = None
            player.last_draw_was_supplement = False
            player.remaining_time = self.round_time
        for _ in range(11):
            for offset in range(4):
                self.players[(self.dealer_index + offset) % 4].hand.append(self.wall.pop())
        self.current_player_index = self.dealer_index
        self.last_discard = None
        self.claim_options.clear()
        self.claim_responses.clear()
        self._claim_apply_broadcast.clear()
        self._claim_applied = None
        self._claim_grace_active = False
        self._grace_passed_players.clear()
        self._claim_upgrade_players.clear()
        self.round_result = None
        self.events = []
        self.phase = "turn"
        self._start_turn_clock()
        self._draw_for_current_player()
        if self.Debug:
            # 调试：发牌后覆盖为场景固定牌例（上家含 AX1 首打，自家/下家听 AX1）。
            apply_hongque_debug_hands(self)
        self.message = f"第 {self.current_round} 局开始"
        self._advance_tick()
        await self.broadcast_state(sync_mode="round_start")
        self._schedule_turn_timeout()
        self._schedule_bot_if_needed()

    def _draw_for_current_player(self, reason: str = "draw") -> Optional[str]:
        if not self.wall:
            return None
        player = self.players[self.current_player_index]
        tile = self.wall.pop()
        player.hand.append(tile)
        player.drawn_tile = tile
        player.last_draw_was_supplement = reason == "supplement"
        self._record_event(reason, player=player.index, tile=tile)
        return tile

    def _record_event(self, event_type: str, **payload: Any) -> dict:
        self.event_sequence += 1
        event = {"id": self.event_sequence, "type": event_type, **payload}
        self.events.append(event)
        return event

    def _advance_tick(self) -> None:
        self.action_tick += 1

    def _start_turn_clock(self) -> None:
        self.claim_deadline = None
        self.claim_started_at = None
        self.claim_deadlines.clear()
        self.turn_started_at = time.monotonic()
        player = self.players[self.current_player_index]
        self.turn_deadline = self.turn_started_at + self.step_time + player.remaining_time

    def _start_claim_clock(self) -> None:
        self.turn_deadline = None
        self.turn_started_at = None
        self.claim_started_at = time.monotonic()
        self.claim_deadlines = {
            index: self.claim_started_at + self.step_time + self.players[index].remaining_time
            for index in self.claim_options
        }
        self.claim_deadline = max(self.claim_deadlines.values(), default=self.claim_started_at)

    def _remaining_clock(self, viewer: HongquePlayer) -> tuple[int, int]:
        # 战术鸣牌打断窗口：剩余时间按 grace 秒数显示，不再叠加步时（与国标一致）。
        if (self._claim_grace_active and self.phase == "claim"
                and viewer.index in self.claim_options
                and viewer.index not in self.claim_responses
                and self._claim_grace_deadline is not None):
            remaining = max(
                0, int(self._claim_grace_deadline - time.monotonic() + 0.999)
            )
            return remaining, 0
        started_at: Optional[float] = None
        if self.phase == "turn" and viewer.index == self.current_player_index:
            started_at = self.turn_started_at
        elif (self.phase == "claim" and viewer.index in self.claim_options
                and viewer.index not in self.claim_responses):
            started_at = self.claim_started_at
        if started_at is None:
            return max(0, viewer.remaining_time), 0
        elapsed = max(0.0, time.monotonic() - started_at)
        step_remaining = max(0, int(self.step_time - elapsed + 0.999))
        bank_spent = max(0.0, elapsed - self.step_time)
        bank_remaining = max(0, int(viewer.remaining_time - bank_spent + 0.999))
        return bank_remaining, step_remaining

    def _consume_time_bank(self, player: HongquePlayer, started_at: Optional[float]) -> None:
        if started_at is None:
            return
        elapsed = max(0.0, time.monotonic() - started_at)
        overtime = max(0, int(elapsed) - self.step_time)
        if overtime:
            player.remaining_time = max(0, player.remaining_time - overtime)

    def _schedule_turn_timeout(self) -> None:
        if (self._turn_timeout_task and self._turn_timeout_task is not asyncio.current_task()
                and not self._turn_timeout_task.done()):
            self._turn_timeout_task.cancel()
        if self.phase != "turn" or self.turn_deadline is None:
            return
        self._turn_timeout_task = asyncio.create_task(
            self._turn_timeout(self.action_tick, self.current_player_index, self.turn_deadline)
        )

    async def _turn_timeout(self, tick: int, player_index: int, deadline: float) -> None:
        try:
            while True:
                await asyncio.sleep(max(0.0, deadline - time.monotonic()))
                async with self._lock:
                    if (self.phase != "turn" or self.action_tick != tick
                            or self.current_player_index != player_index):
                        return
                    # 投票暂停会顺延权威 deadline；旧睡眠结束后按新值继续等待。
                    if self.turn_deadline is not None \
                            and time.monotonic() < self.turn_deadline:
                        deadline = self.turn_deadline
                        continue
                    self.events = []
                    player = self.players[player_index]
                    player.remaining_time = 0
                    if player.hand:
                        if self.Debug:
                            forced = get_debug_forced_discard(self, player_index)
                            if forced and forced in player.hand:
                                code = forced
                            else:
                                code = (
                                    player.drawn_tile
                                    if player.drawn_tile in player.hand
                                    else player.hand[-1]
                                )
                        else:
                            code = (
                                player.drawn_tile
                                if player.drawn_tile in player.hand
                                else player.hand[-1]
                            )
                    else:
                        result = best_win_result(
                            player.hand,
                            player.melds,
                            self_draw=True,
                            before_first_discard=not any(item.discards for item in self.players),
                            wall_empty=not self.wall,
                            allow_kong_win=True,
                        )
                        if result is not None:
                            result["winning_hand"] = list(player.hand)
                            await self._finish_round([(player, result)], "self_draw")
                        return
                    await self._discard_and_open_claim(player, code)
                    return
        except asyncio.CancelledError:
            return

    async def submit_action(self, user_id: int, action: str, tile: Optional[str] = None,
                            candidate_id: Optional[str] = None, action_tick: Optional[int] = None) -> None:
        async with self._lock:
            self.events = []
            player = next((item for item in self.players if item.user_id == user_id), None)
            if player is None:
                raise ValueError("玩家不在本局中")
            if action_tick is not None and int(action_tick) != self.action_tick:
                raise ValueError("操作已过期，请按最新局面重试")
            if self.phase == "round_end" and action == "ready":
                await self._handle_ready_action(player)
            elif self.phase == "turn":
                await self._handle_turn_action(player, action, tile, candidate_id)
            elif self.phase == "claim":
                await self._handle_claim_action(player, action, candidate_id)
            else:
                raise ValueError("当前阶段不能操作")

    async def _handle_turn_action(self, player: HongquePlayer, action: str,
                                  tile: Optional[str], candidate_id: Optional[str]) -> None:
        if player.index != self.current_player_index:
            raise ValueError("还没有轮到你")
        if action == "discard":
            code = HongqueTile.parse(tile or "").code
            if code not in player.hand:
                raise ValueError("手牌中没有这张牌")
            self._consume_time_bank(player, self.turn_started_at)
            await self._discard_and_open_claim(player, code)
            return
        if action == "supplement":
            if player.supplements >= 2 or not self.wall:
                raise ValueError("本局补牌次数已用尽或牌库已空")
            self._consume_time_bank(player, self.turn_started_at)
            player.supplements += 1
            tile = self.wall.pop()
            player.hand.append(tile)
            player.drawn_tile = tile
            player.last_draw_was_supplement = True
            self._record_event("supplement", player=player.index, tile=tile)
            self.message = f"{player.username} 补牌"
        elif action == "kong":
            if len(player.hand) == 1:
                # 手牌只剩最后一张时杠后必然空手成和，直接走“和”即可，普通杠不单独成立。
                raise ValueError("手牌只剩一张时请使用和")
            candidates = kong_candidates(player.hand, player.melds)
            candidate = next((item for item in candidates if item["id"] == candidate_id), None)
            if candidate is None:
                raise ValueError("杠牌候选无效")
            self._consume_time_bank(player, self.turn_started_at)
            self._apply_kong(player, candidate)
            self.message = f"{player.username} 杠牌"
        elif action == "win":
            result = best_win_result(
                player.hand,
                player.melds,
                self_draw=True,
                before_first_discard=not any(item.discards for item in self.players),
                wall_empty=not self.wall,
                allow_kong_win=True,
            )
            if result is None:
                raise ValueError("当前手牌不能和牌")
            self._consume_time_bank(player, self.turn_started_at)
            result["winning_hand"] = list(player.hand)
            await self._finish_round([(player, result)], "self_draw")
            return
        else:
            raise ValueError("未知的回合操作")
        self._start_turn_clock()
        self._advance_tick()
        await self.broadcast_state()
        self._schedule_turn_timeout()
        self._schedule_bot_if_needed()

    def _apply_kong(self, player: HongquePlayer, candidate: dict) -> None:
        """把杠/杠和候选中的手牌并入对应明牌，同步手牌、副露与事件。"""
        for code in candidate["hand_tiles"]:
            player.hand.remove(code)
        if player.drawn_tile not in player.hand:
            player.drawn_tile = None
        claimed_tile = player.melds[candidate["meld_index"]].get("claimed_tile")
        player.melds[candidate["meld_index"]]["tiles"] = list(candidate["tiles"])
        player.melds[candidate["meld_index"]]["kind"] = classify_meld(
            candidate["tiles"]
        ).kind
        self._record_event(
            "kong",
            player=player.index,
            tiles=list(candidate["tiles"]),
            hand_tiles=list(candidate["hand_tiles"]),
            meld_index=candidate["meld_index"],
            claimed_tile=claimed_tile,
        )

    async def _discard_and_open_claim(self, player: HongquePlayer, code: str) -> None:
        """Apply the one authoritative discard transition for humans and bots."""
        if self._turn_timeout_task and self._turn_timeout_task is not asyncio.current_task():
            self._turn_timeout_task.cancel()
        player.hand.remove(code)
        player.discards.append(code)
        cut_class = code == player.drawn_tile
        player.drawn_tile = None
        player.last_draw_was_supplement = False
        self.last_discard = {"player": player.index, "tile": code}
        self._record_event(
            "discard",
            player=player.index,
            tile=code,
            cut_class=cut_class,
        )
        # Match the normal mahjong wire order: the authoritative discard is
        # visible immediately.  Candidate/win enumeration for the other three
        # seats must not sit in front of the discard animation.
        self.phase = "resolving"
        self.turn_deadline = None
        self.turn_started_at = None
        self.message = f"{player.username} 出牌"
        self._advance_tick()
        await self.broadcast_state()
        self.events = []
        await self._open_claim_window()

    async def _open_claim_window(self) -> None:
        assert self.last_discard is not None
        discard_player = self.last_discard["player"]
        discarded = self.last_discard["tile"]
        self.claim_options = {}
        self.claim_responses = {}
        self._claim_apply_broadcast.clear()
        self._claim_upgrade_players.clear()
        for player in self.players:
            if player.index == discard_player:
                continue
            options = call_candidates(
                player.hand,
                discarded,
                claimant_index=player.index,
                discarder_index=discard_player,
            )
            if is_winning_hand(player.hand + [discarded], player.melds):
                options.insert(0, {"id": "ron", "kind": "win", "priority": 7,
                                   "tiles": [discarded], "hand_tiles": []})
            if options:
                self.claim_options[player.index] = options
        if not self.claim_options:
            await self._advance_after_unclaimed_discard()
            return
        self.phase = "claim"
        self._start_claim_clock()
        self.message = "等待亮牌或捉和"
        self._advance_tick()
        await self.broadcast_state()
        # The discard batch has been delivered.  Any automatic bot passes and
        # following draw form a new batch; event ids prevent reconnect/reply
        # updates from replaying the discard animation.
        self.events = []
        smart_bot_indices: list[int] = []
        for index in self.claim_options:
            debug_force_ron = (
                self.Debug
                and resolve_debug_scenario(self) == "double_ron"
                and index == 1
                and any(option.get("kind") == "win" for option in self.claim_options[index])
            )
            if self.players[index].user_id in (2, 3) or debug_force_ron:
                smart_bot_indices.append(index)
            elif self.players[index].is_bot:
                self.claim_responses[index] = {"action": "pass"}
        if all(index in self.claim_responses for index in self.claim_options):
            await self._resolve_claims()
            return
        if self._claim_timeout_task and not self._claim_timeout_task.done():
            self._claim_timeout_task.cancel()
        self._claim_timeout_task = asyncio.create_task(self._claim_timeout(self.action_tick))
        for index in smart_bot_indices:
            self._schedule_bot_claim(index, self.action_tick)

    async def _handle_claim_action(self, player: HongquePlayer, action: str,
                                   candidate_id: Optional[str]) -> None:
        if player.index not in self.claim_options:
            raise ValueError("你没有可用的亮牌操作")
        existing = self.claim_responses.get(player.index)
        if action == "pass":
            if existing is not None:
                raise ValueError("已经回应过本次亮牌询问")
            self._consume_time_bank(player, self.claim_started_at)
            self.claim_responses[player.index] = {"action": "pass"}
            if self._claim_grace_active:
                # 打断窗口内的 pass：本轮不再重复询问该玩家。
                self._grace_passed_players.add(player.index)
                if not self._claim_grace_pending():
                    await self._resolve_claims()
                else:
                    await self.send_state_to(player.index)
                return
        elif action == "claim":
            candidate = next((item for item in self.claim_options[player.index] if item["id"] == candidate_id), None)
            if candidate is None:
                raise ValueError("亮牌候选无效")
            if existing is not None and existing["action"] == "claim":
                # 川麻模式：已提交低/中优先级亮牌后，仍可升级为更高优先级亮牌（不锁死）。
                existing_priority = int(existing["candidate"].get("priority", 0) or 0)
                new_priority = int(candidate.get("priority", 0) or 0)
                if new_priority <= existing_priority:
                    raise ValueError("不能降级为更低优先级的亮牌")
                self.claim_responses[player.index] = {"action": "claim", "candidate": candidate}
                self._claim_upgrade_players.discard(player.index)
            elif existing is not None:
                raise ValueError("已经回应过本次亮牌询问")
            else:
                self._consume_time_bank(player, self.claim_started_at)
                self.claim_responses[player.index] = {"action": "claim", "candidate": candidate}
            if self._claim_grace_active:
                # 打断窗口内：更高优先级（或更近同优先级）才替换申请；同优先级和牌共存（多家和）。
                if self._claim_upgrades_applied(player.index, candidate):
                    await self._begin_claim_grace(player.index, candidate)
                else:
                    await self._broadcast_claim_apply(player.index, candidate)
                    if not self._claim_grace_pending():
                        await self._resolve_claims()
                return
            await self._begin_claim_grace(player.index, candidate)
            return
        else:
            raise ValueError("未知的亮牌操作")
        if all(index in self.claim_responses for index in self.claim_options):
            await self._resolve_claims()
        else:
            await self.send_state_to(player.index)

    async def _broadcast_claim_apply(self, player_index: int, candidate: dict) -> None:
        """战术鸣牌申请：立即广播发声/动画帧，不改牌面，与国标 is_claim 一致。

        荣和也走申请帧（和牌声提前播放），最终结算/执行帧标记 silent 避免重复发声。
        """
        if not candidate:
            return
        candidate_id = candidate.get("id")
        if self._claim_apply_broadcast.get(player_index) == candidate_id:
            return
        self._claim_apply_broadcast[player_index] = candidate_id
        self.events = []
        self._record_event(
            "claim_apply",
            player=player_index,
            kind=candidate.get("kind"),
            base_kind=candidate.get("base_kind", candidate.get("kind")),
            tile=self.last_discard["tile"] if self.last_discard else None,
            tiles=list(candidate.get("tiles", ())),
            hand_tiles=list(candidate.get("hand_tiles", ())),
        )
        await self.broadcast_state()

    async def _begin_claim_grace(self, player_index: int, candidate: dict) -> None:
        """开始战术鸣牌打断流程（与国标 apply_tactical_claim_if_needed 对齐）：

        1) 申请帧立即广播；2) 停顿 tactical_pre_grace_delay 后重新询问更高优先级玩家
        （含主询问已 pass 者）；3) 打断窗口按 tactical_grace_seconds 独立计时。
        """
        self._claim_applied = (player_index, candidate)
        self._claim_grace_active = True
        self._grace_passed_players = set()
        if self._claim_timeout_task and self._claim_timeout_task is not asyncio.current_task():
            self._claim_timeout_task.cancel()
            self._claim_timeout_task = None
        if self._claim_grace_timeout_task \
                and self._claim_grace_timeout_task is not asyncio.current_task():
            self._claim_grace_timeout_task.cancel()
            self._claim_grace_timeout_task = None
        await self._broadcast_claim_apply(player_index, candidate)
        self._schedule_claim_grace_flow()

    def _schedule_claim_grace_flow(self) -> None:
        if self._claim_grace_task and self._claim_grace_task is not asyncio.current_task() \
                and not self._claim_grace_task.done():
            self._claim_grace_task.cancel()
        self._claim_grace_task = asyncio.create_task(
            self._claim_grace_flow(self.action_tick)
        )

    async def _claim_grace_flow(self, tick: int) -> None:
        """申请帧后的停顿 + 打开打断窗口；在后台任务执行，避免占用房间锁。"""
        try:
            await asyncio.sleep(max(0.0, self.tactical_pre_grace_delay))
            async with self._lock:
                if (not self._claim_grace_active or self.phase != "claim"
                        or self.action_tick != tick):
                    return
                if self._claim_grace_task is not asyncio.current_task():
                    return
                await self._open_grace_window()
        except asyncio.CancelledError:
            return

    async def _open_grace_window(self) -> None:
        """重新询问更高优先级玩家（含主询问已 pass 者），并按 grace 秒数开窗。

        川麻模式：已提交低/中优先级亮牌的玩家，只要仍有更高优先级选项，也重新弹出
        更高档按钮，允许在窗口内升级亮牌（不锁死首亮）。
        """
        if not self._claim_applied:
            return
        applied_player, applied_candidate = self._claim_applied
        applied_rank = self._claim_rank(applied_player, applied_candidate)
        reasked = False
        for pid, options in self.claim_options.items():
            if pid == applied_player or pid in self._grace_passed_players:
                continue
            if self.players[pid].is_bot:
                # 机器人的 pass 是最终决定：只有智能机器人有决策任务且只在窗口开启时调度一次，
                # 打断窗口内重询不会再有机器人响应，跳过以免空等整个 grace 窗口。
                continue
            best_rank = max(
                (self._claim_rank(pid, o) for o in options),
                default=0,
            )
            if best_rank <= applied_rank:
                continue
            response = self.claim_responses.get(pid)
            if response is not None and response["action"] == "pass":
                # 主询问阶段已 pass：打断窗口内重新询问，允许改选更高动作。
                del self.claim_responses[pid]
                reasked = True
        upgrade_changed = False
        for pid, options in self.claim_options.items():
            if self.players[pid].is_bot:
                continue
            response = self.claim_responses.get(pid)
            if response is None or response["action"] != "claim":
                continue
            claim_priority = int(response["candidate"].get("priority", 0) or 0)
            best_rank = max(
                (self._claim_rank(pid, o) for o in options),
                default=0,
            )
            if best_rank > claim_priority:
                if pid not in self._claim_upgrade_players:
                    self._claim_upgrade_players.add(pid)
                    upgrade_changed = True
            else:
                self._claim_upgrade_players.discard(pid)
        if reasked or upgrade_changed:
            await self.broadcast_state()
        pending = self._claim_grace_pending()
        if pending:
            # 打断窗口重新计时；倒计时显示按 grace 秒数走。
            self.claim_started_at = time.monotonic()
            self.claim_deadline = None
            self._claim_grace_deadline = time.monotonic() + self.tactical_grace_seconds
            self._schedule_claim_grace_timeout()
            return
        await self._resolve_claims()

    def _claim_grace_pending(self) -> list[int]:
        """打断窗口内仍需回应的玩家：同/更高优先级且尚未回应（含潜在多家和、可升级亮牌者）。"""
        if not self._claim_applied:
            return []
        applied_player, applied_candidate = self._claim_applied
        applied_rank = self._claim_rank(applied_player, applied_candidate)
        pending: list[int] = []
        for pid in self._claim_upgrade_players:
            if pid in self._grace_passed_players:
                continue
            options = self.claim_options.get(pid, ())
            best_rank = max(
                (self._claim_rank(pid, o) for o in options),
                default=0,
            )
            if best_rank > applied_rank and pid not in pending:
                pending.append(pid)
        for pid, options in self.claim_options.items():
            if pid == applied_player or pid in self._grace_passed_players:
                continue
            best_rank = max(
                (self._claim_rank(pid, o) for o in options),
                default=0,
            )
            if best_rank < applied_rank:
                continue
            if pid not in self.claim_responses:
                pending.append(pid)
        return pending

    def _claim_upgrades_applied(self, pid: int, candidate: dict) -> bool:
        """打断窗口内新亮牌是否替换当前申请。"""
        if not self._claim_applied:
            return True
        applied_pid, applied_candidate = self._claim_applied
        if candidate.get("kind") == "win":
            # 荣和与荣和共存（多家和），不替换；荣和高于一切非和牌。
            return applied_candidate.get("kind") != "win"
        if applied_candidate.get("kind") == "win":
            return False
        return self._claim_beats(pid, candidate, applied_pid, applied_candidate)

    def _claim_beats(self, pid: int, candidate: dict,
                     other_pid: int, other_candidate: dict) -> bool:
        return self._claim_rank(pid, candidate) > self._claim_rank(
            other_pid, other_candidate
        )

    def _claim_rank(self, pid: int, candidate: dict) -> int:
        """虹雀亮牌优先级：候选自带（和7 > 虹6 > 碰5 > chi_first4 > chi_second3 > chi_third2）。

        吃按出牌者相对位置分档（与国标和牌 hu_first/second/third 同构），
        同档位（如两家碰）由 _resolve_claims 按提交先后裁决，与座位无关。
        """
        return int(candidate.get("priority", 0) or 0)

    def _schedule_claim_grace_timeout(self) -> None:
        if self._claim_grace_timeout_task \
                and self._claim_grace_timeout_task is not asyncio.current_task() \
                and not self._claim_grace_timeout_task.done():
            self._claim_grace_timeout_task.cancel()
        self._claim_grace_timeout_task = asyncio.create_task(
            self._claim_grace_timeout(self.action_tick)
        )

    async def _claim_grace_timeout(self, tick: int) -> None:
        try:
            deadline = self._claim_grace_deadline or time.monotonic()
            while True:
                await asyncio.sleep(max(0.0, deadline - time.monotonic()))
                async with self._lock:
                    if (not self._claim_grace_active or self.phase != "claim"
                            or self.action_tick != tick):
                        return
                    if self._claim_grace_timeout_task is not asyncio.current_task():
                        return
                    if self._claim_grace_deadline is not None \
                            and time.monotonic() < self._claim_grace_deadline:
                        deadline = self._claim_grace_deadline
                        continue
                    # 超时：未回应的同/更高优先级玩家视为放弃，执行当前申请。
                    await self._resolve_claims()
                    return
        except asyncio.CancelledError:
            return

    async def _claim_timeout(self, tick: int) -> None:
        try:
            while True:
                async with self._lock:
                    if self.phase != "claim" or self.action_tick != tick:
                        return
                    pending = [
                        index for index in self.claim_options
                        if index not in self.claim_responses
                    ]
                    if not pending:
                        await self._resolve_claims()
                        return
                    now = time.monotonic()
                    expired = [
                        index for index in pending
                        if now >= self.claim_deadlines.get(index, now)
                    ]
                    if expired:
                        self.events = []
                        for index in expired:
                            self.players[index].remaining_time = 0
                            self.claim_responses[index] = {"action": "pass"}
                        if all(index in self.claim_responses for index in self.claim_options):
                            await self._resolve_claims()
                            return
                        continue
                    sleep_seconds = min(self.claim_deadlines[index] for index in pending) - now
                await asyncio.sleep(max(0.0, sleep_seconds))
        except asyncio.CancelledError:
            return

    async def _resolve_claims(self) -> None:
        self._cancel_bot_claim_tasks()
        if self._claim_timeout_task and self._claim_timeout_task is not asyncio.current_task():
            self._claim_timeout_task.cancel()
        if self._claim_grace_task and self._claim_grace_task is not asyncio.current_task():
            self._claim_grace_task.cancel()
        if self._claim_grace_timeout_task \
                and self._claim_grace_timeout_task is not asyncio.current_task():
            self._claim_grace_timeout_task.cancel()
        self._claim_grace_active = False
        self._claim_applied = None
        self._claim_upgrade_players.clear()
        claims = []
        discarder = self.last_discard["player"]
        for order, (index, response) in enumerate(self.claim_responses.items()):
            if response["action"] != "claim":
                continue
            candidate = response["candidate"]
            priority = int(candidate.get("priority", 0) or 0)
            # 优先级高者胜；同优先级（如两家碰）按提交先后先到先得（与国标一致）。
            claims.append((-priority, order, index, candidate))
        if not claims:
            await self._advance_after_unclaimed_discard()
            return
        ron_claims = [claim for claim in claims if claim[3]["kind"] == "win"]
        if ron_claims:
            # 多家和按距出牌者的顺时针距离排序：最近者成为下一局庄家。
            ron_claims.sort(key=lambda claim: (claim[2] - discarder) % len(self.players))
            if self.hepai_way == "head_bump":
                # 头跳：只保留最近的一家荣和，其余截和。
                ron_claims = ron_claims[:1]
            discarded = self.last_discard["tile"]
            winners: list[tuple[HongquePlayer, dict]] = []
            for _, _, winner_index, _ in ron_claims:
                winner = self.players[winner_index]
                result = best_win_result(
                    winner.hand + [discarded],
                    winner.melds,
                    self_draw=False,
                    before_first_discard=False,
                    wall_empty=not self.wall,
                )
                if result is not None:
                    result["winning_hand"] = list(winner.hand) + [discarded]
                    winners.append((winner, result))
            if winners:
                ron_silent = any(
                    self._claim_apply_broadcast.get(claim_index) == claim_candidate.get("id")
                    for _, _, claim_index, claim_candidate in ron_claims
                )
                await self._finish_round(winners, "ron", silent=ron_silent)
                return
        _, _, winner_index, candidate = min(claims)
        winner = self.players[winner_index]
        for code in candidate["hand_tiles"]:
            winner.hand.remove(code)
        winner.melds.append({
            "kind": candidate["kind"],
            "tiles": candidate["tiles"],
            "from_player": self.last_discard["player"],
            "claimed_tile": self.last_discard["tile"],
        })
        discarder_player = self.players[self.last_discard["player"]]
        if discarder_player.discards and discarder_player.discards[-1] == self.last_discard["tile"]:
            discarder_player.discards.pop()
        self.current_player_index = winner_index
        winner.drawn_tile = None
        self.phase = "turn"
        self._start_turn_clock()
        claim_applied = self._claim_apply_broadcast.get(winner_index) == candidate.get("id")
        self._record_event(
            candidate["kind"],
            player=winner_index,
            from_player=self.last_discard["player"],
            tile=self.last_discard["tile"],
            tiles=candidate["tiles"],
            hand_tiles=candidate["hand_tiles"],
            base_kind=candidate.get("base_kind", candidate["kind"]),
            silent=claim_applied,
        )
        self.message = f"{winner.username} 亮牌（{candidate['kind']}）"
        self.claim_options.clear()
        self.claim_responses.clear()
        self._advance_tick()
        await self.broadcast_state()
        self._schedule_turn_timeout()
        self._schedule_bot_if_needed()

    async def _advance_after_unclaimed_discard(self) -> None:
        self.claim_options.clear()
        self.claim_responses.clear()
        self._claim_upgrade_players.clear()
        if not self.wall:
            await self._finish_round([], "draw")
            return
        self.current_player_index = (self.last_discard["player"] + 1) % 4
        self._start_turn_clock()
        self._draw_for_current_player()
        self.phase = "turn"
        self.message = f"轮到 {self.players[self.current_player_index].username}"
        self._advance_tick()
        await self.broadcast_state()
        self._schedule_turn_timeout()
        self._schedule_bot_if_needed()

    async def _finish_round(
        self,
        winners: list[tuple[HongquePlayer, dict]],
        reason: str,
        silent: bool = False,
    ) -> None:
        self.phase = "round_end"
        self._cancel_bot_claim_tasks()
        if self._turn_timeout_task and self._turn_timeout_task is not asyncio.current_task():
            self._turn_timeout_task.cancel()
        if self._claim_timeout_task and self._claim_timeout_task is not asyncio.current_task():
            self._claim_timeout_task.cancel()
        if self._claim_grace_task and self._claim_grace_task is not asyncio.current_task():
            self._claim_grace_task.cancel()
        if self._claim_grace_timeout_task \
                and self._claim_grace_timeout_task is not asyncio.current_task():
            self._claim_grace_timeout_task.cancel()
        self._claim_grace_active = False
        self._claim_applied = None
        self.turn_deadline = None
        self.claim_deadline = None
        self.turn_started_at = None
        self.claim_started_at = None
        self.claim_deadlines.clear()
        if winners:
            score_changes: dict[str, int] = {}
            winner_results = []
            for winner, result in winners:
                points = result["points"]
                winner.score += points
                score_changes[str(winner.index)] = points
                winner_results.append({
                    "player": winner.index,
                    "hand": result["winning_hand"],
                    "partition": result["partition"],
                    "groups": result["groups"],
                    "pair": result.get("pair", []),
                    "base": result["base"],
                    "fans": result["fans"],
                    "fan_total": result["fan_total"],
                    "points": points,
                    "melds": list(winner.melds),
                })
            # Ron winners are ordered by distance from the discarder.  The
            # nearest one therefore becomes the next dealer.
            self.dealer_index = winners[0][0].index
            names = "、".join(winner.username for winner, _ in winners)
            label = "摸和" if reason == "self_draw" else "捉和"
            self.message = f"{names} {label}"
            self.round_result = {
                "reason": reason,
                "winner_indices": [winner.index for winner, _ in winners],
                "score_changes": score_changes,
                "scores": {str(player.index): player.score for player in self.players},
                "winners": winner_results,
                "silent": bool(silent),
                "multi_ron": len(winners) > 1,
            }
            self._record_event(reason, players=self.round_result["winner_indices"])
        else:
            self.message = "牌库耗尽，本局流局，庄家不变"
            self.round_result = {
                "reason": "draw",
                "winner_indices": [],
                "score_changes": {},
                "scores": {str(player.index): player.score for player in self.players},
            }
            self._record_event("draw_game", players=[])
        # 与通用计分板一致：多家和的每位赢家各占一行，局号可重复；流局占一行 0。
        history_rows = [
            {str(winner.index): int(result["points"])}
            for winner, result in winners
        ] or [{}]
        for row in history_rows:
            for player in self.players:
                delta = int(row.get(str(player.index), 0) or 0)
                player.score_history.append(f"+{delta}" if delta > 0 else str(delta))
                player.round_number_history.append(self.current_round)
        self._advance_tick()
        await self.broadcast_state()
        fan_count = max(
            (len(result.get("fans", [])) for _, result in winners),
            default=0,
        )
        self._ready_phase_active = bool(winners)
        self._ready_players = {
            player.index for player in self.players if player.is_bot
        }
        self._ready_event.clear()
        self._round_task = asyncio.create_task(
            self._complete_round_after_result(
                bool(winners), fan_count, multi_ron=len(winners) > 1
            )
        )

    async def _handle_ready_action(self, player: HongquePlayer) -> None:
        if not self._ready_phase_active:
            raise ValueError("当前没有需要确认的和牌结算")
        if player.index in self._ready_players:
            return
        self._ready_players.add(player.index)
        self._ready_event.set()
        await self.broadcast_ready_status()

    async def send_ready_status_to(self, player_index: int) -> None:
        player = self.players[player_index]
        if player.is_bot or not player.online or self.game_server is None:
            return
        connection = getattr(self.game_server, "user_id_to_connection", {}).get(player.user_id)
        if connection is None or getattr(connection, "websocket", None) is None:
            return
        await connection.websocket.send_json({
            "type": "gamestate/hongque/ready_status",
            "success": True,
            "message": "准备状态更新",
            "ready_status_info": {
                "player_to_ready": {
                    str(item.index): item.index in self._ready_players
                    for item in self.players
                },
            },
        })

    async def broadcast_ready_status(self) -> None:
        await asyncio.gather(*(
            self.send_ready_status_to(player.index) for player in self.players
        ))

    async def _wait_for_hu_ready(self, fan_count: int, extra_seconds: float = 0.0) -> None:
        wait_time = hu_result_ready_wait_seconds(fan_count) + extra_seconds
        deadline = time.monotonic() + wait_time
        panel_visible_at = (
            time.monotonic()
            + hu_result_ready_pre_panel_seconds()
            + ROUND_END_PRESENTATION_FADE_SEC
        )
        await self.broadcast_ready_status()
        rebroadcasted_for_panel = False
        while self._ready_phase_active:
            pending = {
                player.index for player in self.players
                if not player.is_bot and player.index not in self._ready_players
            }
            if not pending:
                break
            now = time.monotonic()
            if now >= deadline:
                self._ready_players.update(pending)
                await self.broadcast_ready_status()
                break
            if not rebroadcasted_for_panel and now >= panel_visible_at:
                await self.broadcast_ready_status()
                rebroadcasted_for_panel = True
                continue
            next_wakeup = deadline
            if not rebroadcasted_for_panel:
                next_wakeup = min(next_wakeup, panel_visible_at)
            self._ready_event.clear()
            # A ready response can arrive between clear() and wait(). Recheck
            # the set so an already-complete table never sleeps until timeout.
            if all(
                player.is_bot or player.index in self._ready_players
                for player in self.players
            ):
                break
            try:
                await asyncio.wait_for(
                    self._ready_event.wait(),
                    timeout=max(0.01, next_wakeup - time.monotonic()),
                )
            except asyncio.TimeoutError:
                pass

    async def _complete_round_after_result(
        self, has_winner: bool, fan_count: int, multi_ron: bool = False
    ) -> None:
        try:
            if has_winner:
                if multi_ron:
                    # 多家和：先依次播完前几家的完整面板（倒牌+番数动画+3s 维持），
                    # 最后一家才进入 8s 确认倒计时（ready 阶段），避免服务端提前推进。
                    winners = self.round_result.get("winners", ())
                    for index, result in enumerate(winners):
                        if index == len(winners) - 1:
                            break
                        fan_i = len(result.get("fans", ()))
                        await asyncio.sleep(
                            hu_result_ready_pre_panel_seconds()
                            + sichuan_settle_hu_panel_wait_seconds(fan_i, is_final=False)
                        )
                    await self._wait_for_hu_ready(fan_count)
                else:
                    await self._wait_for_hu_ready(fan_count)
            else:
                await asyncio.sleep(liuju_ready_wait_seconds())
            game_ended = False
            async with self._lock:
                if self.phase != "round_end":
                    return
                self._ready_phase_active = False
                self.current_round += 1
                if self.current_round > self.max_round:
                    self.phase = "game_end"
                    self.message = "虹雀对局结束"
                    self._advance_tick()
                    await self.broadcast_state()
                    game_ended = True
                else:
                    await self._start_round()
            if game_ended:
                await self._complete_game_lifecycle()
        except asyncio.CancelledError:
            return

    async def _complete_game_lifecycle(self) -> None:
        await asyncio.sleep(4)
        if self.game_server is None:
            return
        manager = getattr(self.game_server, "gamestate_manager", None)
        if manager is not None:
            await manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
        room_manager = getattr(self.game_server, "room_manager", None)
        if room_manager is not None and hasattr(room_manager, "finish_custom_game_room"):
            await room_manager.finish_custom_game_room(self.room_id)

    def _legal_turn_actions(self, player: HongquePlayer) -> tuple[list[str], list[dict]]:
        if self.phase != "turn" or player.index != self.current_player_index:
            return [], []
        actions = ["discard"] if player.hand else []
        candidates: list[dict] = []
        # 和牌包括普通拆解与“杠和”拆解（自摸牌并入明牌即和），统一按“和”下发；
        # 杠和看作普通和牌，不在结算时实际移动手牌。
        if self._can_self_draw_win(player.hand, player.melds):
            actions.append("win")
        # 手牌只剩最后一张（自摸/补牌张）时，杠后必然空手成和，
        # 只下发“和”，普通杠没有独立意义。
        if len(player.hand) != 1:
            kong = kong_candidates(player.hand, player.melds)
            if kong:
                actions.append("kong")
                candidates.extend(kong)
        if player.supplements < 2 and self.wall:
            actions.append("supplement")
        return actions, candidates

    @staticmethod
    def _can_self_draw_win(hand: Sequence[str], melds: Sequence[dict]) -> bool:
        """自摸和判定：手内成组，或自摸牌可并入明牌（杠和）后成和。"""
        return is_winning_hand(hand, melds) or bool(
            kong_win_candidates(hand, melds)
        )

    @staticmethod
    def _score_hint(result: Optional[dict], tile: Optional[str] = None) -> Optional[dict]:
        if result is None:
            return None
        return {
            "tile": tile,
            "base": result["base"],
            "fan_total": result["fan_total"],
            "points": result["points"],
            "fans": result["fans"],
            "self_draw_only": False,
        }

    def _viewer_win_hint(self, viewer: HongquePlayer, actions: Sequence[str]) -> Optional[dict]:
        if self.phase == "turn" and "win" in actions:
            return self._score_hint(best_win_result(
                viewer.hand,
                viewer.melds,
                self_draw=True,
                before_first_discard=not any(item.discards for item in self.players),
                wall_empty=not self.wall,
                allow_kong_win=True,
            ), viewer.drawn_tile or (viewer.hand[-1] if viewer.hand else None))
        if self.phase == "claim" and "claim" in actions and self.last_discard is not None:
            options = self.claim_options.get(viewer.index, ())
            if any(option.get("kind") == "win" for option in options):
                return self._score_hint(best_win_result(
                    viewer.hand + [self.last_discard["tile"]],
                    viewer.melds,
                    self_draw=False,
                    before_first_discard=False,
                    wall_empty=not self.wall,
                ), self.last_discard["tile"])
        return None

    def _visible_event(self, source: dict, viewer_index: int) -> dict:
        event = dict(source)
        if event.get("type") in {"draw", "supplement"} and event.get("player") != viewer_index:
            event["tile"] = None
        return event

    def build_state(self, viewer_index: int, *, sync_mode: str = "events",
                    events_override: Optional[list[dict]] = None) -> dict:
        viewer = self.players[viewer_index]
        actions, candidates = self._legal_turn_actions(viewer)
        if self.phase == "claim" and viewer_index in self.claim_options:
            existing = self.claim_responses.get(viewer_index)
            if (existing is not None and existing["action"] == "claim"
                    and viewer_index in self._claim_upgrade_players):
                # 川麻模式：已亮低/中优先级后，只弹出更高优先级候选供升级。
                existing_priority = int(existing["candidate"].get("priority", 0) or 0)
                higher = [
                    item for item in self.claim_options[viewer_index]
                    if int(item.get("priority", 0) or 0) > existing_priority
                ]
                actions = ["claim"] if higher else []
                candidates = higher
            elif viewer_index not in self.claim_responses:
                actions = ["pass", "claim"]
                candidates = self.claim_options[viewer_index]
            else:
                actions, candidates = [], []
        win_hint = self._viewer_win_hint(viewer, actions)
        # 听牌形与分值提示由客户端 C# 本地计算；服务端仅保留实际可和动作及结算的 Python 权威计分。
        wait_hints: list[dict] = []
        waits: list[str] = []
        remaining_time, step_remaining = self._remaining_clock(viewer)
        state = {
            "sync_mode": sync_mode,
            "phase": self.phase,
            "round": self.current_round,
            "dealer": self.dealer_index,
            "current_player": self.current_player_index,
            "wall_count": len(self.wall),
            "you": viewer_index,
            "action_tick": self.action_tick,
            "remaining_time": remaining_time,
            "step_remaining": step_remaining,
            "tips": self.tips,
            "message": self.message,
            "round_result": self.round_result,
            "events": [
                self._visible_event(event, viewer_index)
                for event in (self.events if events_override is None else events_override)
            ],
            "legal_actions": actions,
            "candidates": candidates,
            "win_hint": win_hint,
            "waiting_tiles": waits,
            "waiting_hints": wait_hints,
        }
        if sync_mode not in {"round_start", "reconnect"}:
            return state

        state.update({
            "room_id": int(self.room_id),
            "max_round": self.max_round,
            "round_time": self.round_time,
            "step_time": self.step_time,
            "hand": list(viewer.hand),
            "players": [{
                "index": player.index,
                "user_id": player.user_id,
                "username": player.username,
                "hand_count": len(player.hand),
                "discards": list(player.discards),
                "melds": list(player.melds),
                "score": player.score,
                "supplements": player.supplements,
                "online": player.online,
                "title_used": player.title_used,
                "profile_used": player.profile_used,
                "character_used": player.character_used,
                "voice_used": player.voice_used,
                "score_history": list(player.score_history),
                "round_number_history": list(player.round_number_history),
            } for player in self.players],
        })
        return state

    async def send_state_to(self, player_index: int, **kwargs: Any) -> None:
        player = self.players[player_index]
        if player.is_bot or not player.online or self.game_server is None:
            return
        connection = getattr(self.game_server, "user_id_to_connection", {}).get(player.user_id)
        if connection is None or getattr(connection, "websocket", None) is None:
            return
        sync_mode = kwargs.get("sync_mode", "events")
        if sync_mode not in {"events", "round_start", "reconnect"}:
            raise ValueError(f"invalid Hongque sync mode: {sync_mode}")
        message_type = {
            "round_start": "gamestate/hongque/game_start",
            "reconnect": "gamestate/hongque/reconnect",
        }.get(sync_mode, "gamestate/hongque/update")
        await connection.websocket.send_json({
            "type": message_type,
            "success": True,
            "message": self.message,
            "gamestate_id": self.gamestate_id,
            "hongque_state": self.build_state(player_index, **kwargs),
        })

    async def broadcast_state(self, **kwargs: Any) -> None:
        await asyncio.gather(*(self.send_state_to(player.index, **kwargs) for player in self.players))

    def _schedule_bot_if_needed(self) -> None:
        if self.phase != "turn" or not self.players[self.current_player_index].is_bot:
            return
        current_task = asyncio.current_task()
        if (self._bot_task and self._bot_task is not current_task
                and not self._bot_task.done()):
            self._bot_task.cancel()
        tick = self.action_tick
        self._bot_task = asyncio.create_task(self._bot_turn(tick))

    async def _bot_turn(self, tick: int) -> None:
        started_at = time.perf_counter()
        async with self._lock:
            if self.phase != "turn" or self.action_tick != tick:
                return
            player = self.players[self.current_player_index]
            hand_snapshot = tuple(player.hand)
            meld_snapshot = tuple(dict(meld) for meld in player.melds)
            before_first_discard = not any(item.discards for item in self.players)
            wall_empty = not self.wall
            smart_bot = player.user_id == 2
            heuristic_bot = player.user_id == 3
            player_index = player.index
            kong_snapshot = tuple(
                dict(candidate)
                for candidate in (
                    kong_candidates(player.hand, player.melds)
                    + kong_win_candidates(player.hand, player.melds)
                )
            )
            if smart_bot or heuristic_bot:
                visible_snapshot = self._visible_codes_for(player.index)
                supplements = player.supplements
                wall_count = len(self.wall)
                drawn_tile = player.drawn_tile
                last_draw_was_supplement = player.last_draw_was_supplement
            if heuristic_bot:
                opponents_snapshot = tuple(
                    OpponentView.from_player(opponent)
                    for opponent in self.players
                    if opponent.index != player_index
                )
        if smart_bot:
            plan = await run_room_bot_cpu(
                self,
                choose_turn_plan,
                hand_snapshot,
                meld_snapshot,
                visible_snapshot,
                kong_snapshot,
                supplements=supplements,
                wall_count=wall_count,
                drawn_tile=drawn_tile,
                last_draw_was_supplement=last_draw_was_supplement,
            )
        elif heuristic_bot:
            plan = await run_room_bot_cpu(
                self,
                choose_turn_plan_v3,
                hand_snapshot,
                meld_snapshot,
                visible_snapshot,
                kong_snapshot,
                supplements=supplements,
                wall_count=wall_count,
                drawn_tile=drawn_tile,
                last_draw_was_supplement=last_draw_was_supplement,
                opponents=opponents_snapshot,
            )
        else:
            result = await run_room_bot_cpu(
                self,
                best_win_result,
                hand_snapshot,
                meld_snapshot,
                self_draw=True,
                before_first_discard=before_first_discard,
                wall_empty=wall_empty,
            )
            if result is not None:
                plan = {"action": "win"}
            else:
                kong_win = next(
                    (
                        candidate
                        for candidate in kong_snapshot
                        if candidate.get("kind") == "kong_win"
                    ),
                    None,
                )
                if kong_win is not None:
                    # 杠和并入“和”：直接宣言和牌，不实际移动手牌。
                    plan = {"action": "win"}
                else:
                    plan = {
                        "action": "discard",
                        "tile": self._rng.choice(hand_snapshot) if hand_snapshot else None,
                    }
        delay = BOT_ACTION_DELAY - (time.perf_counter() - started_at)
        if delay > 0:
            await asyncio.sleep(delay)
        async with self._lock:
            if (self.phase != "turn" or self.action_tick != tick
                    or self.current_player_index != player_index):
                return
            # Bot decisions bypass submit_action, so they must start their own
            # event batch just like a human action.  Otherwise the previous
            # draw is resent and the bot discard has no animation event.
            self.events = []
            player = self.players[self.current_player_index]
            action = plan.get("action")
            if action == "win":
                await self._handle_turn_action(player, "win", None, None)
                return
            if action == "supplement":
                await self._handle_turn_action(player, "supplement", None, None)
                return
            if action == "kong":
                await self._handle_turn_action(
                    player, "kong", None, plan.get("candidate_id")
                )
                return
            if self.Debug:
                forced = get_debug_forced_discard(self, player.index)
                if forced and forced in player.hand:
                    code = forced
                else:
                    code = plan.get("tile")
                    if code not in player.hand:
                        code = (
                            player.drawn_tile
                            if player.drawn_tile in player.hand
                            else (self._rng.choice(player.hand) if player.hand else None)
                        )
            else:
                code = plan.get("tile")
                if code not in player.hand:
                    code = (
                        player.drawn_tile
                        if player.drawn_tile in player.hand
                        else (self._rng.choice(player.hand) if player.hand else None)
                    )
            if code is None:
                return
            await self._discard_and_open_claim(player, code)

    def _visible_codes_for(self, player_index: int) -> tuple[str, ...]:
        visible = set(self.players[player_index].hand)
        for player in self.players:
            visible.update(player.discards)
            for meld in player.melds:
                visible.update(meld.get("tiles", ()))
        if self.last_discard is not None:
            visible.add(self.last_discard["tile"])
        return tuple(sorted(visible))

    def _schedule_bot_claim(self, player_index: int, tick: int) -> None:
        previous = self._bot_claim_tasks.get(player_index)
        current = asyncio.current_task()
        if previous is not None and previous is not current and not previous.done():
            previous.cancel()
        self._bot_claim_tasks[player_index] = asyncio.create_task(
            self._bot_claim(player_index, tick)
        )

    def _cancel_bot_claim_tasks(self) -> None:
        current = asyncio.current_task()
        for task in self._bot_claim_tasks.values():
            if task is not current and not task.done():
                task.cancel()
        self._bot_claim_tasks.clear()

    async def _bot_claim(self, player_index: int, tick: int) -> None:
        started_at = time.perf_counter()
        try:
            async with self._lock:
                if (self.phase != "claim" or self.action_tick != tick
                        or player_index in self.claim_responses
                        or player_index not in self.claim_options):
                    return
                player = self.players[player_index]
                hand_snapshot = tuple(player.hand)
                meld_snapshot = tuple(dict(meld) for meld in player.melds)
                candidate_snapshot = tuple(
                    dict(candidate) for candidate in self.claim_options[player_index]
                )
                visible_snapshot = self._visible_codes_for(player_index)
            claim_fn = (
                choose_claim_plan_v3 if self.players[player_index].user_id == 3
                else choose_claim_plan
            )
            plan = await run_room_bot_cpu(
                self,
                claim_fn,
                hand_snapshot,
                meld_snapshot,
                candidate_snapshot,
                visible_snapshot,
            )
            delay = BOT_ACTION_DELAY - (time.perf_counter() - started_at)
            if delay > 0:
                await asyncio.sleep(delay)
            async with self._lock:
                if (self.phase != "claim" or self.action_tick != tick
                        or player_index in self.claim_responses
                        or player_index not in self.claim_options):
                    return
                candidate_id = plan.get("candidate_id")
                candidate = next(
                    (
                        item for item in self.claim_options[player_index]
                        if item.get("id") == candidate_id
                    ),
                    None,
                )
                if plan.get("action") == "claim" and candidate is not None:
                    self.claim_responses[player_index] = {
                        "action": "claim",
                        "candidate": candidate,
                    }
                    if self._claim_grace_active:
                        if self._claim_upgrades_applied(player_index, candidate):
                            await self._begin_claim_grace(player_index, candidate)
                        else:
                            await self._broadcast_claim_apply(player_index, candidate)
                            if not self._claim_grace_pending():
                                await self._resolve_claims()
                    else:
                        await self._begin_claim_grace(player_index, candidate)
                else:
                    self.claim_responses[player_index] = {"action": "pass"}
                    if self._claim_grace_active:
                        self._grace_passed_players.add(player_index)
                        if not self._claim_grace_pending():
                            await self._resolve_claims()
                        return
                if not self._claim_grace_active and all(
                    index in self.claim_responses for index in self.claim_options
                ):
                    await self._resolve_claims()
        except asyncio.CancelledError:
            return
        except Exception:
            logger.exception("虹雀牌效机器人亮牌决策失败: player=%s", player_index)
            async with self._lock:
                if (self.phase == "claim" and self.action_tick == tick
                        and player_index in self.claim_options
                        and player_index not in self.claim_responses):
                    self.claim_responses[player_index] = {"action": "pass"}
                    if self._claim_grace_active:
                        self._grace_passed_players.add(player_index)
                        if not self._claim_grace_pending():
                            await self._resolve_claims()
                    elif all(index in self.claim_responses for index in self.claim_options):
                        await self._resolve_claims()
        finally:
            task = asyncio.current_task()
            if self._bot_claim_tasks.get(player_index) is task:
                self._bot_claim_tasks.pop(player_index, None)

    async def player_disconnect(self, user_id: int) -> None:
        async with self._lock:
            player = next((item for item in self.players if item.user_id == user_id), None)
            if player is None:
                return
            player.online = False
            self.event_sequence += 1
            presence = {
                "id": self.event_sequence,
                "type": "presence",
                "player": player.index,
                "online": False,
            }
            await self.broadcast_state(events_override=[presence])

    async def player_reconnect(self, user_id: int) -> None:
        async with self._lock:
            player = next((item for item in self.players if item.user_id == user_id), None)
            if player is None:
                return
            player.online = True
            # A reconnect is an explicit authoritative restore.  It must not be
            # inferred client-side from an empty/unknown event batch, because
            # doing so makes normal discard messages rebuild the whole table.
            await self.send_state_to(player.index, sync_mode="reconnect")
            if self._ready_phase_active:
                await self.send_ready_status_to(player.index)
            self.event_sequence += 1
            presence = {
                "id": self.event_sequence,
                "type": "presence",
                "player": player.index,
                "online": True,
            }
            await self.broadcast_state(events_override=[presence])

    async def cleanup_game_state(self) -> None:
        current = asyncio.current_task()
        self._cancel_bot_claim_tasks()
        for task in (
            self._bot_task,
            self._round_task,
            self._claim_timeout_task,
            self._claim_grace_task,
            self._claim_grace_timeout_task,
            self._turn_timeout_task,
        ):
            if task and task is not current and not task.done():
                task.cancel()
        if self.game_task and self.game_task is not current and not self.game_task.done():
            self.game_task.cancel()
            await asyncio.gather(self.game_task, return_exceptions=True)

    async def add_spectator(self, user_id: int, connection: Any) -> None:
        raise ValueError("虹雀规则不支持观战")

    async def remove_spectator(self, user_id: int) -> None:
        return None
