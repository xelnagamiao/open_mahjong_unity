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
        replacement = None
        if self.opening_replacement and len(self.tiles_list) > self.dead_wall_count+4:
            replacement = self.tiles_list.pop()
            p.hand_tiles.append(replacement)
        self._event("buhua",index,tile=tile,buhua_tile=tile,is_mo_buhua=drawn,
                    replacement_tile=replacement)
        if self.opening_replacement:
            if replacement is None:
                return self.end_draw()
            # Keep two domain/record ticks, but send the pair in one do_action,
            # just like Guobiao opening replacement. The next seat still acts
            # after exactly one replacement, even when the new tile is a flower.
            self._event("deal_buhua_tile",index,tile=replacement,combined_replacement=True)
            return self._next_opening_flower((index+1)%4)
        p.has_draw_slot = False
        return self.draw_for(index,kind="flower")

    def open_gold(self):
        while len(self.tiles_list) > self.dead_wall_count+4:
            tile = self.tiles_list.pop(0)
            if tile in book.FLOWERS:
                p = self.player_list[self.dealer_index]
                p.huapai_list.append(tile)
                self.update_reserve()
                self._event("kaijin_flower",self.dealer_index,tile=tile)
                continue
            self.jin = tile
            self._event("kaijin",self.dealer_index,tile=tile)
            return self.after_gold_window()
        return self.end_draw()

    def draw_for(self,index,kind="normal"):
        if self.in_last_four:
            return self.next_sea_draw()
        if len(self.tiles_list) <= self.dead_wall_count+4:
            self.in_last_four = True
            # In double-youjin the owner gets only their one final draw.
            # The other three final tiles remain physically in the wall.
            self.sea_queue = ([self.double_youjin_owner] if self.double_youjin_owner is not None
                              else [(index+s)%4 for s in range(4)])
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
        if self.can_flower_win(index):
            return self.flower_win_window(index)
        return self.begin_turn(index)

    def flower_win_window(self,index):
        actions = ["fuzhou_flower_win"]
        if self.can_three_gold(index):
            actions.append("sanjin")
        return self._window(P.FLOWER_WIN,index,actions={**empty_actions(),index:actions})

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
        if self.can_flower_win(index):
            return self.flower_win_window(index)
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
