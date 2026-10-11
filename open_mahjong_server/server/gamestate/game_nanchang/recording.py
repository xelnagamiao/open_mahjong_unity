from ..public import game_record_manager as record
from .hints import private_hints
from ...game_calculation.nanchang import rules as book


class Recording:
    def _update_record_counter_for_settlement(self, settlement, fan_ids):
        winner_index = settlement.get("winner")
        if winner_index not in range(len(self.player_list)):
            return
        winner = self.player_list[winner_index]
        previous_turns = winner.record_counter.win_turn
        super()._update_record_counter_for_settlement(settlement, fan_ids)
        # Meld claims remove tiles from the visible river, never from the
        # complete discard history used for this player's winning turn.
        winner.record_counter.win_turn = previous_turns + len(winner.discard_origin_tiles) + 1

    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule,rule_version=self.rule_version,
                                            detailed_config=dict(self.detailed_config))
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        self._record_hints = {}
        return dict(nanchang=dict(self.nanchang_info(),start_scores=list(self.round_start_scores)),
                    nanchang_hints={i:private_hints(self,i,for_record=True) for i in range(4)})

    def record_visible_action(self,event):
        if not self._has_active_round_record():
            return
        action,index,tile = event["action"],event["player"],event.get("tile")
        if action == "cut":
            record.append_action_tick(self,["reset",index])
            record.player_action_record_cut(self,tile,bool(event.get("cutClass")),tile in book.jing_tiles(self.indicator))
        elif action == "nc_jingdiao":
            record.append_action_tick(self,["nanchang","jingdiao",index])
        else:
            if action.startswith("hu"):
                entry = self._latest_hu_settlement_for(index)
                if entry and not entry.get("recorded"):
                    payer = self._settlement_payer_index(entry)
                    record.append_action_tick(self,["nanchang","win_source",index,
                        payer if payer is not None else -1,entry["source"],tile,self.nanchang_info()])
            super().record_visible_action(event)
        if action in ("chi_left","chi_mid","chi_right"):
            record.append_action_tick(self,["nanchang","meld",index,event["combination_mask"],event["meld_code"]])

    def finalize_round_recording(self):
        if not self._has_active_round_record():
            return
        self.apply_deferred_score_changes()
        for p in self.player_list:
            if any(c[0] in "skg" for c in p.combination_tiles):
                p.record_counter.fulu_times += 1
        if not self.deferred_hu_settlements:
            record.player_action_record_liuju(self)
        extras = [self.round_changes[i] - sum(h["score_changes"][i] for h in self.deferred_hu_settlements) for i in range(4)]
        record.append_action_tick(self,["nanchang","extras","gs",*extras])
        record.append_action_tick(self,["nanchang","state",self.nanchang_info(),[p.score for p in self.player_list]])
        record.player_action_record_round_end(self)
        self.round_record_finalized = True

    def persist_game_record(self):
        self.finalize_match_result()
        if self.db_manager is None or not self.game_record:
            return None
        return self.db_manager.store_nanchang_game_record(self.game_record,self.player_list,self.room_type,f"{self.max_round}/4")

    def finalize_match_result(self):
        results = book.match_results([p.score for p in self.player_list])
        for p,result in zip(self.player_list,results):
            p.record_counter.rank_result = result["rank"]
        if self.game_record.get("game_title") is not None:
            self.game_record["game_title"]["nanchang_final_result"] = {
                p.original_player_index:result for p,result in zip(self.player_list,results)}
        return results

    def finalize_game_recording(self):
        self.finalize_match_result()
        super().finalize_game_recording()
