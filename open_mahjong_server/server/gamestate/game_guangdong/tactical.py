"""MIL 六-4/5 改报；普通时钟与独立五秒抢断由不同窗口负责。"""
import time
import logging

from ...response import Response, Do_action_info

from ..game_taiwan import boardcast
from ..public import tactical_claim as tactical
from ..public.claim_protection import has_bot_players
from ..public.game_record_manager import track_claim_application, flush_unexecuted_claim_applications

logger = logging.getLogger(__name__)


class GuangdongTacticalMixin:
    def initialize_tactical_calls(self, room):
        # Missing legacy settings stay off; the room validator defaults NEW rooms on.
        self.tactical_call = bool(room.get("tactical_call", False)) and not has_bot_players(self.player_list)
        self.tactical_commit_lock = False  # MIL permits raising a declaration after a scene change.
        self.tactical_grace_seconds = tactical.TACTICAL_GRACE_SECONDS
        self._guangdong_tactical_recheck = False
        self._tactical_silent_action = False
        # Hu action names already encode distance from the discarder; all outrank
        # equally ranked pung/kong, and the closest winner interrupts farther hu.
        self.action_priority = {**self.action_priority, 'hu_first': 6, 'hu_second': 5,
                                'hu_third': 4, 'force_pass': 0}
        tactical.clear_tactical_round_state(self)

    def on_action_window_broadcast(self):
        if not self._guangdong_tactical_recheck:
            if self.game_status in ('waiting_action_after_cut', 'waiting_action_qianggang'):
                tactical.add_tactical_force_pass_options(self, self.action_dict)
                tactical.init_tactical_round_state(self)
            else:
                tactical.clear_tactical_round_state(self)
        super().on_action_window_broadcast()

    def claim_clock_fields(self, player, reconnecting=False):
        fields = super().claim_clock_fields(player, reconnecting=reconnecting)
        # Shared reconnect uses the same hook. Ordinary initial broadcast passes
        # its existing None flag explicitly, so omit this key outside a recheck.
        if self._guangdong_tactical_recheck and reconnecting:
            fields['is_tactical_recheck'] = True
        return fields

    def tactical_action_receipt_valid(self, index, item):
        if not isinstance(item, dict):
            return False
        if item.get('action_type') == 'force_pass':
            return tactical.tactical_force_pass_is_live(self, index, item.get('_action_tick'))
        window = self._action_windows.get(index)
        return window is not None and self._valid_receipt(window, item)

    @staticmethod
    async def broadcast_tactical_recheck(state, *, remaining_time_override, is_tactical_recheck):
        state._guangdong_tactical_recheck = True
        # Shared phase already drains pre-submitted claims before this new tick.
        state.prepare_action_window()
        await boardcast.broadcast_ask_other_action(state, remaining_time_override=remaining_time_override,
                                                 is_tactical_recheck=is_tactical_recheck)

    @staticmethod
    async def _broadcast_claim(state, *, action_list, action_player, cut_tile, is_claim):
        # Application is cosmetic: no tile mutation, no draw-slot reset and no
        # executable replay tick. The existing replay tracker records losers as ca.
        state.server_action_tick += 1
        await state.prepare_private_fields(range(4))
        track_claim_application(state, action_player, action_list[0], cut_tile)
        for index in range(4):
            payload = boardcast._build_do_action_payload(state, action_list, action_player, index, cut_tile=cut_tile)
            payload['is_claim'] = True
            response = Response(type='gamestate/guangdong/do_action', success=True, message='返回操作内容',
                                do_action_info=Do_action_info(**payload))
            player = state.player_list[index]
            connection = state.game_server.user_id_to_connection.get(player.user_id)
            if connection is not None and 'offline' not in player.tag_list:
                try:
                    await connection.websocket.send_json(response.model_dump(exclude_none=True))
                except Exception:
                    logger.warning('广东申请广播失败 player_index=%s', index, exc_info=True)
            # A disconnected player must not swallow their authorized spectators.
            try:
                await state.send_to_realtime_spectators(index, response)
            except Exception:
                logger.warning('广东申请观战广播失败 player_index=%s', index, exc_info=True)

    async def _resolve_tactical(self, responses, allowed, *, rob_kong):
        submitted = {i: data.get('action_type', 'pass') for i, data in responses.items()}
        for i, action in submitted.items():
            if action == 'force_pass':
                tactical.tactical_mark_player_force_passed(self, i)
        candidates = [(self.action_priority.get(data.get('action_type'), 0), -((i - self.current_player_index) % 4), i, data)
                      for i, data in responses.items() if data.get('action_type') in allowed.get(i, [])
                      and not tactical.is_decline_action(data.get('action_type', 'pass'))]
        resolver = super().resolve_rob_kong_responses if rob_kong else super().resolve_discard_responses
        if not self.tactical_call or not candidates:
            tactical.clear_tactical_round_state(self)
            return await resolver(responses, allowed)
        _, _, index, data = max(candidates, key=lambda item: item[:2])
        # Direct resolver callers have no broadcast; freeze their validated options.
        if tactical.tactical_opening_snapshot(self) is None:
            self.action_dict = {i: list(allowed.get(i, [])) for i in range(4)}
            tactical.add_tactical_force_pass_options(self, self.action_dict)
            tactical.init_tactical_round_state(self)
        self._tactical_silent_action = False
        tile = self.jiagang_tile if rob_kong else self.player_list[self.current_player_index].discard_tiles[-1]
        try:
            action, index, final_data, claimed = await tactical.apply_tactical_claim_if_needed(
                self, data['action_type'], index, data,
                broadcast_do_action=self._broadcast_claim,
                broadcast_ask_other_action=self.broadcast_tactical_recheck,
                submitted_actions=submitted)
            flush_unexecuted_claim_applications(self, tile, executed_player=index, executed_action_type=action)
            final = {i: {'action_type': 'pass' if not tactical.is_decline_action(a) else a} for i, a in submitted.items()}
            final[index] = {**final_data, 'action_type': action}
            for i, window in self._action_windows.items():
                if not window.closed:
                    self._complete_window(i, min(time.monotonic(), window.deadline))
            await resolver(final, allowed)
        finally:
            self._guangdong_tactical_recheck = False
            tactical.clear_tactical_round_state(self)
            if not self.pending_winners:
                self._tactical_silent_action = False

    async def resolve_discard_responses(self, responses, allowed):
        return await self._resolve_tactical(responses, allowed, rob_kong=False)

    async def resolve_rob_kong_responses(self, responses, allowed):
        return await self._resolve_tactical(responses, allowed, rob_kong=True)

    def build_private_do_action_info(self, action_player, viewer_index):
        fields = super().build_private_do_action_info(action_player, viewer_index)
        if self._tactical_silent_action:
            fields['silent'] = True
        return fields

    def _reset_hand_runtime(self):
        super()._reset_hand_runtime()
        self._guangdong_tactical_recheck = False
        self._tactical_silent_action = False
        tactical.clear_tactical_round_state(self)
