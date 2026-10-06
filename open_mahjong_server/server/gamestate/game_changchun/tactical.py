"""Changchun's unlocked tactical claims, using the shared priority engine."""
import logging
import time
from ..public.tactical_claim import (
    apply_tactical_claim_if_needed, is_decline_action, tactical_force_pass_is_live,
    tactical_mark_player_force_passed, tactical_mark_player_passed_in_grace,
    clear_tactical_round_state,
)
from ..public.game_record_manager import flush_unexecuted_claim_applications, track_claim_application
from ..game_taiwan.boardcast import (
    broadcast_ask_other_action, _build_do_action_payload, _send_do_action_payload_to_viewer,
)


CLAIM_PHASES = frozenset(('waiting_action_after_cut', 'waiting_action_qianggang'))


class ChangchunTacticalFlow:
    def accept_action_ingress(self, index, action_type):
        if action_type != 'force_pass':
            return True
        window = self.action_clock_manager.windows.get(index)
        return window is not None and (window.deadline is None or time.monotonic() < window.deadline)

    def validate_tactical_queued_action(self, index, data):
        """The shared engine may fish queued replies only from a live ask."""
        manager = self.action_clock_manager
        if not isinstance(data, dict):
            return False
        window = manager.windows.get(index)
        if window is None or window.deadline is None:
            return False
        action = data.get('action_type')
        snapshot = getattr(self, '_tactical_action_snapshot', None) or {}
        if action not in snapshot.get(index, ()):
            return False
        tick = data.get('_action_tick')
        if tick not in (None, window.tick) and not (
            action == 'force_pass' and tactical_force_pass_is_live(self, index, tick)
        ):
            return False
        if data.get('_cc_window_serial') != manager.serial:
            return False
        received = data.get('_cc_received_at')
        return received is not None and received < window.deadline

    async def collect_tactical_recheck(self, priority, submitted_actions):
        # This clock owns receipt/deadline validation and never debits the hand bank.
        responses, _ = await self.action_clock_manager.collect()
        best = None
        for index, data in responses.items():
            action = data['action_type']
            if submitted_actions is not None:
                submitted_actions[index] = action
            if is_decline_action(action):
                if action == 'force_pass':
                    tactical_mark_player_force_passed(self, index)
                tactical_mark_player_passed_in_grace(self, index)
                self.action_dict[index] = []
            elif self.action_priority.get(action, -1) > priority:
                candidate = (self.action_priority[action], action, index, data)
                if best is None or candidate[0] > best[0]:
                    best = candidate
        if best is None and submitted_actions is not None:
            for index, actions in self.action_dict.items():
                if actions:
                    submitted_actions[index] = 'pass'
        await self.action_clock_manager.finish_claim_window()
        return best

    async def _broadcast_cc_claim(self, _state, *, action_list, action_player, cut_tile, is_claim):
        # Application frames announce without moving tiles or advancing the ask tick.
        # The shared Taiwan payload builder keeps Changchun's private views intact.
        track_claim_application(self, action_player, action_list[0], cut_tile)
        for viewer in range(len(self.player_list)):
            payload = _build_do_action_payload(self, action_list, action_player, viewer,
                                              cut_tile=cut_tile, is_claim=is_claim)
            try:
                await _send_do_action_payload_to_viewer(self, viewer, payload)
            except Exception:
                logging.getLogger(__name__).exception('Changchun claim delivery failed seat=%s', viewer)

    async def _broadcast_tactical_recheck(self, _state, **_clock_options):
        self._cc_tactical_recheck = True
        await broadcast_ask_other_action(self, **_clock_options)

    async def _collect_changchun_claims(self):
        responses, allowed = await self.collect_action_responses()
        if not self.tactical_call:
            return responses, allowed
        submitted = {index: data['action_type'] for index, data in responses.items()}
        claims = [(self.action_priority.get(data['action_type'], 0),
                   -((index - self.current_player_index) % 4), index, data)
                  for index, data in responses.items()
                  if not is_decline_action(data['action_type'])]
        if not claims:
            clear_tactical_round_state(self)
            return responses, allowed
        _, _, index, data = max(claims)
        # Pause normal clocks at the transition. Transport/announcement/grace time
        # belongs to neither another step allowance nor the normal thinking bank.
        await self.action_clock_manager.finish_claim_window()
        try:
            action, index, data, _ = await apply_tactical_claim_if_needed(
                self, data['action_type'], index, data,
                broadcast_do_action=self._broadcast_cc_claim,
                broadcast_ask_other_action=self._broadcast_tactical_recheck,
                submitted_actions=submitted,
            )
            tile = self.jiagang_tile if self.game_status == 'waiting_action_qianggang' else \
                self.player_list[self.current_player_index].discard_tiles[-1]
            flush_unexecuted_claim_applications(self, tile, executed_player=index,
                                               executed_action_type=action)
            # A lower application was announced, not executed. Preserve declines
            # for missed-win bookkeeping and let the existing executor settle only
            # the final winner/claim, with the original legal options.
            final = {pid: {'action_type': choice if is_decline_action(choice) else 'pass'}
                     for pid, choice in submitted.items()}
            final[index] = dict(data, action_type=action)
            return final, allowed
        finally:
            clear_tactical_round_state(self)
            self._cc_tactical_recheck = False
            self.waiting_players_list = []

    async def wait_claim_action(self):
        responses, allowed = await self._collect_changchun_claims()
        if self.game_status == 'waiting_action_after_cut':
            await self.resolve_discard_responses(responses, allowed)
        else:
            await self.resolve_rob_kong_responses(responses, allowed)
        return True
