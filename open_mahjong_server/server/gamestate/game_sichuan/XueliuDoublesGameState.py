"""血流双打的随机单桌 2V2；不采用复式双桌赛制。"""
from copy import deepcopy
import asyncio
import time

from .XueliuGameState import XueliuGameState
from ...game_calculation.sichuan.doubles_rules import XUELIU_DOUBLES_SUB_RULE, team_scores, match_result

EXCHANGE_ANIMATION_SECONDS = 1.4


class XueliuDoublesGameState(XueliuGameState):
    is_team_game = True
    is_xueliu_doubles = True

    def _configure_rule(self):
        if self.sub_rule not in (XUELIU_DOUBLES_SUB_RULE, "sichuan/xueliu_doubles_duplicate"):
            raise ValueError("血流双打状态机不支持此子规则")
        super()._configure_rule()
        self.xueliu_rule_profile.update(
            sub_rule=self.sub_rule, version="xueliu-single-table-2v2-v3",
            format="single_table_2v2", exchange_direction=2,
            exchange_order="ES_WN,SW_NE,WN_ES,NE_SW",
            teammate_exchange_visibility="suit_only_after_first_group",
            force_missing_suit_discard=False, teammate_payments=False,
            direct_kong_payment=2, added_kong_payment=1, concealed_kong_payment=2,
            flower_pig_minimum=32, post_hu_opponent_ron_required=True,
            post_hu_teammate_ron_optional=True, team_first_two_end=False,
            tied_match="extra_board_until_decided",
        )

    def _record_rule_profile(self):
        super()._record_rule_profile()
        self.game_record["game_title"]["doubles_rule_profile"] = deepcopy(self.xueliu_rule_profile)
        self.game_record["game_title"]["team_by_player_index"] = [p.team_id for p in self.player_list]

    def _reset_round_state(self):
        super()._reset_round_state()
        self.exchange_stage = 0
        self.exchange_groups = []
        self.exchange_animation_deadline = 0
        for player in self.player_list:
            player.can_hold_dingque = True

    def _can_score_between(self, payer, receiver):
        return payer != receiver and self.player_list[payer].team_id != self.player_list[receiver].team_id

    def _wall_end_payment(self, base, flower_pig):
        return max(32, base * 2) if flower_pig else base

    def _refund_gang_record(self, record):
        if record.get("refunded"):
            return {}
        changes = super()._refund_gang_record(record)
        record["refunded"] = True
        return changes

    def _is_match_end(self):
        scores = team_scores(self.player_list)
        return super()._is_match_end() and scores[0] != scores[1]

    def _should_play_round(self):
        scores = team_scores(self.player_list)
        return super()._should_play_round() or scores[0] == scores[1]

    def _finish_team_match(self):
        # 血流累计队分，同分加副，直至分出胜负。
        self.team_result = match_result(team_scores(self.player_list), [])
        self.team_result["extra_boards"] = max(0, len(self.team_round_history) - self.max_round * 4)
        self.game_record["doubles_result"] = self.team_result
        self.game_record["game_title"]["doubles_result"] = self.team_result
        for player in self.player_list:
            player.record_counter.rank_result = 1 if player.team_id == self.team_result["winner_team_id"] else 2

    async def _opening_phase(self):
        await super()._opening_phase()
        record = self.game_record["game_round"][f"round_index_{self.round_index}"]
        record["xueliu_exchange_groups"] = deepcopy(self.exchange_groups)

    async def _xueliu_throw_three_phase(self):
        # 座位 0/1/2/3 = 东/南/西/北；按副序轮换，不随上副胜者定庄跳动。
        first = (self.current_round - 1) % 4
        self.exchange_groups = [[first, (first + 1) % 4], [(first + 2) % 4, (first + 3) % 4]]
        for stage, seats in enumerate(self.exchange_groups, start=1):
            self.exchange_stage = stage
            await self._collect_xueliu_selection(seats)
            # 两人都提交后才展示这组的换牌；动画期间没有任何选牌权限。
            self.game_status = "xueliu_exchange_animation"
            self.server_action_tick += 1
            self.action_dict = {i: [] for i in range(4)}
            self.exchange_animation_deadline = time.time() + EXCHANGE_ANIMATION_SECONDS
            from .boardcast import broadcast_xueliu_exchange_animation
            await broadcast_xueliu_exchange_animation(self)
            await asyncio.sleep(EXCHANGE_ANIMATION_SECONDS)
            if getattr(self, "lifecycle_state", "running") != "running":
                return
            self.exchange_animation_deadline = 0
        self._exchange_selected_tiles()
        self.exchange_stage = 0

    def _exchange_selected_tiles(self):
        self.xueliu_exchange_direction = 2
        for index, player in enumerate(self.player_list):
            self.player_list[(index + 2) % 4].hand_tiles.extend(player.xueliu_throw_tiles)

    def exchange_view(self, index):
        stage = getattr(self, "exchange_stage", 0)
        groups = getattr(self, "exchange_groups", [])
        active = groups[stage - 1] if stage in (1, 2) and groups else []
        teammate = self.player_list[(index + 2) % 4]
        incoming = teammate.xueliu_throw_tiles
        animating = self.game_status == "xueliu_exchange_animation"
        incoming_visible = stage == 2 or (animating and (index + 2) % 4 in active)
        outgoing = self.player_list[index].xueliu_throw_tiles
        return {
            "stage": stage, "active_players": list(active),
            "selection_done": bool(outgoing),
            # 换牌动画及后手选牌只展示本队花色，不下发牌面数字或对手花色。
            "teammate_suit": incoming[0] // 10 if incoming_visible and incoming else 0,
            "outgoing_suit": outgoing[0] // 10 if outgoing else 0,
            "animating": animating,
            "animation_seconds": max(0, getattr(self, "exchange_animation_deadline", 0) - time.time()) if animating else 0,
        }

    def _default_xueliu_throw_tiles(self, player_index):
        from .xueliu_bot import choose_opening
        hand = self.player_list[player_index].hand_tiles
        incoming_suit = self.exchange_view(player_index)["teammate_suit"]
        alternatives = [tile for tile in hand if tile // 10 != incoming_suit]
        if incoming_suit and any(sum(t // 10 == suit for t in alternatives) >= 3 for suit in (1, 2, 3)):
            return choose_opening(alternatives, exchange=True)
        return super()._default_xueliu_throw_tiles(player_index)
