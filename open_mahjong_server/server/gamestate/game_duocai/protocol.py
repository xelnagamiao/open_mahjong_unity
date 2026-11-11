from ..public.settlement_sync import send_game_packet
import math
from dataclasses import asdict

from ..game_zhongyong import boardcast as wire
from ...game_calculation.duocai import tiles as t
from .actions import KINDS, legal_cuts, riichi_cuts
from .client_context import private_context
from .state_machine import Phase as P


class Protocol:
    def duocai_info(self, viewer=None):
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        result = dict(rule_version=self.rule_version, variant=self.variant, phase=self.game_status, base_hand_tiles=13,
            hand_number=self.round_index, start_scores=list(self.round_start_scores),
            seat_to_original=[p.original_player_index for p in self.player_list],
            honba=self.honba, riichi_sticks=self.riichi_sticks, match_finishing=self.match_finishing,
            ledger=self.round_settlement if ended else None)
        if self.wall:
            ura = ended and any(e.get('fan_details', {}).get('ura_revealed') for e in self.deferred_hu_settlements)
            result.update(wall=self.wall.public(ura=ura), deck=self.wall.deck, opening=self.wall.opening,
                dice=list(self.wall.dice), indicators=self.wall.indicators(), ura_indicators=self.wall.indicators(True) if ura else [],
                reserves=[len(v) for v in self.decks.reserves], retired=[list(v) for v in self.decks.retired])
        if viewer in range(4):
            result['self_has_draw_slot'] = self.player_list[viewer].has_draw_slot
        return result

    def _player_view(self, index, viewer):
        p = self.player_list[index]
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        result = wire.player_info_payload(self, index, viewer, reveal_final=ended)
        result.update(remaining_time=math.ceil(max(0.0, p.remaining_time)),
            hand_tiles=list(p.hand_tiles) if index == viewer or ended else None,
            hand_tiles_count=len(p.hand_tiles), discard_riichi_flags=list(p.discard_riichi_flags),
            duocai_hand_backs=[(self.wall.deck + int(t.unusual_back(v))) % 2 for v in p.hand_tiles])
        melds, masks, codes, backs = [], [], [], []
        for i, m in enumerate(p.melds):
            hide = m.concealed and self.variant == 'guobiao' and not ended and index != viewer
            melds.append(dict(physical=[0] * len(m.physical) if hide else list(m.physical),
                              concealed=m.concealed, declared=m.declared, source=m.source))
            masks.append([0 if hide and n % 2 else v for n, v in enumerate(p.combination_mask[i])])
            codes.append('G0' if hide else p.combination_tiles[i])
            backs.append([(self.wall.deck + int(t.unusual_back(v))) % 2 for v in p.combination_mask[i][1::2]])
        result.update(combination_tiles=codes, combination_mask=masks, duocai_melds=melds,
                      duocai_meld_backs=backs, duocai_riichi=p.riichi)
        return result

    def _adapt(self, payload, viewer):
        payload.setdefault('message', '')
        payload['player_index'] = viewer
        info = payload.get('game_info')
        if info is not None:
            info.update(detailed_config=dict(self.detailed_config), duocai_info=self.duocai_info(viewer),
                        hepai_limit=self.hepai_limit, players_info=[self._player_view(i, viewer) for i in range(4)])
            if viewer in range(4):
                info['self_hand_tiles'] = list(self.player_list[viewer].hand_tiles)
                info['duocai_hints'] = private_context(self, viewer)
        return payload

    def build_game_start_payload(self, index):
        return self._adapt(wire.game_start_payload(self, index), index)

    def _ask_payload(self, index, window):
        actor = window.get('player') if window.get('player') is not None else self.current_player_index
        payload = wire.ask_action_payload(self, index, self.action_dict.get(index, []), action_player_index=actor,
            cut_tile=window.get('tile') if window['status'] == P.RESPONSE.value else None,
            rob_kong_tile=window.get('tile') if window['status'] == P.ROB_KONG.value else None)
        info = payload.get('ask_hand_action_info') or payload.get('ask_other_action_info')
        if info is not None:
            info['duocai_choices'] = [dict(c.public(), action=KINDS.get(c.kind, c.kind)) for c in self.meld_choices.get(index, ())]
            if index == actor and payload.get('ask_hand_action_info'):
                p = self.player_list[index]
                info['deal_tile_type'] = ('deal_gang_tile' if p.after_kong else 'deal_tile') if p.has_draw_slot else None
                info['forbidden_cut_tiles'] = sorted(set(p.hand_tiles) - set(legal_cuts(self, index)))
                info['riichi_cut_tiles'] = list(riichi_cuts(tuple(p.hand_tiles), tuple(p.melds))) if 'riichi_cut' in self.action_dict[index] else []
        return self._adapt(payload, index)

    def emit_window_payloads(self, window):
        if window['status'] == P.END.value:
            self._final_result_pending = True
            return []
        if self._has_active_round_record():
            from ..public.game_record_manager import append_action_tick
            snapshots = self.__dict__.setdefault('_record_hints', {})
            for i in range(4):
                current = private_context(self, i, for_record=True)
                if current != snapshots.get(i):
                    append_action_tick(self, ['duocai', 'waits', i, current])
                    snapshots[i] = current
        payloads = [self._ask_payload(i, window) for i in range(4)
                    if window['status'] not in (P.RESPONSE.value, P.ROB_KONG.value) or self.action_dict.get(i)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_pending_action_payload(self, index):
        if self.machine.phase in (P.END, P.READY, P.FINISHED) or not self.live_pending_window:
            return None
        return self._ask_payload(index, self.live_pending_window)

    def emit_visible_action_payloads(self, event, *, reveal_final=False):
        self.record_visible_action(event)
        if event['action'].startswith('hu'):
            return []
        payloads = []
        for viewer in range(4):
            payload = wire.visible_action_payload(self, viewer, event)
            info = payload['do_action_info']
            info.update(duocai_meld_index=event.get('meld_index'), is_riichi_horizontal=event.get('is_riichi_horizontal', False),
                        duocai_wall_position=event.get('wall_position'))
            if event['action'] in ('angang', 'jiagang', 'rob_kan'):
                info['cut_tile'] = event['tile']
            actor = event['player']
            if event.get('meld_index') is not None:
                mi = event['meld_index']
                p = self.player_list[actor]
                hidden = p.melds[mi].concealed and self.variant == 'guobiao' and viewer != actor
                view = self._player_view(actor, viewer)
                info['combination_mask'] = view['combination_mask'][mi]
                info['combination_target'] = view['combination_tiles'][mi]
                payload['meld_code'] = view['combination_tiles'][mi]
                if hidden:
                    payload['tile'] = info['cut_tile'] = None
            payloads.append(self._adapt(payload, viewer))
        self.outbound_payloads.extend(payloads)
        return payloads

    def build_final_settlement_payload(self, index):
        self.apply_deferred_score_changes()
        info = wire._final_show_result_info(self)
        info.update(next_status='match_end' if self.match_finishing else 'round_end_by_ready',
            duocai_info=self.duocai_info(index), score_changes={p.original_player_index:self.round_changes[p.player_index] for p in self.player_list},
            player_to_score={p.player_index:p.score for p in self.player_list},
            duocai_end_hands={p.player_index:list(p.hand_tiles) for p in self.player_list})
        return self._adapt(dict(type='gamestate/duocai/show_result', success=True,
            game_info=wire.game_info_payload(self, index, reveal_final=True), show_result_info=info), index)

    async def present_final_settlements(self):
        for i in range(4):
            await self._send_claim_protection_payload(i, self.build_final_settlement_payload(i))

    def build_ready_status_payload(self, index):
        result = wire.ready_status_payload(self, index)
        result['ready_status_info'].update(action_tick=self.server_action_tick, duocai_info=self.duocai_info(index))
        return self._adapt(result, index)

    def emit_ready_status_payloads(self):
        payloads = [self.build_ready_status_payload(i) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    def restore_payloads(self, index):
        ended = self.machine.phase in (P.END, P.READY, P.FINISHED)
        if ended:
            self.apply_deferred_score_changes()
        payloads = [self.build_game_start_payload(index)]
        if ended:
            payloads.append(self.build_final_settlement_payload(index))
            if self.machine.phase == P.READY:
                payloads.append(self.build_ready_status_payload(index))
        else:
            pending = self.build_pending_action_payload(index)
            if pending:
                payloads.append(pending)
        return payloads

    def emit_game_end_payloads(self):
        self.finalize_match_result()
        payloads = [wire.game_end_payload(self, i) for i in range(4)]
        self.outbound_payloads.extend(payloads)
        return payloads

    async def player_reconnect(self, user_id):
        for index, p in enumerate(self.player_list):
            if p.user_id == user_id:
                from ..public.outbound_pipe import drain_viewer
                await drain_viewer(self, index)
                if 'offline' in p.tag_list:
                    p.tag_list.remove('offline')
                for payload in self.restore_payloads(index):
                    await self.send_payload_to_player(index, payload)
                return

    async def send_realtime_spectator_snapshot(self, user_id, index):
        if index not in range(4) or self.game_server is None:
            return
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and connection.websocket is not None:
            for payload in self.restore_payloads(index):
                await send_game_packet(self, connection.websocket, payload)
