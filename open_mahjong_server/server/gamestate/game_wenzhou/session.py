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
        window["_clock_started_by_player"] = {}
        window["_clock_deadlines"] = {}
        window["_clock_bank"] = {i:p.remaining_time for i,p in enumerate(self.player_list)}
        window["_clock_step"] = self.step_time
        window["_clock_finished"] = {}
        window["_clock_finished_exact"] = {}
        window["_clock_responses"] = {}
        # Clear stale replies BEFORE broadcasting, preserving fast new replies.
        for i in range(4):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        return super().open_action_window(window)

    def _clock_start(self, index, window):
        starts = window.get("_clock_started_by_player", {})
        # The single-start fallback also supports driver-created windows.
        return starts.get(index) if starts else window.get("_clock_started")

    def start_action_clock(self, index, window=None):
        """Anchor the first actual player ask; retransmits never renew it."""
        window = window if window is not None else self.live_pending_window
        if not window or not window["actions"].get(index) or index in window["_clock_finished"]:
            return
        started = self._clock_start(index, window)
        if started is None:
            started = time.monotonic()
        window["_clock_started_by_player"].setdefault(index, started)
        if window["_clock_started"] is None:
            window["_clock_started"] = started
        seconds = window["_clock_step"] + max(0, window["_clock_bank"][index])
        if self.player_list[index].is_bot:
            from ..public.ai.pacing import bot_delay
            # Bot pacing is an automatic action, not an extra human grace.
            seconds = max(seconds, bot_delay(self)+0.1)
        window["_clock_deadlines"].setdefault(index, started+max(0,seconds))

    def _clock_values(self, index, window, now=None):
        if index in window.get("_clock_finished_exact", {}):
            return window["_clock_finished_exact"][index]
        started = self._clock_start(index, window)
        elapsed = max(0, (time.monotonic() if now is None else now)-started) if started is not None and window["actions"].get(index) else 0
        bank = window.get("_clock_bank", {}).get(index, self.player_list[index].remaining_time)
        step = window.get("_clock_step", self.step_time)
        return max(0, bank-max(0, elapsed-step)), max(0, step-elapsed)

    def remaining_clock(self, index, window):
        """Round only the wire display, never the authoritative bank."""
        if index in window.get("_clock_finished", {}):
            return window["_clock_finished"][index]
        return tuple(math.ceil(value) for value in self._clock_values(index,window))

    def _finish_action_clock(self, index, window, received_at):
        if index in window["_clock_finished"]:
            return
        remaining = self._clock_values(index,window,received_at)
        self.player_list[index].remaining_time = remaining[0]
        window["_clock_finished_exact"][index] = remaining
        window["_clock_finished"][index] = tuple(math.ceil(value) for value in remaining)

    async def submit_action(self, player_index, action_type, **kwargs):
        received_at = time.monotonic()
        if type(player_index) is not int or player_index not in range(4) or not self.action_queues[player_index].empty():
            raise ValueError("非法座位或重复响应")
        self.validate_response(player_index, dict(action_type=action_type, **kwargs), self.action_dict.get(player_index, []))
        if self.machine.phase != P.READY:
            window = self.live_pending_window
            deadline = window["_clock_deadlines"].get(player_index)
            if deadline is None and self._clock_start(player_index,window) is not None:
                self.start_action_clock(player_index,window)
                deadline = window["_clock_deadlines"].get(player_index)
            if deadline is not None and received_at >= deadline:
                raise ValueError("行动时间已耗尽")
            self._finish_action_clock(player_index,window,received_at)
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
        ready = self.machine.phase == P.READY
        window = self.live_pending_window
        responses = {} if ready else window["_clock_responses"]
        deadlines = {}
        for i in self.waiting_players_list:
            if ready:
                deadlines[i] = time.monotonic()+max(0,8 if timeout is None else timeout)
            else:
                # Offline/local drivers start here; already sent asks keep their
                # per-seat start, including after collector cancellation/retry.
                self.start_action_clock(i,window)
                deadlines[i] = window["_clock_deadlines"].get(i,time.monotonic())
        while self.waiting_players_list:
            for i in list(self.waiting_players_list):
                if not self.action_queues[i].empty():
                    responses[i] = self.action_queues[i].get_nowait()
                    self.waiting_players_list.remove(i)
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if not ready:
                        self._finish_action_clock(i,window,time.monotonic())
                elif time.monotonic() >= deadlines[i]:
                    if not ready:
                        window["_clock_finished"][i] = (0,0)
                        window["_clock_finished_exact"][i] = (0,0)
                    responses[i] = self._build_timeout_action(i)
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
            try:
                await asyncio.wait(tasks, timeout=max(0,min(deadlines[i] for i in self.waiting_players_list)-time.monotonic()),
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
