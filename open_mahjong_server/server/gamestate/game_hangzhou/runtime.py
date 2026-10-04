"""Async clock/queue adapter; domain mutation stays in HandFlow."""

import asyncio

from ..public.vote_manager import vote_checkpoint
from .actions import empty_actions, legal_cuts
from .state_machine import Phase as P


class Runtime:
    def open_action_window(self, window):
        # Clear OLD responses only. A reply received before wait_action starts
        # already belongs to the newly published action tick and must survive.
        for index in range(4):
            self.action_events[index].clear()
            while not self.action_queues[index].empty():
                self.action_queues[index].get_nowait()
        self._action_deadlines = {}
        return super().open_action_window(window)

    async def submit_action(self, player_index, action_type, **kwargs):
        if type(player_index) is not int or player_index not in range(4) or not self.action_queues[player_index].empty():
            raise ValueError("非法座位或重复响应")
        self.validate_response(player_index, dict(action_type=action_type, **kwargs), self.action_dict.get(player_index, []))
        await super().submit_action(player_index, action_type, **kwargs)

    def _build_timeout_action(self, index):
        if "cut" in self.action_dict.get(index, []):
            p = self.player_list[index]
            legal = legal_cuts(self, index)
            position = next(i for i in range(len(p.hand_tiles) - 1, -1, -1) if p.hand_tiles[i] in legal)
            self.action_dict[index] = []
            self.waiting_players_list.remove(index)
            p.remaining_time = 0
            return dict(player_index=index, action_type="cut", TileId=p.hand_tiles[position], cutIndex=position,
                        cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1, is_timeout_action=True)
        bank = self.player_list[index].remaining_time
        result = super()._build_timeout_action(index)
        if self.machine.phase == P.RESPONSE:
            self.player_list[index].remaining_time = bank  # Three-second call time is not the turn bank.
        return result

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
        started, responses, deadlines = loop.time(), {}, {}
        for index in self.waiting_players_list:
            if self.machine.phase == P.READY:
                seconds = 8 if timeout is None else timeout
            elif self.machine.phase == P.RESPONSE:
                seconds = 3 if timeout is None else min(3, timeout)
            else:
                seconds = self.step_time + self.player_list[index].remaining_time
                if timeout is not None:
                    seconds = min(seconds, timeout)
            if self.player_list[index].is_bot:
                from ..public.ai.pacing import bot_delay
                seconds = max(seconds, bot_delay(self) + 0.1)
            deadlines[index] = started + max(0, seconds)
        self._action_deadlines = deadlines
        while self.waiting_players_list:
            for index in list(self.waiting_players_list):
                if not self.action_queues[index].empty():
                    responses[index] = self.action_queues[index].get_nowait()
                    self.waiting_players_list.remove(index)
                    self.action_dict[index] = []
                    self.action_events[index].clear()
                    if self.machine.phase not in (P.READY, P.RESPONSE):
                        self._consume_time_bank(index, loop.time() - started)
                elif loop.time() >= deadlines[index]:
                    response = self._build_timeout_action(index)
                    if response is None:
                        raise RuntimeError("等待中的杭州行动窗口没有合法超时动作")
                    responses[index] = response
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[index].wait()) for index in self.waiting_players_list]
            try:
                await asyncio.wait(tasks, timeout=max(0, min(deadlines[index] for index in self.waiting_players_list) - loop.time()),
                                   return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
        return responses

    async def run_game_loop(self):
        self._async_hints_enabled = True
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
            tasks = list(self.bot_tasks)
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)

    async def flush_outbound_payloads(self):
        if self._async_hints_enabled:
            from .hints import prepare
            await prepare(self, payloads=self.outbound_payloads[self.outbound_send_cursor:])
        await super().flush_outbound_payloads()

    async def prepare_private_hints(self, index):
        if self._async_hints_enabled:
            from .hints import prepare
            await prepare(self, indices=(index,))

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
