import asyncio
from copy import deepcopy
from dataclasses import replace

from ...database.fulu_utils import record_fulu_rounds_for_players
from ...game_calculation.guangdong import config
from ...game_calculation.guangdong.scoring import coefficient
from ...game_calculation.guangdong.settlement import kong_payments, refund_kongs, win_payments
from ...game_calculation.sichuan.doubles_rules import match_result, team_scores
from ...response import Refresh_player_tag_list_info, Response, Show_result_info
from ..game_guobiao.blood_battle import HU_TAGS, next_active_index, player_snapshot
from ..game_guobiao.combination_mask_view import build_revealed_angang_masks
from ..game_taiwan import broadcast
from ..game_tuidao.result import broadcast_result
from ..public.game_record_manager import (
    append_action_tick, player_action_record_hu, player_action_record_round_end,
)
from ..public.hand_slot_utils import clear_draw_slot, has_draw_slot
from ..public.round_end_timing import (
    hu_result_ready_pre_panel_seconds, liuju_ready_wait_seconds, sichuan_settle_hu_panel_wait_seconds,
)
from ..public.settlement_sync import send_game_packet
from .GuangdongGameState import GuangdongGameState
from .doubles_claims import DoublesClaimMixin


class GuangdongDoublesGameState(DoublesClaimMixin, GuangdongGameState):
    profile = config.DOUBLES_SUB_RULE
    multi_win_mode = "multiple_winners"

    def __init__(self, game_server, room_data, calculation_service, db_manager, gamestate_id):
        teams = room_data.get("team_by_seat", [])
        if len(teams) != 4 or teams.count(0) != 2 or teams.count(1) != 2:
            raise ValueError("广东双打需要两队各两名选手")
        super().__init__(game_server, room_data, calculation_service, db_manager, gamestate_id)
        for player, team in zip(self.player_list, teams):
            player.team_id = team
        self.team_round_history = []
        self.team_boards = []
        self.team_sets = []
        self.forfeited_teams = set()
        self.team_result = None
        self.action_priority.update(hu_first=6, hu_second=6, hu_third=6)

    def _arrange_players(self, rng):
        groups = [[p for p in self.player_list if p.team_id == team] for team in (0, 1)]
        left, right = groups if self.player_list[0].team_id == 0 else reversed(groups)
        self.player_list[:] = [left[0], right[0], left[1], right[1]]

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        if self.current_round > 1 and self.current_round % 8 == 1:
            self.forfeited_teams.clear()
        self.win_events = []
        self.rulings = {}
        self.chicken_teams = set()
        self.valid_kongs = 0
        self.settled = False
        for player in self.player_list:
            player.is_hu = False
            player.hu_order = 0
            player.blood_win_tile = None
            player.blood_win_is_zimo = False
            player.blood_win_multi = False
            player.blood_win_source = None
            player.tag_list[:] = [tag for tag in player.tag_list
                                  if tag not in (*HU_TAGS, "gd_chicken", "gd_non_chicken", "peida")]

    def next_round_number(self):
        if self.forfeited_teams:
            return ((self.current_round - 1) // 8 + 1) * 8 + 1
        return super().next_round_number()

    def next_dealer(self):
        upcoming = self.next_round_number()
        if upcoming % 8 == 1:
            assigned = (upcoming - 1) // 8 % 4
            return next(p.player_index for p in self.player_list if p.original_player_index == assigned)
        return self.win_events[0]["index"] if self.win_events else 0

    def team_ghosts(self, index):
        team = self.player_list[index].team_id
        return sum(p.discarded_ghosts for p in self.player_list if p.team_id == team)

    def scoring_context(self, index, source, **kwargs):
        return replace(super().scoring_context(index, source, **kwargs),
                       doubles=True, discarded_ghosts=self.team_ghosts(index),
                       chicken_available=self.player_list[index].team_id not in self.chicken_teams)

    def score_candidate(self, index, source, tile=None, **kwargs):
        if self.player_list[index].is_hu:
            return None
        return super().score_candidate(index, source, tile, ignore_pass=True)

    def enter_water(self, index):
        return False

    def _finalize_ready_after_discard(self, index, declared):
        player = self.player_list[index]
        if player.last_discarded_tile in config.GHOSTS and "peida" not in player.tag_list:
            player.discarded_ghosts += 1
            append_action_tick(self, ["guangdong", "ghost_discard", index, player.discarded_ghosts])
        self.direct_kong_payer = None

    def public_guangdong_state(self):
        return {**super().public_guangdong_state(), "edition": config.DOUBLES_EDITION,
                "chicken_teams": sorted(self.chicken_teams)}

    def player_view_fields(self, player, viewer):
        return {"team_id": player.team_id, "has_draw_slot": has_draw_slot(player) if player.player_index == viewer else None,
                "blood_hu_source": player.blood_win_source,
                **player_snapshot(player, viewer)}

    def build_game_info_fields(self):
        return {**super().build_game_info_fields(), "sichuan_teams": {
                "team_by_player_index": [p.team_id for p in self.player_list],
                "settled_scores": self.team_round_history[-1]["scores"] if self.team_round_history else {0: 0, 1: 0}}}

    def build_record_title_fields(self):
        return {**super().build_record_title_fields(), "blood_battle": True, "team_by_player_index":
                [p.team_id for p in sorted(self.player_list, key=lambda p: p.original_player_index)]}

    def local_context(self, index):
        from ..public.local_hint_context import hint_context
        p = self.player_list[index]
        hand, melds = sorted(p.hand_tiles), list(p.combination_tiles)
        return dict(hand=hand, melds=melds, **hint_context("guangdong", hand, melds,
                    discarded_ghosts=self.team_ghosts(index), require_minimum_score=True,
                    doubles=True, chicken_available=p.team_id not in self.chicken_teams,
                    blocked=p.is_hu or "peida" in p.tag_list))

    def _selected_winners(self, selected):
        return super()._selected_winners([(i, action) for i, action in selected
                                          if "peida" not in self.player_list[i].tag_list])

    def accept_self_draw(self, index):
        if "peida" in self.player_list[index].tag_list:
            self.action_dict = self.after_claim_actions()
            self.game_status = "waiting_hand_action"
            return
        super().accept_self_draw(index)

    async def adjudicate(self, round_index, seat, decision):
        if round_index != self.round_index or self.settled or self.game_status in ("waiting", "waiting_ready", "END"):
            raise ValueError("该副牌已变更或结束")
        if type(seat) is not int or not 0 <= seat < 4 or decision not in ("false_win", "second_chicken", "forfeit"):
            raise ValueError("判罚无效")
        key = (seat, decision)
        if key in self.rulings:
            return self.rulings[key]
        p = next(player for player in self.player_list if player.original_player_index == seat)
        penalty = 0
        if decision == "forfeit":
            self.forfeited_teams.add(p.team_id)
        else:
            if p.is_hu or "peida" in p.tag_list or any(w["index"] == p.player_index for w in self.pending_winners):
                raise ValueError("选手已经和牌或已被判停和")
            if decision == "second_chicken" and p.team_id not in self.chicken_teams:
                raise ValueError("本队本副尚未鸡和")
            if decision == "false_win":
                opponents = [q for q in self.player_list if q.team_id != p.team_id and not q.is_hu]
                if not opponents:
                    raise ValueError("两名对手均已和牌，本副已结束")
                penalty = 32 * max(coefficient(q.hand_tiles, self.team_ghosts(q.player_index)) for q in opponents)
                p.record_counter.cuohe_times += 1
                p.score -= penalty
            p.tag_list.append("peida")
        changes = [-penalty if q is p else 0 for q in self.player_list]
        append_action_tick(self, ["guangdong", "penalty", changes, p.player_index, decision])
        result = self.rulings[key] = dict(round_index=round_index, original_seat=seat, decision=decision, penalty=penalty)
        tags = {q.player_index: list(q.tag_list) for q in self.player_list}
        scores = {q.player_index: q.score for q in self.player_list}
        for q in self.player_list:
            self.record_local_context(q.player_index)
            info = Refresh_player_tag_list_info(player_to_tag_list=tags, player_to_score=scores,
                                                 **self.build_private_game_info_fields(q.player_index))
            await self._send_view(q, Response(type="gamestate/guangdong/adjudication", success=True,
                                              message="裁判判定", refresh_player_tag_list_info=info))
        return result

    def spectator_record_tick(self, tick):
        if tick and str(tick[0]).startswith("hu_"):
            projected = list(tick)
            projected[2:4] = [0, ["鸡和" if "gd_chicken" in self.player_list[tick[1]].tag_list else "非鸡和"]]
            if tick[0] == "hu_self":
                projected[5] = 0
            return projected
        return tick

    def complete_spectator_record(self, record):
        return {**deepcopy(self.game_record), "players_settings": record["players_settings"]}

    def _queue_winner_resolution(self, winners):
        frozen = []
        payer = winners[0]["payer"]
        for item in sorted(winners, key=lambda i: (i["index"] - (payer or 0)) % 4):
            player = self.player_list[item["index"]]
            if player.is_hu:
                continue
            if item["detail"]["chicken"]:
                if player.team_id in self.chicken_teams:
                    continue
                self.chicken_teams.add(player.team_id)
            frozen.append(item)
        if not frozen:
            self.game_status = "deal_card"
            return
        self.pending_winners = frozen
        self.game_status = "doubles_win"

    async def wait_action(self):
        result = await super().wait_action()
        if self.game_status == "doubles_win":
            await self.confirm_wins()
        return result

    def _active_payments(self, changes, receiver):
        active = {p.player_index for p in self.player_list if not p.is_hu}
        row = {i: delta if i in active else 0 for i, delta in changes.items()}
        row[receiver] = -sum(delta for i, delta in row.items() if i != receiver)
        return row

    async def _pay_kong(self, index, kind, tile, *, payer=None, drawn=True):
        if kind == "direct":
            changes = dict.fromkeys(range(4), 0)
            changes[index], changes[payer] = 2, -2
        else:
            changes = self._active_payments(kong_payments(index, kind, drawn=drawn), index)
        if any(changes.values()):
            self.kong_ledger.append(dict(player=index, kind=kind, tile=tile, payer=payer, changes=changes))

    async def confirm_wins(self):
        winners, self.pending_winners = self.pending_winners, []
        source, tile = winners[0]["source"], winners[0]["tile"]
        self_draw, payer = source == "self_draw", winners[0]["payer"]
        if source == "discard":
            river = self.player_list[payer]
            if river.discard_tiles[-1] != tile:
                raise ValueError("和牌张与末张弃牌不符")
            river.discard_origin_tiles.append(river.discard_tiles.pop())
        multi = len(winners) > 1
        for order, item in enumerate(winners):
            index, detail = item["index"], item["detail"]
            p = self.player_list[index]
            changes = win_payments(index, detail["base_score"], payer=payer,
                                   direct_kong_payer=item["liable_payer"],
                                   rob_kong=source == "robbing_kong")["changes"]
            item["changes"] = self._active_payments(changes, index)
            if not self_draw:
                p.hand_tiles.append(tile)
            p.is_hu = True
            p.hu_order = len(self.win_events) + 1
            p.blood_win_tile, p.blood_win_is_zimo, p.blood_win_multi = tile, self_draw, multi
            p.blood_win_source = payer
            p.tag_list.extend([HU_TAGS[p.hu_order - 1], "gd_chicken" if detail["chicken"] else "gd_non_chicken"])
            p.waiting_tiles = set()
            clear_draw_slot(p)
            self.win_events.append(item)
            fan_names = list(detail["fan_names"])
            if detail["coefficient"] != 1:
                fan_names.append(f"GD|coefficient|×{detail['coefficient']}|分数系数")
            item["fan_names"] = fan_names
            item["recycle"] = source == "discard" and order == len(winners) - 1
            counter = p.record_counter
            counter.recorded_fans.append(fan_names)
            counter.win_score += detail["base_score"]
            counter.win_turn += self.xunmu
            counter.zimo_times += int(self_draw)
            counter.dianhe_times += int(not self_draw)
            if not self_draw:
                counter = self.player_list[payer].record_counter
                counter.fangchong_times += 1
                counter.fangchong_score += detail["base_score"]
            player_action_record_hu(self, item["hu_class"], detail["base_score"], fan_names, index,
                                    [0] * 4, hepai_tile=tile, multi_ron=multi,
                                    ron_discarder_index=payer, recycle_discard=item["recycle"],
                                    statistics_fan=detail["fan"])
            await self._announce_win(item, multi)
        self.valid_kongs = len(self.kong_ledger)
        self.jiagang_tile = self.direct_kong_payer = None
        self.result_dict.clear()
        self.hu_class = None
        self.current_player_index = winners[-1]["index"]
        teams = [self.player_list[i["index"]].team_id for i in self.win_events]
        if len(teams) >= 3 or len(teams) != len(set(teams)):
            self.draw_reason = "three_winners" if len(teams) >= 3 else "team_finished"
            self.game_status = "END"
        else:
            self.game_status = "deal_card"

    async def _deal_normal(self):
        if self.can_take_normal_tile():
            index = next_active_index(self, self.current_player_index)
            if self.player_list[0].is_hu and index <= self.current_player_index:
                self.xunmu += 1
            self.current_player_index = (index - 1) % 4
        await super()._deal_normal()

    async def _send_view(self, player, response):
        await self.send_to_realtime_spectators(player.player_index, response)
        connection = self.game_server.user_id_to_connection.get(player.user_id)
        if connection is not None and "offline" not in player.tag_list:
            await send_game_packet(self, connection.websocket, response.model_dump(exclude_none=True))

    async def _announce_win(self, item, multi):
        self.server_action_tick += 1
        index = item["index"]
        self_draw = item["source"] == "self_draw"
        await broadcast.broadcast_refresh_player_tag_list(self)
        for p in self.player_list:
            self.record_local_context(p.player_index)
            info = Show_result_info(hu_class=item["hu_class"], hepai_player_index=index,
                guangdong_chicken=item["detail"]["chicken"], **self.build_private_game_info_fields(p.player_index),
                hepai_tile=item["tile"] if p.player_index == index or not self_draw else 0,
                is_zimo=self_draw, is_qianggang=item["source"] == "robbing_kong",
                multi_ron=multi, ron_discarder_index=item["payer"], recycle_discard=item["recycle"],
                suppress_hand_reveal=True, defer_score_settlement=True, round_continues=True,
                blood_battle_step="mid_win", next_status="round_continue", action_tick=self.server_action_tick)
            response = Response(type="gamestate/guangdong/show_result", success=True, message="和牌",
                                show_result_info=info)
            await self._send_view(p, response)
        await asyncio.sleep(hu_result_ready_pre_panel_seconds())

    async def _settle_hand(self, scores_before):
        end = self.next_round_number() > self.max_round * 4
        if self.settled:
            return end
        self.settled = True
        status = "match_end" if end else "round_end_by_ready"
        kongs = {i: -v for i, v in refund_kongs(self.kong_ledger[:self.valid_kongs]).items()}
        for i, value in kongs.items():
            self.player_list[i].score += value
        if any(kongs.values()):
            append_action_tick(self, ["guangdong", "kong_score", list(kongs.values()), "board", None, None])
        hands = {p.player_index: list(p.hand_tiles) for p in self.player_list}
        masks = build_revealed_angang_masks(self.player_list)
        if self.win_events:
            append_action_tick(self, ["blood", "reveal_hu", hands])
        for n, item in enumerate(self.win_events):
            final = n == len(self.win_events) - 1
            index, changes, detail = item["index"], item["changes"], item["detail"]
            for i, amount in changes.items():
                self.player_list[i].score += amount
            totals = {p.player_index: p.score - scores_before[p.original_player_index] for p in self.player_list}
            append_action_tick(self, ["blood", "settle_hu", item["hu_class"], index,
                               detail["base_score"], item["fan_names"], list(changes.values()), int(final), []])
            await broadcast_result(self, hu_class=item["hu_class"], hepai_player_index=index,
                hepai_tile=item["tile"], hu_score=detail["base_score"], hu_fan=item["fan_names"],
                hepai_player_hand=hands[index], hepai_player_huapai=[],
                hepai_player_combination_mask=self.player_list[index].combination_mask,
                revealed_angang_masks=masks, is_qianggang=item["source"] == "robbing_kong",
                is_zimo=item["source"] == "self_draw", ron_discarder_index=item["payer"],
                recycle_discard=False, skip_hand_reveal=True, silent=True,
                blood_battle_step="settle_hu", blood_round_changes=totals, liuju_hu_hands=hands,
                liuju_status_final=final, blood_end_reason=self.draw_reason,
                player_to_score={p.player_index: p.score for p in self.player_list}, score_changes=changes,
                guangdong_result={"edition": config.DOUBLES_EDITION, **detail, "round_changes": totals},
                next_status=status if final else "round_continue")
            if not final or end:
                await asyncio.sleep(sichuan_settle_hu_panel_wait_seconds(len(item["fan_names"]), is_final=final))
        if not self.win_events:
            self.hu_class = "liuju"
            self._record_liuju(self.draw_reason)
            deltas = {p.player_index: p.score - scores_before[p.original_player_index] for p in self.player_list}
            await broadcast_result(self, hu_class="liuju", next_status=status, revealed_angang_masks=masks,
                liuju_hu_hands=hands, blood_round_changes=deltas, liuju_status_final=True,
                guangdong_result={"edition": config.DOUBLES_EDITION, "draw": True, "round_changes": deltas},
                player_to_score={p.player_index: p.score for p in self.player_list}, score_changes=deltas)
            await asyncio.sleep(liuju_ready_wait_seconds())
        for p in self.player_list:
            change = p.score - scores_before[p.original_player_index]
            p.score_history.append(f"{change:+d}" if change else "0")
            p.round_number_history.append(self.current_round)
            p.record_counter.round_score_total += change
        record_fulu_rounds_for_players(self.player_list)
        player_action_record_round_end(self)
        scores = team_scores(self.player_list)
        previous = self.team_round_history[-1]["scores"] if self.team_round_history else {0: 0, 1: 0}
        self.team_round_history.append(dict(round=self.current_round, end_reason=self.draw_reason, scores=scores,
                                           score_changes={i: scores[i] - previous[i] for i in (0, 1)}))
        self.team_boards.append(dict(version=1, round=self.current_round, reason=self.draw_reason,
            players=[dict(seat=p.player_index, original_seat=p.original_player_index, user_id=p.user_id,
                          username=p.username, team_id=p.team_id, status="hu" if p.is_hu else "unfinished")
                     for p in self.player_list],
            changes={p.player_index: p.score - scores_before[p.original_player_index] for p in self.player_list},
            scores_after={p.player_index: p.score for p in self.player_list}, team_totals=[scores[0], scores[1]]))
        if self.current_round % 8 == 0 or self.forfeited_teams or end:
            self.team_sets.append(self._set_result())
        if self.win_events and not end:
            await self.run_hu_result_ready_phase(len(self.win_events[-1]["fan_names"]))
        return end

    def _set_result(self):
        first = (self.current_round - 1) // 8 * 8 + 1
        boards = [r for r in self.team_round_history if r["round"] >= first]
        scores = {team: sum(r["score_changes"][team] for r in boards) for team in (0, 1)}
        result = match_result(scores, boards)
        if self.forfeited_teams:
            winner = next(iter({0, 1} - self.forfeited_teams), None)
            result.update(winner_team_id=winner, reason="forfeit",
                          match_points={t: int(t == winner) for t in (0, 1)})
        return {**result, "first_round": first, "last_round": self.current_round}

    def _finish_match(self):
        sets = self.team_sets or [self._set_result()]
        if len(sets) == 1:
            result = dict(sets[0])
        else:
            points = {team: sum(s["match_points"][team] for s in sets) for team in (0, 1)}
            winner = None if points[0] == points[1] else max(points, key=points.get)
            result = dict(scores=team_scores(self.player_list), match_points=points,
                          winner_team_id=winner, reason="draw" if winner is None else "match_points")
        self.team_result = result
        self.match_panel = dict(duplicate=False, reason=result["reason"], tables={"single": self.team_boards},
            scores=[result["scores"][i] for i in (0, 1)], match_points=[result["match_points"][i] for i in (0, 1)],
            players=[dict(seat=p.player_index, original_seat=p.original_player_index, team_id=p.team_id,
                          user_id=p.user_id, username=p.username, table="single", score=p.score,
                          history=[int(delta) for delta in p.score_history]) for p in self.player_list])
        self.game_record["doubles_sets"] = sets
        self.game_record["doubles_rounds"] = self.team_round_history
        self.game_record["doubles_result"] = self.game_record["game_title"]["doubles_result"] = result
        for p in self.player_list:
            p.record_counter.rank_result = 1 if result["winner_team_id"] == p.team_id or (
                result["winner_team_id"] is None and any(result["match_points"].values())) else 2
