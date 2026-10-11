import asyncio
import random

from ...game_calculation.shenyang import rules as book
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_game_end
from ..game_tuidao.result import broadcast_result
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (capture_player_entry_order, init_game_record, init_game_round,
    player_action_record_hu, player_action_record_round_end, end_game_record, remember_local_record_detail)
from ..public.random_seed_manager import setup_random_seed_system, derive_round_seed
from ..public.logic_common import assign_competition_final_ranks
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players


class ShenyangLifecycle:
    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        wall = [t for t in book.TILES for _ in range(4)]
        random.Random(self.round_random_seed).shuffle(wall)
        for p in self.player_list:
            p.hand_tiles = []
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(wall[:4])
                del wall[:4]
        first = wall[:5]
        del wall[:5]
        for i, p in enumerate(self.player_list):
            p.hand_tiles.append(first[i])
        self.player_list[0].hand_tiles.append(first[4])
        self.player_list[0].last_drawn_tile = first[4]
        self.player_list[0].has_draw_slot = True
        self.tiles_list = wall

    def next_dealer(self):
        return self.pending_winners[0]["index"] if self.pending_winners else 0

    async def _settle_hand(self, before):
        match_end = self.current_round >= self.max_round * 4
        next_status = "match_end" if match_end else "round_end_by_ready"
        changes = dict.fromkeys(range(4), 0)
        rows = []
        if self.pending_winners:
            item = self.pending_winners[0]
            index, detail = item["index"], item["detail"]
            payer = None if item["source"] == "self_draw" else item["payer"]
            changes, rows = book.payments(index, detail, discarder=payer, closed=tuple(self.closed(i) for i in range(4)))
            for p in self.player_list:
                p.score += changes[p.player_index]
            winner = self.player_list[index]
            counter = winner.record_counter
            counter.win_score += changes[index]
            counter.win_turn += self.xunmu
            if payer is None:
                counter.zimo_times += 1
            else:
                counter.dianhe_times += 1
                self.player_list[payer].record_counter.fangchong_times += 1
                self.player_list[payer].record_counter.fangchong_score -= changes[payer]
            best = max(rows, key=lambda r: r["raw_fan"])
            labels = best["fan_names"]
            hand = list(winner.hand_tiles) + ([] if payer is None else [item["tile"]])
            ron = item["source"] == "discard"
            player_action_record_hu(self, hu_class=item["hu_class"], hu_score=best["fan"], hu_fan=labels,
                hepai_player_index=index, score_changes=[changes[i] for i in range(4)], hepai_tile=item["tile"],
                ron_discarder_index=payer if ron else None, recycle_discard=True if ron else None)
            result = dict(hepai_player_index=index, hu_score=best["fan"], hu_fan=labels, hu_class=item["hu_class"],
                hepai_player_hand=hand, hepai_player_huapai=[], hepai_player_combination_mask=winner.combination_mask,
                hepai_tile=item["tile"], is_qianggang=True if item["source"] == "robbing_kong" else None,
                ron_discarder_index=payer, recycle_discard=True if ron else None)
        else:
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            result = dict(hu_class="liuju")
        totals = {p.original_player_index: p.score - before[p.original_player_index] for p in self.player_list}
        await self.emit(dict(kind="settlement", payments=rows, tail_tiles=self.tail_tiles,
                             long_hair=self.long_hair, round_score_changes=totals))
        await broadcast_result(self, **result, score_changes={p.original_player_index: changes[p.player_index] for p in self.player_list},
            player_to_score={p.player_index: p.score for p in self.player_list},
            revealed_angang_masks=build_revealed_angang_masks(self.player_list), next_status=next_status,
            shenyang=dict(payments=rows, tail_tiles=self.tail_tiles, long_hair=self.long_hair, round_score_changes=totals))
        if self.pending_winners and not match_end:
            await self.run_hu_result_ready_phase(len(result["hu_fan"]))
        elif not self.pending_winners:
            await asyncio.sleep(liuju_ready_wait_seconds())
        for p in self.player_list:
            delta = totals[p.original_player_index]
            p.score_history.append(f"{delta:+d}" if delta else "0")
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total += delta
        record_fulu_rounds_for_players(self.player_list)
        player_action_record_round_end(self)
        self.previous_draw = not bool(self.pending_winners)
        return match_end

    async def game_loop_chinese(self):
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for i, p in enumerate(self.player_list):
            p.player_index = p.original_player_index = i
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round <= self.max_round * 4:
            before = {p.original_player_index: p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            await broadcast_game_start(self)
            init_game_round(self)
            await self.prepare_private_fields(range(4))
            await self._run_hand()
            if await self._settle_hand(before):
                break
            dealer = self.next_dealer()
            for p in self.player_list:
                p.player_index = (p.player_index - dealer) % 4
                for values in (p.hand_tiles, p.combination_tiles, p.combination_mask, p.discard_tiles, p.discard_origin_tiles, p.huapai_list):
                    values.clear()
                p.remaining_time = self.round_time
            self.player_list.sort(key=lambda p: p.player_index)
            self.current_round += 1
            self.round_index += 1
            self.xunmu = 1
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        mode = f"{self.max_round}/4"
        store = getattr(self.db_manager, "store_shenyang_game_record", None)
        game_id = store(self.game_record, self.player_list, self.room_type, mode) if store else None
        remember_local_record_detail(self, game_id, mode)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
