"""Guizhou adapter for the shared tactical priority engine.

Normal clocks pause only after an actual lower-priority application. Rechecks
start at each recipient's delivery, without charging their hand bank. VII.4
permits a higher amendment; VIII.3 retains every accepted ron.
"""
import asyncio

from ..public import tactical_claim as tactical
from ..public import game_record_manager as record
from ..game_zhongyong import boardcast as wire
from .clock import ActionClock
from .state_machine import Phase as P


class TacticalClaims:
    def reset_tactical_window(self):
        self._tactical_active = False
        self._tactical_recheck = False
        self._tactical_clocks = {}
        self._tactical_delivered = set()
        self._tactical_responses = {}
        self._tactical_receipts = {}
        self._tactical_announced = {}
        self._tactical_amendment_offered = set()
        tactical.clear_tactical_round_state(self)

    def action_clock(self, index):
        if self._tactical_active:
            return self._tactical_clocks.get(index) if self._tactical_recheck else None
        return (self.live_pending_window or {}).get("_clocks", {}).get(index)

    def pause_normal_claim_clocks(self):
        for index, clock in self.live_pending_window.get("_clocks", {}).items():
            clock.stop()
            self.player_list[index].remaining_time = clock.bank_remaining()
        self._tactical_active = True

    def validate_tactical_queued_action(self, index, data):
        receipt = self._tactical_receipts.get(index)
        valid = bool(receipt and receipt[2] == data.get("action_type")
                     and self._tactical_opening_action_tick <= receipt[0] <= self.server_action_tick
                     and data.get("action_type") in (tactical.tactical_opening_snapshot(self) or {}).get(index, []))
        if valid:
            self._tactical_responses[index] = data
            self.action_dict[index] = []
            if index in self.waiting_players_list:
                self.waiting_players_list.remove(index)
        return valid

    async def submit_tactical_force_pass(self, index, tick):
        if not tactical.tactical_force_pass_is_live(self, index, tick):
            raise ValueError("放弃不属于当前弃牌")
        if self._tactical_receipts.get(index, (None, None, None))[2] == "hu":
            raise ValueError("已接受的和牌不能改报为放弃")
        if self.action_dict.get(index) and index in self.waiting_players_list:
            await self.submit_action(index, "force_pass")
        else:
            tactical.tactical_mark_player_force_passed(self, index)
            self._tactical_responses[index] = dict(player_index=index, action_type="force_pass")

    async def _broadcast_tactical_claim(self, gs, *, action_list, action_player, cut_tile, **unused):
        action = action_list[0]
        if self._tactical_announced.get(action_player) == action:
            return
        self._tactical_announced[action_player] = action
        if action == "hu":
            self._tactical_hu_announced.add(action_player)
        record.track_claim_application(self, action_player, action, cut_tile)
        event = dict(action=action, player=action_player, tile=cut_tile)
        for viewer in range(4):
            payload = self._adapt(wire.visible_action_payload(self, viewer, event), viewer)
            payload["do_action_info"].update(is_claim=True, action_tick=self.server_action_tick,
                                             cut_tile=cut_tile, cut_from_player=self.current_player_index)
            await self._send_claim_protection_payload(viewer, payload)

    async def _broadcast_tactical_recheck(self, gs=None, *, remaining_time_override=None,
                                          is_tactical_recheck=True, reuse=False):
        self._tactical_recheck = True
        self.server_action_tick += 1
        self.live_pending_window["action_tick"] = self.server_action_tick
        seconds = max(0, float(self.tactical_grace_seconds))
        for index in self.waiting_players_list:
            if not reuse or index not in self._tactical_clocks:
                self._tactical_clocks[index] = ActionClock(0, seconds)
                self._tactical_delivered.discard(index)
        # No queue flush: replies may arrive while later views are sent.
        for index in list(self.waiting_players_list):
            if self.action_dict[index] and self.action_queues[index].empty():
                await self._send_claim_protection_payload(index, self._ask_payload(index, self.live_pending_window))
        for index in self.waiting_players_list:
            if index not in self._tactical_delivered and self.action_queues[index].empty():
                self._tactical_clocks[index].start(0, seconds)
                self._tactical_delivered.add(index)

    def _collect_grace_response(self, index, data, submitted):
        action = data["action_type"]
        self._tactical_responses[index] = data
        submitted[index] = action
        if tactical.is_decline_action(action):
            tactical.tactical_mark_player_passed_in_grace(self, index)
            if action == "force_pass":
                tactical.tactical_mark_player_force_passed(self, index)
        self.action_dict[index] = []
        if index in self.waiting_players_list:
            self.waiting_players_list.remove(index)
        self.action_events[index].clear()
        self._tactical_clocks[index].stop()

    async def _offer_higher_amendments(self, submitted):
        snapshot = tactical.tactical_opening_snapshot(self) or self.live_pending_window["actions"]
        new_players = []
        for index, choices in snapshot.items():
            previous = self._tactical_announced.get(index)
            changed_applicant = previous is not None and self.action_priority[previous] < self.action_priority["hu"]
            # A hu already queued during the low application may bypass the
            # shared recheck. Other eligible ron players still get that ask.
            skipped_recheck = not self._tactical_recheck
            if ((changed_applicant or skipped_recheck)
                    and "hu" in choices and submitted.get(index) != "hu"
                    and submitted.get(index) != "force_pass"
                    and index not in self._tactical_amendment_offered
                    and not tactical.tactical_player_has_force_passed(self, index)):
                # Another player's hu changes the situation (VII.4).
                self.action_dict[index] = ["hu"] + [a for a in snapshot[index] if tactical.is_decline_action(a)]
                if index not in self.waiting_players_list:
                    self.waiting_players_list.append(index)
                self._tactical_clocks.pop(index, None)
                self._tactical_amendment_offered.add(index)
                new_players.append(index)
        if new_players:
            if tactical.tactical_opening_snapshot(self) is None:
                self._tactical_action_snapshot = {i: list(a) for i, a in snapshot.items()}
                self._tactical_opening_action_tick = self._tactical_original_tick
                self._tactical_force_passed_players = {i for i, a in submitted.items() if a == "force_pass"}
            await self._broadcast_tactical_recheck(reuse=True)
        return bool(new_players)

    async def collect_tactical_recheck(self, current_priority, submitted_actions):
        """Shared-engine hook: all ron responders, independent delivery clocks."""
        best = None
        amended = False
        while self.waiting_players_list:
            for index in list(self.waiting_players_list):
                clock = self._tactical_clocks[index]
                if not self.action_queues[index].empty():
                    data = self.action_queues[index].get_nowait()
                elif clock.remaining() <= 0:
                    action = "hu" if self.action_dict[index] == ["hu"] else "pass"
                    data = dict(player_index=index, action_type=action, is_timeout_action=True)
                else:
                    continue
                self._collect_grace_response(index, data, submitted_actions)
                priority = self.action_priority.get(data["action_type"], -1)
                if priority > current_priority and (best is None or priority > best[0]):
                    best = (priority, data["action_type"], index, dict(data))
            if best is not None and not amended:
                amended = True
                await self._broadcast_tactical_claim(self, action_list=[best[1]], action_player=best[2],
                                                     cut_tile=self.live_pending_window["tile"])
                await self._offer_higher_amendments(submitted_actions)
            if not self.waiting_players_list:
                break
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
            try:
                await asyncio.wait(tasks, timeout=min(self._tactical_clocks[i].remaining() for i in self.waiting_players_list),
                                   return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
        return best

    async def finish_tactical_claim(self, responses):
        candidates = [(self.action_priority[d["action_type"]], -((i-self.current_player_index) % 4), i, d)
                      for i, d in responses.items() if not tactical.is_decline_action(d["action_type"])]
        if not candidates:
            tactical.clear_tactical_round_state(self)
            return responses
        _, _, index, data = max(candidates, key=lambda row: row[:2])
        action = data["action_type"]
        self._tactical_responses = responses
        submitted = {i: d["action_type"] for i, d in responses.items()}
        if tactical.should_enter_tactical_grace(self, action, index):
            self.pause_normal_claim_clocks()
        if self.machine.phase == P.KONG:
            self.jiagang_tile = self.live_pending_window["tile"]
        action, index, data, announced = await tactical.apply_tactical_claim_if_needed(
            self, action, index, data, broadcast_do_action=self._broadcast_tactical_claim,
            broadcast_ask_other_action=self._broadcast_tactical_recheck, submitted_actions=submitted)
        if self._tactical_active and action == "hu" and await self._offer_higher_amendments(submitted):
            await self.collect_tactical_recheck(self.action_priority["peng"], submitted)
            tactical.clear_tactical_round_state(self, submitted)
        responses[index] = data
        for player, choices in self.live_pending_window["actions"].items():
            if choices:
                chosen = submitted.get(player, "hu" if choices == ["hu"] else "pass")
                responses[player] = dict(responses.get(player, {}), player_index=player, action_type=chosen)
        # The public engine returns one best claimant; retain every accepted hu.
        for player, response in responses.items():
            if response["action_type"] == "hu" and self._tactical_announced.get(player) != "hu":
                await self._broadcast_tactical_claim(self, action_list=["hu"], action_player=player,
                                                     cut_tile=self.live_pending_window["tile"])
        executed = {(i, "hu") for i, d in responses.items() if d["action_type"] == "hu"} or {(index, action)}
        pending = getattr(self, "_claim_apply_pending", [])
        self._claim_apply_pending = [p for p in pending if (p["player_index"], p["apply_action"]) not in
                                    {(i, record._normalize_claim_apply_action(a)) for i, a in executed}]
        if self._has_active_round_record():
            record.flush_all_unexecuted_claim_applications(self)
        else:
            self._claim_apply_pending.clear()
        self._tactical_recheck = False
        self.action_dict = {i: [] for i in range(4)}
        self.waiting_players_list = []
        return responses
