"""Deal, settle and persist exactly once, including side payments on drawn hands."""
import asyncio
import random
import logging

from ...game_calculation.singapore import rules as book
from ...response import Response, Show_result_info
from ...database.fulu_utils import record_fulu_rounds_for_players
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_game_end
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (capture_player_entry_order, init_game_record, init_game_round,
    player_action_record_hu, player_action_record_round_end, end_game_record, remember_local_record_detail, append_action_tick)
from ..public.random_seed_manager import setup_random_seed_system, derive_round_seed
from ..public.logic_common import assign_competition_final_ranks
from ..public.round_end_timing import liuju_ready_wait_seconds

logger = logging.getLogger(__name__)


class SingaporeLifecycle:
    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        rng = random.Random(self.round_random_seed)
        wall = list(book.WALL); rng.shuffle(wall)
        for p in self.player_list: p.hand_tiles = []
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(wall[:4]); del wall[:4]
        tail, wall = wall[:5], wall[5:]
        self.player_list[0].hand_tiles.extend((tail[0], tail[4]))
        for i in range(1, 4): self.player_list[i].hand_tiles.append(tail[i])
        for p in self.player_list:
            p.has_draw_slot = p.player_index == 0
            p.last_drawn_tile = p.hand_tiles[-1] if p.has_draw_slot else None
        self.tiles_list = wall

    def next_dealer(self):
        if self.pending_winners: return 0 if self.pending_winners[0]["index"] == 0 else 1
        return int(self._kong_count() > 0)

    async def player_reconnect(self, user_id):
        await super().player_reconnect(user_id)
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and self._terminal_result is not None:
            await connection.websocket.send_json(self._terminal_result)

    async def _broadcast_singapore_result(self, **fields):
        self.server_action_tick += 1
        response = Response(type="gamestate/singapore/show_result", success=True, message="显示结算结果",
            show_result_info=Show_result_info(**fields, singapore_info=self.public_info(), action_tick=self.server_action_tick))
        self._terminal_result = response.model_dump(exclude_none=True)
        for p in self.player_list:
            connection = self.game_server.user_id_to_connection.get(p.user_id)
            if p.user_id > 10 and connection is not None and "offline" not in p.tag_list:
                try:
                    await connection.websocket.send_json(self._terminal_result)
                except Exception:
                    logger.warning("Singapore settlement delivery failed user_id=%s; retained for reconnect", p.user_id, exc_info=True)
                await self.send_to_realtime_spectators(p.player_index, response)

    async def _settle_hand(self, scores_before):
        if self._settled_round == self.round_index:
            return self._settled_match_end
        self._settled_round = self.round_index
        winner = self.pending_winners[0] if self.pending_winners else None
        changes = {i: 0 for i in range(4)}
        if winner:
            index, value, payer = winner["index"], winner["detail"], winner.get("payer")
            changes = book.payments(index, value, payer=payer, liable=winner.get("liable_payer"))
            for seat, delta in changes.items(): self.player_list[seat].score += delta
            p = self.player_list[index]
            counter = p.record_counter
            counter.win_score += changes[index]
            counter.win_turn += max(1, self.xunmu)
            if payer is None: counter.zimo_times += 1
            else:
                counter.dianhe_times += 1
                self.player_list[payer].record_counter.fangchong_times += 1
                self.player_list[payer].record_counter.fangchong_score -= changes[payer]
            player_action_record_hu(self, hu_class=winner["hu_class"], hu_score=value["tai"], hu_fan=value["fan_names"],
                hepai_player_index=index, score_changes=[changes[i] for i in range(4)], hepai_tile=winner["tile"],
                multi_ron=False if payer is not None else None, ron_discarder_index=payer,
                recycle_discard=winner["source"] == "discard" if payer is not None else None)
        else:
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
        self._settled_match_end = (any(p.score <= 0 for p in self.player_list) or
                                  (self.current_round >= self.max_round * 4 and self.next_dealer() != 0))
        totals = {p.original_player_index: p.score - scores_before[p.original_player_index] for p in self.player_list}
        fields = dict(player_to_score={p.player_index: p.score for p in self.player_list}, score_changes=totals,
            revealed_angang_masks=build_revealed_angang_masks(self.player_list),
            next_status="match_end" if self._settled_match_end else "round_end_by_ready")
        if winner:
            p = self.player_list[winner["index"]]
            hand = list(p.hand_tiles)
            if winner["source"] in ("discard", "robbing_kong"): hand.append(winner["tile"])
            fields.update(hepai_player_index=winner["index"], hu_class=winner["hu_class"], hu_score=winner["detail"]["tai"],
                hu_fan=winner["detail"]["fan_names"], hepai_player_hand=hand,
                hepai_player_huapai=p.huapai_list, hepai_player_combination_mask=p.combination_mask,
                hepai_tile=winner["tile"], is_qianggang=winner["source"] == "robbing_kong",
                ron_discarder_index=winner.get("payer"), recycle_discard=winner["source"] == "discard")
        else: fields.update(hu_class="liuju", draw_reason=self.draw_reason)
        for p in self.player_list:
            delta = totals[p.original_player_index]
            p.score_history.append(f"{delta:+d}" if delta else "0")
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total += delta
        record_fulu_rounds_for_players(self.player_list)
        player_action_record_round_end(self)
        await self._broadcast_singapore_result(**fields)
        if not self._settled_match_end:
            if winner: await self.run_hu_result_ready_phase(len(winner["detail"]["fan_names"]))
            else: await asyncio.sleep(liuju_ready_wait_seconds())
        return self._settled_match_end

    async def _persist_match(self):
        if self._stored_match: return
        self._stored_match = True
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type = f"{self.max_round}/4"
        game_id = self.db_manager.store_singapore_game_record(self.game_record, self.player_list, self.room_type, match_type)
        remember_local_record_detail(self, game_id, match_type)

    async def game_loop_chinese(self):
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for index, p in enumerate(self.player_list): p.player_index = p.original_player_index = index
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round <= self.max_round * 4:
            before = {p.original_player_index: p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            init_game_round(self)
            append_action_tick(self, ["singapore", "round_start", [p.score for p in self.player_list]])
            await broadcast_game_start(self)
            await self._run_hand()
            if await self._settle_hand(before): break
            dealer = self.next_dealer()
            for p in self.player_list:
                p.player_index = (p.player_index - dealer) % 4
                for values in (p.hand_tiles, p.combination_tiles, p.combination_mask, p.discard_tiles, p.discard_origin_tiles, p.huapai_list): values.clear()
                p.remaining_time = self.round_time
            self.player_list.sort(key=lambda p: p.player_index)
            self.current_round += dealer
            self.round_index += 1
            self.xunmu = 1
        await self._persist_match()
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
