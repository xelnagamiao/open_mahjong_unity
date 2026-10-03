"""Backward-compatible physical action ticks plus explicit Wenzhou metadata."""

import copy
import logging

from ..public import game_record_manager as record

logger = logging.getLogger(__name__)


class Recording:
    def start_game_recording(self):
        record.init_game_record(self)
        self.game_record["game_title"].update(sub_rule=self.sub_rule, rule_version=self.rule_version,
                                             detailed_config=dict(self.detailed_config))
        self.spectator_manager.record_game_title()

    def build_record_round_fields(self):
        return dict(wenzhou=dict(self.wenzhou_info(),start_scores=list(self.round_start_scores)),
                    wenzhou_waits={i:self.authoritative_waits(i) for i in range(4)})

    def record_waits(self):
        if not self._has_active_round_record() or not self.tips:
            return
        if not hasattr(self,"_recorded_waits"):
            self._recorded_waits = {}
        for i in range(4):
            value = self.authoritative_waits(i)
            if self._recorded_waits.get(i) != value:
                self._recorded_waits[i] = copy.deepcopy(value)
                record.append_action_tick(self,["wenzhou","waits",i,value])

    def record_visible_action(self,event):
        if not self._has_active_round_record():
            return
        action,actor,tile = event["action"],event["player"],event.get("tile")
        if action == "cut":
            record.append_action_tick(self,["reset",actor])
            record.player_action_record_cut(self,tile,bool(event.get("cutClass")))
        elif action == "wenzhou_kong_claim_source":
            record.append_action_tick(self,["wenzhou","kong_claim_source",actor,tile,event["is_mo_gang"]])
        elif action in ("angang","jiagang","peng","gang","chi_left","chi_mid","chi_right"):
            mask,code = event["combination_mask"],event["meld_code"]
            changes = event.get("gang_score_changes")
            if action == "angang":
                record.player_action_record_angang(self,int(code[1:]),event["is_mo_gang"],mask,changes)
            elif action == "jiagang":
                record.player_action_record_jiagang(self,int(code[1:]),event["is_mo_gang"],changes,actual_tile=tile)
            else:
                record.player_action_record_chipenggang(self,action,tile,actor,mask,changes)
            record.append_action_tick(self,["wenzhou","meld",actor,list(mask),code])
        else:
            if action.startswith("hu"):
                entry = self._latest_hu_settlement_for(actor)
                if entry and not entry.get("recorded"):
                    payer = self._settlement_payer_index(entry)
                    record.append_action_tick(self,["wenzhou","win_source",actor,
                        payer if payer is not None else -1,entry["source"],tile,self.wenzhou_info()])
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
        record.append_action_tick(self,["wenzhou","state",self.wenzhou_info(),[p.score for p in self.player_list]])
        record.player_action_record_round_end(self)
        self.round_record_finalized = True

    def persist_game_record(self):
        if self.db_manager is None or not self.game_record:
            return None
        try:
            return self.db_manager.store_wenzhou_game_record(
                self.game_record,self.player_list,self.room_type,f"{self.max_round}/4")
        except Exception:
            logger.exception("温州牌谱保存失败，room_id=%s",self.room_id)
            return None
