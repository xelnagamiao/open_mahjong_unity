"""Async clock/queue adapter; domain mutation stays in HandFlow."""

import asyncio
import math
import time

from ..public.vote_manager import vote_checkpoint
from .actions import empty_actions, legal_cuts
from .state_machine import Phase as P
from .timing import ActionClock


class Runtime:
    def _timing_now(self):
        return time.monotonic()

    def _decision_paused(self):
        return getattr(getattr(self, "vote_manager", None), "phase", None) in {"paused", "resume_voting", "resume_countdown"}

    def open_action_window(self, window):
        self.sync_tactical_enabled()
        self._tactical_recheck_active = self._tactical_in_application = False
        self._tactical_initial_submission = None
        # Clear OLD responses only. A reply received before wait_action starts
        # already belongs to the newly published action tick and must survive.
        for index in range(4):
            self.action_events[index].clear()
            while not self.action_queues[index].empty():
                self.action_queues[index].get_nowait()
        self._action_deadlines = {}
        self._action_clocks = {
            index: ActionClock(max(0.0, float(self.player_list[index].remaining_time)), max(0.0, float(self.step_time)))
            for index, actions in (window.get("actions") or {}).items() if actions
        }
        result = super().open_action_window(window)
        from ..public.tactical_claim import init_tactical_round_state
        init_tactical_round_state(self)
        return result

    def _start_action_clock(self, index, payload=None):
        if self.machine.phase == P.READY or self._decision_paused():
            return
        if payload is not None and payload.get("action_tick") != self.server_action_tick:
            return
        clock = getattr(self, "_action_clocks", {}).get(index)
        if clock is not None and index in self.waiting_players_list:
            clock.start(self._timing_now())
            self._action_deadlines[index] = clock.deadline

    def _clock_parts(self, index):
        clock = getattr(self, "_action_clocks", {}).get(index)
        if clock is not None and index in self.waiting_players_list:
            return clock.parts(clock.received_at if clock.received_at is not None else self._timing_now())
        return math.ceil(max(0.0, self.player_list[index].remaining_time)), 0

    def _refresh_clock_payload(self, index, payload):
        if payload.get("action_tick") != self.server_action_tick:
            return
        info = payload.get("ask_hand_action_info") or payload.get("ask_other_action_info")
        if info is not None:
            info["remaining_time"], info["step_remaining"] = self._clock_parts(index)
            for seat, player in enumerate((payload.get("game_info") or {}).get("players_info", [])):
                player["remaining_time"] = (math.ceil(max(0.0, self.player_list[seat].remaining_time))
                                            if self._tactical_recheck_active else self._clock_parts(seat)[0])

    async def _send_timed_payload(self, index, payload, websocket):
        self._refresh_clock_payload(index, payload)
        await websocket.send_json(payload)
        self.websocket_sent_payloads.append(payload)
        if payload.get("ask_hand_action_info") or payload.get("ask_other_action_info"):
            self._start_action_clock(index, payload)

    async def _deliver_claim_payload(self, index, payload):
        # Hook the existing FIFO delivery at the primary player's send, before
        # slower spectator forwarding; another viewer cannot restart this clock.
        player = self.player_list[index]
        connection = (getattr(self.game_server, "user_id_to_connection", {}) or {}).get(player.user_id)
        if connection is not None and getattr(connection, "websocket", None) is not None:
            await self._send_timed_payload(index, payload, connection.websocket)
        await self.send_to_realtime_spectators(index, payload)

    async def send_to_realtime_spectators(self, index, payload):
        from ..public.spectator_rules import deliver_realtime_spectator_message
        await deliver_realtime_spectator_message(self, index, payload,
            prepare_payload=lambda packet: self._refresh_clock_payload(index, packet))

    async def send_payload_to_player(self, index, payload, *, record_fallback=True):
        if index not in range(4):
            raise ValueError("非法座位")
        payload.setdefault("player_index", index)
        connection = (getattr(self.game_server, "user_id_to_connection", {}) or {}).get(self.player_list[index].user_id)
        if connection is not None and getattr(connection, "websocket", None) is not None:
            from ..public.outbound_pipe import send_to_viewer
            from ..public.claim_protection import take_post_meld_gap_delay
            async def send():
                await self._send_timed_payload(index, payload, connection.websocket)
            await send_to_viewer(self, index, send, delay_before=take_post_meld_gap_delay(self, index))
            return True
        if record_fallback:
            self.outbound_payloads.append(payload)
        return False

    def _consume_time_bank(self, index, elapsed_seconds):
        # Keep fractions internally so repeated short overtimes cannot be free.
        self.player_list[index].remaining_time = max(0.0, self.player_list[index].remaining_time - max(0.0, elapsed_seconds - self.step_time))

    async def submit_action(self, player_index, action_type, *, target_tile=None, TileId=None,
                            cutIndex=-1, cutClass=False):
        now = self._timing_now()
        if self.machine.phase != P.READY and self._decision_paused():
            raise ValueError("牌局暂停期间不能操作")
        if (type(player_index) is not int or player_index not in range(4)
                or player_index not in self.waiting_players_list or not self.action_queues[player_index].empty()):
            raise ValueError("非法座位或重复响应")
        data = dict(player_index=player_index, action_type=action_type, target_tile=target_tile,
                    TileId=TileId, cutIndex=cutIndex, cutClass=cutClass)
        self.validate_response(player_index, data, self.action_dict.get(player_index, []))
        clock = getattr(self, "_action_clocks", {}).get(player_index)
        if self.machine.phase != P.READY and clock is not None:
            if clock.deadline is not None and now >= clock.deadline:
                raise ValueError("行动时间已耗尽")
            if not self._tactical_in_application or self._tactical_recheck_active:
                clock.received_at = now
        if action_type == "force_pass":
            from ..public.tactical_claim import tactical_mark_player_force_passed
            tactical_mark_player_force_passed(self, player_index)
        data.update(_receipt_tick=self.server_action_tick, _receipt_time=now,
                    _receipt_deadline=clock.deadline if clock is not None else None)
        self.action_queues[player_index].put_nowait(data)
        self.action_events[player_index].set()

    def _build_timeout_action(self, index):
        if self._tactical_recheck_active:
            self.action_dict[index] = []
            self.waiting_players_list.remove(index)
            return dict(player_index=index, action_type="pass", is_timeout_action=True)
        if "cut" in self.action_dict.get(index, []):
            p = self.player_list[index]
            legal = legal_cuts(self, index)
            position = next(i for i in range(len(p.hand_tiles) - 1, -1, -1) if p.hand_tiles[i] in legal)
            self.action_dict[index] = []
            self.waiting_players_list.remove(index)
            p.remaining_time = 0
            return dict(player_index=index, action_type="cut", TileId=p.hand_tiles[position], cutIndex=position,
                        cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1, is_timeout_action=True)
        return super()._build_timeout_action(index)

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
        ready = self.machine.phase == P.READY
        responses, deadlines = {}, {}
        for index in self.waiting_players_list:
            if ready:
                deadlines[index] = self._timing_now() + max(0, 8 if timeout is None else timeout)
                continue
            clock = self._action_clocks[index]
            if self.player_list[index].is_bot:
                from ..public.ai.pacing import bot_delay
                clock.minimum_duration = bot_delay(self) + 0.1
            # Normal live asks were started by first actual delivery. Offline
            # seats / pure drivers get a finite window when waiting begins.
            self._start_action_clock(index)
            deadlines[index] = clock.deadline
        self._action_deadlines = deadlines
        self.schedule_bot_actions()
        while self.waiting_players_list:
            for index in list(self.waiting_players_list):
                if not self.action_queues[index].empty():
                    responses[index] = self.action_queues[index].get_nowait()
                    self.waiting_players_list.remove(index)
                    self.action_dict[index] = []
                    self.action_events[index].clear()
                    if not ready and not self._tactical_recheck_active:
                        clock = self._action_clocks[index]
                        received = clock.received_at if clock.received_at is not None else self._timing_now()
                        self.player_list[index].remaining_time = clock.remaining_bank(received)
                elif self._timing_now() >= deadlines[index]:
                    response = self._build_timeout_action(index)
                    if response is None:
                        raise RuntimeError("等待中的杭州行动窗口没有合法超时动作")
                    responses[index] = response
                else:
                    self.action_events[index].clear()
            initial = self.choose_tactical_submission(responses)
            if initial is not None:
                self._tactical_initial_submission = initial
                return responses
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[index].wait()) for index in self.waiting_players_list]
            try:
                await asyncio.wait(tasks, timeout=max(0, min(deadlines[index] for index in self.waiting_players_list) - self._timing_now()),
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
                while self.machine.phase != P.END:
                    await vote_checkpoint(self)
                    # Publish the next decision only after a pending pause has
                    # resumed; the first delivered ask starts its real budget.
                    await self.flush_outbound_payloads()
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
        self._action_clocks, self._action_deadlines = {}, {}
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
