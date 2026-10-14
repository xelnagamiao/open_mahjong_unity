import asyncio
import time
from collections import Counter
from ..public.ai.pacing import wait_bot_delay, bot_delay
from ..public.vote_manager import vote_checkpoint
from .phases import P
from . import actions as A
from ...game_calculation.malaysia import rules as R


class Runtime:
    def open_action_window(self, window):
        for i in range(3):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        return super().open_action_window(window)

    async def submit_action(self, i, action_type, **kwargs):
        if type(i) is not int or i not in range(3) or not self.action_queues[i].empty():
            raise ValueError("非法座位或重复响应")
        self.validate_response(i, dict(action_type=action_type, **kwargs), self.action_dict.get(i, []))
        await super().submit_action(i, action_type, **kwargs)

    def automatic_action(self, i, *, bot=False):
        p, actions = self.player_list[i], self.action_dict[i]
        for a in ("ready", "buhua", "hu_self", "hu"):
            if a in actions:
                return dict(action_type=a)
        if bot:
            for a in actions:
                if a.startswith("malaysia_redeem:"):
                    return dict(action_type=a)
            for a in ("angang", "jiagang"):
                if a in actions:
                    return dict(action_type=a, target_tile=A.kongs(self, i, a)[0])
            if "malaysia_fly" in actions and R.side_fan(p.hand_tiles, p.melds, p.huapai_list, i) < 5 and len(p.huapai_list) > 0:
                return dict(action_type="malaysia_fly")
            for a in actions:
                if a == "malaysia_peng:0" or a == "gang":
                    return dict(action_type=a)
        if "pass" in actions:
            return dict(action_type="pass")
        if "cut" in actions:
            legal = A.cuts(p)
            # Keep pairs and connected dots. The baseline bot is deterministic,
            # never substitutes or exposes another seat's hidden tiles.
            counts = Counter(p.hand_tiles)
            def keep(t):
                return counts[t] * 3 + sum(counts[t + d] for d in (-2, -1, 1, 2) if t in R.DOTS and t + d in R.DOTS)
            tile = min(legal, key=lambda t: (keep(t), t)) if bot else next(t for t in reversed(p.hand_tiles) if t in legal)
            pos = len(p.hand_tiles) - 1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
            return dict(action_type="cut", TileId=tile, cutIndex=pos, cutClass=p.has_draw_slot and pos == len(p.hand_tiles) - 1)
        raise RuntimeError("操作窗口没有可执行的自动动作")

    def _build_timeout_action(self, i):
        data = self.automatic_action(i)
        self.action_dict[i] = []
        if i in self.waiting_players_list:
            self.waiting_players_list.remove(i)
        if data["action_type"] != "ready":
            self.player_list[i].remaining_time = 0
        return dict(player_index=i, is_timeout_action=data["action_type"] == "cut", **data)

    def schedule_bot_actions(self):
        async def play(i, tick):
            started = time.monotonic()
            data = self.automatic_action(i, bot=True)
            await wait_bot_delay(self, started)
            if tick == self.server_action_tick and i in self.waiting_players_list and self.action_queues[i].empty():
                await self.submit_action(i, **data)
        for i in self.waiting_players_list:
            if self.player_list[i].is_bot and self.bot_action_ticks.get(i) != self.server_action_tick:
                self.bot_action_ticks[i] = self.server_action_tick
                task = asyncio.create_task(play(i, self.server_action_tick))
                self.bot_tasks.add(task)
                task.add_done_callback(self.bot_tasks.discard)

    async def wait_action(self, timeout=None):
        self.schedule_bot_actions()
        loop, responses = asyncio.get_running_loop(), {}
        started = loop.time()
        deadlines = {}
        for i in self.waiting_players_list:
            seconds = (timeout if timeout is not None else 8) if self.machine.phase == P.READY else self.step_time + self.player_list[i].remaining_time
            if self.player_list[i].is_bot:
                seconds = max(seconds, bot_delay(self) + .1)
            deadlines[i] = started + max(0, seconds)
        while self.waiting_players_list:
            for i in list(self.waiting_players_list):
                if not self.action_queues[i].empty():
                    responses[i] = self.action_queues[i].get_nowait()
                    self.waiting_players_list.remove(i)
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if self.machine.phase != P.READY:
                        self._consume_time_bank(i, loop.time() - started)
                elif loop.time() >= deadlines[i]:
                    responses[i] = self._build_timeout_action(i)
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
            try:
                await asyncio.wait(tasks, timeout=max(0, min(deadlines[i] for i in self.waiting_players_list) - loop.time()), return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
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
                    await self.resolve_action_window()
                    await self.flush_outbound_payloads()
                self.finalize_round_recording()
                await self.run_round_ready_phase(self.estimated_round_result_ready_timeout())
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
            if tasks:
                await asyncio.gather(*tasks, return_exceptions=True)

    async def run_round_ready_phase(self, timeout=None):
        self.machine.transition(P.READY)
        self.live_pending_window = None
        self.server_action_tick += 1
        self.action_dict = {i: ["ready"] if not p.is_bot and not self.match_finishing else [] for i, p in enumerate(self.player_list)}
        self.waiting_players_list = [i for i, a in self.action_dict.items() if a]
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()
        if self.waiting_players_list:
            await self.wait_action(timeout)
        self.action_dict = A.empty()
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise ValueError("当前不能进入下一局")
        n = self.next_dealer
        self.player_list = self.player_list[n:] + self.player_list[:n]
        for i, p in enumerate(self.player_list):
            p.player_index = i
        self.current_round += 1
        self.round_index += 1
        self.machine.transition(P.START)

    @property
    def match_finishing(self):
        return self.current_round >= self.max_round * 3

    def is_match_end(self):
        return self.match_finishing
