"""Nanjing domain state on the project's existing turn-based transport shell."""

import asyncio
import random
import time
from collections import Counter

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ..public.vote_manager import vote_checkpoint
from ...game_calculation.nanjing.rules import VERSION as RULE_VERSION, SUB_RULE, TILES, FLOWERS, PHYSICAL, normalize_config, score_hand
from .actions import ActionPolicy, empty_actions, legal_cuts
from .flowers import FlowerFlow
from .flow import HandFlow
from .player import NanjingPlayer
from .protocol import Protocol
from .recording import Recording
from .settlement import EndOfHand
from .state_machine import Phase as P, StateMachine
from .timing import ActionClock


class NanjingGameState(FlowerFlow, HandFlow, Protocol, Recording, EndOfHand, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None,
                 db_manager=None, gamestate_id="nanjing-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", SUB_RULE) != SUB_RULE:
            raise ValueError("未开放的南京麻将子规则")
        config = normalize_config(data.get("detailed_config"))
        if any(data.get(key, False) for key in ("open_cuohe", "tactical_call", "claim_protection", "tian_di_ren_he")):
            raise ValueError("南京麻将不支持此选项")
        if data.get("use_flowers", True) is not True:
            raise ValueError("南京麻将固定使用中发白及八花")
        data.update(room_rule="nanjing", sub_rule=SUB_RULE, use_flowers=True)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [NanjingPlayer(**vars(p)) for p in self.player_list]
        self.player_entry_order = [p.user_id for p in self.player_list]
        self.action_policy = ActionPolicy()
        self.use_flowers = True
        self.dead_wall_count = 0
        self.rule_version = RULE_VERSION
        self.detailed_config = config
        self.bot_speed = data.get("bot_speed", "normal")
        self.match_finishing = False
        self.next_dealer = 0
        self.dealer_streak = 0
        self.round_start_scores = [p.score for p in self.player_list]
        self.round_changes = [0]*4
        self.round_settlement = None
        self.pending_kong = self.replacement_pending = None
        self.sea_discard = False
        self.opening_queue = None
        self.ledger = []
        self.contracts = [set() for _ in range(4)]
        self.last_follow_end = -1
        self.kong_chain_payer = None
        self.kong_chain_actor = None
        self.kong_chain_count = 0
        self.domain_events = []
        self.dice = []
        self.action_clocks = {}
        self._timing_now = time.monotonic

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="nanjing-test", room_rule="nanjing", sub_rule=SUB_RULE,
                    player_list=[101,102,103,104], player_settings={}, game_round=1,
                    round_timer=0, step_timer=0, room_type="custom", tips=False, random_seed=1)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed, self.round_index)

    def initialize_round(self, *, wall=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须先结束上局的确认阶段")
        self.reset_round_state()
        self.pending_kong = self.replacement_pending = None
        self.sea_discard = False
        self.opening_queue = None
        self.ledger = []
        self.contracts = [set() for _ in range(4)]
        self.last_follow_end = -1
        self.kong_chain_payer = None
        self.kong_chain_actor = None
        self.kong_chain_count = 0
        self.domain_events = []
        self.round_settlement = None
        self.round_changes = [0]*4
        self.round_start_scores = [p.score for p in self.player_list]
        physical = list(PHYSICAL)
        rng = random.Random(self.round_random_seed)
        self.dice = [rng.randint(1,6),rng.randint(1,6)]
        if wall is None:
            rng.shuffle(physical)
            offset = (((sum(self.dice)-1) % 4)*36 + min(self.dice)*2) % 144
            wall = physical[offset:] + physical[:offset]
        elif Counter(wall) != Counter(physical):
            raise ValueError("牌墙须为144张、含字牌和八花的完整牌组")
        self.tiles_list = list(wall)
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        final, self.tiles_list = self.tiles_list[:5], self.tiles_list[5:]
        for index, offset in enumerate((0,1,3,4)):
            self.player_list[index].hand_tiles.append(final[offset])
        self.player_list[0].hand_tiles.append(final[2])
        self.player_list[0].has_draw_slot = True
        self.player_list[0].draw_kind = "normal"
        self.dealer_index = self.current_player_index = 0

    def public_pungs(self):
        return [int(m[1:]) for p in self.player_list for m in p.combination_tiles if m.startswith("k")]

    def score_win(self, index, source, tile, *, payer=None, hand=None, ignore_minimum=False):
        p = self.player_list[index]
        cards = list(p.hand_tiles) if hand is None else list(hand)
        if source != "self_draw":
            cards.append(tile)
        return score_hand(cards, p.combination_tiles, p.huapai_list, winning_tile=tile,
            self_draw=source == "self_draw", after_flower=source == "self_draw" and p.draw_kind == "flower",
            after_kong=source == "self_draw" and p.after_kong, rob_kong=source == "rob_kong",
            pung_claimed=p.pung_claimed, public_pungs=self.public_pungs(), ignore_minimum=ignore_minimum)

    def can_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        if p.is_hu or "peida" in p.tag_list or source not in ("self_draw", "discard", "rob_kong"):
            return False
        if source == "self_draw" and not p.has_draw_slot:
            return False
        quote = self.score_win(index, source, tile, payer=payer)
        return quote is not None and (source == "self_draw" or p.passed_win_value is None
                                      or quote.pattern_points > p.passed_win_value)

    def legal_discard_tiles(self, index):
        return legal_cuts(self, index)

    def open_action_window(self, window):
        fixed=self.fixed_response_window()
        self.action_clocks={i:ActionClock(0 if fixed else self.player_list[i].remaining_time,3 if fixed else self.step_time)
                            for i,actions in window.get("actions",{}).items() if actions}
        for i in range(4):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        result = super().open_action_window(window)
        self.record_hints()
        return result

    async def submit_action(self, player_index, action_type, **kwargs):
        if type(player_index) is not int or player_index not in range(4) or not self.action_queues[player_index].empty():
            raise ValueError("非法座位或重复响应")
        self.validate_response(player_index, dict(action_type=action_type, **kwargs), self.action_dict.get(player_index, []))
        clock=self.action_clocks.get(player_index)
        now=self._timing_now()
        if clock and clock.deadline is not None and now>=clock.deadline:
            raise ValueError("行动时间已耗尽")
        if clock:clock.received_at=now
        await super().submit_action(player_index, action_type, **kwargs)

    async def _deliver_claim_payload(self, viewer_index, payload):
        info=payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
        if info and info.get("action_tick")==self.server_action_tick and info.get("action_list"):
            clock=self.action_clocks.get(viewer_index)
            if clock:clock.start(self._timing_now())
        await super()._deliver_claim_payload(viewer_index,payload)

    def _build_timeout_action(self, index):
        actions = self.action_dict.get(index, [])
        if "buhua" in actions:
            data = dict(action_type="buhua")
        elif "cut" in actions:
            p = self.player_list[index]
            legal = legal_cuts(self, index)
            position = next(i for i in range(len(p.hand_tiles)-1,-1,-1) if p.hand_tiles[i] in legal)
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
            task = asyncio.create_task(play_bot(self,index,self.server_action_tick))
            self.bot_tasks.add(task)
            task.add_done_callback(self.bot_tasks.discard)

    async def wait_action(self, timeout=None):
        self.schedule_bot_actions()
        loop = asyncio.get_running_loop()
        started, responses = loop.time(), {}
        fixed_window = self.fixed_response_window()
        deadlines = {}
        for i in self.waiting_players_list:
            seconds = (timeout if timeout is not None else 8) if self.machine.phase == P.READY else (3 if fixed_window else self.step_time+self.player_list[i].remaining_time)
            if self.player_list[i].is_bot:
                from ..public.ai.pacing import bot_delay
                seconds = max(seconds, bot_delay(self)+0.1)
            clock=self.action_clocks.get(i)
            if clock and self.machine.phase!=P.READY:
                clock.minimum_duration=seconds if self.player_list[i].is_bot else 0
                clock.start(self._timing_now())
                deadlines[i]=clock.deadline
            else:deadlines[i]=self._timing_now()+max(0,seconds)
        while self.waiting_players_list:
            for i in list(self.waiting_players_list):
                if not self.action_queues[i].empty():
                    responses[i] = self.action_queues[i].get_nowait()
                    self.waiting_players_list.remove(i)
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if self.machine.phase != P.READY and not fixed_window:
                        clock=self.action_clocks.get(i)
                        self.player_list[i].remaining_time=clock.remaining_bank(clock.received_at or self._timing_now()) if clock else self.player_list[i].remaining_time
                elif self._timing_now() >= deadlines[i]:
                    bank = self.player_list[i].remaining_time
                    data = self._build_timeout_action(i)
                    if fixed_window:
                        self.player_list[i].remaining_time = bank
                    if data is not None:
                        responses[i] = data
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
            try:
                await asyncio.wait(tasks, timeout=max(0,min(deadlines[i] for i in self.waiting_players_list)-self._timing_now()),
                                   return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks,return_exceptions=True)
        return responses

    def fixed_response_window(self):
        # Online meld/rob-kong decisions share the room's step and hand bank.
        # The rulebook's separate in-play flower-replacement limit is unchanged.
        return self.machine.phase == P.REPLACEMENT and self.opening_queue is None

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
        self.action_clocks = {}
        for i in range(4):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        pending = [] if self.match_finishing else [i for i,p in enumerate(self.player_list) if not p.is_bot]
        self.server_action_tick += 1
        self.action_dict = {i:["ready"] if i in pending else [] for i in range(4)}
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
        self.player_list = self.player_list[1:] + self.player_list[:1]
        self.current_round += 1
        self.dealer_streak = 0
        for i,p in enumerate(self.player_list):
            p.player_index = i
        self.round_index += 1
        self.dealer_index = self.current_player_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
