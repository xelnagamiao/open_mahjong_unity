"""MIL declaration upgrades on the shared tactical claim/recheck pipeline."""

from ..game_zhongyong import boardcast as wire
from ..public import game_record_manager as record
from ..game_guobiao.wait_action import select_tactical_initial_submission
from ..public.tactical_claim import (apply_tactical_claim_if_needed, clear_tactical_round_state,
                                    is_decline_action, tactical_mark_player_passed_in_grace)
from .state_machine import Phase as P
from .timing import ActionClock


class Tactical:
    def sync_tactical_enabled(self):
        # Sticky off: removing a bot must never undo a host's explicit choice.
        if any(player.is_bot for player in self.player_list):
            self.tactical_call = False
        room = getattr(getattr(self.game_server, "room_manager", None), "rooms", {}).get(self.room_id)
        if room is not None and room.get("tactical_call") is False:
            self.tactical_call = False
        if room is not None and not self.tactical_call:
            room["tactical_call"] = False
        return self.tactical_call

    def tactical_action_rank(self, action, player):
        # Pung and exposed kong have equal rank; nearest seat wins the tie.
        return self.action_priority.get(action, -1), -((player - self.current_player_index) % 4)

    def tactical_action_receipt_valid(self, index, data):
        tick, received, deadline = (data.get(key) for key in
                                    ("_receipt_tick", "_receipt_time", "_receipt_deadline"))
        opening = getattr(self, "_tactical_opening_action_tick", None)
        return (type(tick) is int and opening is not None and opening <= tick <= self.server_action_tick
                and isinstance(received, (int, float)) and (deadline is None or received < deadline)
                and data.get("action_type") in (self._tactical_action_snapshot or {}).get(index, []))

    def _finish_normal_claim_clocks(self, trigger_index):
        # Close at authoritative receipt, before application broadcasting/animation.
        trigger = self._action_clocks[trigger_index].received_at
        boundary = self._timing_now() if trigger is None else trigger
        for index, clock in self._action_clocks.items():
            receipt = boundary if clock.received_at is None else min(boundary, clock.received_at)
            clock.received_at = receipt
            self.player_list[index].remaining_time = clock.remaining_bank(receipt)

    async def collect_tactical_recheck(self, current_priority, submitted_actions):
        """Shared-engine hook; reuse delivery/receipt clocks without charging the hand bank."""
        responses = await self.wait_action()
        current_rank = self.tactical_action_rank(*self._tactical_current_claim)
        candidates = []
        for index, data in responses.items():
            action = data["action_type"]
            if submitted_actions is not None:
                submitted_actions[index] = action
            if is_decline_action(action):
                tactical_mark_player_passed_in_grace(self, index)
            elif self.tactical_action_rank(action, index) > current_rank:
                candidates.append((self.action_priority[action], action, index, data))
        return max(candidates, key=lambda item: self.tactical_action_rank(item[1], item[2]), default=None)

    async def _broadcast_tactical_application(self, state, *, action_list, action_player, cut_tile, is_claim):
        self._tactical_current_claim = (action_list[0], action_player)
        if self._has_active_round_record():
            record.track_claim_application(self, action_player, action_list[0], cut_tile)
        for viewer in range(4):
            payload = wire.visible_action_payload(self, viewer, dict(action=action_list[0], player=action_player,
                tile=cut_tile, cut_from_player=self.current_player_index))
            payload["do_action_info"]["is_claim"] = True
            self.outbound_payloads.append(self._adapt(payload, viewer))
        # Applications only announce a choice. Tile ownership/replay commit happens once below.
        await self.flush_outbound_payloads()

    async def _broadcast_tactical_recheck(self, state, *, remaining_time_override, is_tactical_recheck):
        self._tactical_recheck_active = True
        self._tactical_initial_submission = None
        self.server_action_tick += 1
        self.live_pending_window["action_tick"] = self.server_action_tick
        self._waiting_action_tick = self.server_action_tick
        self._action_deadlines = {}
        self._action_clocks = {index: ActionClock(float(self.tactical_grace_seconds), 0.0)
                               for index in self.waiting_players_list}
        for index in self.waiting_players_list:
            self.action_events[index].clear()
            self.outbound_payloads.append(self._ask_payload(index, self.live_pending_window))
        await self.flush_outbound_payloads()
        for index in self.waiting_players_list:
            self._start_action_clock(index)  # Finite fallback for disconnected/pure drivers.

    async def resolve_action_window(self, timeout=None, *, settlements=None):
        window = self.live_pending_window
        if window is None:
            raise ValueError("No live action window is open.")
        responses = await self.wait_action(timeout)
        initial = self._tactical_initial_submission
        if window["status"] != P.RESPONSE.value or not self.sync_tactical_enabled() or initial is None:
            return self.apply_action_results(window, responses, settlements=settlements)
        index, data = initial
        self._finish_normal_claim_clocks(index)
        submitted = {seat: response["action_type"] for seat, response in responses.items()}
        for seat, response in responses.items():
            if seat != index and not is_decline_action(response["action_type"]):
                self.action_queues[seat].put_nowait(dict(response))
                self.action_events[seat].set()
        self._tactical_in_application = True
        try:
            action, winner, final_data, _ = await apply_tactical_claim_if_needed(self,
                data["action_type"], index, data,
                broadcast_do_action=self._broadcast_tactical_application,
                broadcast_ask_other_action=self._broadcast_tactical_recheck, submitted_actions=submitted)
            if self._has_active_round_record():
                record.flush_unexecuted_claim_applications(self, window["tile"],
                    executed_player=winner, executed_action_type=action)
            complete = {seat: dict(player_index=seat, action_type="pass")
                        for seat, offered in window["actions"].items() if offered}
            complete[winner] = {**final_data, "player_index": winner, "action_type": action}
            window["action_tick"] = self.server_action_tick
        finally:
            self._tactical_recheck_active = self._tactical_in_application = False
            clear_tactical_round_state(self)
        return self.apply_action_results(window, complete, settlements=settlements)

    def choose_tactical_submission(self, responses):
        if self.machine.phase != P.RESPONSE or not self.sync_tactical_enabled():
            return None
        candidates = [(index, response) for index, response in responses.items()
                      if not is_decline_action(response["action_type"])]
        return select_tactical_initial_submission(self, candidates)
