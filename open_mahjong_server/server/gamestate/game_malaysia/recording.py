from ..public import game_record_manager as R
from .phases import P


class Recording:
    def start_game_recording(self):
        R.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule, rule_version=self.rule_version,
            detailed_config=dict(self.detailed_config), player_count=3, joker_tiles=[81], hepai_limit=5)
        self.spectator_manager.record_game_title()
        if self.spectator_manager.enabled:
            self.spectator_manager.game_title.update(sub_rule=self.sub_rule, rule_version=self.rule_version, player_count=3, joker_tiles=[81])

    def build_record_round_fields(self):
        return dict(malaysia=self.public_info())

    def record_visible_action(self, event):
        if not self._has_active_round_record():
            return
        a, actor, tile = event["action"], event["player"], event.get("tile")
        if a == "cut":
            R.append_action_tick(self, ["reset", actor])
            R.player_action_record_cut(self, tile, bool(event.get("cutClass")))
        elif a == "buhua":
            R.player_action_record_buhua(self, tile, actor, event.get("is_mo_buhua", False))
        elif a == "deal_buhua_tile":
            R.player_action_record_deal(self, tile, "bd", actor)
        elif a == "malaysia_payment":
            R.append_action_tick(self, ["malaysia", "payment", event["score_changes"], list(self.ledger)])
        elif a == "malaysia_redeem":
            R.append_action_tick(self, ["malaysia", "redeem", actor, tile, event["meld_index"], event["redeemed_slot"]])
        elif a == "rob_kan":
            # The following win_source record consumes this uncommitted tile once.
            return
        elif a.startswith("hu"):
            entry = self.deferred_hu_settlements[0]
            if entry.get("recorded"):
                return
            payer = self._settlement_payer_index(entry)
            R.append_action_tick(self, ["malaysia", "win_source", actor, payer if payer is not None else -1, entry["source"], tile, entry["automatic"]])
            hu_class = "hu_self" if entry["source"] == "self_draw" else {1:"hu_first", 2:"hu_second"}[(actor - payer) % 3]
            R.player_action_record_hu(self, hu_class, entry["points"], entry["fan_ids"], actor,
                entry["score_changes"], hepai_tile=tile, ron_discarder_index=payer, recycle_discard=entry["recycle_discard"])
            entry["recorded"] = True
            p = self.player_list[actor]
            p.record_counter.win_score += entry["points"]
            p.record_counter.win_turn += len(p.discard_origin_tiles) + 1
            if entry["source"] == "self_draw":
                p.record_counter.zimo_times += 1
            else:
                p.record_counter.dianhe_times += 1
                self.player_list[payer].record_counter.fangchong_times += 1
                self.player_list[payer].record_counter.fangchong_score += entry["points"]
        else:
            super().record_visible_action(event)

    def finalize_round_recording(self):
        if not self._has_active_round_record() or self.round_record_finalized:
            return
        self.apply_deferred_score_changes()
        for p in self.player_list:
            p.record_counter.fulu_times += int(any(not m.concealed for m in p.melds))
        if not self.deferred_hu_settlements:
            R.player_action_record_liuju(self)
        R.append_action_tick(self, ["malaysia", "state", self.public_info(), [p.score for p in self.player_list]])
        R.player_action_record_round_end(self)
        self.round_record_finalized = True

    def persist_game_record(self):
        if self.db_manager is None or not self.game_record:
            return None
        if hasattr(self, "_persisted_game_id"):
            return self._persisted_game_id
        game_id = self.db_manager.store_jiandan_game_record(self.game_record, self.player_list, self.room_type, f"{self.max_round}/3")
        if game_id:
            self._persisted_game_id = game_id
        return game_id
