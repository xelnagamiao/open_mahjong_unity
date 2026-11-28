import asyncio
import time
from collections import Counter
from ..public.ai.pacing import wait_bot_delay, bot_delay
from ..public.action_window import prepare_action_window
from ..public.vote_manager import vote_checkpoint
from .phases import P
from . import actions as A
from ...game_calculation.malaysia import rules as R


class Runtime:
    def open_action_window(self, window):
        self._active_deadlines = {}
        self._received_at = {}
        for i in range(3):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        return super().open_action_window(window)

    async def submit_action(self, i, action_type, **kwargs):
        if type(i) is not int or i not in range(3) or not self.action_queues[i].empty():
            raise ValueError("非法座位或重复响应")
        self.validate_response(i, dict(action_type=action_type, **kwargs), self.action_dict.get(i, []))
        now = asyncio.get_running_loop().time()
        deadline = getattr(self, '_active_deadlines', {}).get(i)
        if deadline is not None and now > deadline:
            raise ValueError("操作时限已过")
        await super().submit_action(i, action_type, **kwargs)
        self._received_at[i] = now

    def automatic_action(self, i, *, bot=False):
        p, actions = self.player_list[i], self.action_dict[i]
        from ..public.ai.auto_cut_policy import is_auto_cut_bot, choose_auto_cut_action
        if is_auto_cut_bot(p):
            # Flies cannot be discarded; exposing them draws a replacement.
            if "malaysia_fly" in actions:
                return dict(action_type="malaysia_fly")
            return choose_auto_cut_action(p, actions, A.cuts(p) if "cut" in actions else None)
        for a in ("ready", "buhua"):
            if a in actions:
                return dict(action_type=a)
        if bot:
            for a in ("hu_self", "hu"):
                if a in actions:
                    return dict(action_type=a)
            for a in actions:
                if a.startswith("malaysia_redeem:"):
                    return dict(action_type=a)
            for a in ("angang", "jiagang"):
                if a in actions:
                    return dict(action_type=a, target_tile=A.kongs(self, i, a)[0])
            if "malaysia_fly" in actions and R.side_fan(p.hand_tiles, p.melds, p.huapai_list, i) < 5 and len(p.huapai_list) > 0:
                return dict(action_type="malaysia_fly")
            claim_ops = [a for a in actions if a == "gang" or a.startswith("malaysia_peng:")]
            if claim_ops:
                from ..public.ai.guobiao_shanten import xiangting_yiban
                def _my_score(hand, melds):
                    waits = R.waits(hand, melds)
                    if waits:
                        qual = sum(1 for w in waits if R.score_hand(list(hand) + [w], melds, p.huapai_list, seat=i, winning_tile=w, self_draw=True) is not None)
                        if qual > 0:
                            return (0, qual)
                    natural = [t for t in hand if t != R.FLY]
                    return (min(-1, -(xiangting_yiban(Counter(natural), len(melds)) - hand.count(R.FLY))), 0)
                baseline = _my_score(p.hand_tiles, p.melds)
                if "gang" in claim_ops:
                    return dict(action_type="gang")
                if "malaysia_peng:0" in claim_ops:
                    tile = (getattr(self, "live_pending_window", None) or {}).get("tile")
                    if tile is not None and tile in R.TILES and p.hand_tiles.count(tile) >= 2:
                        rest = list(p.hand_tiles)
                        rest.remove(tile); rest.remove(tile)
                        next_melds = list(p.melds) + [R.Meld("triplet", (tile, tile, tile), (tile, tile, tile))]
                        if rest and max(_my_score(rest[:k] + rest[k + 1:], next_melds) for k in range(len(rest))) > baseline:
                            return dict(action_type="malaysia_peng:0")
        if "pass" in actions:
            return dict(action_type="pass")
        if "cut" in actions:
            legal = A.cuts(p)
            if bot:
                from ..public.ai.guobiao_shanten import xiangting_yiban
                def _cut_rank(t):
                    rest = list(p.hand_tiles)
                    rest.remove(t)
                    waits = R.waits(rest, p.melds)
                    qual = sum(1 for w in waits if R.score_hand(rest + [w], p.melds, p.huapai_list, seat=i, winning_tile=w, self_draw=True) is not None) if waits else 0
                    natural = [x for x in rest if x != R.FLY]
                    dist = max(-1, xiangting_yiban(Counter(natural), len(p.melds)) - rest.count(R.FLY))
                    counts = Counter(rest)
                    conn = sum(counts[x] * 3 + sum(counts[x + d] for d in (-2, -1, 1, 2) if x in R.DOTS and x + d in R.DOTS) for x in counts)
                    return (0 if qual else 1, -qual, dist, -conn, t)
                tile = min(legal, key=_cut_rank)
            else:
                tile = next(t for t in reversed(p.hand_tiles) if t in legal)
            pos = len(p.hand_tiles) - 1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
            return dict(action_type="cut", TileId=tile, cutIndex=pos, cutClass=p.has_draw_slot and pos == len(p.hand_tiles) - 1)
        # No optional decision remains (for example, a hand containing only
        # flies). Keep the window progressing without treating timeout as bot play.
        for a in ("hu_self", "hu", "malaysia_fly"):
            if a in actions:
                return dict(action_type=a)
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
        self._active_deadlines = deadlines
        while self.waiting_players_list:
            for i in list(self.waiting_players_list):
                if not self.action_queues[i].empty():
                    responses[i] = self.action_queues[i].get_nowait()
                    self.waiting_players_list.remove(i)
                    self.action_dict[i] = []
                    self.action_events[i].clear()
                    if self.machine.phase != P.READY:
                        self._consume_time_bank(i, max(0, self._received_at.pop(i, loop.time()) - started))
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
        from ..public.settlement_sync import begin_ready, ready_remaining
        begin_ready(self, timeout)
        self.machine.transition(P.READY)
        self.live_pending_window = None
        # Ready may arrive during the first broadcast, before wait_action has
        # replaced the previous hand's (possibly expired) receive deadline.
        self._active_deadlines = {}
        self._received_at = {}
        self.server_action_tick += 1
        self.action_dict = {i: ["ready"] if not p.is_bot and not self.match_finishing else [] for i, p in enumerate(self.player_list)}
        prepare_action_window(self, new_ask=True, tactical=False)
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()
        if self.waiting_players_list:
            await self.wait_action(timeout=ready_remaining(self))
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
