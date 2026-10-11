"""垃圾和复用上海起手补花、摸打、重连和整场流程，独立裁决和牌与计分。"""
from dataclasses import replace

from ...game_calculation.shanghai import lajihe as calculation
from .QinghunpengGameState import QinghunpengGameState
from ..game_taiwan.TaiwanGameState import TaiwanGameState
from ..game_taiwan import action_check as actions
from ..game_taiwan.boardcast import broadcast_ask_hand_action, broadcast_result
from ..game_taiwan.wait_action import _collect_responses
from ..public.game_record_manager import append_action_tick
from ..public.hand_slot_utils import has_draw_slot
from ..public.ai.pacing import paced_bot, submit_bot_action
from ..public.ai.get_action import get_ai_action
from ..public.ai.smart_bot_ai import _BOT_DELAY, _wait_until_actionable
from .bot import shanghai_smart_bot_action


@paced_bot(lambda: _BOT_DELAY)
async def lajihe_bot(state, index, allowed, status):
    if status == "waiting_flower_choice":
        if await _wait_until_actionable(state, index):
            await submit_bot_action(get_ai_action, state, index, "lajihe_flower_continue", None, None, None, None)
        return
    # 上海机器人已有节奏包装，复用其原函数避免二次延时。
    return await shanghai_smart_bot_action.__wrapped__(state, index, allowed, status)


class LajiheGameState(QinghunpengGameState):
    sub_rule_id = calculation.SUB_RULE
    draw_debt_enabled = False
    concealed_kongs_public = True

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.rules = replace(self.rules, chow_discard_restriction_mode="strict")
        self.rules_dict = {"edition": "lajihe", "self_draw_garbage": 5, "self_draw_other": 10}
        self.smart_bot_action = lajihe_bot

    def _reset_taiwan_players(self):
        super()._reset_taiwan_players()
        for player in self.player_list:
            player.lajihe_passed_win = False
            player.passed_pungs = set()
            player.eight_flowers_paid = False

    def refresh_waits(self, player_index):
        player = self.player_list[player_index]
        player.waiting_tiles = calculation.waits(player.hand_tiles, player.combination_tiles)
        return player.waiting_tiles

    def score_candidate(self, player_index, source, tile=None, **_):
        player = self.player_list[player_index]
        if player.lajihe_passed_win:
            return None
        if source == "self_draw" and self.opening_before_draw is not None:
            return None
        hand = list(player.hand_tiles)
        if source != "self_draw":
            if tile is None:
                return None
            hand.append(tile)
        return calculation.score(hand, player.combination_tiles, player.huapai_list,
            self_draw=source == "self_draw", replacement=source == "self_draw" and self.last_draw_after_kong,
            rob_kong=source == "robbing_kong")

    def enter_water(self, player_index):
        if self.game_status in ("waiting_action_after_cut", "waiting_action_qianggang"):
            player = self.player_list[player_index]
            if not player.lajihe_passed_win:
                player.lajihe_passed_win = True
                self._record_state("lajihe_passed_win", player_index, "T")
        return False  # 不将基于私牌的过和资格公开给其他座位。

    def _clear_missed_on_draw(self, index):
        player = self.player_list[index]
        if player.lajihe_passed_win:
            player.lajihe_passed_win = False
            self._record_state("lajihe_passed_win", index, "F")
        player.passed_pungs.clear()

    async def _deal_normal(self):
        index = (self.current_player_index + 1) % 4
        player = self.player_list[index]
        if self.can_take_normal_tile() and not (not player.opening_flowers_done and any(t in self.flower_tiles for t in player.hand_tiles)):
            self._clear_missed_on_draw(index)
        await super()._deal_normal()

    async def _deal_supplement(self):
        player = self.player_list[self.current_player_index]
        if self.can_take_supplement_tile() and not any(t in self.flower_tiles for t in player.hand_tiles):
            self._clear_missed_on_draw(self.current_player_index)
        await super()._deal_supplement()

    async def _draw_tail_for_player(self, player_index, *, opening):
        if self.can_take_supplement_tile():
            self._clear_missed_on_draw(player_index)
        return await super()._draw_tail_for_player(player_index, opening=opening)

    def _remember_claim_liability(self, *args):
        pass  # 垃圾和没有三口承包。

    def build_private_game_info_fields(self, player_index):
        return {"lajihe_passed_win": self.player_list[player_index].lajihe_passed_win}

    def build_private_hand_action_info(self, player_index):
        return {**super().build_private_hand_action_info(player_index), **self.build_private_game_info_fields(player_index)}

    def build_private_do_action_info(self, action_player, viewer_index):
        return self.build_private_game_info_fields(viewer_index)

    def _kong_allowed(self, index, tile, kind):
        player = self.player_list[index]
        if (self.opening_before_draw is not None or any(t in self.flower_tiles for t in player.hand_tiles)
                or tile not in self.structure_tiles or not self.can_take_supplement_tile()
                or (not has_draw_slot(player) and not self.opening_dealer_action)):
            return False
        return (f"k{tile}" in player.combination_tiles and tile in player.hand_tiles) if kind == "added" else player.hand_tiles.count(tile) == 4

    def check_discard_actions(self, tile):
        result = {i: [] for i in range(4)}
        if tile not in self.structure_tiles:
            return result
        for distance in (1, 2, 3):
            index = (self.current_player_index + distance) % 4
            if self.opening_claim_window is not None and index != self.opening_claim_window:
                continue
            player = self.player_list[index]
            detail = self.score_candidate(index, "discard", tile)
            if detail:
                action = actions.hu_action_for_player(self.current_player_index, index)
                result[index].append(action)
                self.result_dict[action] = detail
            if self.can_take_normal_tile():
                possible = ["peng", "gang"]
                if distance == 1 and tile in calculation.NUMBERS:
                    possible += ["chi_left", "chi_mid", "chi_right"]
                for action in possible:
                    _, required, _ = self._claim_mask(action, tile, "left")
                    if action.startswith("chi") and not actions._valid_sequence_members(tile, *required):
                        continue
                    if any(player.hand_tiles.count(t) < required.count(t) for t in set(required)):
                        continue
                    if action == "peng" and tile in player.passed_pungs:
                        continue
                    # 本规则不留王牌；上面的有牌判断同时保证明杠有补牌。
                    remaining = list(player.hand_tiles)
                    for used in required:
                        remaining.remove(used)
                    fourth_chow = action.startswith("chi") and len(player.combination_tiles) == 3
                    forbidden = set() if fourth_chow else actions.strict_kuikae_forbidden(action, tile, "strict")
                    # 起手补花可能换来合法弃牌；补花完成后仍校验禁食替。
                    if action != "gang" and not any(t not in forbidden for t in remaining):
                        continue
                    result[index].append(action)
            if result[index]:
                result[index].append("pass")
        return result

    async def execute_claim(self, player_index, action_type):
        discards = self.player_list[self.current_player_index].discard_tiles
        if not discards or action_type not in self.check_discard_actions(discards[-1])[player_index]:
            return
        await TaiwanGameState.execute_claim(self, player_index, action_type)
        self.opening_claim_window = None
        player = self.player_list[player_index]
        if action_type.startswith("chi") and len(player.combination_tiles) == 4:
            player.kuikae_forbidden_tiles.clear()

    async def resolve_discard_responses(self, responses, allowed):
        tile = self.player_list[self.current_player_index].discard_tiles[-1]
        for index, options in allowed.items():
            if "peng" in options and responses.get(index, {}).get("action_type") not in ("peng", "gang"):
                self.player_list[index].passed_pungs.add(tile)
        await super().resolve_discard_responses(responses, allowed)

    async def execute_cut(self, player_index, action_data, *, declare_ready=False, is_timeout_action=False):
        player = self.player_list[player_index]
        if declare_ready or self.opening_before_draw is not None or any(t in self.flower_tiles for t in player.hand_tiles):
            return
        await TaiwanGameState.execute_cut(self, player_index, action_data, is_timeout_action=is_timeout_action)

    def _finalize_ready_after_discard(self, player_index, declare_ready):
        super()._finalize_ready_after_discard(player_index, declare_ready)
        player = self.player_list[player_index]
        # 弃牌已成功落地、广播尚未发送：本次消息就要携带过和提示。
        if player.discard_tiles[-1] in player.waiting_tiles and not player.lajihe_passed_win:
            player.lajihe_passed_win = True
            self._record_state("lajihe_passed_win", player_index, "T")

    def _mark_eight_flowers_if_ready(self, player_index, **_):
        pass  # 第八花亮出时立即收分，不依赖还有没有补牌。

    async def _replace_one_flower(self, player_index, flower_index, *, is_drawn, opening):
        player = self.player_list[player_index]
        flower = player.hand_tiles.pop(flower_index)
        self._publish_flower(player_index, flower)
        await self._broadcast_flower(player_index, flower, is_drawn=is_drawn)
        if len(set(player.huapai_list)) == 8 and not player.eight_flowers_paid:
            await self._award_eight_flowers(player_index)
            if self.game_status == "END":
                return False
        return await self._draw_tail_for_player(player_index, opening=opening) is not None

    async def _award_eight_flowers(self, index):
        player = self.player_list[index]
        if player.eight_flowers_paid or set(player.huapai_list) != set(self.flower_tiles):
            return
        player.eight_flowers_paid = True
        changes = calculation.payments(index, 30)
        for seat, delta in changes.items():
            other = self.player_list[seat]
            other.score += delta
            other.score_history.append(f"{delta:+d}")
            other.round_number_history.append(self.current_round)
        append_action_tick(self, ["lajihe_eight_flowers", index, 30, ["八花齐"], [changes[i] for i in range(4)]])
        await broadcast_result(self, hepai_player_index=index, hu_class="lajihe_eight_flowers", hu_score=30,
            hu_fan=["八花齐"], player_to_score={p.player_index: p.score for p in self.player_list},
            score_changes={p.original_player_index: changes[p.player_index] for p in self.player_list},
            next_status="round_continue")
        self.current_player_index = index
        self.action_dict = {i: ["lajihe_flower_end", "lajihe_flower_continue"] if i == index else [] for i in range(4)}
        self.game_status = "waiting_flower_choice"
        self.prepare_action_window()
        await broadcast_ask_hand_action(self)
        responses, _ = await _collect_responses(self)
        stop = responses.get(index, {}).get("action_type") == "lajihe_flower_end"
        self._record_state("lajihe_flower_choice", index, "end" if stop else "continue")
        if stop:
            self.draw_reason = "eight_flowers_choice"
            self.game_status = "END"
        else:
            self.game_status = "waiting_hand_action"

    async def _process_drawn_flowers(self, player_index, origin):
        # 末张恰是第八花时也必须先收八花分，不能提前流局吞掉奖励。
        player = self.player_list[player_index]
        if player.hand_tiles and player.hand_tiles[-1] in self.flower_tiles and not self.can_take_supplement_tile():
            return await self._replace_one_flower(player_index, len(player.hand_tiles) - 1, is_drawn=True, opening=False)
        return await super()._process_drawn_flowers(player_index, origin)

    def _settlement_for_winner(self, item):
        return calculation.payments(item["index"], item["detail"]["base_score"],
            discarder=item.get("payer"), rob_kong=item["source"] == "robbing_kong")
