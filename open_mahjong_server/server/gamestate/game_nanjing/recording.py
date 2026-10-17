"""Replay extensions carry source ownership, flower order, and rule version."""

from ..public import game_record_manager as record


class Recording:
    def _update_record_counter_for_settlement(self, settlement, fan_ids):
        winner_index = settlement.get("winner")
        if winner_index not in range(len(self.player_list)):
            return
        super()._update_record_counter_for_settlement(settlement, fan_ids)
        winner = self.player_list[winner_index]
        winner.record_counter.win_turn += len(winner.discard_origin_tiles) - len(winner.discard_tiles)
        payer_index = self._settlement_payer_index(settlement)
        if settlement.get("source") != "self_draw" and payer_index in range(len(self.player_list)):
            paid = max(0, -settlement["score_changes"][payer_index])
            self.player_list[payer_index].record_counter.fangchong_score += paid - int(settlement.get("points", 0) or 0)

    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule,rule_version=self.rule_version,
                                             detailed_config=dict(self.detailed_config))
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        return dict(nanjing=dict(self.nanjing_info(),start_scores=list(self.round_start_scores)))

    def record_visible_action(self,event):
        if not self._has_active_round_record():
            return
        action,actor,tile = event["action"],event["player"],event.get("tile")
        if action == "cut":
            record.append_action_tick(self,["reset",actor])
            record.player_action_record_cut(self,tile,bool(event.get("cutClass")))
        elif action == "buhua":
            record.player_action_record_buhua(self,tile,actor,event.get("is_mo_buhua",False))
        elif action == "deal_buhua_tile":
            record.player_action_record_deal(self,tile,"bd",actor)
        elif action == "nanjing_score":
            record.append_action_tick(self,["nanjing","score",event["ledger"],list(self.round_changes),"gs",*event["ledger"]["changes"]])
        else:
            if action in ("angang", "jiagang"):
                record.append_action_tick(self,["reset",actor])
            if action.startswith("hu"):
                entry = self._latest_hu_settlement_for(actor)
                if entry and not entry.get("recorded"):
                    payer = self._settlement_payer_index(entry)
                    record.append_action_tick(self,["nanjing","win_source",actor,
                        payer if payer is not None else -1,entry["source"],tile,self.nanjing_info(),entry.get("forced",False),entry.get("recycle_discard",False)])
            super().record_visible_action(event)

    def finalize_round_recording(self):
        if not self._has_active_round_record():
            return
        self.apply_deferred_score_changes()
        for p in self.player_list:
            if any(c[0] in "skg" for c in p.combination_tiles):
                p.record_counter.fulu_times += 1
        if not self.deferred_hu_settlements:
            record.player_action_record_liuju(self)
        record.append_action_tick(self,["nanjing","state",self.nanjing_info(),[p.score for p in self.player_list]])
        record.player_action_record_round_end(self)
        self.round_record_finalized = True

    def record_hints(self):
        if not self._has_active_round_record():
            return
        from .client_context import local_context
        record.append_action_tick(self,["nanjing","hints",[local_context(self,i) for i in range(4)]])
