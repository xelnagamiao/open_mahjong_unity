"""Flower actions reuse the Guobiao reveal/replacement wire interaction."""

from ...game_calculation.yixing.rules import FLOWERS
from .actions import empty_actions
from .state_machine import Phase as P


class FlowerFlow:
    def opening_window(self):
        return self.begin_turn(0)

    def _replacement_window(self, index, tile, *, discard_only=False):
        self.current_player_index = index
        self.replacement_pending = (index, tile, discard_only)
        return self._window(P.REPLACEMENT, index, actions={**empty_actions(), index: ["buhua"]})

    def resolve_replacement(self):
        index, tile, discard_only = self.replacement_pending
        p = self.player_list[index]
        drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        p.hand_tiles.remove(tile)
        p.huapai_list.append(tile)
        p.has_draw_slot = p.after_kong = False
        self.replacement_pending = None
        self._event("buhua", index, tile=tile, buhua_tile=tile, is_mo_buhua=drawn)
        if not self.tiles_list:
            return self.end_draw()
        return self.draw_for(index, kind="flower", discard_only=discard_only)

    def draw_for(self, index, kind="normal", *, take_last=False, discard_only=False):
        if not self.tiles_list:
            return self.end_draw()
        if kind == "normal" and len(self.tiles_list) == 1 and not take_last:
            self.last_draw_passes = []
            return self.last_draw_window(index)
        p = self.player_list[index]
        p.passed_pungs.clear()
        p.passed_wins.clear()
        if not discard_only:
            p.forbidden_discards.clear()
        p.hand_tiles.sort()
        tile = self.tiles_list.pop(0) if kind == "normal" else self.tiles_list.pop()
        p.hand_tiles.append(tile)
        p.has_draw_slot = True
        p.after_kong = kind == "kong"
        p.last_draw_last_wall = not self.tiles_list
        p.draw_kind = kind
        if kind == "normal":
            p.draw_count += 1
            self.natural_draw_count[index] = p.draw_count
        self.current_player_index = index
        self._event({"normal":"deal_tile", "kong":"deal_gang_tile", "flower":"deal_buhua_tile"}[kind], index, tile=tile)
        return self.begin_turn(index, discard_only=discard_only)

    def last_draw_window(self, index):
        self.current_player_index = index
        return self._window(P.LAST_DRAW, index, actions={**empty_actions(), index: ["yixing_last_draw", "pass"]})

    def resolve_last_draw(self, index, action):
        self._event("yixing_last_choice", index, take=action == "yixing_last_draw")
        if action == "yixing_last_draw":
            return self.draw_for(index, take_last=True)
        self.last_draw_passes.append(index)
        if len(self.last_draw_passes) == 4:
            return self.end_draw()
        return self.last_draw_window((index + 1) % 4)
