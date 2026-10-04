"""Physical replay plus versioned public state and per-seat hint snapshots."""

from ..public import game_record_manager as record


class Recording:
    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule, rule_version=self.rule_version,
                                             detailed_config=dict(self.detailed_config), joker_tiles=[46])
        self.spectator_manager.record_game_title()
        # The shared delayed spectator header does not call this override.
        if self.spectator_manager.enabled:
            self.spectator_manager.game_title.update(sub_rule=self.sub_rule, rule_version=self.rule_version,
                                                     detailed_config=dict(self.detailed_config), joker_tiles=[46])

    def build_record_round_fields(self):
        return dict(hangzhou=dict(self.hangzhou_info(), start_scores=list(self.round_start_scores)))

    def record_state(self):
        if self._has_active_round_record():
            # Replay already applied a hu tick even while live score animation
            # is deferred. A later absolute snapshot must not undo that tick.
            won = any(entry.get("recorded") for entry in self.deferred_hu_settlements)
            scores = ([start + change for start, change in zip(self.round_start_scores, self.round_changes)]
                      if won else [p.score for p in self.player_list])
            record.append_action_tick(self, ["hangzhou", "state", self.hangzhou_info(), scores])

    def record_hints(self, index, hints):
        if not hints or not self._has_active_round_record():
            return
        key = (tuple(hints["source_hand_tiles"]), tuple(hints["source_melds"]),
               tuple((tile, tuple(waits)) for tile, waits in hints["waiting_by_discard"].items()),
               tuple(hints["waiting_tiles"]))
        if self._recorded_hint_keys.get(index) != key:
            self._recorded_hint_keys[index] = key
            record.append_action_tick(self, ["hangzhou", "hints", index, hints])

    def record_visible_action(self, event):
        if not self._has_active_round_record():
            return
        action, actor, tile = event["action"], event["player"], event.get("tile")
        if action == "cut":
            record.append_action_tick(self, ["reset", actor])
            record.player_action_record_cut(self, tile, bool(event.get("cutClass")))
        elif action == "hangzhou_tail_burn":
            record.append_action_tick(self, ["hangzhou", "tail_burn", actor, tile])
        elif action == "hu_self":
            entry = self._latest_hu_settlement_for(actor)
            if entry.get("recorded"):
                return
            record.append_action_tick(self, ["hangzhou", "win_source", actor, entry["source"],
                                            self.round_settlement["tile"], self.hangzhou_info()])
            record.player_action_record_hu(self, "hu_self", entry["points"], entry["fan_ids"], actor,
                entry["score_changes"], hepai_tile=entry["tile"], multi_ron=False,
                ron_discarder_index=-1, recycle_discard=False)
            entry["recorded"] = True
            p = self.player_list[actor]
            p.record_counter.recorded_fans.append(entry["fan_ids"])
            p.record_counter.win_score += entry["points"]
            p.record_counter.win_turn += len(p.discard_origin_tiles) + (entry["source"] != "ten_winds")
            p.record_counter.zimo_times += 1
        else:
            super().record_visible_action(event)

    def finalize_round_recording(self):
        if not self._has_active_round_record():
            return
        self.apply_deferred_score_changes()
        for p in self.player_list:
            if any(code[0] in "skg" for code in p.combination_tiles):
                p.record_counter.fulu_times += 1
        if not self.deferred_hu_settlements:
            record.player_action_record_liuju(self)
        self.record_state()
        record.player_action_record_round_end(self)
        self.round_record_finalized = True
