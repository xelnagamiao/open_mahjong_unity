"""Authoritative classic160 flow on the existing turn-based transport/record shell.

As in Guobiao, a discard opens a separate response window and only the server
selects the winning claim. Domain validation precedes every table mutation.
"""
import asyncio
import random
from collections import Counter

from ...game_calculation.vietnamese import rules as book
from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ..public.vote_manager import vote_checkpoint
from .player import VietnamesePlayer
from .domain import VietnameseDomain
from .protocol import Protocol
from .recording import Recording
from .state_machine import Phase as P, StateMachine


def empty_actions():
    return {i: [] for i in range(4)}


class VietnameseGameState(VietnameseDomain, Protocol, Recording, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None,
                 db_manager=None, gamestate_id="vietnamese-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("未知越南子规则")
        if len(data.get("player_list", [])) != 4:
            raise ValueError("Classic160需要四名玩家")
        self.rules = book.Rules.from_dict(data.get("detailed_config"))
        data.update(room_rule="vietnamese", sub_rule=book.SUB_RULE, claim_protection=False)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [VietnamesePlayer(**vars(p)) for p in self.player_list]
        self.player_entry_order = [p.user_id for p in self.player_list]
        self.detailed_config = self.rules.snapshot()
        self.rule_version = book.VERSION
        self.dead_wall_count = 26
        for p in self.player_list:
            p.score = 300
        self.last_winner_user_id = None
        self.consecutive_wins = 0
        self.bot_speed = data.get("bot_speed", "normal")
        self.match_finishing = False
        self.opening_queue = None
        self.round_changes = [0] * 4
        self.round_start_scores = [p.score for p in self.player_list]
        self.ledger = []
        self.domain_events = []
        self.dice = []
        self.hand_multiplier = 1
        self.next_dealer = 1
        self.dealer_streak = 0

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="vietnamese-test", room_rule="vietnamese", sub_rule=book.SUB_RULE,
                    player_list=[101, 102, 103, 104], player_settings={}, game_round=1,
                    round_timer=0, step_timer=0, room_type="custom", tips=False, random_seed=1)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed, self.round_index)

    def _window(self, phase, player=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=player, tile=tile, actions=actions or empty_actions())

    def _event(self, action, actor, **details):
        from copy import deepcopy
        event = deepcopy(dict(action=action, player=actor, **details))
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied:
            return
        for index, p in enumerate(self.player_list):
            change = self.round_changes[index]
            p.score += change
            p.record_counter.round_score_total = getattr(p.record_counter, "round_score_total", 0) + change
            p.score_history.append(f"{change:+d}" if change else "0")
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True

    def open_action_window(self, window):
        self._active_deadlines = {}
        self._received_at = {}
        for index in range(4):
            self.action_events[index].clear()
            while not self.action_queues[index].empty():
                self.action_queues[index].get_nowait()
        return super().open_action_window(window)

    async def submit_action(self, player_index, action_type, **kwargs):
        if type(player_index) is not int or player_index not in range(4) or not self.action_queues[player_index].empty():
            raise ValueError("非法座位或重复应答")
        self.validate_response(player_index, dict(action_type=action_type, **kwargs), self.action_dict.get(player_index, []))
        now = asyncio.get_running_loop().time()
        deadline = getattr(self, '_active_deadlines', {}).get(player_index)
        if deadline is not None and now > deadline:
            raise ValueError("操作时限已过")
        await super().submit_action(player_index, action_type, **kwargs)
        self._received_at[player_index] = now

    def _build_timeout_action(self, index):
        actions = self.action_dict[index]
        if "buhua" in actions:
            data = dict(action_type="buhua")
        elif "vn_replace" in actions and "cut" not in actions and "pass" not in actions:
            from ...game_calculation.vietnamese.tiles import JOKERS
            data = dict(action_type="vn_replace", target_tile=sum(1 << JOKERS.index(t) for t in self.replacement_candidates(index)))
        elif "hu_self" in actions and "cut" not in actions:
            data = dict(action_type="hu_self")
        elif "cut" in actions:
            p = self.player_list[index]
            position = max(i for i, t in enumerate(p.hand_tiles) if t in self.legal_discard_tiles(index))
            data = dict(action_type="cut", TileId=p.hand_tiles[position], cutIndex=position,
                        cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1, is_timeout_action=True)
        else:
            data = dict(action_type="ready" if "ready" in actions else "pass")
        self.action_dict[index] = []
        if index in self.waiting_players_list:
            self.waiting_players_list.remove(index)
        return dict(player_index=index, **data)

    def schedule_bot_actions(self):
        from .bot import play_bot
        for index in self.waiting_players_list:
            if self.player_list[index].is_bot and self.bot_action_ticks.get(index) != self.server_action_tick:
                self.bot_action_ticks[index] = self.server_action_tick
                task = asyncio.create_task(play_bot(self, index, self.server_action_tick))
                self.bot_tasks.add(task)
                task.add_done_callback(self.bot_tasks.discard)

    async def wait_action(self, timeout=None):
        self.schedule_bot_actions()
        loop = asyncio.get_running_loop()
        started, results = loop.time(), {}
        deadlines = {}
        for index in self.waiting_players_list:
            seconds = timeout if self.machine.phase == P.READY and timeout is not None else self.step_time + self.player_list[index].remaining_time
            if self.player_list[index].is_bot:
                from ..public.ai.pacing import bot_delay
                seconds = max(seconds, bot_delay(self) + 0.2)
            deadlines[index] = started + max(0, seconds)
        self._active_deadlines = deadlines
        while self.waiting_players_list:
            for index in list(self.waiting_players_list):
                if not self.action_queues[index].empty():
                    results[index] = self.action_queues[index].get_nowait()
                    self.waiting_players_list.remove(index)
                    self.action_dict[index] = []
                    self.action_events[index].clear()
                    if self.machine.phase != P.READY:
                        self._consume_time_bank(index, max(0, self._received_at.pop(index, loop.time()) - started))
                elif loop.time() >= deadlines[index]:
                    results[index] = self._build_timeout_action(index)
                    if self.machine.phase != P.READY:
                        self.player_list[index].remaining_time = 0
            if self.waiting_players_list:
                tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
                try:
                    await asyncio.wait(tasks, timeout=max(0, min(deadlines[i] for i in self.waiting_players_list) - loop.time()), return_when=asyncio.FIRST_COMPLETED)
                finally:
                    for task in tasks:
                        task.cancel()
                    await asyncio.gather(*tasks, return_exceptions=True)
        return results

    async def run_game_loop(self):
        try:
            self.start_game_recording()
            while True:
                self.initialize_round()
                self.start_round_recording()
                self.emit_game_start_payloads()
                self.open_action_window(self.opening_window())
                await self.flush_outbound_payloads()
                while self.machine.phase != P.END:
                    await vote_checkpoint(self)
                    await self.resolve_action_window(timeout=self.estimated_action_window_timeout())
                    await self.flush_outbound_payloads()
                self.finalize_round_recording()
                await self.run_round_ready_phase(timeout=self.estimated_round_result_ready_timeout())
                if self.match_finishing:
                    self.machine.transition(P.FINISHED)
                    break
                self.advance_round_after_ready()
            self.finalize_game_recording()
            await self.complete_game_lifecycle()
        finally:
            for task in list(self.bot_tasks):
                task.cancel()

    async def run_round_ready_phase(self, timeout=None):
        self.machine.transition(P.READY)
        self.live_pending_window = None
        self._active_deadlines = {}
        self._received_at = {}
        for i in range(4):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        pending = [] if self.match_finishing else [i for i, p in enumerate(self.player_list) if not p.is_bot]
        self.server_action_tick += 1
        self.action_dict = {i: ["ready"] if i in pending else [] for i in range(4)}
        self.waiting_players_list = pending
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()
        if pending:
            await self.wait_action(timeout)
        self.action_dict = empty_actions()
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()

    def is_match_end(self):
        return self.match_finishing
