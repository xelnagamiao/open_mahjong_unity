"""Round lifecycle and queue timing around the shared transport implementation."""

import asyncio
import math
import time

from ..public.vote_manager import vote_checkpoint
from .actions import empty_actions
from .state_machine import Phase as P


class Session:
    def open_action_window(self, window):
        window["_clock_started"] = None
        window["_clock_bank"] = {i:p.remaining_time for i,p in enumerate(self.player_list)}
        window["_clock_finished"] = {}
        # Clear stale replies BEFORE broadcasting, preserving fast new replies.
        for i in range(4):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        return super().open_action_window(window)

    def remaining_clock(self, index, window):
        """Project the existing action budget; reconnect never starts a new one."""
        if index in window.get("_clock_finished", {}):
            return window["_clock_finished"][index]
        started = window.get("_clock_started")
        elapsed = max(0, time.monotonic()-started) if started is not None and window["actions"].get(index) else 0
        bank = window.get("_clock_bank", {}).get(index, self.player_list[index].remaining_time)
        return (math.ceil(max(0, bank-max(0, elapsed-self.step_time))),
                math.ceil(max(0, self.step_time-elapsed)))

    async def submit_action(self, player_index, action_type, **kwargs):
        if type(player_index) is not int or player_index not in range(4) or not self.action_queues[player_index].empty():
            raise ValueError("非法座位或重复响应")
        self.validate_response(player_index, dict(action_type=action_type, **kwargs), self.action_dict.get(player_index, []))
        await super().submit_action(player_index, action_type, **kwargs)

    def _build_timeout_action(self, index):
        if "cut" not in self.action_dict.get(index, []):
            return super()._build_timeout_action(index)
        p = self.player_list[index]
        position = len(p.hand_tiles)-1
        self.action_dict[index] = []
        if index in self.waiting_players_list:
            self.waiting_players_list.remove(index)
        p.remaining_time = 0
        return dict(player_index=index, action_type="cut", TileId=p.hand_tiles[position], cutIndex=position,
                    cutClass=p.has_draw_slot, is_timeout_action=True)

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
        started, responses, deadlines = loop.time(), {}, {}
        if self.machine.phase != P.READY:
            self.live_pending_window["_clock_started"] = time.monotonic()
        for i in self.waiting_players_list:
            seconds = (8 if timeout is None else timeout) if self.machine.phase == P.READY else self.step_time+self.player_list[i].remaining_time
            if self.player_list[i].is_bot:
                from ..public.ai.pacing import bot_delay
                seconds = max(seconds, bot_delay(self)+0.1)
            deadlines[i] = started+max(0,seconds)
        while self.waiting_players_list:
            for i in list(self.waiting_players_list):
                if not self.action_queues[i].empty():
                    responses[i] = self.action_queues[i].get_nowait()
                    self.waiting_players_list.remove(i)
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if self.machine.phase != P.READY:
                        remaining = self.remaining_clock(i,self.live_pending_window)
                        self.live_pending_window["_clock_finished"][i] = remaining
                        self.player_list[i].remaining_time = remaining[0]
                elif loop.time() >= deadlines[i]:
                    if self.machine.phase != P.READY:
                        self.live_pending_window["_clock_finished"][i] = (0,0)
                    responses[i] = self._build_timeout_action(i)
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
            try:
                await asyncio.wait(tasks, timeout=max(0,min(deadlines[i] for i in self.waiting_players_list)-loop.time()),
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
