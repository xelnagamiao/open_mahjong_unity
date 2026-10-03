"""One Hong Kong family, with rule-local flow and the existing transport shell."""

import asyncio

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.vote_manager import vote_checkpoint
from .core import HongKongCore
from .flowers import HongKongFlowers
from .hand_flow import HongKongHandFlow
from .settlement import HongKongSettlement
from .protocol import HongKongProtocol
from .recording import HongKongRecording
from .state_machine import HongKongPhase as P
from .bot import play_bot


class HongKongGameState(HongKongCore,HongKongFlowers,HongKongHandFlow,HongKongSettlement,
                       HongKongProtocol,HongKongRecording,ZhongyongGameState):
    async def run_game_loop(self):
        self.start_game_recording()
        while True:
            self.initialize_round()
            self.start_round_recording()
            self.emit_game_start_payloads()
            self.open_action_window(self.opening_window())
            await self.flush_outbound_payloads()
            while self.machine.phase!=P.END:
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

    def open_action_window(self,window):
        # Drain the OLD window before publishing the new tick. wait_action must
        # preserve early replies that arrive while another viewer is being sent.
        for index in range(4):
            self.action_events[index].clear()
            while not self.action_queues[index].empty():
                self.action_queues[index].get_nowait()
        return super().open_action_window(window)

    async def submit_action(self,player_index,action_type,**kwargs):
        index = player_index
        if type(index) is not int or index not in range(4) or not self.action_queues[index].empty():
            raise ValueError("Duplicate or invalid seat response")
        self.validate_response(index,dict(action_type=action_type,**kwargs),self.action_dict.get(index,[]))
        return await super().submit_action(index,action_type,**kwargs)

    def schedule_bot_actions(self):
        if self.machine.phase in (P.END,P.FINISHED):
            return
        for index in list(self.waiting_players_list):
            if not self.player_list[index].is_bot or self.bot_action_ticks.get(index)==self.server_action_tick:
                continue
            self.bot_action_ticks[index] = self.server_action_tick
            task = asyncio.create_task(play_bot(self,index,list(self.action_dict[index]),self.game_status,self.server_action_tick))
            self.bot_tasks.add(task)
            task.add_done_callback(self.bot_tasks.discard)

    async def wait_action(self,timeout=None):
        self.schedule_bot_actions()
        loop = asyncio.get_running_loop()
        started = loop.time()
        deadlines = {}
        for i in self.waiting_players_list:
            seconds = (timeout if timeout is not None else 8) if self.machine.phase==P.READY else self.step_time+self.player_list[i].remaining_time
            if self.player_list[i].is_bot:
                from ..public.ai.pacing import bot_delay
                seconds = max(seconds,bot_delay(self)+0.1)
            deadlines[i] = started+max(0,seconds)
        results = {}
        while self.waiting_players_list:
            for i in list(self.waiting_players_list):
                if not self.action_queues[i].empty():
                    results[i] = self.action_queues[i].get_nowait()
                    self.waiting_players_list.remove(i)
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if self.machine.phase!=P.READY:
                        self._consume_time_bank(i,loop.time()-started)
                elif loop.time()>=deadlines[i]:
                    decision = self._build_timeout_action(i)
                    if decision is not None:
                        results[i] = decision
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
            try:
                await asyncio.wait(tasks,timeout=max(0,min(deadlines[i] for i in self.waiting_players_list)-loop.time()),
                                   return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    if not task.done():
                        task.cancel()
                await asyncio.gather(*tasks,return_exceptions=True)
        return results

    async def run_round_ready_phase(self,timeout=None):
        self.machine.transition(P.READY)
        self.live_pending_window = None
        # The last result's confirm button opens the final ranking locally.
        # It does not submit ready; publish game_end immediately so that the
        # presentation layer can defer it until that confirmation.
        pending = set() if self.match_finishing else {p.player_index for p in self.player_list if not p.is_bot}
        while pending:
            self.server_action_tick += 1
            self.action_dict = {i:(["ready"] + (["pull_cut"] if self.cut_eligible_pulls(i) and not self.match_finishing else [])) if i in pending else [] for i in range(4)}
            self.waiting_players_list = sorted(pending)
            self.emit_ready_status_payloads()
            await self.flush_outbound_payloads()
            responses = await self.wait_action(timeout)
            for index,data in responses.items():
                if data["action_type"]=="pull_cut":
                    self.cut_pulls(index)
                else:
                    pending.remove(index)
        self.action_dict = {i:[] for i in range(4)}
        self.waiting_players_list = []
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()
