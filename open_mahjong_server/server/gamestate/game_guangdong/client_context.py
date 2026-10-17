"""Public rule state and private inputs for client-side previews."""
from ...game_calculation.guangdong import config
from ..public.game_record_manager import append_action_tick

class GuangdongClientContextMixin:
    def public_guangdong_state(self):
        return {"edition": config.EDITION,
                "ghost_discard_counts": [p.discarded_ghosts for p in self.player_list],
                "kong_ledger": [{"player": item["player"], "kind": item["kind"],
                    "payer": item["payer"], "changes": [item["changes"][i] for i in range(4)],
                    "tile": item["tile"]}
                    for item in self.kong_ledger]}

    def build_game_info_fields(self):
        return {"detailed_config": dict(self.rules_dict), "guangdong_state": self.public_guangdong_state()}

    def build_record_title_fields(self):
        return {"detailed_config": dict(self.rules_dict),
                "tactical_call": self.tactical_call, "tactical_grace_seconds": self.tactical_grace_seconds,
                "round_timer": self.round_time, "step_timer": self.step_time}

    def build_record_round_fields(self):
        return self.build_game_info_fields()

    def local_context(self, index):
        from ..public.local_hint_context import hint_context
        player = self.player_list[index]
        hand = sorted(player.hand_tiles)
        request = hint_context("guangdong", hand, player.combination_tiles,
            discarded_ghosts=player.discarded_ghosts,
            require_minimum_score=self.rules_dict.get("require_minimum_score", True))["local_request"]
        return dict(hand=hand, melds=list(player.combination_tiles),
                    local_request=request)

    def record_local_context(self, index):
        if not (self.tips or self.count_tips) or not self.game_record.get("game_round", {}).get(f"round_index_{self.round_index}"):
            return
        payload = self.local_context(index)
        if self._recorded_tips.get(index) != payload:
            append_action_tick(self, ["guangdong", "tips", index, payload])
            self._recorded_tips[index] = payload

    def build_private_game_info_fields(self, index):
        return {"guangdong_tips": self.local_context(index)} if (self.tips or self.count_tips) and index in range(4) else {}

    def build_private_hand_action_info(self, index):
        player = self.player_list[index]
        self.record_local_context(index)
        return {**self.build_private_game_info_fields(index),
                **self.action_clock_fields(player),
                "kong_candidates": {action: [tile for tile in sorted(set(player.hand_tiles))
                    if self.kong_allowed(index, tile, kind)]
                    for action, kind in (("angang", "concealed"), ("jiagang", "added"))},
                "riichi_candidate_cuts": {}, "forbidden_cut_tiles": []}

    def build_private_do_action_info(self, action_player, viewer_index):
        fields = {"guangdong_state": self.public_guangdong_state(), **(self._score_event or {})}
        # 所有观战者仅收到被授权视角的提示，不能收到动作对手的手牌。
        fields.update(self.build_private_game_info_fields(viewer_index))
        self.record_local_context(viewer_index)
        return fields
