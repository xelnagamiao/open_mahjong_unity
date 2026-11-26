import math
from ..game_zhongyong import broadcast as wire
from ..game_zhongyong.recording import RoundRecording
from ..game_new_chinese.protocol import Protocol as WindowProtocol
from ..game_new_chinese.state_machine import Phase as P
from ..game_beijing.actions import CHOWS, claim_tiles
from ..public import game_record_manager as record
from ...game_calculation.sanhua.rules import VALUES


class Protocol(WindowProtocol):
    def view(self, viewer=None):
        table = self.table()
        return dict(indicator=self.indicator, skin=self.skin, joker=self.joker, dice=list(self.dice),
                    wall_front_draws=self.wall_front_draws, wall_back_draws=self.wall_back_draws,
                    table_fans=[sum(VALUES[n] for n in row) for row in table],
                    opened=["开口番" in row for row in table],
                    declared=[p.baoqing for p in self.player_list],
                    liable=[p.liable if p.liable is not None else -1 for p in self.player_list],
                    after_kong=[p.after_kong for p in self.player_list],
                    self_has_draw_slot=viewer in range(4) and self.player_list[viewer].has_draw_slot,
                    phase=self.game_status, payer=self.current_payer(),
                    settlement=self.deferred_hu_settlements[-1]["fan_details"] if self.deferred_hu_settlements else None)

    def adapt(self, payload, viewer):
        payload.setdefault("message", "")
        payload["player_index"] = viewer
        info = payload.get("game_info")
        if info:
            info.update(sanhua=self.view(viewer), hepai_limit=4, detailed_config=self.detailed_config)
            for fields, p in zip(info["players_info"], self.player_list):
                fields.update(huapai_list=list(p.specials), remaining_time=math.ceil(max(0, p.remaining_time)))
        return payload

    def _ask(self, viewer, window):
        phase, actor, tile = window["status"], window["player"], window["tile"]
        options = self.action_dict.get(viewer, [])
        payload = wire.ask_action_payload(self, viewer, options, action_player_index=actor,
            cut_tile=tile if phase == P.RESPONSE else None, rob_kong_tile=tile if phase == P.ROB_KONG else None)
        hand = payload.get("ask_hand_action_info")
        if hand is not None and viewer == actor:
            hand["kong_candidates"] = {k: sorted(self.kong_tiles(viewer, k)) for k in ("angang", "jiagang")}
        claim = payload.get("ask_other_action_info")
        if claim is not None:
            claim["chi_candidates"] = {a: [claim_tiles(a, tile)] for a in options if a in CHOWS}
        return self.adapt(payload, viewer)

    def emit_visible_action_payloads(self, event, **kwargs):
        packets = super().emit_visible_action_payloads(event, **kwargs)
        if event["action"] == "buhua":
            for packet in packets:
                packet["do_action_info"].update(buhua_tile=event["tile"], is_mo_buhua=event["is_mo_gang"])
        return packets

    def build_final_settlement_payload(self, index):
        packet = super().build_final_settlement_payload(index)
        info = packet["show_result_info"]
        if info["hepai_player_index"] in range(4):
            info["hepai_player_huapai"] = list(self.player_list[info["hepai_player_index"]].specials)
        return packet

    def build_record_round_fields(self):
        return dict(sanhua=self.view())

    def spectator_record_tick(self, tick):
        return tick

    def record_visible_action(self, event):
        if not self._has_active_round_record():
            return
        kind, seat = event["action"], event["player"]
        if kind == "buhua":
            record.player_action_record_buhua(self, event["tile"], seat, event["is_mo_gang"])
        elif kind == "sanhua_rob":
            record.append_action_tick(self, ["sanhua", "rob", seat, event["tile"]])
        else:
            if kind == "cut":
                record.append_action_tick(self, ["reset", seat])
            RoundRecording.record_visible_action(self, event)
        if kind in ("buhua", "baoqing", "peng", "gang", "angang", "jiagang", *CHOWS):
            self.record_state()

    def open_action_window(self, window):
        result = super().open_action_window(window)
        self.record_state()
        return result

    def record_state(self):
        if self._has_active_round_record():
            record.append_action_tick(self, ["sanhua", "state", self.view()])
