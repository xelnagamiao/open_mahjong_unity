"""Authoritative delivery clocks and the shared transport/lifecycle hooks."""
import asyncio
import math
from dataclasses import dataclass

from ..public.vote_manager import vote_checkpoint
from .actions import empty_actions, legal_cuts
from .state_machine import Phase as P


@dataclass
class ActionClock:
    bank: float
    step: float
    start_time: float | None = None
    received_at: float | None = None
    minimum_duration: float = 0

    def start(self,now):
        if self.start_time is None:
            self.start_time = now

    @property
    def deadline(self):
        return None if self.start_time is None else self.start_time+max(self.bank+self.step,self.minimum_duration)

    def remaining_bank(self,now):
        elapsed = 0 if self.start_time is None else max(0,now-self.start_time)
        return max(0,self.bank-max(0,elapsed-self.step))

    def parts(self,now):
        elapsed = 0 if self.start_time is None else max(0,now-self.start_time)
        total = math.ceil(max(0,self.bank+self.step-elapsed))
        step = min(total,math.ceil(max(0,self.step-elapsed)))
        return total-step,step


class Runtime:
    def fixed_response_window(self):
        return self.machine.phase in (P.RESPONSE,P.KONG) or self.machine.phase==P.REPLACEMENT and not self.opening_replacement

    def open_action_window(self,window):
        fixed = self.fixed_response_window()
        self.action_clocks = {i:ActionClock(0 if fixed else self.player_list[i].remaining_time,3 if fixed else self.step_time)
                              for i,a in window.get("actions",{}).items() if a}
        for i in range(4):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        result = super().open_action_window(window)
        self.record_hints()
        return result

    async def submit_action(self,index,action_type,**kwargs):
        if type(index) is not int or index not in range(4) or not self.action_queues[index].empty():
            raise ValueError("非法座位或重复响应")
        self.validate_response(index,dict(action_type=action_type,**kwargs),self.action_dict.get(index,[]))
        clock = self.action_clocks.get(index)
        now = self._timing_now()
        if clock and clock.deadline is not None and now>=clock.deadline:
            raise ValueError("行动已超时")
        if clock:
            clock.received_at = now
        await super().submit_action(index,action_type,**kwargs)

    async def _deliver_claim_payload(self,viewer_index,payload):
        info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
        if info and info.get("action_tick")==self.server_action_tick and info.get("action_list"):
            clock = self.action_clocks.get(viewer_index)
            if clock:
                clock.start(self._timing_now())
        await super()._deliver_claim_payload(viewer_index,payload)

    def _build_timeout_action(self,index):
        actions = self.action_dict.get(index,[])
        data = None
        if "buhua" in actions:
            data = dict(action_type="buhua")
        elif "cut" in actions:
            p = self.player_list[index]
            legal = legal_cuts(self,index)
            position = next(n for n in range(len(p.hand_tiles)-1,-1,-1) if p.hand_tiles[n] in legal)
            data = dict(action_type="cut",TileId=p.hand_tiles[position],cutIndex=position,
                        cutClass=p.has_draw_slot and position==len(p.hand_tiles)-1,is_timeout_action=True)
        if data is None:
            return super()._build_timeout_action(index)
        self.action_dict[index] = []
        if index in self.waiting_players_list:
            self.waiting_players_list.remove(index)
        self.player_list[index].remaining_time = 0
        return dict(player_index=index,**data)

    def schedule_bot_actions(self):
        from .bot import play_bot
        for i in self.waiting_players_list:
            if not self.player_list[i].is_bot or self.bot_action_ticks.get(i)==self.server_action_tick:
                continue
            self.bot_action_ticks[i] = self.server_action_tick
            task = asyncio.create_task(play_bot(self,i,self.server_action_tick))
            self.bot_tasks.add(task)
            task.add_done_callback(self.bot_tasks.discard)

    async def wait_action(self,timeout=None):
        self.schedule_bot_actions()
        fixed, responses, deadlines = self.fixed_response_window(),{},{}
        for i in self.waiting_players_list:
            seconds = (8 if timeout is None else timeout) if self.machine.phase==P.READY else 3 if fixed else self.step_time+self.player_list[i].remaining_time
            if self.player_list[i].is_bot:
                from ..public.ai.pacing import bot_delay
                seconds = max(seconds,bot_delay(self)+0.1)
            clock = self.action_clocks.get(i)
            if clock and self.machine.phase!=P.READY:
                clock.minimum_duration = seconds if self.player_list[i].is_bot else 0
                clock.start(self._timing_now())
                deadlines[i] = clock.deadline
            else:
                deadlines[i] = self._timing_now()+max(0,seconds)
        while self.waiting_players_list:
            for i in list(self.waiting_players_list):
                if not self.action_queues[i].empty():
                    responses[i] = self.action_queues[i].get_nowait()
                    self.waiting_players_list.remove(i)
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if self.machine.phase!=P.READY and not fixed:
                        clock = self.action_clocks.get(i)
                        if clock:
                            self.player_list[i].remaining_time = clock.remaining_bank(
                                clock.received_at if clock.received_at is not None else self._timing_now())
                elif self._timing_now()>=deadlines[i]:
                    bank = self.player_list[i].remaining_time
                    data = self._build_timeout_action(i)
                    if fixed:
                        self.player_list[i].remaining_time = bank
                    if data is not None:
                        responses[i] = data
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
            try:
                await asyncio.wait(tasks,timeout=max(0,min(deadlines[i] for i in self.waiting_players_list)-self._timing_now()),
                                   return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks,return_exceptions=True)
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
                while self.machine.phase!=P.END:
                    await vote_checkpoint(self)
                    await self.resolve_action_window()
                    await self.flush_outbound_payloads()
                await self.run_round_ready_phase(self.estimated_round_result_ready_timeout())
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

    async def run_round_ready_phase(self,timeout=None):
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
