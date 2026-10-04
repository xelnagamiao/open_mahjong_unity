"""广东的局终支付与赢家坐庄；动作推进仍由共享回合执行器负责。"""

import asyncio
import random

from ...game_calculation.guangdong.config import EDITION
from ...game_calculation.guangdong.settlement import winning_horses, win_payments, refund_kongs
from ..game_tuidao.result import broadcast_result
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_game_end
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (capture_player_entry_order, init_game_record, init_game_round,
    append_action_tick, player_action_record_hu, player_action_record_round_end,
    end_game_record, remember_local_record_detail)
from ..public.random_seed_manager import setup_random_seed_system
from ..public.logic_common import assign_competition_final_ranks
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players


class GuangdongMatchMixin:
    async def _settle_hand(self, scores_before):
        match_end = self.current_round >= self.max_round * 4
        next_status = "match_end" if match_end else "round_end_by_ready"
        kong_changes = [-refund_kongs(self.kong_ledger)[i] for i in range(4)]
        if self.pending_winners:
            item = self.pending_winners[0]
            index, detail = item["index"], item["detail"]
            horse_tiles = []
            hits = [0] * 4
            self_draw = item["source"] == "self_draw"
            responsible = item.get("liable_payer") if self_draw else None
            if self_draw:
                horse_tiles = self.tiles_list[:4]
                del self.tiles_list[:len(horse_tiles)]
                hits[index] = len(winning_horses(horse_tiles, index))
            payment = win_payments(index, detail["base_score"], payer=item["payer"],
                horse_hits=hits[index], direct_kong_payer=responsible,
                rob_kong=item["source"] == "robbing_kong")
            changes = payment["changes"]
            horses = {"winner": index, "tiles": horse_tiles, "hits": hits,
                      "changes": [payment["horse_changes"][i] for i in range(4)], "payer": responsible}
            if self_draw:
                append_action_tick(self, ["guangdong", "horses", horses])
            result = {"edition": EDITION, "fan": detail["fan"], "base_score": detail["base_score"],
                "coefficient": detail["coefficient"], "discarded_ghosts": detail["discarded_ghosts"],
                "substitutions": detail["substitutions"], "logical_hand": detail["logical_hand"],
                "shape": detail["shape"], "horses": horses,
                "win_changes": [changes[i] for i in range(4)], "kong_changes": kong_changes}
            append_action_tick(self, ["guangdong", "settlement", result])
            for i, delta in changes.items():
                self.player_list[i].score += delta
            winner = self.player_list[index]
            names = list(detail["fan_names"])
            if detail["coefficient"] != 1:
                names.append(f"GD|coefficient|×{detail['coefficient']}|分数系数")
            counter = winner.record_counter
            counter.recorded_fans.append(names)
            counter.win_score += changes[index]
            counter.win_turn += self.xunmu
            if self_draw:
                counter.zimo_times += 1
            else:
                counter.dianhe_times += 1
                self.player_list[item["payer"]].record_counter.fangchong_times += 1
                self.player_list[item["payer"]].record_counter.fangchong_score -= changes[item["payer"]]
            ron = item["source"] == "discard"
            player_action_record_hu(self, hu_class=item["hu_class"], hu_score=detail["base_score"],
                hu_fan=names, hepai_player_index=index, score_changes=[changes[i] for i in range(4)],
                hepai_tile=item["tile"], ron_discarder_index=item["payer"] if ron else None,
                recycle_discard=True if ron else None)
            await broadcast_result(self, hepai_player_index=index,
                player_to_score={p.player_index: p.score for p in self.player_list},
                hu_score=detail["base_score"], hu_fan=names, hu_class=item["hu_class"],
                hepai_player_hand=list(winner.hand_tiles) + ([] if self_draw else [item["tile"]]),
                hepai_player_huapai=[], hepai_player_combination_mask=winner.combination_mask,
                score_changes={p.original_player_index: changes[p.player_index] for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list), hepai_tile=item["tile"],
                is_qianggang=True if item["source"] == "robbing_kong" else None,
                ron_discarder_index=item["payer"], recycle_discard=True if ron else None,
                guangdong_result=result, next_status=next_status)
            if not match_end:
                await self.run_hu_result_ready_phase(len(names))
        else:
            refund = refund_kongs(self.kong_ledger)
            if any(refund.values()):
                for i, delta in refund.items():
                    self.player_list[i].score += delta
                append_action_tick(self, ["guangdong", "refund_kongs", [refund[i] for i in range(4)]])
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            await broadcast_result(self, hu_class="liuju", score_changes={i: 0 for i in range(4)},
                player_to_score={p.player_index: p.score for p in self.player_list},
                gang_refund_changes={p.original_player_index: refund[p.player_index] for p in self.player_list},
                guangdong_result={"edition": EDITION, "draw": True, "kong_changes": kong_changes,
                                  "refund_changes": [refund[i] for i in range(4)]},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list), next_status=next_status)
            await asyncio.sleep(liuju_ready_wait_seconds())
        for player in self.player_list:
            delta = player.score - scores_before[player.original_player_index]
            player.score_history.append(f"{delta:+d}" if delta else "0")
            player.round_number_history.append(self.current_round)
            player.record_counter.round_score_total += delta
        record_fulu_rounds_for_players(self.player_list)
        player_action_record_round_end(self)
        return match_end

    async def game_loop_chinese(self):
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for i, player in enumerate(self.player_list):
            player.player_index = player.original_player_index = i
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round <= self.max_round * 4:
            before = {p.original_player_index: p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            init_game_round(self)
            await broadcast_game_start(self)
            for i in range(4):
                self.record_guangdong_tips(i)
            await self._run_hand()
            if await self._settle_hand(before):
                break
            dealer = self.next_dealer()
            for player in self.player_list:
                player.player_index = (player.player_index - dealer) % 4
                for values in (player.hand_tiles, player.combination_tiles, player.combination_mask,
                               player.discard_tiles, player.discard_origin_tiles, player.huapai_list):
                    values.clear()
                player.remaining_time = self.round_time
            self.player_list.sort(key=lambda p: p.player_index)
            self.current_round += 1
            self.round_index += 1
            self.xunmu = 1
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type = f"{self.max_round}/4"
        store = getattr(self.db_manager, "store_guangdong_game_record", None)
        game_id = store(self.game_record, self.player_list, self.room_type, match_type) if store else None
        remember_local_record_detail(self, game_id, match_type)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
