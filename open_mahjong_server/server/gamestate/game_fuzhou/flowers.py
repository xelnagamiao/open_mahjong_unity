from ...game_calculation.fuzhou import rules as book
from .actions import empty_actions
from .state_machine import Phase as P


class FlowerFlow:
    def opening_window(self):
        self.opening_replacement = True
        return self._next_opening_flower(self.dealer_index)

    def _next_opening_flower(self, start):
        for shift in range(4):
            index = (start+shift)%4
            tile = next((t for t in self.player_list[index].hand_tiles if t in book.FLOWERS),None)
            if tile is not None:
                return self._replacement_window(index,tile)
        self.opening_replacement = False
        for i,p in enumerate(self.player_list):
            p.has_draw_slot = i == self.dealer_index
        return self.open_gold()

    def _replacement_window(self, index, tile):
        self.current_player_index = index
        self.replacement_pending = (index,tile)
        return self._window(P.REPLACEMENT,index,actions={**empty_actions(),index:["buhua"]})

    def resolve_replacement(self):
        index,tile = self.replacement_pending
        p = self.player_list[index]
        drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        position = len(p.hand_tiles)-1 if drawn else p.hand_tiles.index(tile)
        p.hand_tiles.pop(position)
        p.huapai_list.append(tile)
        self.replacement_pending = None
        self.update_reserve()
        self._event("buhua",index,tile=tile,buhua_tile=tile,is_mo_buhua=drawn)
        if len(p.huapai_list) >= 20:
            return self.settle_win(index,"self_draw",special="flower")
        if self.opening_replacement:
            if len(self.tiles_list) <= self.dead_wall_count+4:
                return self.end_draw()
            replacement = self.tiles_list.pop()
            p.hand_tiles.append(replacement)
            self._event("deal_buhua_tile",index,tile=replacement)
            # Each seat gets one NON-flower per turn. A flower chain remains
            # with this seat; other original flowers wait for the next cycle.
            if replacement in book.FLOWERS:
                return self._replacement_window(index,replacement)
            return self._next_opening_flower((index+1)%4)
        p.has_draw_slot = False
        return self.draw_for(index,kind="flower")

    def open_gold(self):
        while len(self.tiles_list) > self.dead_wall_count+4:
            tile = self.tiles_list.pop()
            if tile in book.FLOWERS:
                p = self.player_list[self.dealer_index]
                p.huapai_list.append(tile)
                self.update_reserve()
                self._event("kaijin_flower",self.dealer_index,tile=tile)
                if len(p.huapai_list) >= 20:
                    return self.settle_win(self.dealer_index,"self_draw",special="flower")
                continue
            self.jin = tile
            self._event("kaijin",self.dealer_index,tile=tile)
            return self.opening_claim_window()
        return self.end_draw()

    def draw_for(self,index,kind="normal"):
        if self.in_last_four:
            return self.next_sea_draw()
        if len(self.tiles_list) <= self.dead_wall_count+4:
            self.in_last_four = True
            self.sea_queue = [(index+s)%4 for s in range(4)]
            return self.next_sea_draw()
        p = self.player_list[index]
        p.hand_tiles.sort()
        tile = self.tiles_list.pop(0 if kind=="normal" else -1)
        p.hand_tiles.append(tile)
        p.has_draw_slot = True
        p.draw_kind = kind
        if kind == "normal":
            p.draw_count += 1
            p.after_ready_kong = False
            self.natural_draw_count[index] = p.draw_count
        self.current_player_index = index
        self._event({"normal":"deal_tile","kong":"deal_gang_tile","flower":"deal_buhua_tile"}[kind],index,tile=tile)
        return self.begin_turn(index)

    def next_sea_draw(self):
        if not self.sea_queue or len(self.tiles_list) <= self.dead_wall_count:
            return self.end_draw()
        index = self.sea_queue.pop(0)
        p = self.player_list[index]
        p.hand_tiles.sort()
        tile = self.tiles_list.pop(0)
        p.hand_tiles.append(tile)
        p.has_draw_slot = True
        p.draw_kind = "sea"
        p.after_ready_kong = False
        p.draw_count += 1
        self.current_player_index = index
        self._event("deal_tile",index,tile=tile)
        actions = []
        if self.can_win(index,"self_draw",tile):
            actions.append("hu_self")
        if self.can_three_gold(index):
            actions.append("sanjin")
        actions.append("pass")
        return self._window(P.SEA,index,actions={**empty_actions(),index:actions})

    def next_turn_index(self, actor):
        if self.double_youjin_owner is not None:
            if self.double_youjin_remaining:
                return self.double_youjin_remaining.pop(0)
            return self.double_youjin_owner
        return (actor+1)%4
