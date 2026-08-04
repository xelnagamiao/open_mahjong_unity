from __future__ import annotations

import asyncio
import logging
import random
import time
from dataclasses import dataclass, field
from typing import Any, Optional, Sequence

from .efficiency_bot import choose_claim_plan, choose_turn_plan
from .rules import call_candidates, classify_meld, kong_candidates
from .scoring import best_win_result
from .tenpai_check import waiting_tiles
from .tile import HongqueTile, full_deck
from .win_check import is_winning_hand
from ..public.ai.bot_executor import run_room_bot_cpu
from ..public.round_end_timing import (
    ROUND_END_PRESENTATION_FADE_SEC,
    hu_result_ready_pre_panel_seconds,
    hu_result_ready_wait_seconds,
    liuju_ready_wait_seconds,
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
        self._claim_timeout_task: Optional[asyncio.Task] = None
        self._turn_timeout_task: Optional[asyncio.Task] = None
        self._wait_hint_cache: dict[tuple, list[dict]] = {}
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
            await asyncio.Future()
        except asyncio.CancelledError:
            raise

    async def _start_round(self) -> None:
        self._cancel_bot_claim_tasks()
        self._ready_phase_active = False
        self._ready_players.clear()
        self._ready_event.clear()
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
        self._wait_hint_cache.clear()
        for _ in range(11):
            for offset in range(4):
                self.players[(self.dealer_index + offset) % 4].hand.append(self.wall.pop())
        self.current_player_index = self.dealer_index
        self.last_discard = None
        self.claim_options.clear()
        self.claim_responses.clear()
        self.round_result = None
        self.events = []
        self.phase = "turn"
        self._start_turn_clock()
        self._draw_for_current_player()
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
            await asyncio.sleep(max(0.0, deadline - time.monotonic()))
            async with self._lock:
                if (self.phase != "turn" or self.action_tick != tick
                        or self.current_player_index != player_index):
                    return
                self.events = []
                player = self.players[player_index]
                player.remaining_time = 0
                if player.hand:
                    code = player.drawn_tile if player.drawn_tile in player.hand else player.hand[-1]
                    await self._discard_and_open_claim(player, code)
                    return
                result = best_win_result(
                    player.hand,
                    player.melds,
                    self_draw=True,
                    before_first_discard=not any(item.discards for item in self.players),
                    wall_empty=not self.wall,
                )
                if result is not None:
                    result["winning_hand"] = list(player.hand)
                    await self._finish_round([(player, result)], "self_draw")
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
            candidates = kong_candidates(player.hand, player.melds)
            candidate = next((item for item in candidates if item["id"] == candidate_id), None)
            if candidate is None:
                raise ValueError("杠牌候选无效")
            self._consume_time_bank(player, self.turn_started_at)
            for code in candidate["hand_tiles"]:
                player.hand.remove(code)
            if player.drawn_tile not in player.hand:
                player.drawn_tile = None
            claimed_tile = player.melds[candidate["meld_index"]].get("claimed_tile")
            player.melds[candidate["meld_index"]]["tiles"] = candidate["tiles"]
            player.melds[candidate["meld_index"]]["kind"] = classify_meld(candidate["tiles"]).kind
            self._record_event(
                "kong",
                player=player.index,
                tiles=candidate["tiles"],
                hand_tiles=candidate["hand_tiles"],
                meld_index=candidate["meld_index"],
                claimed_tile=claimed_tile,
            )
            self.message = f"{player.username} 杠牌"
        elif action == "win":
            result = best_win_result(
                player.hand,
                player.melds,
                self_draw=True,
                before_first_discard=not any(item.discards for item in self.players),
                wall_empty=not self.wall,
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
        for player in self.players:
            if player.index == discard_player:
                continue
            options = call_candidates(player.hand, discarded)
            if is_winning_hand(player.hand + [discarded], player.melds):
                options.insert(0, {"id": "ron", "kind": "win", "priority": 4,
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
            if self.players[index].user_id == 2:
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
        if player.index in self.claim_responses:
            raise ValueError("已经回应过本次亮牌询问")
        if action == "pass":
            self._consume_time_bank(player, self.claim_started_at)
            self.claim_responses[player.index] = {"action": "pass"}
        elif action == "claim":
            candidate = next((item for item in self.claim_options[player.index] if item["id"] == candidate_id), None)
            if candidate is None:
                raise ValueError("亮牌候选无效")
            self._consume_time_bank(player, self.claim_started_at)
            self.claim_responses[player.index] = {"action": "claim", "candidate": candidate}
        else:
            raise ValueError("未知的亮牌操作")
        if all(index in self.claim_responses for index in self.claim_options):
            await self._resolve_claims()
        else:
            await self.send_state_to(player.index)

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
        claims = []
        discarder = self.last_discard["player"]
        for index, response in self.claim_responses.items():
            if response["action"] != "claim":
                continue
            candidate = response["candidate"]
            # The discarder's seat is the current seat for this claim window.
            # Smaller clockwise distance wins after claim priority, including
            # the wraparound from seat 3 back to seat 0.
            distance = (index - discarder) % len(self.players)
            priority = int(candidate.get("priority", 0) or 0)
            claims.append((-priority, distance, index, candidate))
        if not claims:
            await self._advance_after_unclaimed_discard()
            return
        ron_claims = [claim for claim in claims if claim[3]["kind"] == "win"]
        if ron_claims:
            ron_claims.sort(key=lambda claim: claim[1])
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
                await self._finish_round(winners, "ron")
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
        self._record_event(
            candidate["kind"],
            player=winner_index,
            from_player=self.last_discard["player"],
            tile=self.last_discard["tile"],
            tiles=candidate["tiles"],
            hand_tiles=candidate["hand_tiles"],
            base_kind=candidate.get("base_kind", candidate["kind"]),
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

    async def _finish_round(self, winners: list[tuple[HongquePlayer, dict]], reason: str) -> None:
        self.phase = "round_end"
        self._cancel_bot_claim_tasks()
        if self._turn_timeout_task and self._turn_timeout_task is not asyncio.current_task():
            self._turn_timeout_task.cancel()
        if self._claim_timeout_task and self._claim_timeout_task is not asyncio.current_task():
            self._claim_timeout_task.cancel()
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
            self._complete_round_after_result(bool(winners), fan_count)
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

    async def _wait_for_hu_ready(self, fan_count: int) -> None:
        wait_time = hu_result_ready_wait_seconds(fan_count)
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

    async def _complete_round_after_result(self, has_winner: bool, fan_count: int) -> None:
        try:
            if has_winner:
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
        if is_winning_hand(player.hand, player.melds):
            actions.append("win")
        kong = kong_candidates(player.hand, player.melds)
        if kong:
            actions.append("kong")
            candidates.extend(kong)
        if player.supplements < 2 and self.wall:
            actions.append("supplement")
        return actions, candidates

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
        }

    def _viewer_win_hint(self, viewer: HongquePlayer, actions: Sequence[str]) -> Optional[dict]:
        if self.phase == "turn" and "win" in actions:
            return self._score_hint(best_win_result(
                viewer.hand,
                viewer.melds,
                self_draw=True,
                before_first_discard=not any(item.discards for item in self.players),
                wall_empty=not self.wall,
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

    @staticmethod
    def _wait_cache_key(hand: Sequence[str], melds: Sequence[dict]) -> tuple:
        return (
            tuple(sorted(hand)),
            tuple(
                (meld.get("kind", ""), tuple(sorted(meld.get("tiles", ()))))
                for meld in melds
            ),
        )

    def _wait_hints_for(self, viewer: HongquePlayer) -> list[dict]:
        key = self._wait_cache_key(viewer.hand, viewer.melds)
        cached = self._wait_hint_cache.get(key)
        if cached is not None:
            return cached
        hints: list[dict] = []
        for tile in waiting_tiles(viewer.hand, viewer.melds):
            result = best_win_result(
                viewer.hand + [tile],
                viewer.melds,
                self_draw=False,
                before_first_discard=False,
                wall_empty=False,
            )
            hint = self._score_hint(result, tile)
            if hint is not None:
                hints.append(hint)
        self._wait_hint_cache[key] = hints
        return hints

    def _visible_event(self, source: dict, viewer_index: int) -> dict:
        event = dict(source)
        if event.get("type") in {"draw", "supplement"} and event.get("player") != viewer_index:
            event["tile"] = None
        return event

    def build_state(self, viewer_index: int, *, sync_mode: str = "events",
                    events_override: Optional[list[dict]] = None) -> dict:
        viewer = self.players[viewer_index]
        actions, candidates = self._legal_turn_actions(viewer)
        if self.phase == "claim" and viewer_index in self.claim_options and viewer_index not in self.claim_responses:
            actions = ["pass", "claim"]
            candidates = self.claim_options[viewer_index]
        win_hint = self._viewer_win_hint(viewer, actions)
        wait_hints = []
        # Match the common table flow: stable tenpai tips are shown after the
        # player's discard/claim, while their active hand-action window hides
        # the diamond.  An immediately available win uses win_hint instead.
        if (self.tips and win_hint is None and self.phase in {"turn", "claim"}
                and not (self.phase == "turn" and viewer_index == self.current_player_index)):
            wait_hints = self._wait_hints_for(viewer)
        waits = [hint["tile"] for hint in wait_hints]
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
            player_index = player.index
            if smart_bot:
                visible_snapshot = self._visible_codes_for(player.index)
                kong_snapshot = tuple(
                    dict(candidate) for candidate in kong_candidates(player.hand, player.melds)
                )
                supplements = player.supplements
                wall_count = len(self.wall)
                drawn_tile = player.drawn_tile
                last_draw_was_supplement = player.last_draw_was_supplement
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
            plan = {"action": "win"} if result is not None else {
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
            code = plan.get("tile")
            if code not in player.hand:
                code = player.drawn_tile if player.drawn_tile in player.hand else (
                    self._rng.choice(player.hand) if player.hand else None
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
            plan = await run_room_bot_cpu(
                self,
                choose_claim_plan,
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
                else:
                    self.claim_responses[player_index] = {"action": "pass"}
                if all(index in self.claim_responses for index in self.claim_options):
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
                    if all(index in self.claim_responses for index in self.claim_options):
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
        for task in (self._bot_task, self._round_task, self._claim_timeout_task, self._turn_timeout_task):
            if task and task is not current and not task.done():
                task.cancel()
        if self.game_task and self.game_task is not current and not self.game_task.done():
            self.game_task.cancel()
            await asyncio.gather(self.game_task, return_exceptions=True)

    async def add_spectator(self, user_id: int, connection: Any) -> None:
        raise ValueError("虹雀规则不支持观战")

    async def remove_spectator(self, user_id: int) -> None:
        return None
