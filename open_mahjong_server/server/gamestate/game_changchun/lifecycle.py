"""Round setup, settlement and persistence using the common record format."""
import asyncio
import random

from ...game_calculation.changchun import rules as book
from ..game_taiwan.boardcast import broadcast_game_start, broadcast_game_end
from ..game_tuidao.result import broadcast_result
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (capture_player_entry_order,init_game_record,init_game_round,
    player_action_record_hu,player_action_record_round_end,end_game_record,remember_local_record_detail)
from ..public.random_seed_manager import setup_random_seed_system,derive_round_seed
from ..public.logic_common import assign_competition_final_ranks
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players


class ChangchunLifecycle:
    def init_tiles(self):
        self.round_random_seed=derive_round_seed(self.master_seed,self.round_index)
        wall=[tile for tile in book.TILES for _ in range(4)]
        random.Random(self.round_random_seed).shuffle(wall)
        for player in self.player_list: player.hand_tiles=[]
        for _ in range(3):
            for player in self.player_list:
                player.hand_tiles.extend(wall[:4]);del wall[:4]
        tail=wall[:5];del wall[:5]
        self.player_list[0].hand_tiles.extend((tail[0],tail[4]))
        for i in range(1,4): self.player_list[i].hand_tiles.append(tail[i])
        for player in self.player_list:
            player.has_draw_slot=player.player_index==0
            player.last_drawn_tile=player.hand_tiles[-1] if player.has_draw_slot else None
        self.tiles_list=wall
        self.cc_wall_ids=list(range(len(wall)))

    def next_dealer(self):
        return 0 if not self.pending_winners or self.pending_winners[0]["index"]==0 else 1

    async def player_reconnect(self,user_id):
        await super().player_reconnect(user_id)
        connection=self.game_server.user_id_to_connection.get(user_id)
        if connection is not None and self._terminal_result is not None:
            await connection.websocket.send_json(self._terminal_result)

    def spectator_record_tick(self,tick):
        # Delayed omniscient records still must not reveal an active secret
        # indicator. The saved full record and final table closure reveal it.
        if tick and tick[0]=="cc" and tick[1].get("kind") in ("bao_reveal","bao_change"):
            return ["cc",{**{k:v for k,v in tick[1].items() if k not in ("tile","dice")},"tile":0}]
        return tick

    def spectator_round_header(self,header):
        # A hidden event is insufficient when the initial wall and deterministic
        # per-round seed can reconstruct its identity. Preserve only positions.
        return {**{k:v for k,v in header.items() if k!="round_random_seed"},
                "tiles_list":[0]*len(header["tiles_list"]),"changchun_private_wall":True}

    def complete_spectator_record(self,record):
        # Called only by the common manager's terminal record path. The saved
        # canonical record retains wall, seed and all original indicator events.
        from copy import deepcopy
        return {**deepcopy(self.game_record),"players_settings":record["players_settings"]}

    async def _settle_hand(self,scores_before):
        match_end=self.current_round>=self.max_round*4
        next_status="match_end" if match_end else "round_end_by_ready"
        reveal={"bao_tile":self.cc_bao_tile or 0,"bao_revision":self.cc_bao_revision,"bao_visible":True,
                "hands":{p.player_index:list(p.hand_tiles) for p in self.player_list},
                "tail_tiles":[{"player":i,"tile":t} for i,t in self.cc_tail_tiles]}
        await self.emit_cc_event({"kind":"round_reveal",**reveal})
        if self.pending_winners:
            item=self.pending_winners[0]
            index,detail=item["index"],item["detail"]
            self_draw=item["source"] in ("self_draw","bao_indicator")
            payer=None if self_draw else item["payer"]
            changes,breakdown=book.payments(index,detail["raw_fan"],discarder=payer,
                bao_seen=True if payer is None else self.bao_visible(payer))
            for i,delta in changes.items(): self.player_list[i].score+=delta
            winner=self.player_list[index]
            counter=winner.record_counter
            counter.recorded_fans.append(detail["fan_names"])
            counter.win_score+=changes[index]
            counter.win_turn+=self.xunmu
            if self_draw: counter.zimo_times+=1
            else:
                counter.dianhe_times+=1
                self.player_list[payer].record_counter.fangchong_times+=1
                self.player_list[payer].record_counter.fangchong_score-=changes[payer]
            ron=item["source"]=="discard"
            winning_hand=list(winner.hand_tiles)+([item["tile"]] if item["source"]!="self_draw" else [])
            await self.emit_cc_event({"kind":"settlement","winner":index,"source":item["source"],
                "tile":item["tile"],
                "payments":breakdown,"bao":detail.get("bao",""),"represented_win":detail.get("represented_win")})
            player_action_record_hu(self,hu_class=item["hu_class"],hu_score=detail["fan"],hu_fan=detail["fan_names"],
                hepai_player_index=index,score_changes=[changes[i] for i in range(4)],hepai_tile=item["tile"],
                ron_discarder_index=payer if ron else None,recycle_discard=True if ron else None)
            tactical_silent = bool(getattr(self, '_tactical_silent_action', False))
            self._tactical_silent_action = False
            await broadcast_result(self,silent=tactical_silent,hepai_player_index=index,
                player_to_score={p.player_index:p.score for p in self.player_list},
                hu_score=detail["fan"],hu_fan=detail["fan_names"],hu_class=item["hu_class"],
                hepai_player_hand=winning_hand,hepai_player_huapai=[],hepai_player_combination_mask=winner.combination_mask,
                score_changes={p.original_player_index:changes[p.player_index] for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list),hepai_tile=item["tile"],
                is_qianggang=True if item["source"]=="robbing_kong" else None,
                ron_discarder_index=payer,recycle_discard=True if ron else None,next_status=next_status,
                changchun={**reveal,"payments":breakdown,"represented_win":detail.get("represented_win"),
                           "bao":detail.get("bao",""),
                           "round_score_changes":{p.original_player_index:p.score-scores_before[p.original_player_index] for p in self.player_list}})
            if not match_end: await self.run_hu_result_ready_phase(len(detail["fan_names"]))
        else:
            self.hu_class="liuju"
            self._record_liuju(self.draw_reason)
            await broadcast_result(self,hu_class="liuju",score_changes={i:0 for i in range(4)},
                player_to_score={p.player_index:p.score for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list),next_status=next_status,
                changchun={**reveal,"round_score_changes":{p.original_player_index:p.score-scores_before[p.original_player_index] for p in self.player_list}})
            await asyncio.sleep(liuju_ready_wait_seconds())
        for player in self.player_list:
            delta=player.score-scores_before[player.original_player_index]
            player.score_history.append(f"{delta:+d}" if delta else "0")
            player.round_number_history.append(self.current_round)
            player.record_counter.round_score_total+=delta
        record_fulu_rounds_for_players(self.player_list)
        player_action_record_round_end(self)
        return match_end

    async def game_loop_chinese(self):
        self.master_seed,self.salt,self.commitment,self.isPlayerSetRandomSeed=setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for i,player in enumerate(self.player_list): player.player_index=player.original_player_index=i
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round<=self.max_round*4:
            before={p.original_player_index:p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index=0
            await broadcast_game_start(self)
            init_game_round(self)
            await self._run_hand()
            if await self._settle_hand(before): break
            dealer=self.next_dealer()
            for player in self.player_list:
                player.player_index=(player.player_index-dealer)%4
                for values in (player.hand_tiles,player.combination_tiles,player.combination_mask,
                               player.discard_tiles,player.discard_origin_tiles,player.huapai_list): values.clear()
                player.remaining_time=self.round_time
            self.player_list.sort(key=lambda p:p.player_index)
            self.current_round+=1;self.round_index+=1;self.xunmu=1
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type=f"{self.max_round}/4"
        store=getattr(self.db_manager,"store_changchun_game_record",None)
        game_id=store(self.game_record,self.player_list,self.room_type,match_type) if store else None
        remember_local_record_detail(self,game_id,match_type)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)
