"""120张开牌、逐家结算、胜者坐庄与普通牌谱持久化。"""
import asyncio
import random

from ...game_calculation.jiujiang import patterns as book
from ...game_calculation.jiujiang.scoring import payments
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_game_end
from .result import broadcast_result
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (capture_player_entry_order,init_game_record,init_game_round,
    player_action_record_hu,player_action_record_round_end,end_game_record,remember_local_record_detail,append_action_tick)
from ..public.random_seed_manager import setup_random_seed_system,derive_round_seed
from ..public.logic_common import assign_competition_final_ranks
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players


class JiujiangLifecycle:
    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed,self.round_index)
        rng = random.Random(self.round_random_seed)
        wall = [tile for tile in book.WALL_TILES for _ in range(4)]
        rng.shuffle(wall)
        self.jj_dice = [rng.randint(1,6),rng.randint(1,6)]
        self.jj_break_side = (sum(self.jj_dice)-1)%4
        offset = (self.jj_break_side*30+min(self.jj_dice)*2)%120
        wall = wall[offset:]+wall[:offset]
        for player in self.player_list: player.hand_tiles = []
        for _ in range(3):
            for player in self.player_list:
                player.hand_tiles.extend(wall[:4])
                del wall[:4]
        tail, wall = wall[:5],wall[5:]
        self.player_list[0].hand_tiles.extend((tail[0],tail[4]))
        for index in range(1,4): self.player_list[index].hand_tiles.append(tail[index])
        for player in self.player_list:
            player.has_draw_slot = player.player_index==0
            player.last_drawn_tile = player.hand_tiles[-1] if player.has_draw_slot else None
        self.tiles_list = wall

    def build_record_round_fields(self):
        return {"jiujiang_dice":getattr(self,"jj_dice",[]),"jiujiang_break_side":getattr(self,"jj_break_side",0)}

    def next_dealer(self):
        return self.pending_winners[0]["index"] if self.pending_winners else 0

    async def player_reconnect(self,user_id):
        await super().player_reconnect(user_id)
        connection = self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and self._terminal_result is not None:
            await connection.websocket.send_json(self._terminal_result)

    def _settlement_for_winner(self,item):
        # 放炮者此刻已经打出牌；其他输家仍是13张结构，不依赖花分或报听声明。
        ready = {p.player_index for p in self.player_list if
                 book.structural_waits(tuple(sorted(p.hand_tiles)),tuple(p.combination_tiles))}
        declared = {p.player_index for p in self.player_list if p.declared_ready}
        return payments(item["index"],item["detail"]["base_score"],discarder=item.get("payer"),
            exposed_flowers=item["detail"]["exposed_flower_points"],ready=ready,declared=declared)

    async def _settle_hand(self,scores_before):
        match_end = self.current_round>=self.max_round*4
        next_status = "match_end" if match_end else "round_end_by_ready"
        if any(code.startswith("G") for p in self.player_list for code in p.combination_tiles):
            masks = [[[(0 if value==2 else value) if i%2==0 else value for i,value in enumerate(mask)]
                      for mask in p.combination_mask] for p in self.player_list]
            append_action_tick(self,["jiujiang","reveal_kongs",masks])
        if self.pending_winners:
            item = self.pending_winners[0]
            index, detail = item["index"],item["detail"]
            changes, breakdown = self._settlement_for_winner(item)
            for seat,delta in changes.items(): self.player_list[seat].score += delta
            winner, payer = self.player_list[index],item.get("payer")
            counter = winner.record_counter
            counter.recorded_fans.append(detail["fan_names"])
            counter.win_score += changes[index]
            counter.win_turn += self.xunmu
            if item["source"]=="self_draw": counter.zimo_times += 1
            else:
                counter.dianhe_times += 1
                self.player_list[payer].record_counter.fangchong_times += 1
                self.player_list[payer].record_counter.fangchong_score -= changes[payer]
            append_action_tick(self,["jiujiang","payments",breakdown])
            ron = item["source"]=="discard"
            player_action_record_hu(self,hu_class=item["hu_class"],hu_score=detail["base_score"],hu_fan=detail["fan_names"],
                hepai_player_index=index,score_changes=[changes[i] for i in range(4)],hepai_tile=item["tile"],
                multi_ron=False if payer is not None else None,ron_discarder_index=payer,
                recycle_discard=ron if payer is not None else None)
            await broadcast_result(self,hepai_player_index=index,
                player_to_score={p.player_index:p.score for p in self.player_list},
                hu_score=detail["base_score"],hu_fan=detail["fan_names"],hu_class=item["hu_class"],
                hepai_player_hand=list(winner.hand_tiles)+([item["tile"]] if item["source"]!="self_draw" else []),
                hepai_player_huapai=winner.huapai_list,hepai_player_combination_mask=winner.combination_mask,
                score_changes={p.original_player_index:changes[p.player_index] for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list),hepai_tile=item["tile"],
                is_qianggang=True if item["source"]=="robbing_kong" else None,
                ron_discarder_index=payer,recycle_discard=True if ron else None,next_status=next_status,
                jiujiang_payments=breakdown)
            if not match_end: await self.run_hu_result_ready_phase(len(detail["fan_names"]))
        else:
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            await broadcast_result(self,hu_class="liuju",score_changes={i:0 for i in range(4)},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list),next_status=next_status)
            await asyncio.sleep(liuju_ready_wait_seconds())
        for player in self.player_list:
            delta = player.score-scores_before[player.original_player_index]
            player.score_history.append(f"{delta:+d}" if delta else "0")
            player.round_number_history.append(self.current_round)
            player.record_counter.round_score_total += delta
        record_fulu_rounds_for_players(self.player_list)
        player_action_record_round_end(self)
        return match_end

    async def game_loop_chinese(self):
        self.master_seed,self.salt,self.commitment,self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for index,player in enumerate(self.player_list): player.player_index=player.original_player_index=index
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round<=self.max_round*4:
            before = {p.original_player_index:p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            await broadcast_game_start(self)
            init_game_round(self)
            await self.prepare_private_fields(range(4))
            await self._run_hand()
            if await self._settle_hand(before): break
            dealer = self.next_dealer()
            for player in self.player_list:
                player.player_index = (player.player_index-dealer)%4
                for values in (player.hand_tiles,player.combination_tiles,player.combination_mask,
                               player.discard_tiles,player.discard_origin_tiles,player.huapai_list): values.clear()
                player.remaining_time = self.round_time
            self.player_list.sort(key=lambda p:p.player_index)
            self.current_round += 1
            self.round_index += 1
            self.xunmu = 1
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type = f"{self.max_round}/4"
        store = getattr(self.db_manager,"store_jiujiang_game_record",None)
        game_id = store(self.game_record,self.player_list,self.room_type,match_type) if store else None
        remember_local_record_detail(self,game_id,match_type)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
