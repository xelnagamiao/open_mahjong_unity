"""复用公共动作执行器，九江的资格、补发财、报听和结算独立裁决。"""
from collections import Counter
from dataclasses import replace

from ...game_calculation.jiujiang import patterns as book
from ...game_calculation.jiujiang import scoring
from ...room.jiujiang_room import CONFIG
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan import action_check as actions
from ..game_taiwan.boardcast import broadcast_do_action, broadcast_refresh_player_tag_list
from ..public.hand_action_notify import apply_player_cut
from ..public.hand_slot_utils import has_draw_slot
from ..public.game_record_manager import append_action_tick, player_action_record_cut
from ..public.ai.bot_executor import run_room_bot_cpu
from .hints import compute_hand
from .lifecycle import JiujiangLifecycle


class JiujiangGameState(JiujiangLifecycle, TaiwanGameState):
    flower_tiles = (book.FA,) * 4
    structure_tiles = book.TILES

    def __init__(self, server, room, calculation_service, db, state_id):
        if room.get("sub_rule",book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("不支持的九江麻将子规则")
        super().__init__(server,{**room,"detailed_config":None},calculation_service,db,state_id)
        self.room_rule, self.sub_rule = book.RULE, book.SUB_RULE
        self.rules = replace(self.rules, dead_wall_count=0, ready_qualification_mode="disabled",
            public_ready_enabled=True, multi_win_mode="head_bump", chow_discard_restriction_mode="none",
            pung_same_tile_discard_forbidden=False, allow_kong_from_upper_discard=True,
            direct_kong_replacement_win_allowed=True, missed_win_blocks_self_draw=False,
            missed_win_blocks_claims=False, missed_win_released_by_kong=False,
            seven_flowers_steal_eighth_enabled=False)
        self.rules_dict, self.dead_wall_count, self.hepai_limit, self.open_cuohe = dict(CONFIG), 0, 1, False
        self._reset_hand_runtime()
        from .bot import jiujiang_bot_action
        self.smart_bot_action = jiujiang_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self._hint_cache, self._recorded_hints = {}, {}
        self._hint_generation = getattr(self,"_hint_generation",0) + 1
        self._terminal_result = None
        self._tail_upper_next = True
        self.jj_kong_discard = False
        for player in self.player_list:
            player.jj_claim_discard = False

    def _register_initial_heavenly_ready(self): pass
    def _revoke_qualification(self, *args, **kwargs): pass
    def _remember_claim_liability(self, *args): pass
    def _mark_eight_flowers_if_ready(self, *args, **kwargs): pass
    def _liability_payer_for_win(self, *args): return None
    def enter_water(self, index): return False

    async def _opening_flower_replacement(self):
        # 用户确认发财可以保留、弃出或自行选择补牌；没有强制开局补花窗口。
        return

    def _take_supplement_tile(self):
        offset = -2 if self._tail_upper_next and len(self.tiles_list) >= 2 else -1
        self._tail_upper_next = not self._tail_upper_next
        return self.tiles_list.pop(offset)

    async def _process_drawn_flowers(self, index, origin):
        self.player_list[index].jj_claim_discard = False
        self.last_draw_after_kong = origin in ("angang","jiagang","direct_kong")
        self.supplement_win_allowed = True
        return True

    def refresh_waits(self, index):
        player = self.player_list[index]
        player.waiting_tiles = set(book.structural_waits(tuple(sorted(player.hand_tiles)),tuple(player.combination_tiles)))
        return player.waiting_tiles

    def score_candidate(self, index, source, tile=None, **kwargs):
        player = self.player_list[index]
        if "peida" in player.tag_list:
            return None
        self_draw = source == "self_draw"
        if self_draw and not has_draw_slot(player):
            return None
        tile = player.last_drawn_tile if tile is None and self_draw else tile
        hand = list(player.hand_tiles) + ([] if self_draw else [tile])
        return scoring.score(hand,player.combination_tiles,player.huapai_list,winning_tile=tile,
            self_draw=self_draw,after_kong=self_draw and self.last_draw_after_kong,
            after_kong_discard=source=="discard" and self.jj_kong_discard,
            rob_kong=source=="robbing_kong",last_tile=not self.can_take_normal_tile(),
            heaven_earth=player.discard_count==0,declared_ready=player.declared_ready)

    def ready_candidate_cuts(self, index):
        player = self.player_list[index]
        if player.discard_count or player.declared_ready or "peida" in player.tag_list:
            return {}
        result = {}
        for tile in set(player.hand_tiles):
            hand = list(player.hand_tiles)
            hand.remove(tile)
            waits = book.structural_waits(tuple(sorted(hand)),tuple(player.combination_tiles))
            if waits: result[tile] = list(waits)
        return result

    def _finalize_ready_after_discard(self, index, declared):
        if declared: self._declare_ready(index)

    def _kong_candidates(self, index, added=False):
        player = self.player_list[index]
        if not has_draw_slot(player) or not self.can_take_supplement_tile() or player.jj_claim_discard:
            return []
        tiles = [t for t in set(player.hand_tiles) if t in book.TILES and
                 (f"k{t}" in player.combination_tiles if added else player.hand_tiles.count(t)==4)]
        return sorted(t for t in tiles if not player.ready_locked or t==player.last_drawn_tile)

    def check_hand_actions(self, index):
        player = self.player_list[index]
        result = {i:[] for i in range(4)}
        offered = result[index]
        detail = self.score_candidate(index,"self_draw")
        if detail:
            offered.append("hu_self")
            self.result_dict["hu_self"] = detail
        if book.FA in player.hand_tiles and self.can_take_supplement_tile(): offered.append("buhua")
        if "peida" not in player.tag_list:
            if self._kong_candidates(index): offered.append("angang")
            if self._kong_candidates(index,True): offered.append("jiagang")
        player.riichi_candidate_cuts = self.ready_candidate_cuts(index)
        if player.riichi_candidate_cuts: offered.append("riichi_cut")
        offered.append("cut")
        return result

    def check_discard_actions(self, tile):
        if tile not in book.TILES: return {i:[] for i in range(4)}
        result = actions.check_action_after_cut(self,tile)
        for index, offered in result.items():
            if len(self.player_list[index].hand_tiles) != 4:
                offered[:] = [a for a in offered if not a.startswith("chi_")]
            if offered == ["pass"]: offered.clear()
        return result

    def check_added_kong_actions(self, tile):
        return actions.check_action_jiagang(self,tile)

    def after_claim_actions(self):
        return self.check_hand_actions(self.current_player_index)

    async def execute_claim(self, index, action):
        discards = self.player_list[self.current_player_index].discard_tiles
        if not discards or action not in self.check_discard_actions(discards[-1])[index]: return
        await super().execute_claim(index,action)
        self.player_list[index].jj_claim_discard = action != "gang"

    async def execute_angang(self, index, tile):
        if tile in self._kong_candidates(index): await super().execute_angang(index,tile)

    async def execute_jiagang(self, index, tile):
        if tile in self._kong_candidates(index,True): await super().execute_jiagang(index,tile)

    async def execute_buhua(self, index):
        player = self.player_list[index]
        if book.FA not in player.hand_tiles or not self.can_take_supplement_tile(): return
        flower_index = len(player.hand_tiles)-1-player.hand_tiles[::-1].index(book.FA)
        is_drawn = has_draw_slot(player) and flower_index==len(player.hand_tiles)-1
        if not await self._replace_one_flower(index,flower_index,is_drawn=is_drawn,opening=False): return
        self.last_draw_after_kong = False
        # 补发财是新的补牌槽；此后可杠或自摸，但本次补牌不加杠上分。
        player.jj_claim_discard = False
        await self._prepare_hand_action_after_draw()

    async def execute_cut(self, index, data, *, declare_ready=False, is_timeout_action=False):
        player = self.player_list[index]
        tile = data.get("TileId")
        if player.ready_locked and (not has_draw_slot(player) or tile != player.last_drawn_tile): return
        if declare_ready and tile not in self.ready_candidate_cuts(index): return
        result = await apply_player_cut(self,index,data)
        if result is None: return
        tile, moqie, cut_index = result
        self.jj_kong_discard = self.last_draw_after_kong
        player.last_drawn_tile = None
        player.last_discarded_tile = tile
        player.discard_tiles.append(tile)
        player.discard_count += 1
        player.first_discard_done = True
        player.jj_claim_discard = False
        if index == 0: self.xunmu += 1
        self.refresh_waits(index)
        player_action_record_cut(self,cut_tile=tile,is_moqie=moqie)
        self._finalize_ready_after_discard(index,declare_ready)
        self.opening_dealer_action = self.last_draw_after_kong = self.last_draw_was_last = False
        await broadcast_do_action(self,action_list=["cut"],action_player=index,cut_tile=tile,
            cut_class=moqie,cut_tile_index=cut_index,is_timeout_action=is_timeout_action)
        if declare_ready: await broadcast_refresh_player_tag_list(self)
        self.action_dict = self.check_discard_actions(tile)
        self.game_status = "waiting_action_after_cut" if any(self.action_dict.values()) else "deal_card"

    async def prepare_private_fields(self, indices):
        for index in indices:
            player = self.player_list[index]
            key = (tuple(player.hand_tiles),tuple(player.combination_tiles),tuple(player.huapai_list),
                   player.declared_ready,player.first_discard_done)
            generation = self._hint_generation
            cached = self._hint_cache.get(index)
            if cached is None or cached[0] != key:
                payload = await run_room_bot_cpu(self,compute_hand,*key)
                if generation != self._hint_generation or self.player_list[index] is not player: continue
                current = (tuple(player.hand_tiles),tuple(player.combination_tiles),tuple(player.huapai_list),
                           player.declared_ready,player.first_discard_done)
                if current != key: continue
                self._hint_cache[index] = (key,payload)
            if (f"round_index_{self.round_index}" in self.game_record.get("game_round",{})
                    and self._recorded_hints.get(index) != key):
                append_action_tick(self,["jiujiang","hints",index,self._hint_cache[index][1]])
                self._recorded_hints[index] = key

    def build_private_game_info_fields(self, index):
        player = self.player_list[index]
        fields = {"jiujiang_info":{"self_has_draw_slot":has_draw_slot(player)}}
        if self.tips or self.count_tips:
            cached = self._hint_cache.get(index)
            if cached: fields["jiujiang_hints"] = cached[1]
        return fields

    def build_private_hand_action_info(self, index):
        player = self.player_list[index]
        return {**self.build_private_game_info_fields(index),"riichi_candidate_cuts":player.riichi_candidate_cuts,
            "forced_cut_tiles":[player.last_drawn_tile] if player.ready_locked and has_draw_slot(player) else [],
            "kong_candidates":{"angang":self._kong_candidates(index),"jiagang":self._kong_candidates(index,True)}}

    def build_private_do_action_info(self, action_player, viewer_index):
        return self.build_private_game_info_fields(viewer_index) if action_player==viewer_index else {}

    def build_concealed_kong_mask(self, tiles): return [v for tile in tiles for v in (2,tile)]
