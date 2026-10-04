"""血流成河独立状态机；复用四川的连接、牌墙与通用行牌基础设施。"""
from __future__ import annotations
import asyncio
import json
import random
import logging
import time
from typing import Dict, List, Optional, Tuple, Set
from .boardcast import broadcast_result, broadcast_xueliu_continue, broadcast_dingque_done
from ..public.game_record_manager import player_action_record_hu, player_action_record_liuju, player_action_record_sichuan_liuju_step, player_action_record_gang_refund
from ..public.round_end_timing import ROUND_END_HAND_REVEAL_SEC, sichuan_chajiao_panel_wait_seconds
from ...game_calculation.sichuan.xueliu_rules import XUELIU_RULE_PROFILE, XUELIU_EXCHANGE_RULE_PROFILE, is_xueliu_sub_rule
from .SichuanGameState import SichuanGameState, SichuanPlayer
from .action_check import refresh_waiting_tiles
from ..public.game_record_manager import init_game_round
from ..public.hand_slot_utils import clear_draw_slot
logger = logging.getLogger(__name__)

class XueliuGameState(SichuanGameState):
    is_xueliu = True

    def _configure_rule(self):
        if not is_xueliu_sub_rule(self.sub_rule):
            raise ValueError('不支持的血流子规则')
        self.xueliu_exchange = self.sub_rule == 'sichuan/xueliu_exchange'
        self.xueliu_meld_count = 4 if self.xueliu_exchange else 3
        self.xueliu_opening_action = 'xueliu_exchange_three' if self.xueliu_exchange else 'xueliu_throw_three'
        self.xueliu_rule_profile = (XUELIU_EXCHANGE_RULE_PROFILE if self.xueliu_exchange else XUELIU_RULE_PROFILE).as_dict()

    def _record_rule_profile(self):
        self.game_record['game_title']['xueliu_rule_profile'] = dict(self.xueliu_rule_profile)
        self.game_record['game_title']['detailed_config'] = {'xueliu_exchange_scoring': self.xueliu_exchange}

    async def _opening_phase(self):
        await self._xueliu_throw_three_phase()
        for player in self.player_list:
            refresh_waiting_tiles(self, player.player_index, is_first_action=player.player_index == self.dealer_index)
        # 回放从开局选牌完成后的手牌开始；弃置牌另存，避免以 13/14 张回放。
        init_game_round(self)
        self.game_record['game_round'][f'round_index_{self.round_index}']['xueliu_throw_tiles'] = {
            p.player_index: list(p.xueliu_throw_tiles) for p in self.player_list
        }
        if self.xueliu_exchange:
            self.game_record['game_round'][f'round_index_{self.round_index}']['xueliu_exchange_direction'] = self.xueliu_exchange_direction
        await self.broadcast_game_start()
        if self.xueliu_exchange:
            # 先同步换回的手牌，再让四家独立选定缺；弃三张只要求和牌缺门。
            await self._dingque_phase()
            await broadcast_dingque_done(self)
            self.game_record['game_round'][f'round_index_{self.round_index}']['dingque_suits'] = {
                p.player_index: p.dingque_suit for p in self.player_list
            }
            for player in self.player_list:
                refresh_waiting_tiles(self, player.player_index, is_first_action=player.player_index == self.dealer_index)

    async def _xueliu_throw_three_phase(self):
        """同时选三张同花牌；弃牌版移出对局，换牌版统一按随机方向交换。"""
        self.game_status = 'waiting_xueliu_throw'
        self.action_dict = {i: [self.xueliu_opening_action] for i in range(4)}
        self.waiting_players_list = list(range(4))
        self.server_action_tick += 1
        for p in self.player_list:
            p.remaining_time = self.round_time
        for i in range(4):
            while not self.action_queues[i].empty():
                try:
                    self.action_queues[i].get_nowait()
                except Exception:
                    break
            self.action_events[i].clear()
        from .boardcast import broadcast_xueliu_throw_three_ask
        self.xueliu_throw_deadline = time.time() + max(self.step_time, 10)
        await broadcast_xueliu_throw_three_ask(self)
        pending = set(range(4))
        deadline = self.xueliu_throw_deadline
        while pending and time.time() < deadline:
            tasks = [asyncio.create_task(self.action_events[i].wait()) for i in pending]
            try:
                await asyncio.wait(tasks, timeout=1, return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in tasks:
                    if not task.done():
                        task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
            for i in list(pending):
                if not self.action_events[i].is_set():
                    continue
                chosen = None
                while not self.action_queues[i].empty():
                    data = self.action_queues[i].get_nowait()
                    if data.get('_action_tick', self.server_action_tick) != self.server_action_tick or data.get('action_type') != self.xueliu_opening_action:
                        continue
                    chosen = self._validate_xueliu_throw_tiles(i, data.get('selected_tiles'))
                    if chosen is not None:
                        break
                if chosen is None:
                    self.action_events[i].clear()
                    continue
                self._consume_xueliu_throw_tiles(i, chosen)
                self.action_dict[i] = []
                self.action_events[i].clear()
                pending.discard(i)
        for i in pending:
            chosen = self._default_xueliu_throw_tiles(i)
            self._consume_xueliu_throw_tiles(i, chosen)
            self.action_dict[i] = []
            self.action_events[i].clear()
        if self.xueliu_exchange:
            self._exchange_selected_tiles()
        self.waiting_players_list = []
        logger.info('血流%s完成: %s', '换三张' if self.xueliu_exchange else '弃三张', [getattr(p, 'xueliu_throw_tiles', []) for p in self.player_list])

    def _exchange_selected_tiles(self):
        # 使用本局种子独立派生，选牌全收齐后才发牌，不改牌墙或影响洗牌序列。
        direction = random.Random(f'{self.round_random_seed}:exchange_three').choice((1, 2, 3))
        self.xueliu_exchange_direction = direction
        outgoing = [list(player.xueliu_throw_tiles) for player in self.player_list]
        for index, tiles in enumerate(outgoing):
            self.player_list[(index + direction) % 4].hand_tiles.extend(tiles)

    def _validate_xueliu_throw_tiles(self, player_index: int, selected_tiles) -> Optional[List[int]]:
        if not isinstance(selected_tiles, (list, tuple)) or len(selected_tiles) != 3:
            return None
        if any(type(tile) is not int or tile // 10 not in (1, 2, 3) or not 1 <= tile % 10 <= 9 for tile in selected_tiles):
            return None
        chosen = list(selected_tiles)
        player = self.player_list[player_index]
        if any((player.hand_tiles.count(tile) < chosen.count(tile) for tile in set(chosen))):
            return None
        suits = {tile // 10 for tile in chosen}
        if len(suits) != 1 or next(iter(suits), 0) not in (1, 2, 3):
            return None
        return chosen

    def _default_xueliu_throw_tiles(self, player_index: int) -> List[int]:
        from .xueliu_bot import choose_opening
        return choose_opening(self.player_list[player_index].hand_tiles, self.xueliu_exchange)

    def _consume_xueliu_throw_tiles(self, player_index: int, chosen: List[int]) -> None:
        player = self.player_list[player_index]
        for tile in chosen:
            player.hand_tiles.remove(tile)
        player.xueliu_throw_tiles = list(chosen)

    def _xueliu_all_tiles(self, player: SichuanPlayer) -> List[int]:
        """Return logical tiles for missing-suit/flower-pig checks."""
        tiles = list(player.hand_tiles)
        for meld in player.combination_tiles:
            try:
                tile = int(meld[1:])
            except (TypeError, ValueError):
                continue
            if meld[:1] in ('k', 'K', 'g', 'G'):
                tiles.extend([tile, tile, tile])
            elif meld[:1] in ('s', 'S'):
                tiles.extend([tile - 1, tile, tile + 1])
        return tiles

    def _xueliu_base_from_fan(self, fan: int) -> int:
        checker = getattr(self.calculation_service, 'Sichuan_xueliu_base_from_fan', None)
        return int(checker(fan)) if callable(checker) else max(1, int(fan))

    def _evaluate_xueliu_wall_end(self, player: SichuanPlayer) -> Tuple[str, Set[int], int, List[str]]:
        """Evaluate wall-end tenpai and flower-pig status."""
        all_tiles = self._xueliu_all_tiles(player)
        if len({tile // 10 for tile in all_tiles}) == 3 or (self.xueliu_exchange and player.dingque_suit in (1, 2, 3) and any(tile // 10 == player.dingque_suit for tile in all_tiles)):
            return ('hua_zhu', set(), 0, [])
        hand = list(player.hand_tiles)
        candidates = []
        if len(hand) == (self.xueliu_meld_count - len(player.combination_tiles)) * 3 + 2:
            candidates = [hand[:i] + hand[i + 1:] for i in range(len(hand))]
        else:
            candidates = [hand]
        best_waits: Set[int] = set()
        best_fan, best_names = (0, [])
        for candidate in candidates:
            waits = self.calculation_service.Sichuan_xueliu_tingpai_check(candidate, player.combination_tiles, meld_count=self.xueliu_meld_count)
            if not waits:
                continue
            candidate_fan, candidate_names = (0, [])
            legal_waits = set()
            for wait in waits:
                fan, names = self.calculation_service.Sichuan_xueliu_hepai_check(candidate + [wait], player.combination_tiles, ['自摸'], wait, player.dingque_suit if self.xueliu_exchange else 0, meld_count=self.xueliu_meld_count)
                if not names or fan < getattr(self, "hepai_limit", 0):
                    continue
                legal_waits.add(wait)
                if fan > candidate_fan:
                    candidate_fan, candidate_names = (fan, list(names))
            if candidate_fan > best_fan or (candidate_fan == best_fan and len(legal_waits) > len(best_waits)):
                best_waits = legal_waits
                best_fan, best_names = (candidate_fan, candidate_names)
        if not best_waits:
            return ('no_ting', set(), 0, [])
        return ('ting', best_waits, best_fan, best_names)

    async def _settle_xueliu_wall_end(self):
        """wall-end settlement: no-tenpai pays every tenpai player."""
        statuses: Dict[int, str] = {}
        waits_map: Dict[int, Set[int]] = {}
        max_fans: Dict[int, int] = {}
        fan_names: Dict[int, List[str]] = {}
        for player in self.player_list:
            status, waits, max_fan, names = self._evaluate_xueliu_wall_end(player)
            statuses[player.player_index] = status
            waits_map[player.player_index] = waits
            max_fans[player.player_index] = max_fan
            fan_names[player.player_index] = names
        ting_players = [idx for idx, status in statuses.items() if status == 'ting']
        noting_players = [idx for idx, status in statuses.items() if status != 'ting']
        score_changes_by_payer: Dict[int, Dict[int, int]] = {}
        for payer_idx in noting_players:
            payer_changes = {i: 0 for i in range(4)}
            multiplier = 2 if statuses[payer_idx] == 'hua_zhu' else 1
            for target_idx in ting_players:
                fan = max_fans[target_idx] + sum(
                    r.get('xueliu_fan', 0) for r in self.player_list[payer_idx].gang_score_records if not self.xueliu_exchange
                )
                base = self._xueliu_base_from_fan(fan)
                amount = base * multiplier
                payer_changes[payer_idx] -= amount
                payer_changes[target_idx] += amount
            score_changes_by_payer[payer_idx] = payer_changes
        logger.info('血流查叫 sub_rule=%s statuses=%s max_fans=%s changes=%s', self.sub_rule, statuses, max_fans, score_changes_by_payer)
        player_action_record_liuju(self)
        all_hands = {p.player_index: list(p.hand_tiles) for p in self.player_list}
        player_scores = {p.player_index: p.score for p in self.player_list}
        await broadcast_result(self, hu_class='liuju', liuju_step='reveal_hu', liuju_hu_hands=all_hands, player_to_score=player_scores, round_continues=False)
        await asyncio.sleep(ROUND_END_HAND_REVEAL_SEC)
        # 全员听牌也展示查叫结果，保证最终面板拥有准备/结束入口。
        settlement_players = noting_players or ting_players
        for index, payer_idx in enumerate(settlement_players):
            changes = score_changes_by_payer.get(payer_idx, {i: 0 for i in range(4)})
            display_status = statuses[payer_idx]
            if not ting_players and display_status in ('hua_zhu', 'no_ting'):
                display_status += '_no_payee'
            for player in self.player_list:
                player.score += changes.get(player.player_index, 0)
            player_scores = {p.player_index: p.score for p in self.player_list}
            is_final = index == len(settlement_players) - 1
            self.next_status = ('match_end' if self.current_round >= self.max_round * 4 else 'round_end_by_ready') if is_final else 'round_continue'
            player_action_record_sichuan_liuju_step(self, 'chajiao', payer_idx, display_status, json.dumps(list(self.player_list[payer_idx].hand_tiles)), [changes.get(i, 0) for i in range(4)], 1 if is_final else 0)
            await broadcast_result(self, hu_class='liuju', liuju_step='chajiao', hepai_player_index=payer_idx, hepai_player_combination_mask=self.player_list[payer_idx].combination_mask, liuju_status={payer_idx: display_status}, liuju_hands={payer_idx: list(self.player_list[payer_idx].hand_tiles)}, score_changes=changes, player_to_score=player_scores, liuju_status_final=is_final, liuju_refund=False, round_continues=False, next_status=self.next_status)
            if not is_final:
                await asyncio.sleep(sichuan_chajiao_panel_wait_seconds(is_final=False, has_refund=False))
        self._liuju_final_panel_shown = True

    def _record_gang_score(self, gainer: int, tile: int, gtype: str, payer_index: Optional[int]=None) -> Dict[int, int]:
        if self.xueliu_exchange:
            # 即时杠分与胡牌的根倍率相互独立；不沿用血战的杠上炮转移规则。
            if gtype == 'guafeng':
                payers = {payer_index: 3} if payer_index is not None else {}
            else:
                amount = 2 if gtype == 'xiayu2' else 1
                payers = {p.player_index: amount for p in self.player_list if p.player_index != gainer}
            changes = {p.player_index: 0 for p in self.player_list}
            for index, amount in payers.items():
                self.player_list[index].score -= amount
                self.player_list[gainer].score += amount
                changes[index] -= amount
                changes[gainer] += amount
            self.player_list[gainer].gang_score_records.append({
                'type': gtype, 'tile': tile, 'gainer': gainer, 'payers': payers, 'total': sum(payers.values())})
            return changes
        gang_fan = 2 if gtype == 'xiayu2' else 1
        record = {'type': gtype, 'tile': tile, 'gainer': gainer, 'payers': {}, 'total': 0, 'xueliu_fan': gang_fan}
        self.player_list[gainer].gang_score_records.append(record)
        return {p.player_index: 0 for p in self.player_list}

    def _apply_hu_score_changes(self, winner: int, base: int, is_zimo: bool, discarder: Optional[int]=None, fan: Optional[int]=None) -> Dict[int, int]:
        """和牌收支，返回四家分数变更（未参与者显式为 0，便于客户端展示）。"""
        changes: Dict[int, int] = {p.player_index: 0 for p in self.player_list}
        if is_zimo:
            total = 0
            winner_order = self.player_list[winner].hu_order
            for p in self.player_list:
                if p.player_index == winner:
                    continue
                extra_fan = sum((r.get('xueliu_fan', 0) for r in getattr(p, 'gang_score_records', [])))
                pay = fan + extra_fan if extra_fan and fan is not None else base
                p.score -= pay
                changes[p.player_index] = -pay
                total += pay
            self.player_list[winner].score += total
            changes[winner] = total
        elif discarder is not None:
            extra_fan = sum((r.get('xueliu_fan', 0) for r in getattr(self.player_list[discarder], 'gang_score_records', [])))
            payer_base = fan + extra_fan if extra_fan and fan is not None else base
            self.player_list[discarder].score -= payer_base
            changes[discarder] = -payer_base
            self.player_list[winner].score += payer_base
            changes[winner] = payer_base
        return changes

    async def _settle_win(self):
        pw = self.pending_win
        self.pending_win = None
        gang_refund_changes: Dict[int, int] = {}
        if pw['type'] == 'ron' and self.paofen_watch and (self.paofen_watch[0] == pw['discarder']):
            gang_refund_changes = self._refund_gang_record(self.paofen_watch[1])
            self.paofen_watch = None
        elif pw.get('gang_refund_changes'):
            gang_refund_changes = pw['gang_refund_changes']
        if pw['type'] == 'zimo':
            winners = [self.current_player_index]
        else:
            discarder = pw['discarder']
            winners = sorted(self.sichuan_hu_results.keys(), key=lambda x: self._distance_from(discarder, x))
        if self.first_win_event is None:
            self.first_win_event = {'winners': list(winners), 'discarder': pw.get('discarder')}
        has_gang_refund = gang_refund_changes and any((v != 0 for v in gang_refund_changes.values()))
        if has_gang_refund:
            player_action_record_gang_refund(self, gang_refund_changes)
        ron_idx = 0
        for ron_i, w in enumerate(winners):
            info = self.sichuan_hu_results.get(w)
            if not info:
                continue
            fan = info['fan']
            fan_list = info['fan_list']
            base = self._xueliu_base_from_fan(fan)
            is_zimo = pw['type'] == 'zimo'
            discarder = pw.get('discarder') if not is_zimo else None
            changes = self._apply_hu_score_changes(w, base, is_zimo, discarder, fan=fan)
            if is_zimo:
                self.player_list[w].record_counter.zimo_times += 1
            else:
                self.player_list[w].record_counter.dianhe_times += 1
                self.player_list[discarder].record_counter.fangchong_times += 1
                self.player_list[discarder].record_counter.fangchong_score += -changes.get(discarder, -base)
            self.hu_order_counter += 1
            self.player_list[w].hu_order = self.hu_order_counter
            self.player_list[w].has_won = True
            self.player_list[w].win_count += 1
            self.player_list[w].post_hu_lock = True
            if not self.player_list[w].locked_waiting_tiles:
                self.player_list[w].locked_waiting_tiles = set(self.player_list[w].waiting_tiles)
            self.player_list[w].record_counter.recorded_fans.append(fan_list)
            self.player_list[w].record_counter.win_score += base
            # 最后一张和牌之后仍有查叫，结束/下一局按钮只由查叫最终面板发出。
            round_continues = True
            hepai_tile = info.get('hepai_tile', 0)
            is_qianggang = pw['type'] == 'qianggang'
            multi_ron = not is_zimo and len(winners) > 1
            recycle_discard = not is_zimo and (not multi_ron or ron_i == len(winners) - 1)
            if is_zimo:
                hu_class = 'hu_self'
            else:
                hu_class = ['hu_first', 'hu_second', 'hu_third'][min(ron_idx, 2)]
                ron_idx += 1
            hu_changes_list = [changes.get(i, 0) for i in range(4)]
            player_action_record_hu(self, hu_class=hu_class, hu_score=base, hu_fan=fan_list, hepai_player_index=w, score_changes=hu_changes_list, hepai_tile=hepai_tile, multi_ron=multi_ron if not is_zimo else None, ron_discarder_index=discarder if not is_zimo else None, recycle_discard=recycle_discard if not is_zimo else None)
            # 和牌张离开行牌用手牌，作为可累积的公开花区牌保存；不亮暗手。
            if is_zimo:
                self.player_list[w].hand_tiles.pop()
            self.player_list[w].huapai_list.append(hepai_tile)
            clear_draw_slot(self.player_list[w])
            player_to_score = {p.player_index: p.score for p in self.player_list}
            next_status = 'round_continue'
            self.next_status = next_status
            await broadcast_result(self, hu_class=hu_class, hepai_player_index=w, win_player_index=w, is_zimo=is_zimo, hu_score=base, hu_fan=fan_list, hepai_tile=hepai_tile, multi_ron=multi_ron, is_qianggang=is_qianggang if not is_zimo else None, ron_discarder_index=discarder if not is_zimo else None, recycle_discard=recycle_discard if not is_zimo else None, suppress_hand_reveal=True, defer_score_settlement=False, player_to_score=player_to_score, score_changes=changes, gang_refund_changes=gang_refund_changes if has_gang_refund and ron_i == 0 else None, round_continues=round_continues, next_status=next_status)
            # 只为和牌张移入花区留出动画时间，不进入和牌结算面板。
            await asyncio.sleep(0.5)
        self.sichuan_hu_results = {}
        await broadcast_xueliu_continue(self)
        if len(self.tiles_list) <= self.dead_wall_count:
            self.ended_by = 'liuju'
            self.game_status = 'END'
        else:
            self.current_player_index = winners[-1]
            self.game_status = 'deal_card'
        return

    async def _settle_liuju(self):
        await self._settle_xueliu_wall_end()
        return
