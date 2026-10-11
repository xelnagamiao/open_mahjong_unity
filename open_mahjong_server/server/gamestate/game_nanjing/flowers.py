from ...game_calculation.nanjing.rules import FLOWERS
from .actions import empty_actions
from .state_machine import Phase as P


class FlowerFlow:
    def opening_window(self):
        self.opening_queue = [(i,t) for i,p in enumerate(self.player_list) for t in p.hand_tiles if t in FLOWERS]
        return self._next_opening_flower()

    def _next_opening_flower(self):
        if not self.opening_queue:
            self.opening_queue = [(i,t) for i,p in enumerate(self.player_list) for t in p.hand_tiles if t in FLOWERS]
        if not self.opening_queue:
            self.opening_queue = None
            self.current_player_index = 0
            for i,p in enumerate(self.player_list):
                p.has_draw_slot = i == 0
            return self.begin_turn(0)
        index, tile = self.opening_queue.pop(0)
        return self._replacement_window(index, tile)

    def _replacement_window(self, index, tile):
        self.current_player_index = index
        self.replacement_pending = (index, tile)
        return self._window(P.REPLACEMENT, index, actions={**empty_actions(), index: ["buhua"]})

    def resolve_replacement(self):
        index, tile = self.replacement_pending
        p = self.player_list[index]
        opening = self.opening_queue is not None
        drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        p.hand_tiles.remove(tile)
        p.huapai_list.append(tile)
        p.has_draw_slot = False
        self.replacement_pending = None
        self._event("buhua", index, tile=tile, buhua_tile=tile, is_mo_buhua=drawn)
        new_kong = self.flower_kong(index, tile)
        if new_kong:
            self.settle_kong(index, "flower")
        if not self.tiles_list:
            return self.end_draw()
        if opening:
            supplement = self.tiles_list.pop()
            p.hand_tiles.append(supplement)
            p.draw_kind = "kong" if new_kong else "flower"
            p.after_kong = new_kong
            self._event("deal_gang_tile" if new_kong else "deal_buhua_tile", index, tile=supplement)
            return self._next_opening_flower()
        return self.draw_for(index, kind="kong" if new_kong else "flower")

    def flower_kong(self, index, tile):
        p = self.player_list[index]
        key = tile if tile < 50 else 51 if tile <= 54 else 55
        complete = p.huapai_list.count(tile) == 4 if tile < 50 else all(t in p.huapai_list for t in range(key,key+4))
        if not complete or key in p.flower_kongs:
            return False
        p.flower_kongs.add(key)
        return True

    def draw_for(self, index, kind="normal"):
        if not self.tiles_list:
            return self.end_draw()
        p = self.player_list[index]
        p.passed_pungs.clear()
        p.passed_win_value = None
        p.hand_tiles.sort()
        tile = self.tiles_list.pop(0 if kind == "normal" else -1)
        p.hand_tiles.append(tile)
        p.has_draw_slot = True
        p.after_kong = kind == "kong"
        p.draw_kind = kind
        if kind == "normal":
            p.draw_count += 1
            self.natural_draw_count[index] = p.draw_count
            self.kong_chain_actor = self.kong_chain_payer = None
            self.kong_chain_count = 0
        self.current_player_index = index
        self._event({"normal":"deal_tile", "kong":"deal_gang_tile", "flower":"deal_buhua_tile"}[kind], index, tile=tile)
        return self.begin_turn(index)
