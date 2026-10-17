import asyncio
from copy import deepcopy

from ...game_calculation.shenyang import rules as book
from ..game_guobiao.combination_mask_view import get_combination_fields_for_viewer
from ..public.game_record_manager import append_action_tick
from . import client_context


class ShenyangProtocol:
    def build_game_info_fields(self):
        return {"detailed_config": self.rules_dict}

    def build_record_title_fields(self):
        return {"detailed_config": self.rules_dict, "rule_version": book.VERSION}

    build_record_round_fields = build_record_title_fields

    async def prepare_private_fields(self, indices):
        requests = {i: client_context.request(self, i) for i in indices}
        tick = self.server_action_tick, self.round_index
        values = {i: client_context.local_context(*r) for i, r in requests.items()}
        if tick == (self.server_action_tick, self.round_index):
            self._prepared_hints.update(values)
            if f"round_index_{self.round_index}" in self.game_record.get("game_round", {}):
                for i, value in values.items():
                    if self._recorded_hints.get(i) != requests[i]:
                        append_action_tick(self, ["sy", dict(kind="hints", player=i, hints=value)])
                        self._recorded_hints[i] = requests[i]

    def view(self, viewer):
        p = self.player_list[viewer]
        snapshots = []
        for other in self.player_list:
            codes, masks = get_combination_fields_for_viewer(other, viewer)
            snapshots.append(dict(player=other.player_index, hand_count=len(other.hand_tiles),
                hand=list(other.hand_tiles) if other.player_index == viewer else None,
                melds=codes, masks=masks, score=other.score, opened=other.opened,
                has_draw_slot=other.has_draw_slot if other.player_index == viewer else False,
                last_drawn_tile=(other.last_drawn_tile or 0) if other.player_index == viewer else 0))
        event = deepcopy(self._sy_event)
        if event and event["kind"] == "tail_pass" and event["player"] != viewer:
            event["tile"] = 0
        result = dict(phase=self.game_status, final_four=self.final_four, long_hair=self.long_hair,
            self_has_draw_slot=p.has_draw_slot, self_last_drawn_tile=p.last_drawn_tile or 0,
            passed_fan=p.passed_fan, egg_offer=self.egg_offer if viewer == self.current_player_index else None,
            players=snapshots, event=event,
            tail_tiles={i: t if i == viewer else 0 for i, t in self.tail_tiles.items()})
        if self.tips or self.count_tips:
            prepared = self._prepared_hints.get(viewer)
            if prepared and prepared["source_hand_tiles"] == p.hand_tiles and prepared["source_melds"] == p.combination_tiles:
                result["hints"] = prepared
        return result

    def build_private_game_info_fields(self, viewer):
        return {"shenyang": self.view(viewer)}

    def build_private_hand_action_info(self, index):
        p = self.player_list[index]
        return {"shenyang": self.view(index), "riichi_candidate_cuts": p.riichi_candidate_cuts,
                "kong_candidates": {a: [t for t in sorted(set(p.hand_tiles)) if self.kong_allowed(index, t, k)]
                                    for a, k in (("angang", "concealed"), ("jiagang", "added"))}}

    def build_private_do_action_info(self, actor, viewer):
        return {"shenyang": self.view(viewer), "tile_count": self.playable_wall_count()}
