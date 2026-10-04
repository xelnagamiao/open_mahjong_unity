"""HK extensions coexist with the shared replay events and spectator stream."""

from ..public import game_record_manager as record


class HongKongRecording:
    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule,rule_version=self.rules.version,
                                              starting_score=self.rules.starting_score,
                                              detailed_config=dict(self.detailed_config),hepai_limit=self.rules.minimum_fan)
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        return dict(hongkong=dict(self.hongkong_info(),start_scores=list(self.round_start_scores)))

    def record_opening_complete(self):
        if self._has_active_round_record():
            record.append_action_tick(self,["hongkong","opening_complete",
                [list(p.hand_tiles) for p in self.player_list],[p.has_draw_slot for p in self.player_list]])

    def record_visible_action(self,event):
        if not self._has_active_round_record():
            return
        action,actor,tile = event["action"],event["player"],event.get("tile")
        if action=="cut":
            # Initial tail replacements may leave the replay cursor at any seat.
            record.append_action_tick(self,["reset",actor])
            record.player_action_record_cut(self,tile,bool(event.get("cutClass")),
                                             bool(event.get("riichi_discard")))
            return
        if action.startswith("hu"):
            win = self._latest_hu_settlement_for(actor)
            if not win.get("recorded"):
                payer = self._settlement_payer_index(win)
                record.append_action_tick(self,["hongkong","win_source",actor,
                    payer if payer is not None else -1,win["source"],tile,
                    win.get("flower_win", ""),bool(win.get("recycle_discard")),self.hongkong_info()])
        if action in ("buhua","deal_buhua_tile","riichi","hongkong_score"):
            if action=="buhua":
                record.player_action_record_buhua(self,tile,actor,event.get("is_mo_buhua",False))
            elif action=="deal_buhua_tile":
                record.player_action_record_deal(self,tile,"bd",actor)
            elif action=="riichi":
                # Keep declaration free of Japanese riichi stick semantics.
                record.append_action_tick(self,["hongkong","ready",actor,event.get("ready_qualification","")])
            else:
                record.append_action_tick(self,["hongkong","score",list(event["score_changes"]),event["reason"],self.ledger.snapshot()])
            return
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
        record.append_action_tick(self,["hongkong","state",self.hongkong_info(),[p.score for p in self.player_list]])
        record.player_action_record_round_end(self)
        self.round_record_finalized = True
