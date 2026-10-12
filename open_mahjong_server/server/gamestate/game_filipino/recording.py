from ..public import game_record_manager as record


class Recording:
    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule, rule_version=self.rule_version,
                                             detailed_config=dict(self.detailed_config))
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        return dict(filipino=self.filipino_info(), start_scores=list(self.round_start_scores))

    def record_visible_action(self, event):
        if not self._has_active_round_record():
            return
        action, actor, tile = event["action"], event["player"], event.get("tile")
        if action.startswith('hu'):
            win = self._latest_hu_settlement_for(actor)
            if not win.get('recorded'):
                record.append_action_tick(self, ['filipino', 'win_source', actor,
                    win.get('discarder') if win.get('discarder') is not None else -1, win['source'], tile])
        if action == "buhua":
            record.player_action_record_buhua(self, tile, actor, event.get("is_mo_buhua", False))
        elif action == "deal_buhua_tile":
            record.player_action_record_deal(self, tile, "bd", actor)
        elif action == "filipino_score":
            record.append_action_tick(self, ["filipino", "score", actor, event["reason"], event["score_changes"]])
        else:
            if action in ("cut", "angang", "jiagang"):
                record.append_action_tick(self, ["reset", actor])
            super().record_visible_action(event)

    def _update_record_counter_for_settlement(self, entry, fan_ids):
        winner = self.player_list[entry["winner"]]
        counter = winner.record_counter
        counter.win_turn += len(winner.discard_origin_tiles) + 1
        counter.win_score += entry["points"]
        if entry["source"] == "self_draw":
            counter.zimo_times += 1
        else:
            counter.dianhe_times += 1
            payer = self.player_list[entry["discarder"]]
            payer.record_counter.fangchong_times += 1
            payer.record_counter.fangchong_score += max(0, -entry["score_changes"][entry["discarder"]])
        # Basic metrics only: deliberately do not fill recorded_fans.

    def finalize_round_recording(self):
        if not self._has_active_round_record():
            return
        self.apply_deferred_score_changes()
        for p in self.player_list:
            if any(c[0] in "skg" for c in p.combination_tiles):
                p.record_counter.fulu_times += 1
        if not self.deferred_hu_settlements:
            record.player_action_record_liuju(self)
        record.append_action_tick(self, ["filipino", "state", self.filipino_info(), [p.score for p in self.player_list]])
        record.player_action_record_round_end(self)
        self.round_record_finalized = True

    def persist_game_record(self):
        if self.db_manager is None or not self.game_record:
            return None
        from ...database.taiwan.store_taiwan import store_taiwan_game_record
        return store_taiwan_game_record(self.db_manager, self.game_record, self.player_list,
                                       self.room_type, f"{self.max_round}/4")
