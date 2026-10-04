from __future__ import annotations
from typing import Any, Dict, Optional
import logging
from ..public.logic_common import back_current_num
logger = logging.getLogger(__name__)

class RoundRecording:
    def start_game_recording(self) -> None:
        from ..public.game_record_manager import init_game_record

        init_game_record(self)
        self.game_record["game_title"]["sub_rule"] = self.sub_rule
        self.game_record["game_title"]["hepai_limit"] = self.hepai_limit
        self.game_record["game_title"]["blood_battle"] = self.is_nanque
        self.game_record["game_title"]["rule_version"] = "nanque-blood-v1" if self.is_nanque else "zungjung-3.3"
        self.spectator_manager.record_game_title()


    def start_round_recording(self) -> None:
        from ..public.game_record_manager import init_game_round

        if not self.game_record:
            self.start_game_recording()
        init_game_round(self)
        self.round_record_finalized = False
        self.spectator_manager.record_round_start()


    def _has_active_round_record(self) -> bool:
        return bool(
            self.game_record.get("game_round", {}).get(f"round_index_{self.round_index}")
        ) and not self.round_record_finalized


    def record_visible_action(self, action_info):
        if not self._has_active_round_record(): return
        from ..public.game_record_manager import (
            append_action_tick, player_action_record_angang, player_action_record_chipenggang,
            player_action_record_cut, player_action_record_deal, player_action_record_jiagang,
            player_action_record_hu,
        )
        from .boardcast import _settlement_hu_class
        action, tile, actor = action_info.get("action"), action_info.get("tile"), action_info.get("player")
        if action in {"deal_tile", "deal_gang_tile"}:
            # An explicit pointer survives retired seats and multi-ron in every replay consumer.
            append_action_tick(self, ["reset", actor])
            player_action_record_deal(self, deal_tile=tile, deal_type="d" if action == "deal_tile" else "gd")
        elif action == "cut":
            player_action_record_cut(self, cut_tile=tile, is_moqie=bool(action_info.get("cutClass", False)))
        elif action == "angang":
            player_action_record_angang(self, tile, bool(action_info.get("is_mo_gang")), action_info.get("combination_mask"))
        elif action == "jiagang":
            player_action_record_jiagang(self, tile, bool(action_info.get("is_mo_gang")))
        elif action in {"chi_left", "chi_mid", "chi_right", "peng", "gang"}:
            player_action_record_chipenggang(self, action, tile, actor, action_info.get("combination_mask"))
        elif action and action.startswith("hu"):
            record = self._latest_hu_settlement_for(actor)
            if record.get("recorded"): return
            record["recorded"] = True
            player_action_record_hu(self, _settlement_hu_class(record), record["points"], record["fan_ids"],
                actor, [0]*4 if self.is_nanque else record["score_changes"], hepai_tile=tile,
                multi_ron=record.get("multi_ron", False), ron_discarder_index=self._settlement_payer_index(record),
                recycle_discard=record.get("recycle_discard", True))
            self._update_record_counter_for_settlement(record, record["fan_ids"])

    def finalize_round_recording(self):
        if not self._has_active_round_record(): return
        from ..public.game_record_manager import append_action_tick, player_action_record_round_end, player_action_record_liuju
        from .boardcast import _settlement_hu_class
        self.apply_deferred_score_changes()
        for player in self.player_list:
            if any(m[0] in {"s","k","g"} for m in player.combination_tiles): player.record_counter.fulu_times += 1
        records = self.deferred_hu_settlements
        if self.is_nanque and records:
            hands = {p.player_index: list(p.hand_tiles) for p in self.player_list}
            for record in records:
                if record["source"] != "self_draw": hands[record["winner"]].append(record["tile"])
            append_action_tick(self, ["blood", "reveal_hu", hands])
            for i, record in enumerate(records):
                append_action_tick(self, ["blood", "settle_hu", _settlement_hu_class(record), record["winner"],
                    record["points"], record["fan_ids"], record["score_changes"], int(i == len(records)-1), []])
        elif not records:
            player_action_record_liuju(self)
        player_action_record_round_end(self)
        self.round_record_finalized = True

    def finalize_game_recording(self) -> None:
        if not self.game_record or "game_title" not in self.game_record:
            return
        from ..public.game_record_manager import end_game_record

        end_game_record(self)


    def _update_record_counter_for_settlement(self, settlement: dict, fan_ids: list[str]) -> None:
        winner_index = settlement.get("winner")
        if winner_index not in range(len(self.player_list)):
            return
        winner = self.player_list[winner_index]
        points = int(settlement.get("points", 0) or 0)
        winner.record_counter.recorded_fans.append(fan_ids)
        winner.record_counter.win_score += points
        winner.record_counter.win_turn += len(winner.discard_tiles) + 1
        if settlement.get("source") == "self_draw":
            winner.record_counter.zimo_times += 1
        else:
            winner.record_counter.dianhe_times += 1
            payer_index = self._settlement_payer_index(settlement)
            if payer_index in range(len(self.player_list)):
                payer = self.player_list[payer_index]
                payer.record_counter.fangchong_times += 1
                payer.record_counter.fangchong_score += points


    @staticmethod
    def _settlement_payer_index(settlement: dict) -> Optional[int]:
        payer_index = settlement.get("discarder")
        if payer_index is not None:
            return payer_index
        return settlement.get("kong_player")


    def persist_game_record(self) -> Optional[str]:
        """Persist the finalized replay and rule-local statistics."""
        if self.db_manager is None or not self.game_record:
            return None
        try:
            game_id = self.db_manager.store_jiandan_game_record(
                self.game_record,
                self.player_list,
                self.room_type,
                f"{self.max_round}/4",
            )
            has_ai_player = any(player.user_id <= 10 for player in self.player_list)
            if self.room_type == "events":
                logger.info("Jiandan event game skips player statistics: game_id=%s", game_id)
            elif game_id and not has_ai_player and self.is_nanque:
                total_rounds = len(self.game_record.get("game_round", {}))
                self.db_manager.store_jiandan_game_stats(
                    game_id,
                    self.player_list,
                    self.room_type,
                    self.max_round,
                    total_rounds,
                )
                self.db_manager.store_jiandan_fan_stats(
                    game_id,
                    self.player_list,
                    self.room_type,
                    self.max_round,
                )
            elif has_ai_player:
                logger.info("Jiandan game contains an AI player; statistics skipped")
            return game_id
        except Exception as exc:
            logger.warning(
                "Jiandan replay persistence failed, room_id=%s: %s",
                self.room_id,
                exc,
                exc_info=True,
            )
            return None

