"""权威广东提示与公共快照；提示只随所属座位的私有消息发送。"""

from dataclasses import replace

from ...game_calculation.guangdong import config, rules
from ..public.game_record_manager import append_action_tick
from ..public.ai.bot_executor import run_room_bot_cpu


def calculate_tip_batch(requests):
    """可序列化的纯计算任务，在公共 CPU 工作池运行。"""
    result = []
    for hand, melds, context, passed_score in requests:
        payload = {"hand": list(hand), "melds": list(melds), "waits": [], "discard_waits": {}}
        nominal = len(hand) + 3 * len(melds)
        if nominal == 13:
            payload["waits"] = rules.waiting_scores(hand, melds, context=context, passed_base_score=passed_score)
        elif nominal == 14:
            for cut in sorted(set(hand)):
                remainder = list(hand)
                remainder.remove(cut)
                after_cut = replace(context, heavenly=False, earthly=False, kong_flower=False,
                    discarded_ghosts=context.discarded_ghosts + (cut in config.GHOSTS))
                waits = rules.waiting_scores(remainder, melds, context=after_cut)
                if waits:
                    payload["discard_waits"][str(cut)] = waits
        result.append(payload)
    return result


class GuangdongTipsMixin:
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

    def _tips_key(self, index):
        player = self.player_list[index]
        hand, melds = tuple(sorted(player.hand_tiles)), tuple(player.combination_tiles)
        context = self.scoring_context(index, "self_draw", prediction=True)
        return (hand, melds, context, player.passed_base_score)

    async def prepare_private_fields(self, indices):
        if not self.tips and not self.count_tips:
            return
        requests = [(i, self._tips_key(i)) for i in indices if i in range(4)]
        requests = [(i, key) for i, key in requests
                    if self._tips_cache.get(i, (None,))[0] != key]
        if not requests:
            return
        payloads = await run_room_bot_cpu(self, calculate_tip_batch, [key for _, key in requests])
        for (index, key), payload in zip(requests, payloads):
            # 重连/动作并发时只提交仍然属于该手牌的结果。
            if self._tips_key(index) == key:
                self._tips_cache[index] = (key, payload)

    def guangdong_tips(self, index):
        key = self._tips_key(index)
        cached = self._tips_cache.get(index)
        if cached and cached[0] == key:
            return cached[1]
        return {"hand": list(key[0]), "melds": list(key[1]), "waits": [], "discard_waits": {}}

    def record_guangdong_tips(self, index):
        if not (self.tips or self.count_tips) or not self.game_record.get("game_round", {}).get(f"round_index_{self.round_index}"):
            return
        payload = self.guangdong_tips(index)
        if self._recorded_tips.get(index) != payload:
            append_action_tick(self, ["guangdong", "tips", index, payload])
            self._recorded_tips[index] = payload

    def build_private_game_info_fields(self, index):
        return {"guangdong_tips": self.guangdong_tips(index)} if (self.tips or self.count_tips) and index in range(4) else {}

    def build_private_hand_action_info(self, index):
        player = self.player_list[index]
        self.record_guangdong_tips(index)
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
        self.record_guangdong_tips(viewer_index)
        return fields
