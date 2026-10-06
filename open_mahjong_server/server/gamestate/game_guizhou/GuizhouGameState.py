"""MIL Guizhou rules on the existing queued turn-based transport/record shell."""

import asyncio
import random
from collections import Counter

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ..public.vote_manager import vote_checkpoint
from ...game_calculation.guizhou.rules import RULE_VERSION, SUB_RULE, TILES, normalize_config, score_hand, waiting_tiles
from .actions import ActionPolicy, empty_actions, legal_cuts
from .flow import HandFlow
from .player import GuizhouPlayer
from .protocol import Protocol
from .recording import Recording
from .settlement import EndOfHand
from .state_machine import Phase as P, StateMachine
from .clock import ActionClock
from .tactical import TacticalClaims
from ..public import tactical_claim as tactical


class GuizhouGameState(TacticalClaims, HandFlow, Protocol, Recording, EndOfHand, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None,
                 db_manager=None, gamestate_id="guizhou-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", SUB_RULE) != SUB_RULE:
            raise ValueError("未开放的贵州麻将子规则")
        config = normalize_config(data.get("detailed_config"))
        if any(data.get(key, False) for key in ("use_flowers", "open_cuohe", "claim_protection", "tian_di_ren_he")):
            raise ValueError("贵州 MIL 标准规不支持此选项")
        data.update(room_rule="guizhou", sub_rule=SUB_RULE)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [GuizhouPlayer(**vars(p)) for p in self.player_list]
        # New-room defaults come from the validator; missing/false old settings
        # stay false. Seated bots authoritatively disable tactical claims.
        self.tactical_call = data.get("tactical_call", False) is True and not any(p.is_bot for p in self.player_list)
        self.tactical_commit_lock = False  # VII.4 permits higher amendments.
        self.tactical_grace_seconds = tactical.TACTICAL_GRACE_SECONDS
        self.tactical_pre_grace_delay = tactical.TACTICAL_PRE_GRACE_DELAY
        self.action_priority["force_pass"] = 0
        self.reset_tactical_window()
        self._tactical_hu_announced = set()
        if room_data is not None and any(p.is_bot for p in self.player_list):
            room_data["tactical_call"] = False
        self.action_policy = ActionPolicy()
        self.dead_wall_count = 0
        self.rule_version = RULE_VERSION
        self.detailed_config = config
        self.match_finishing = False
        self.next_dealer = 0
        self.round_start_scores = [p.score for p in self.player_list]
        self.round_settlement = None
        self.pending_kong = None
        self.hot_discard = False
        self.chickens = {}
        self.kongs = []
        self.hidden_opening_kongs = set()
        self.opening_revealed = False
        self.tail_half_used = False
        self.domain_events = []

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="guizhou-test", room_rule="guizhou", sub_rule=SUB_RULE,
                    player_list=[101, 102, 103, 104], player_settings={}, game_round=1,
                    round_timer=0, step_timer=0, room_type="custom", tips=False, random_seed=1)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed, self.round_index)

    def initialize_round(self, *, wall=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须先结束上局的确认阶段")
        self.reset_round_state()
        self.reset_tactical_window()
        self._tactical_hu_announced = set()
        self.pending_kong = None
        self.hot_discard = False
        self.chickens, self.kongs = {}, []
        self.hidden_opening_kongs = set()
        self.opening_revealed = False
        self.tail_half_used = False
        self.domain_events = []
        self.round_settlement = None
        self.round_start_scores = [p.score for p in self.player_list]
        physical = [t for t in TILES for _ in range(4)]
        if wall is None:
            wall = physical
            random.Random(self.round_random_seed).shuffle(wall)
        elif Counter(wall) != Counter(physical):
            raise ValueError("牌墙须为108张无字无花的完整牌组")
        self.tiles_list = list(wall)
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        # Dealer's jump draw: East upper, South lower, East next upper, West
        # next lower, North next upper. East's fourteenth tile stays separated.
        last_five, self.tiles_list = self.tiles_list[:5], self.tiles_list[5:]
        for index, offset in enumerate((0, 1, 3, 4)):
            self.player_list[index].hand_tiles.append(last_five[offset])
            self.player_list[index].hand_tiles.sort()
        self.player_list[0].hand_tiles.append(last_five[2])
        self.player_list[0].has_draw_slot = True
        for p in self.player_list:
            p.initial_quads = {t for t, n in Counter(p.hand_tiles).items() if n == 4}
        self.dealer_index = self.current_player_index = 0

    def opening_window(self):
        actions = empty_actions()
        for index in range(1, 4):
            if waiting_tiles(self.player_list[index].hand_tiles):
                actions[index] = ["baoting_initial", "pass"]
        # A distinct simultaneous declaration phase precedes ANY dealer action.
        return self._window(P.INITIAL_READY, 0, actions=actions)

    def score_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        hand = list(p.hand_tiles)
        if source != "self_draw":
            hand.append(tile)
        return score_hand(hand, p.combination_tiles, ready=p.ready_kind,
                          heavenly=source == "self_draw" and index == 0 and not self.discard_log and not p.combination_tiles,
                          kong_draw=source == "self_draw" and p.after_kong,
                          hot_discard=source == "discard" and self.hot_discard,
                          rob_kong=source == "rob_kong")

    def can_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        if p.is_hu or (source == "self_draw" and not p.has_draw_slot):
            return False
        result = self.score_win(index, source, tile, payer=payer)
        if not result:
            return False
        if source == "self_draw":
            return True
        if p.permanent_ron_block:
            return False
        if result.requires_passport and not any(c[0] in "gG" for c in p.combination_tiles):
            return False
        forced = not p.declared_ready and (source == "rob_kong" or self.hot_discard)
        return forced or result.points > p.passed_points

    def legal_discard_tiles(self, index):
        return legal_cuts(self, index)

    def open_action_window(self, window):
        self.reset_tactical_window()
        if window["status"] in (P.RESPONSE.value, P.KONG.value):
            tactical.add_tactical_force_pass_options(self, window["actions"])
        # Selection/cancel carries the same clock; a physical action starts a
        # new budget. Reconnect only reads it and never creates another clock.
        clocks = window.setdefault("_clocks", {})
        window["_delivered"] = set()
        for index, actions in window.get("actions", {}).items():
            if actions and index not in clocks:
                clocks[index] = ActionClock(self.step_time, self.player_list[index].remaining_time)
        # Drain the old tick, never replies sent while a new ask is being sent.
        for i in range(4):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        result = super().open_action_window(window)
        tactical.init_tactical_round_state(self)
        self._tactical_original_tick = self.server_action_tick
        return result

    async def submit_action(self, player_index, action_type, **kwargs):
        if type(player_index) is not int or player_index not in range(4) or not self.action_queues[player_index].empty():
            raise ValueError("非法座位或重复响应")
        clock = self.action_clock(player_index)
        received_at = clock.now() if clock is not None else None
        self.validate_response(player_index, dict(action_type=action_type, **kwargs), self.action_dict.get(player_index, []))
        if clock is not None and (clock.started is not None or clock.elapsed > 0) and clock.remaining(received_at) <= 0:
            # A packet queued after the deadline cannot beat timeout merely
            # because the collector is still sending another view.
            if not self.player_list[player_index].is_bot:
                self.action_events[player_index].set()
                raise ValueError("行动已超时")
        await super().submit_action(player_index, action_type, **kwargs)
        self._tactical_receipts[player_index] = (self.server_action_tick, received_at, action_type)
        if action_type == "force_pass":
            tactical.tactical_mark_player_force_passed(self, player_index)
        if clock is not None:
            if not self._tactical_recheck:
                clock.prepare(self.step_time, self.player_list[player_index].remaining_time)
            clock.stop(received_at)  # Validation/collection cost is not thinking time.
            if not self._tactical_recheck:
                self.player_list[player_index].remaining_time = clock.bank_remaining()

    def _build_timeout_action(self, index):
        actions = self.action_dict.get(index, [])
        if actions == ["hu"]:
            data = dict(action_type="hu")
        elif "cut" in actions:
            p = self.player_list[index]
            legal = legal_cuts(self, index)
            position = next(i for i in range(len(p.hand_tiles)-1, -1, -1) if p.hand_tiles[i] in legal)
            data = dict(action_type="cut", TileId=p.hand_tiles[position], cutIndex=position,
                        cutClass=p.has_draw_slot and position == len(p.hand_tiles)-1, is_timeout_action=True)
        else:
            return super()._build_timeout_action(index)
        self.action_dict[index] = []
        if index in self.waiting_players_list:
            self.waiting_players_list.remove(index)
        self.player_list[index].remaining_time = 0
        return dict(player_index=index, **data)

    def schedule_bot_actions(self):
        from .bot import play_bot
        for index in self.waiting_players_list:
            if not self.player_list[index].is_bot or self.bot_action_ticks.get(index) == self.server_action_tick:
                continue
            self.bot_action_ticks[index] = self.server_action_tick
            task = asyncio.create_task(play_bot(self, index, self.server_action_tick))
            self.bot_tasks.add(task)
            task.add_done_callback(self.bot_tasks.discard)

    async def wait_action(self, timeout=None):
        self.schedule_bot_actions()
        loop = asyncio.get_running_loop()
        responses, ready_deadlines, bot_deadlines = {}, {}, {}
        for i in self.waiting_players_list:
            if self.machine.phase == P.READY:
                seconds = timeout if timeout is not None else 8
                ready_deadlines[i] = loop.time() + max(0, seconds)
            else:
                clock = self.live_pending_window["_clocks"][i]
                if self.action_queues[i].empty():
                    # Delivery normally started it. Offline/failed delivery
                    # gets one fallback start, never another bank or step.
                    clock.start(self.step_time, self.player_list[i].remaining_time)
                    self.live_pending_window["_delivered"].add(i)
            if self.player_list[i].is_bot:
                from ..public.ai.pacing import bot_delay
                bot_deadlines[i] = loop.time() + bot_delay(self)+0.1

        def remaining(i):
            seconds = (ready_deadlines[i]-loop.time() if self.machine.phase == P.READY
                       else self.live_pending_window["_clocks"][i].remaining())
            return max(0, seconds, bot_deadlines.get(i, 0)-loop.time())

        while self.waiting_players_list:
            for i in list(self.waiting_players_list):
                if not self.action_queues[i].empty():
                    responses[i] = self.action_queues[i].get_nowait()
                    self.waiting_players_list.remove(i)
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if self.machine.phase != P.READY:
                        clock = self.live_pending_window["_clocks"][i]
                        clock.stop()
                        self.player_list[i].remaining_time = clock.bank_remaining()
                elif remaining(i) <= 0:
                    if self.machine.phase != P.READY:
                        self.live_pending_window["_clocks"][i].stop()
                    data = self._build_timeout_action(i)
                    if data is not None:
                        responses[i] = data
            if not self.waiting_players_list:
                break
            if self.tactical_call and self.machine.phase in (P.RESPONSE, P.KONG):
                claims = [(i, d) for i, d in responses.items() if not tactical.is_decline_action(d["action_type"])]
                if claims and not any(d["action_type"] == "hu" for _, d in claims):
                    if any(tactical.should_enter_tactical_grace(self, d["action_type"], i) for i, d in claims):
                        break
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
            try:
                await asyncio.wait(tasks, timeout=min(remaining(i) for i in self.waiting_players_list),
                                   return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
        if self.tactical_call and self.machine.phase in (P.RESPONSE, P.KONG):
            return await self.finish_tactical_claim(responses)
        return responses

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
                await self.run_round_ready_phase(timeout=self.estimated_round_result_ready_timeout())
                self.finalize_round_recording()
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

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise RuntimeError("当前不能开始下一局")
        rotation = self.next_dealer
        self.player_list = self.player_list[rotation:] + self.player_list[:rotation]
        for i, p in enumerate(self.player_list):
            p.player_index = i
        self.current_round += 1
        self.round_index += 1
        self.dealer_index = self.current_player_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
