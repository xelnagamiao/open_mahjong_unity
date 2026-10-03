from ..public import game_record_manager as record
from ...game_calculation.guizhou.rules import RULE_VERSION


class Recording:
    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule, rule_version=RULE_VERSION,
                                              detailed_config=dict(self.detailed_config))
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        return dict(guizhou=dict(self.guizhou_info(), start_scores=list(self.round_start_scores)))

    def record_visible_action(self, event):
        if not self._has_active_round_record():
            return
        action, actor = event["action"], event["player"]
        if action == "riichi":
            record.append_action_tick(self, ["guizhou", "ready", actor, event["ready_qualification"]])
            return
        if action == "guizhou_reveal_kongs":
            record.append_action_tick(self, ["guizhou", "reveal_kongs", [list(p.combination_mask) for p in self.player_list]])
            return
        if action == "cut":
            record.append_action_tick(self, ["reset", actor])
            record.player_action_record_cut(self, event["tile"], bool(event.get("cutClass")), bool(event.get("riichi_discard")))
            return
        if action.startswith("hu"):
            entry = self._latest_hu_settlement_for(actor)
            if not entry or entry.get("recorded"):
                return
            record.append_action_tick(self, ["guizhou", "win_source", actor,
                self._settlement_payer_index(entry), entry["source"], entry["tile"],
                entry["recycle_discard"], self.guizhou_info()])
        super().record_visible_action(event)
        if action in ("gang", "angang", "jiagang", "peng"):
            record.append_action_tick(self, ["guizhou", "ledger", self.guizhou_info(actor)])

    def finalize_round_recording(self):
        if not self._has_active_round_record():
            return
        self.apply_deferred_score_changes()
        for p in self.player_list:
            if any(c[0] in "kg" for c in p.combination_tiles):
                p.record_counter.fulu_times += 1
        if not self.deferred_hu_settlements:
            # Draw settlements have no hu_* tick to carry their score deltas.
            record.append_action_tick(self, ["guizhou", "draw_score", self.round_settlement.changes,
                                            self.guizhou_info()])
            record.player_action_record_liuju(self)
        record.append_action_tick(self, ["guizhou", "state", self.guizhou_info(), [p.score for p in self.player_list],
                                         [list(p.hand_tiles) for p in self.player_list]])
        record.player_action_record_round_end(self)
        self.round_record_finalized = True
