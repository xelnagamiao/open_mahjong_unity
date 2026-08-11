from __future__ import annotations

import asyncio
import logging
import random
import time
from typing import Any, Optional, Sequence

from .rules import (
    classify_meld,
    kong_candidates,
)
from .scoring import best_win_result
from .tile import HongqueTile
from .player import HongquePlayer
from .action_check import (
    can_self_draw_win,
    check_action_hand_action,
)
from .init_tiles import (
    draw_for_current_player as init_draw_for_current_player,
    init_hongque_tiles,
)
from .boardcast import (
    broadcast_state as hongque_broadcast_state,
    build_state as build_hongque_state,
    send_state_to as hongque_send_state_to,
    visible_event as hongque_visible_event,
)
from .get_action import (
    bot_claim as hongque_bot_claim,
    bot_turn as hongque_bot_turn,
    cancel_bot_claim_tasks as hongque_cancel_bot_claim_tasks,
    schedule_bot_claim as hongque_schedule_bot_claim,
    schedule_bot_if_needed as hongque_schedule_bot_if_needed,
    visible_codes_for as hongque_visible_codes_for,
)
from ..public.vote_manager import vote_checkpoint
from ..public.round_end_timing import (
    ROUND_END_PRESENTATION_FADE_SEC,
    hu_result_ready_pre_panel_seconds,
    hu_result_ready_wait_seconds,
    liuju_ready_wait_seconds,
    sichuan_settle_hu_panel_wait_seconds,
)
from .hongque_debug import (
    HONGQUE_DEBUG_SCENARIO,
    get_debug_forced_discard,
)
from .wait_action import (
    advance_after_unclaimed_discard as wait_advance_after_unclaimed_discard,
    broadcast_claim_application as wait_broadcast_claim_application,
    claim_timeout as wait_claim_timeout,
    handle_claim_action as wait_handle_claim_action,
    wait_action as wait_open_claim_window,
    resolve_claims as wait_resolve_claims,
)
from .action_priority import HONGQUE_ACTION_PRIORITY
from .state_machine import HongqueStateMachine, HongqueStatus

logger = logging.getLogger(__name__)
BOT_ACTION_DELAY = 0.5


class HongqueGameState:
    """Authoritative, memory-only Hongque 2 game state.

    The wire format uses HQv3.1 resource keys (``AX1`` ... ``GY9``).  All
    actions are checked server-side.  This rule intentionally has no
    database, statistics, replay, spectator, or match integration.
    """

    @property
    def phase(self) -> str:
        return self.state_machine.phase

    @phase.setter
    def phase(self, value: str) -> None:
        # Wire compatibility for snapshots and tests. Runtime paths use
        # _transition(), which validates every lifecycle edge.
        self.state_machine.force_phase(value)

    @property
    def game_status(self) -> str:
        return self.state_machine.status.value

    def _transition(self, status: HongqueStatus) -> None:
        self.state_machine.transition(status)

    def __init__(self, game_server: Any, room_data: dict, calculation_service: Any = None,
                 db_manager: Any = None, gamestate_id: str = "hongque-test") -> None:
        self.game_server = game_server
        self.room_id = room_data["room_id"]
        self.gamestate_id = gamestate_id
        self.room_rule = "hongque"
        self.sub_rule = room_data.get("sub_rule", "hongque/v1.6")
        self.room_type = room_data.get("room_type", "custom")
        self.allow_spectator_config = False
        self.spectator_enabled = False
        self.realtime_spectators: list = []
        # RoomManager has already normalized Hongque game_round to the actual
        # number of hands so room lists and in-game state share one value.
        self.max_round = max(1, int(room_data.get("game_round", 4)))
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
        self.tactical_grace_seconds = float(
            room_data.get("tactical_grace_seconds", 5.0)
        )
        # 如果您在管理自己规则内的分支，请不要将 Debug = True 的配置上传到公共代码仓库。
        # 与其它规则一致：这是虹雀子规则的固定牌例开关，不跟随服务器全局 Debug。
        self.Debug = False
        self.debug_scenario = HONGQUE_DEBUG_SCENARIO
        # 和牌方式：multi_ron = 多家可同时荣和（依次展示结算面板）；
        # head_bump = 头跳，只由距出牌者最近的荣和者截和。
        self.hepai_way = room_data.get("hepai_way", "multi_ron")
        self.turn_deadline: Optional[float] = None
        self.turn_started_at: Optional[float] = None
        self.claim_started_at: Optional[float] = None
        self.claim_deadlines: dict[int, float] = {}
        self.current_round = 1
        self.dealer_index = 0
        self.current_player_index = 0
        self.state_machine = HongqueStateMachine()
        self.action_priority = dict(HONGQUE_ACTION_PRIORITY)
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
        self.claim_window = None
        # 战术鸣牌：记录本张弃牌区间内已广播「亮牌申请」的 (玩家 -> candidate id)。
        # 申请帧只发声/显示动画，不改变牌面；最终执行帧据此标记 silent，避免重复发声。
        self._claim_apply_broadcast: dict[int, str] = {}
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
        if self.turn_started_at is not None:
            self.turn_started_at += paused_seconds
        if self.claim_started_at is not None:
            self.claim_started_at += paused_seconds
        if self.claim_window is not None and self.claim_window.deadline is not None:
            self.claim_window.deadline += paused_seconds
        self.claim_deadlines = {
            index: deadline + paused_seconds
            for index, deadline in self.claim_deadlines.items()
        }


    def _record_event(self, event_type: str, **payload: Any) -> dict:
        self.event_sequence += 1
        event = {"id": self.event_sequence, "type": event_type, **payload}
        self.events.append(event)
        return event

    def _advance_tick(self) -> None:
        self.action_tick += 1

    def _start_turn_clock(self) -> None:
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

    def _remaining_clock(self, viewer: HongquePlayer) -> tuple[int, int]:
        # 战术鸣牌是全桌共享的五秒窗口：无论当前玩家是否有按钮，四家都看到
        # 同一个刷新后的倒计时，避免各客户端显示不同步。
        if (self.phase == "claim" and self.claim_window is not None
                and self.claim_window.stage == "tactical"
                and self.claim_window.deadline is not None):
            remaining = max(
                0, int(self.claim_window.deadline - time.monotonic() + 0.999)
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
                claim_tick_is_valid = (
                    self.phase == "claim"
                    and self.claim_window is not None
                    and player.index in self.claim_window.pending
                    and self.claim_window.accepts_tick(player.index, int(action_tick))
                )
                if not claim_tick_is_valid:
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
        self._transition(HongqueStatus.RESOLVING_DISCARD)
        self.turn_deadline = None
        self.turn_started_at = None
        self.message = f"{player.username} 出牌"
        self._advance_tick()
        await self.broadcast_state()
        await self._open_claim_window()


    async def _finish_round(
        self,
        winners: list[tuple[HongquePlayer, dict]],
        reason: str,
        silent: bool = False,
    ) -> None:
        self._transition(HongqueStatus.WAITING_READY)
        self._cancel_bot_claim_tasks()
        if self._turn_timeout_task and self._turn_timeout_task is not asyncio.current_task():
            self._turn_timeout_task.cancel()
        if self._claim_timeout_task and self._claim_timeout_task is not asyncio.current_task():
            self._claim_timeout_task.cancel()
        self.turn_deadline = None
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
        # round_end 后允许提前接收 ready，以容忍客户端与服务端动画时钟的小偏移；
        # ready 状态仍只在最后一家面板开始时广播，中间自动面板不展示准备状态。
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

    async def _wait_for_hu_ready(
        self, fan_count: int, extra_seconds: float = 0.0, pre_panel_delay_sec: Optional[float] = None
    ) -> None:
        if pre_panel_delay_sec is None:
            pre_panel_delay_sec = hu_result_ready_pre_panel_seconds()
        wait_time = hu_result_ready_wait_seconds(
            fan_count, pre_panel_delay_sec=pre_panel_delay_sec
        ) + extra_seconds
        deadline = time.monotonic() + wait_time
        panel_visible_at = (
            time.monotonic()
            + pre_panel_delay_sec
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
                    await asyncio.sleep(hu_result_ready_pre_panel_seconds())
                    for index, result in enumerate(winners):
                        if index == len(winners) - 1:
                            break
                        fan_i = len(result.get("fans", ()))
                        await asyncio.sleep(
                            sichuan_settle_hu_panel_wait_seconds(fan_i, is_final=False)
                        )
                    await self._wait_for_hu_ready(fan_count, pre_panel_delay_sec=0.0)
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
                    self._transition(HongqueStatus.END)
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


# 与国标/川麻相同：等待动作实现位于独立 wait_action 模块，主状态类只保留兼容入口。
HongqueGameState._open_claim_window = wait_open_claim_window
HongqueGameState._handle_claim_action = wait_handle_claim_action
HongqueGameState._broadcast_claim_apply = wait_broadcast_claim_application
HongqueGameState._claim_timeout = wait_claim_timeout
HongqueGameState._resolve_claims = wait_resolve_claims
HongqueGameState._advance_after_unclaimed_discard = wait_advance_after_unclaimed_discard
HongqueGameState._start_round = init_hongque_tiles
HongqueGameState._draw_for_current_player = init_draw_for_current_player
HongqueGameState._legal_turn_actions = check_action_hand_action
HongqueGameState._can_self_draw_win = staticmethod(can_self_draw_win)
HongqueGameState._visible_event = staticmethod(hongque_visible_event)
HongqueGameState.build_state = build_hongque_state
HongqueGameState.send_state_to = hongque_send_state_to
HongqueGameState.broadcast_state = hongque_broadcast_state
HongqueGameState._schedule_bot_if_needed = hongque_schedule_bot_if_needed
HongqueGameState._bot_turn = hongque_bot_turn
HongqueGameState._visible_codes_for = hongque_visible_codes_for
HongqueGameState._schedule_bot_claim = hongque_schedule_bot_claim
HongqueGameState._cancel_bot_claim_tasks = hongque_cancel_bot_claim_tasks
HongqueGameState._bot_claim = hongque_bot_claim
