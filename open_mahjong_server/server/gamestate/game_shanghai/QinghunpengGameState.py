"""清混碰独立状态机；仅复用通用摸打、询问、重连和牌谱协议。"""

import asyncio
import random
from collections import Counter
from dataclasses import replace

from ...game_calculation.shanghai import qinghunpeng as calculation
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan import action_check as turn_actions
from ..game_taiwan.boardcast import (
    broadcast_game_start, broadcast_do_action, broadcast_result,
    broadcast_game_end, broadcast_refresh_player_tag_list,
)
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..public.game_record_manager import (
    capture_player_entry_order, init_game_record, init_game_round,
    player_action_record_deal, player_action_record_hu,
    player_action_record_round_end, end_game_record, remember_local_record_detail,
)
from ..public.hand_slot_utils import has_draw_slot
from ..public.logic_common import assign_competition_final_ranks
from ..public.random_seed_manager import setup_random_seed_system, derive_round_seed
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players


class QinghunpengGameState(TaiwanGameState):
    flower_tiles = calculation.FLOWERS
    structure_tiles = calculation.TILES

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        if room_data.get("sub_rule", calculation.SUB_RULE) != calculation.SUB_RULE:
            raise ValueError("清混碰状态机只接受 shanghai/qinghunpeng")
        super().__init__(game_server, {**room_data, "detailed_config": None},
                         calculation_service, db_manager, gamestate_id)
        self.room_rule = "shanghai"
        self.sub_rule = calculation.SUB_RULE
        self.open_cuohe = False
        self.rules = replace(
            self.rules, dead_wall_count=0, multi_win_mode="multiple_winners",
            ready_qualification_mode="disabled", public_ready_enabled=False,
            chow_discard_restriction_mode="same_tile",
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            missed_win_blocks_self_draw=False, missed_win_blocks_claims=False,
            seven_flowers_steal_eighth_enabled=False,
        )
        self.rules_dict = {"edition": "qinghunpeng", "flower_cap": 10}
        self.dead_wall_count = self.hepai_limit = 0
        self.huangfan_count = 0
        self.completed_win_hands = 0
        # 共用遵守服务端动作列表、食替禁切的上海机器人。
        from .bot import shanghai_smart_bot_action
        self.smart_bot_action = shanghai_smart_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self.contracts = set()
        self.opening_before_draw = None
        self.opening_claim_window = None
        self.opening_before_kong_draw = False
        for player in self.player_list:
            player.claim_sources = []
            player.passed_win_tiles = set()
            player.opening_flowers_done = False
            player.tag_list[:] = [tag for tag in player.tag_list if not tag.startswith("chengbao_")]

    def build_concealed_kong_mask(self, tiles):
        # 暗杠仍按暗杠记分，但四张牌公开亮明。
        return [value for tile in tiles for value in (0, tile)]

    def refresh_waits(self, player_index):
        player = self.player_list[player_index]
        player.waiting_tiles = calculation.waits(player.hand_tiles, player.combination_tiles)
        return player.waiting_tiles

    def score_candidate(self, player_index, source, tile=None, **_):
        player = self.player_list[player_index]
        hand = list(player.hand_tiles)
        if source != "self_draw":
            if tile is None or tile in player.passed_win_tiles:
                return None
            hand.append(tile)
        return calculation.score(
            hand, player.combination_tiles, player.huapai_list,
            self_draw=source == "self_draw",
            replacement=source == "self_draw" and self.last_draw_after_kong,
            last_tile=(self.last_draw_was_last if source == "self_draw"
                       else source == "discard" and not self.can_take_normal_tile()),
            rob_kong=source == "robbing_kong",
        )

    def ready_candidate_cuts(self, player_index):
        return {}  # 清混碰无敲牌、锁手或报听前置条件。

    def _register_initial_heavenly_ready(self):
        pass

    def _revoke_qualification(self, player_index, **_):
        pass

    def _mark_eight_flowers_if_ready(self, player_index, **_):
        pass

    def _liability_payer_for_win(self, *args):
        return None

    def enter_water(self, player_index):
        # 仅限制本次放过的同张牌，不产生台湾规则的全听口过水。
        if self.game_status not in ("waiting_action_after_cut", "waiting_action_qianggang"):
            return False  # 拒绝自摸的实际弃牌由 execute_cut 处理。
        tile = self.jiagang_tile if self.game_status == "waiting_action_qianggang" else None
        discards = self.player_list[self.current_player_index].discard_tiles
        if tile is None and discards:
            tile = discards[-1]
        if tile is not None:
            self.player_list[player_index].passed_win_tiles.add(tile)
        return False

    def _remember_claim_liability(self, player_index, payer, tile):
        self.player_list[player_index].claim_sources.append(payer)
        self.contracts = {
            tuple(sorted((index, source)))
            for index, player in enumerate(self.player_list)
            for source, count in Counter(player.claim_sources).items() if count >= 3
        }
        for index, player in enumerate(self.player_list):
            player.tag_list[:] = [tag for tag in player.tag_list if not tag.startswith("chengbao_")]
            player.tag_list.extend(f"chengbao_{other}" for other in sorted(self.contract_partners(index)))

    def contract_partners(self, index):
        return {b if a == index else a for a, b in self.contracts if index in (a, b)}

    @staticmethod
    def _fourth_exposed(player):
        return len(player.combination_tiles) == 4 and all(code[0] != "G" for code in player.combination_tiles)

    def _kong_allowed(self, index, tile, kind):
        player = self.player_list[index]
        if self.opening_before_draw is not None or any(t in self.flower_tiles for t in player.hand_tiles):
            return False
        if tile not in self.structure_tiles or not self.can_take_supplement_tile():
            return False
        if not has_draw_slot(player) and not self.opening_dealer_action:
            return False
        melds = list(player.combination_tiles)
        if kind == "added":
            if f"k{tile}" not in melds or tile not in player.hand_tiles:
                return False
            melds[melds.index(f"k{tile}")] = f"g{tile}"
        else:
            if player.hand_tiles.count(tile) != 4:
                return False
            melds.append(f"G{tile}")
        return calculation.melds_allow_starting_pattern(melds)

    def check_hand_actions(self, player_index):
        if self.opening_before_draw == player_index:
            return {index: ["buhua"] if index == player_index else [] for index in range(4)}
        result = turn_actions.check_action_hand_action(self, player_index)
        player = self.player_list[player_index]
        for action, kind in (("angang", "G"), ("jiagang", "added")):
            if action in result[player_index] and not any(
                self._kong_allowed(player_index, tile, kind) for tile in set(player.hand_tiles)
            ):
                result[player_index].remove(action)
        return result

    def check_discard_actions(self, tile):
        actions = {index: [] for index in range(4)}
        for distance in (1, 2, 3):
            index = (self.current_player_index + distance) % 4
            if self.opening_claim_window is not None and index != self.opening_claim_window:
                continue
            player = self.player_list[index]
            detail = self.score_candidate(index, "discard", tile)
            if detail:
                action = turn_actions.hu_action_for_player(self.current_player_index, index)
                actions[index].append(action)
                self.result_dict[action] = detail
            if self.can_take_normal_tile():
                possible = ["peng", "gang"]
                if distance == 1 and tile in calculation.NUMBERS:
                    possible += ["chi_left", "chi_mid", "chi_right"]
                for action in possible:
                    code, required, _ = self._claim_mask(action, tile, "left")
                    if (any(player.hand_tiles.count(t) < required.count(t) for t in set(required))
                            or not calculation.melds_allow_starting_pattern(player.combination_tiles + [code])):
                        continue
                    if action == "gang" and not self.can_take_supplement_tile():
                        continue
                    remaining = list(player.hand_tiles)
                    for t in required:
                        remaining.remove(t)
                    fourth = len(player.combination_tiles) == 3 and all(c[0] != "G" for c in player.combination_tiles)
                    if action != "gang" and not any(t != tile or fourth for t in remaining):
                        continue
                    actions[index].append(action)
            if actions[index]:
                actions[index].append("pass")
        return actions

    def check_added_kong_actions(self, tile):
        return turn_actions.check_action_jiagang(self, tile)

    def after_claim_actions(self):
        player = self.player_list[self.current_player_index]
        action = "buhua" if any(t in self.flower_tiles for t in player.hand_tiles) else "cut"
        return {index: [action] if index == self.current_player_index else [] for index in range(4)}

    async def execute_claim(self, player_index, action_type):
        discards = self.player_list[self.current_player_index].discard_tiles
        if not discards or action_type not in self.check_discard_actions(discards[-1])[player_index]:
            return
        await super().execute_claim(player_index, action_type)
        self.opening_claim_window = None
        player = self.player_list[player_index]
        if self._fourth_exposed(player):
            player.kuikae_forbidden_tiles.clear()
        for index in range(4):
            self._record_state("chengbao", index, ",".join(map(str, sorted(self.contract_partners(index)))))
        await broadcast_refresh_player_tag_list(self)

    async def execute_angang(self, player_index, target_tile):
        if self._kong_allowed(player_index, target_tile, "G"):
            await super().execute_angang(player_index, target_tile)

    async def execute_jiagang(self, player_index, target_tile):
        if self._kong_allowed(player_index, target_tile, "added"):
            await super().execute_jiagang(player_index, target_tile)

    async def execute_cut(self, player_index, action_data, *, declare_ready=False, is_timeout_action=False):
        player = self.player_list[player_index]
        if declare_ready or any(tile in self.flower_tiles for tile in player.hand_tiles):
            return
        if self.opening_before_draw is not None:
            return
        declined_win = self.score_candidate(player_index, "self_draw") is not None
        before = player.discard_count
        await super().execute_cut(player_index, action_data, is_timeout_action=is_timeout_action)
        if player.discard_count != before and declined_win and not self._fourth_exposed(player):
            player.passed_win_tiles.add(player.discard_tiles[-1])

    async def _opening_flower_replacement(self):
        # 不设补花轮；庄家和鸣牌者在正常手牌窗口被强制补花。
        pass

    async def execute_buhua(self, player_index):
        await super().execute_buhua(player_index)
        player = self.player_list[player_index]
        if self.game_status == "END" or any(t in self.flower_tiles for t in player.hand_tiles):
            return
        player.opening_flowers_done = True
        if self.opening_before_kong_draw:
            self.opening_before_kong_draw = False
            self.game_status = "deal_card_after_gang"
            return
        if self.opening_before_draw == player_index:
            # 上一轮其他家的响应已结束，仅让补完起手花的下家重新响应。
            self.opening_before_draw = None
            self.opening_claim_window = player_index
            player.has_draw_slot = False
            player.last_drawn_tile = None
            self.current_player_index = (player_index - 1) % 4
            tile = self.player_list[self.current_player_index].discard_tiles[-1]
            self.action_dict = self.check_discard_actions(tile)
            self.game_status = "waiting_action_after_cut" if any(self.action_dict.values()) else "deal_card"

    async def _deal_normal(self):
        index = (self.current_player_index + 1) % 4
        player = self.player_list[index]
        self.opening_claim_window = None
        if not player.opening_flowers_done and any(t in self.flower_tiles for t in player.hand_tiles):
            self.opening_before_draw = index
            self.current_player_index = index
            self.action_dict = self.check_hand_actions(index)
            self.game_status = "waiting_hand_action"
            return
        player.opening_flowers_done = True
        if self.can_take_normal_tile():
            player.passed_win_tiles.clear()
        await super()._deal_normal()

    async def _deal_supplement(self):
        index = self.current_player_index
        if any(t in self.flower_tiles for t in self.player_list[index].hand_tiles):
            self.opening_before_kong_draw = True
            self.action_dict = {i: ["buhua"] if i == index else [] for i in range(4)}
            self.game_status = "waiting_hand_action"
            return
        if self.can_take_supplement_tile():
            self.player_list[self.current_player_index].passed_win_tiles.clear()
        await super()._deal_supplement()

    async def _draw_tail_for_player(self, player_index, *, opening):
        if self.can_take_supplement_tile():
            self.player_list[player_index].passed_win_tiles.clear()
        return await super()._draw_tail_for_player(player_index, opening=opening)

    def build_private_hand_action_info(self, player_index):
        return {"forbidden_cut_tiles": sorted(self.player_list[player_index].kuikae_forbidden_tiles)}

    def build_private_do_action_info(self, action_player, viewer_index):
        return {}

    def build_game_info_fields(self):
        return {"detailed_config": {**self.rules_dict, "huangfan_count": self.huangfan_count,
                                    "completed_win_hands": self.completed_win_hands,
                                    "target_hands": self.max_round * 4}}

    def build_record_round_fields(self):
        return self.build_game_info_fields()

    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        wall = [tile for tile in self.structure_tiles for _ in range(4)] + list(self.flower_tiles)
        random.Random(self.round_random_seed).shuffle(wall)
        self.tiles_list = wall
        for player in self.player_list:
            player.hand_tiles = [self.tiles_list.pop(0) for _ in range(13)]
        self.player_list[0].hand_tiles.append(self.tiles_list.pop(0))

    def next_dealer(self):
        if len(self.pending_winners) > 1:
            return self.pending_winners[0]["payer"]
        return self.pending_winners[0]["index"] if self.pending_winners else 0

    def _settlement_for_winner(self, item):
        return calculation.payments(
            item["index"], item["detail"]["base_score"], discarder=item.get("payer"),
            partners=self.contract_partners(item["index"]), rob_kong=item["source"] == "robbing_kong",
        )

    async def _settle_hand(self, scores_before):
        won = bool(self.pending_winners)
        doubled = self.huangfan_count > 0
        remaining_debt = max(0, self.huangfan_count - 1) if won else self.huangfan_count + 1
        match_end = self.current_round >= self.max_round * 4 and remaining_debt == 0
        next_status = "match_end" if match_end else "round_end_by_ready"
        for number, item in enumerate(self.pending_winners):
            if doubled:
                item = {**item, "detail": {**item["detail"],
                    "base_score": item["detail"]["base_score"] * 2,
                    "fan_names": list(item["detail"]["fan_names"]) + ["荒番（×2）"]}}
            changes = self._settlement_for_winner(item)
            detail = item["detail"]
            winner = self.player_list[item["index"]]
            labels = list(detail["fan_names"])
            if self.contract_partners(item["index"]):
                labels.append("承包结算")
            for index, change in changes.items():
                player = self.player_list[index]
                player.score += change
                player.score_history.append(f"{change:+d}" if change else "0")
                player.round_number_history.append(self.current_round)
            counter = winner.record_counter
            counter.recorded_fans.append(labels)
            counter.win_score += detail["base_score"]
            counter.win_turn += self.xunmu
            if item["source"] == "self_draw":
                counter.zimo_times += 1
            else:
                counter.dianhe_times += 1
                payer = self.player_list[item["payer"]].record_counter
                payer.fangchong_times += 1
                payer.fangchong_score += -changes[item["payer"]]
            ron = item["source"] == "discard"
            last = number == len(self.pending_winners) - 1
            multi = len(self.pending_winners) > 1
            player_action_record_hu(
                self, hu_class=item["hu_class"], hu_score=detail["base_score"], hu_fan=labels,
                hepai_player_index=item["index"], score_changes=[changes[i] for i in range(4)],
                hepai_tile=item["tile"], multi_ron=multi if ron else None,
                ron_discarder_index=item["payer"] if ron else None, recycle_discard=last if ron else None,
            )
            hand = list(winner.hand_tiles) + ([item["tile"]] if item["source"] != "self_draw" else [])
            await broadcast_result(
                self, hepai_player_index=item["index"],
                player_to_score={p.player_index: p.score for p in self.player_list},
                hu_score=detail["base_score"], hu_fan=labels, hu_class=item["hu_class"],
                hepai_player_hand=hand, hepai_player_huapai=winner.huapai_list,
                hepai_player_combination_mask=winner.combination_mask,
                score_changes={p.original_player_index: changes[p.player_index] for p in self.player_list},
                revealed_angang_masks=build_revealed_angang_masks(self.player_list),
                hepai_tile=item["tile"], multi_ron=multi if ron else None,
                is_qianggang=True if item["source"] == "robbing_kong" else None,
                ron_discarder_index=item["payer"] if item["source"] != "self_draw" else None,
                recycle_discard=last if ron else None,
                next_status=next_status if last else "round_continue",
            )
            if not (match_end and last):
                await self.run_hu_result_ready_phase(len(labels))
        if not self.pending_winners:
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            await broadcast_result(self, hu_class="liuju", score_changes={i: 0 for i in range(4)},
                                   revealed_angang_masks=build_revealed_angang_masks(self.player_list),
                                   next_status=next_status)
            for player in self.player_list:
                player.score_history.append("0")
                player.round_number_history.append(self.current_round)
            await asyncio.sleep(liuju_ready_wait_seconds())
        record_fulu_rounds_for_players(self.player_list)
        for player in self.player_list:
            player.record_counter.round_score_total += player.score - scores_before[player.original_player_index]
        player_action_record_round_end(self)
        if won:
            self.completed_win_hands += 1
        self.huangfan_count = remaining_debt
        return match_end

    async def game_loop_chinese(self):
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for index, player in enumerate(self.player_list):
            player.player_index = player.original_player_index = index
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while True:
            before = {p.original_player_index: p.score for p in self.player_list}
            self._reset_hand_runtime()
            self.init_tiles()
            self.current_player_index = 0
            await broadcast_game_start(self)
            init_game_round(self)
            await self._run_hand()
            if await self._settle_hand(before):
                break
            dealer = self.next_dealer()
            for player in self.player_list:
                player.player_index = (player.player_index - dealer) % 4
                for values in (player.hand_tiles, player.huapai_list, player.discard_tiles,
                               player.discard_origin_tiles, player.combination_tiles, player.combination_mask):
                    values.clear()
                player.remaining_time = self.round_time
            self.player_list.sort(key=lambda p: p.player_index)
            # 所有局都推进局制，到期后的结束条件由荒番欠局是否清零决定。
            self.current_round += 1
            self.round_index += 1
            self.xunmu = 1
        end_game_record(self)
        assign_competition_final_ranks(self.player_list)
        match_type = f"{self.max_round}/4"
        store = getattr(self.db_manager, "store_shanghai_game_record", None)
        game_id = store(self.game_record, self.player_list, self.room_type, match_type) if store else None
        remember_local_record_detail(self, game_id, match_type)
        await broadcast_game_end(self)
        await self.spectator_manager.send_final_record_and_close()
        await self.game_server.gamestate_manager.cleanup_game_state_complete(gamestate_id=self.gamestate_id)

