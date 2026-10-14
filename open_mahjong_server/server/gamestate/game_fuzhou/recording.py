from ..public import game_record_manager as record
from .hints import build_hints


class Recording:
    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule,rule_version=self.rule_version,
                                             detailed_config=dict(self.detailed_config))
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        return dict(fuzhou=self.fuzhou_info())

    def _update_record_counter_for_settlement(self,entry,fan_ids):
        p = self.player_list[entry["winner"]]
        p.record_counter.recorded_fans.append(list(fan_ids))
        p.record_counter.win_score += entry["points"]
        p.record_counter.win_turn += len(p.discard_origin_tiles)+1
        if entry["payment_source"]=="self_draw":
            p.record_counter.zimo_times += 1
        else:
            p.record_counter.dianhe_times += 1
            payer = self.player_list[entry["discarder"]]
            payer.record_counter.fangchong_times += 1
            payer.record_counter.fangchong_score += entry["points"]

    def record_visible_action(self,event):
        if not self._has_active_round_record():
            return
        action,actor,tile = event["action"],event["player"],event.get("tile")
        if action=="buhua":
            record.player_action_record_buhua(self,tile,actor,event.get("is_mo_buhua",False))
        elif action=="deal_buhua_tile":
            record.player_action_record_deal(self,tile,"bd",actor)
        elif action in ("kaijin","kaijin_flower","qiangjin_take","youjin","tingjin"):
            record.append_action_tick(self,["fuzhou",action,actor,tile,self.fuzhou_info()])
        else:
            # Sea draws have no intervening discard, and double-youjin draws
            # can remain with the same owner. The shared d/gd codec otherwise
            # assumes the ordinary clockwise successor.
            if action in ("cut","angang","jiagang","deal_tile","deal_gang_tile"):
                record.append_action_tick(self,["reset",actor])
            if action.startswith("hu"):
                entry = self._latest_hu_settlement_for(actor)
                if entry and not entry.get("recorded"):
                    record.append_action_tick(self,["fuzhou","win_source",actor,
                        self._settlement_payer_index(entry) if self._settlement_payer_index(entry) is not None else -1,
                        entry["source"],tile or 0,self.fuzhou_info(),
                        entry["forced"],entry["recycle_discard"],entry["fan_names"],entry["payment_source"]])
                    from ..game_zhongyong.boardcast import _settlement_hu_class
                    hu_class = "hu_self" if entry["payment_source"]=="self_draw" else _settlement_hu_class(entry)
                    record.player_action_record_hu(self,hu_class,entry["points"],entry["fan_names"],actor,
                        entry["score_changes"],hepai_tile=tile or 0,multi_ron=False,
                        ron_discarder_index=self._settlement_payer_index(entry),recycle_discard=entry["recycle_discard"])
                    self._update_record_counter_for_settlement(entry,entry["fan_ids"])
                    entry["recorded"] = True
                return
            super().record_visible_action(event)

    def record_hints(self):
        if self._has_active_round_record():
            record.append_action_tick(self,["fuzhou","hints",[build_hints(self,i) for i in range(4)]])

    def finalize_round_recording(self):
        if not self._has_active_round_record():
            return
        self.apply_deferred_score_changes()
        for p in self.player_list:
            if any(m[0] in "skg" for m in p.combination_tiles):
                p.record_counter.fulu_times += 1
        if not self.deferred_hu_settlements:
            record.player_action_record_liuju(self)
        record.append_action_tick(self,["fuzhou","state",self.fuzhou_info(),[p.score for p in self.player_list]])
        record.player_action_record_round_end(self)
        self.round_record_finalized = True
