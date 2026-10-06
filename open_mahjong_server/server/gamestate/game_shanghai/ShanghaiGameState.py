"""上海敲麻状态机，共用已有报听回合制的动作执行、重连与观战协议。"""

import asyncio
import random
from collections import Counter
from dataclasses import replace

from ...game_calculation.shanghai import qiaoma
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
from . import action_timing
from ..public.logic_common import assign_competition_final_ranks
from ..public.random_seed_manager import setup_random_seed_system, derive_round_seed
from ..public.round_end_timing import liuju_ready_wait_seconds
from ...database.fulu_utils import record_fulu_rounds_for_players


class ShanghaiGameState(TaiwanGameState):
    flower_tiles = qiaoma.FLOWERS
    structure_tiles = qiaoma.TILES
    claim_response_seconds = None

    def claim_response_limit(self, player_index):
        # 线上所有实际询问都执行房间步时 + 本局剩余局时，不套用线下报牌上限。
        return None

    def claim_clock(self, player, reconnecting=False):
        return action_timing.clock(self, player, reconnecting)

    action_clock = claim_clock

    def claim_clock_fields(self, player, reconnecting=False):
        return action_timing.clock_fields(self, player, reconnecting)

    async def send_to_realtime_spectators(self, player_index, response):
        from ..public.spectator_rules import deliver_realtime_spectator_message

        await deliver_realtime_spectator_message(
            self, player_index, response,
            prepare_payload=lambda packet: self._refresh_spectator_clock(player_index, packet),
        )

    def _refresh_spectator_clock(self, player_index, packet):
        info = packet.get("ask_hand_action_info") or packet.get("ask_other_action_info")
        if info is None or info.get("action_tick") != self.server_action_tick:
            return
        if not self.is_action_pending(player_index):
            info.update(remaining_time=0, step_remaining=0,
                        remaining_time_ms=0, step_remaining_ms=0, action_list=[])
            return
        player = self.player_list[player_index]
        info["remaining_time"], info["step_remaining"] = self.claim_clock(player, reconnecting=True)
        info.update(self.claim_clock_fields(player, reconnecting=True))

    def on_action_window_delivered(self, player_index):
        value = action_timing.window(self, player_index)
        if not value.delivery_recorded:
            value.monotonic_origin = action_timing.time.monotonic()
            value.delivery_recorded = True

    def on_action_window_broadcast(self):
        self._qiaoma_broadcast_monotonic = action_timing.time.monotonic()
        self._qiaoma_broadcast_wall = self._ask_broadcast_time

    def is_action_pending(self, player_index):
        return bool(self.action_dict.get(player_index))

    def is_valid_timed_response(self, player_index, data):
        """无效的锁手/报敲提交留在原窗口，不能结账后重新获得一步时间。"""
        if self.game_status != "waiting_hand_action":
            return True
        action = data.get("action_type")
        player = self.player_list[player_index]
        if action in ("cut", "riichi_cut"):
            tile = data.get("TileId")
            if tile not in player.hand_tiles or tile in self.flower_tiles or tile in player.kuikae_forbidden_tiles:
                return False
            if player.ready_locked and player.last_drawn_tile is not None and tile != player.last_drawn_tile:
                return False
            if action == "riichi_cut" and tile not in self.ready_candidate_cuts(player_index):
                return False
        elif action in ("angang", "jiagang"):
            tile = data.get("target_tile")
            if tile not in self.structure_tiles:
                return False
            if action == "angang" and player.hand_tiles.count(tile) < 4:
                return False
            if action == "jiagang" and (tile not in player.hand_tiles or f"k{tile}" not in player.combination_tiles):
                return False
            if player.ready_locked and not self._kong_preserves_waits(player_index, tile, "G" if action == "angang" else "added"):
                return False
        return True

    async def collect_action_responses(self):
        return await action_timing.collect_responses(self)

    def prepare_action_window(self):
        super().prepare_action_window()
        self._qiaoma_window_key = None
        # 真人可以在广播尚未发完其他座位时就回复，不能等收集器才开放输入。
        self.waiting_players_list = [i for i, actions in self.action_dict.items() if actions]

    def _reset_hand_runtime(self):
        for player in self.player_list:
            action_timing.set_bank(player, float(self.round_time))
        self._qiaoma_window_key = None
        self._qiaoma_deadlines = {}
        super()._reset_hand_runtime()

    def build_concealed_kong_mask(self, tiles):
        return [value for position, tile in enumerate(tiles)
                for value in (2 if position in (0, 3) else 0, tile)]

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        if room_data.get("sub_rule", qiaoma.SUB_RULE) != qiaoma.SUB_RULE:
            raise ValueError("尚未开放的上海麻将子规则")
        super().__init__(game_server, {**room_data, "detailed_config": None},
                         calculation_service, db_manager, gamestate_id)
        self.action_queues = {i: action_timing.ActionQueue() for i in range(4)}
        self.room_rule = "shanghai"
        self.sub_rule = qiaoma.SUB_RULE
        self.open_cuohe = False
        # 仅复用回合执行器需要的行为配置，不向房间暴露台湾馆规。
        self.rules = replace(
            self.rules, dead_wall_count=0, multi_win_mode="multiple_winners",
            ready_qualification_mode="disabled", public_ready_enabled=True,
            declared_ready_win_policy="force_win", declared_ready_auto_added_kong=True,
            allow_kong_from_upper_discard=True, direct_kong_replacement_win_allowed=True,
            missed_win_blocks_self_draw=False, missed_win_blocks_claims=False,
            seven_flowers_steal_eighth_enabled=False,
        )
        self.rules_dict = {"edition": "MIL2024", "fan_cap": 3}
        self.dead_wall_count = 0
        self.hepai_limit = 1 if room_data.get("hepai_limit", 0) == 1 else 0
        from .bot import shanghai_smart_bot_action
        self.smart_bot_action = shanghai_smart_bot_action

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        self.contracts = set()
        self.opening_claim_exempt = set()
        for player in self.player_list:
            player.claim_sources = []
            player.passed_pungs = set()
            player.opening_flowers_done = False
            player.locked_waits = set()
            player.tag_list[:] = [tag for tag in player.tag_list if not tag.startswith("chengbao_")]

    def refresh_waits(self, player_index):
        player = self.player_list[player_index]
        player.waiting_tiles = qiaoma.waits(player.hand_tiles, player.combination_tiles)
        return player.waiting_tiles

    def score_candidate(self, player_index, source, tile=None, **_):
        player = self.player_list[player_index]
        hand = list(player.hand_tiles)
        if source != "self_draw":
            if tile is None:
                return None
            hand.append(tile)
        return qiaoma.score(
            hand, player.combination_tiles, player.huapai_list,
            declared_ready=player.declared_ready, self_draw=source == "self_draw",
            replacement=source == "self_draw" and self.last_draw_after_kong,
            rob_kong=source == "robbing_kong", min_fan=self.hepai_limit,
        )

    def ready_candidate_cuts(self, player_index):
        player = self.player_list[player_index]
        if player.ready_locked or any(tile in self.flower_tiles for tile in player.hand_tiles):
            return {}
        candidates = {}
        for tile in sorted(set(player.hand_tiles) - player.kuikae_forbidden_tiles):
            hand = list(player.hand_tiles)
            hand.remove(tile)
            waiting = qiaoma.waits(hand, player.combination_tiles)
            if waiting:
                candidates[tile] = sorted(waiting)
        return candidates

    def _declare_ready(self, player_index):
        super()._declare_ready(player_index)
        self.player_list[player_index].locked_waits = set(self.refresh_waits(player_index))

    def _register_initial_heavenly_ready(self):
        pass

    def _revoke_qualification(self, player_index, **_):
        # 敲牌后一直锁手；合法杠不能撤销声明。
        pass

    def enter_water(self, player_index):
        return False  # 敲牌后强制和牌，不存在主动过和。

    def _mark_eight_flowers_if_ready(self, player_index, **_):
        pass

    def _liability_payer_for_win(self, *args):
        return None  # 上海使用对称、可多人的承包关系。

    def _remember_claim_liability(self, player_index, payer, tile):
        giver = self.player_list[payer]
        self.player_list[player_index].claim_sources.append(None if giver.declared_ready else payer)
        self._refresh_contracts()

    def _refresh_contracts(self):
        contracts = set()
        for index, player in enumerate(self.player_list):
            public = [code for code in player.combination_tiles if code[0] != "G"]
            counts = Counter(source for source in player.claim_sources if source is not None)
            for source, count in counts.items():
                if count >= 4 or (count >= 3 and qiaoma.contract_patterns(public)):
                    contracts.add(tuple(sorted((index, source))))
        self.contracts = contracts
        for index, player in enumerate(self.player_list):
            player.tag_list[:] = [tag for tag in player.tag_list if not tag.startswith("chengbao_")]
            player.tag_list.extend(f"chengbao_{other}" for other in sorted(self.contract_partners(index)))

    def contract_partners(self, index):
        return {b if a == index else a for a, b in self.contracts if index in (a, b)}

    def _kong_preserves_waits(self, player_index, tile, kind):
        player = self.player_list[player_index]
        hand, melds = list(player.hand_tiles), list(player.combination_tiles)
        count = 4 if kind == "G" else 3 if kind == "g" else 1
        if hand.count(tile) < count or not self.can_take_supplement_tile():
            return False
        for _ in range(count):
            hand.remove(tile)
        if kind == "added":
            if f"k{tile}" not in melds:
                return False
            melds[melds.index(f"k{tile}")] = f"g{tile}"
        else:
            melds.append(f"{kind}{tile}")
        if not player.locked_waits or qiaoma.waits(hand, melds) != player.locked_waits:
            return False
        if kind != "added":
            before = list(player.hand_tiles)
            if kind == "G":
                # 暗杠必须使用本次摸入张；禁止把原听牌面子的搭子拆成一杠。
                if tile != player.last_drawn_tile:
                    return False
                before.remove(tile)
            for winning in player.locked_waits:
                if any(f"k{tile}" not in sets
                       for _, sets in qiaoma.decompositions(before + [winning], player.combination_tiles)):
                    return False
        return True

    def check_hand_actions(self, player_index):
        player = self.player_list[player_index]
        result = {index: [] for index in range(4)}
        if any(tile in self.flower_tiles for tile in player.hand_tiles):
            result[player_index] = ["buhua"]
            return result
        result = turn_actions.check_action_hand_action(self, player_index)
        if "hu_self" in result[player_index]:
            result[player_index] = ["hu_self"]
            return result
        if not has_draw_slot(player) and not self.opening_dealer_action:
            result[player_index] = [a for a in result[player_index] if a not in ("angang", "jiagang")]
        if player.ready_locked:
            if any(self._kong_preserves_waits(player_index, tile, "G") for tile in set(player.hand_tiles)):
                result[player_index].insert(0, "angang")
            if any(code[0] == "k" and self._kong_preserves_waits(player_index, int(code[1:]), "added")
                   for code in player.combination_tiles):
                result[player_index].insert(0, "jiagang")
        if not self.can_take_supplement_tile():
            result[player_index] = [a for a in result[player_index] if a not in ("angang", "jiagang")]
        return result

    def check_discard_actions(self, tile):
        actions = turn_actions.check_action_after_cut(self, tile)
        for index, player in enumerate(self.player_list):
            if index == self.current_player_index:
                continue
            if tile in player.passed_pungs:
                actions[index] = [a for a in actions[index] if a != "peng"]
            if player.ready_locked and self._kong_preserves_waits(index, tile, "g"):
                actions[index].extend(["gang", "pass"])
            hu = [a for a in actions[index] if a in turn_actions.HU_ACTIONS]
            if hu:
                actions[index] = hu
            elif actions[index] == ["pass"]:
                actions[index] = []
        return actions

    def check_added_kong_actions(self, tile):
        return turn_actions.check_action_jiagang(self, tile)

    def after_claim_actions(self):
        index = self.current_player_index
        player = self.player_list[index]
        player.riichi_candidate_cuts = self.ready_candidate_cuts(index)
        return {i: (["riichi_cut", "cut"] if player.riichi_candidate_cuts else ["cut"])
                if i == index else [] for i in range(4)}

    async def execute_claim(self, player_index, action_type):
        tile = self.player_list[self.current_player_index].discard_tiles[-1]
        if action_type not in self.check_discard_actions(tile)[player_index]:
            return
        await super().execute_claim(player_index, action_type)
        for index in range(4):
            self._record_state("chengbao", index, ",".join(map(str, sorted(self.contract_partners(index)))))
        await broadcast_refresh_player_tag_list(self)

    async def execute_angang(self, player_index, target_tile):
        player = self.player_list[player_index]
        if "angang" not in self.check_hand_actions(player_index)[player_index]:
            return
        if player.ready_locked and not self._kong_preserves_waits(player_index, target_tile, "G"):
            return
        await super().execute_angang(player_index, target_tile)

    async def execute_jiagang(self, player_index, target_tile):
        player = self.player_list[player_index]
        if target_tile not in self.structure_tiles or not self.can_take_supplement_tile():
            return
        if not has_draw_slot(player) and not self.opening_dealer_action:
            return
        if player.ready_locked and not self._kong_preserves_waits(player_index, target_tile, "added"):
            return
        await super().execute_jiagang(player_index, target_tile)

    def _declared_ready_auto_jiagang_tile(self, player_index):
        player = self.player_list[player_index]
        tile = super()._declared_ready_auto_jiagang_tile(player_index)
        # 无花时可保留十花；已有花且只能加杠时必须加杠。
        has_flower = qiaoma.physical_flower_count(player.huapai_list, player.combination_tiles) > 0
        if not has_flower:
            has_flower = any(player.hand_tiles.count(wind) >= 3 for wind in qiaoma.WINDS)
        if tile is not None and has_flower and self._kong_preserves_waits(player_index, tile, "added"):
            return tile
        return None

    async def execute_cut(self, player_index, action_data, *, declare_ready=False, is_timeout_action=False):
        # 服务端执行入口也挡住强制和牌／加杠，不能靠伪造切牌绕过。
        if any(tile in self.flower_tiles for tile in self.player_list[player_index].hand_tiles):
            return
        if self.score_candidate(player_index, "self_draw"):
            self.accept_self_draw(player_index)
            return
        forced = self._declared_ready_auto_jiagang_tile(player_index)
        if forced is not None:
            await self.execute_jiagang(player_index, forced)
            return
        before = self.player_list[player_index].discard_count
        await super().execute_cut(player_index, action_data, declare_ready=declare_ready,
                                  is_timeout_action=is_timeout_action)
        if before == self.player_list[player_index].discard_count:
            return
        self.opening_claim_exempt.clear()
        # 每家首次摸牌前先补初始花，随后仍能响应这张上家弃牌。
        for distance in (1, 2, 3):
            index = (player_index + distance) % 4
            if not self.player_list[index].opening_flowers_done:
                await self._complete_opening_flowers(index)
                if self.game_status == "END":
                    return
        tile = self.player_list[player_index].discard_tiles[-1]
        self.action_dict = self.check_discard_actions(tile)
        self.game_status = "waiting_action_after_cut" if any(self.action_dict.values()) else "deal_card"

    async def resolve_discard_responses(self, responses, allowed):
        tile = self.player_list[self.current_player_index].discard_tiles[-1]
        # 首次出牌回合可过碰；此后同张过碰到本人再次摸牌前失效。
        for index, actions in allowed.items():
            if ("peng" in actions and self.player_list[index].discard_count > 0
                    and responses.get(index, {}).get("action_type") not in ("peng", "gang")):
                self.player_list[index].passed_pungs.add(tile)
        await super().resolve_discard_responses(responses, allowed)

    async def _deal_normal(self):
        self.player_list[(self.current_player_index + 1) % 4].passed_pungs.clear()
        await super()._deal_normal()

    async def _deal_supplement(self):
        self.player_list[self.current_player_index].passed_pungs.clear()
        await super()._deal_supplement()

    async def _replace_one_flower(self, player_index, flower_index, *, is_drawn, opening):
        player = self.player_list[player_index]
        flower = player.hand_tiles.pop(flower_index)
        player.huapai_list.append(flower)
        await self._broadcast_flower(player_index, flower, is_drawn=is_drawn)
        if not self.tiles_list:
            self.draw_reason = "terminal_flower"
            self.game_status = "END"
            return False
        tile = self._take_supplement_tile()
        player.hand_tiles.append(tile)
        player.has_draw_slot = not opening
        player.last_drawn_tile = tile if not opening else None
        player.passed_pungs.clear()
        previous = self.current_player_index
        self.current_player_index = player_index
        player_action_record_deal(self, deal_tile=tile, deal_type="gd")
        self.current_player_index = previous
        await broadcast_do_action(self, action_list=["deal_buhua_tile"],
                                  action_player=player_index, deal_tile=tile)
        return True

    async def _complete_opening_flowers(self, player_index):
        player = self.player_list[player_index]
        if any(tile in self.flower_tiles for tile in player.hand_tiles):
            self.opening_claim_exempt.add(player_index)
        while any(tile in self.flower_tiles for tile in player.hand_tiles):
            index = next(i for i, tile in enumerate(player.hand_tiles) if tile in self.flower_tiles)
            if not await self._replace_one_flower(player_index, index, is_drawn=False, opening=True):
                return
        player.opening_flowers_done = True

    async def _opening_flower_replacement(self):
        await self._complete_opening_flowers(0)

    def build_private_hand_action_info(self, player_index):
        player = self.player_list[player_index]
        value = action_timing.window(self, player_index)
        return {"riichi_candidate_cuts": player.riichi_candidate_cuts,
                "forbidden_cut_tiles": sorted(player.kuikae_forbidden_tiles),
                **action_timing.clock_fields(self, player, reconnecting=value.monotonic_origin is not None)}

    def build_private_do_action_info(self, action_player, viewer_index):
        return {}

    def init_tiles(self):
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        wall = [tile for tile in qiaoma.TILES + (45, 46, 47) for _ in range(4)]
        wall.extend(range(51, 59))
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
        payer = item.get("payer")
        return qiaoma.payments(
            item["index"], item["detail"]["base_score"], discarder=payer,
            partners=self.contract_partners(item["index"]),
            discarder_ready=payer is not None and self.player_list[payer].declared_ready,
        )

    async def _settle_hand(self, scores_before):
        match_end = self.current_round >= self.max_round * 4
        next_status = "match_end" if match_end else "round_end_by_ready"
        for number, item in enumerate(self.pending_winners):
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
        return match_end

    async def game_loop_chinese(self):
        self.master_seed, self.salt, self.commitment, self.isPlayerSetRandomSeed = setup_random_seed_system(self.room_random_seed or None)
        capture_player_entry_order(self)
        random.Random(self.master_seed).shuffle(self.player_list)
        for index, player in enumerate(self.player_list):
            player.player_index = player.original_player_index = index
        init_game_record(self)
        self.game_record["game_title"].update(self.build_record_title_fields())
        while self.current_round <= self.max_round * 4:
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
