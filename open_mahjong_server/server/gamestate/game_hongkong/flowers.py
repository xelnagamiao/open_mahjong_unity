"""Observable flower replacement, optional flower wins, and initial ready window."""

from .state_machine import HongKongPhase as P
from .action_check import empty_actions
from ...game_calculation.hongkong import structural_waits
from ...game_calculation.hongkong.models import FLOWERS


class HongKongFlowers:
    def opening_window(self):
        # Capture this pass's physical flowers. A replacement flower must wait
        # for the next East -> South -> West -> North pass (HKMA opening rule).
        self._initial_queue = [(p.player_index,tile) for p in self.player_list
                               for tile in sorted(p.hand_tiles,reverse=True) if tile in FLOWERS]
        self._initial_next = []
        self._dealt_flower_tiles = {tile for _,tile in self._initial_queue}
        self._initial_cursor = 0
        self._initial_pass = 0
        offer = self._offer_flowers(("initial",))
        return offer or self._continue_opening()

    def _replacement_window(self,index,tile,*,initial=False):
        if not self.tiles_list:
            return self.end_draw()
        self.current_player_index = index
        self.replacement_pending = (index,tile,initial)
        actions = empty_actions()
        actions[index] = ["buhua"]
        return self._window(P.REPLACEMENT,index,actions=actions)

    def resolve_replacement(self):
        index,tile,initial = self.replacement_pending
        self.replacement_pending = None
        self._reveal_flower(index,tile,initial=initial and tile in self._dealt_flower_tiles)
        if initial:
            self._initial_cursor += 1
            return self._draw_opening_replacement(index)
        return self.draw_for(index,kind="flower")

    def _reveal_flower(self,index,tile,*,initial=False,winning=False):
        p = self.player_list[index]
        is_drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        p.hand_tiles.remove(tile)
        p.huapai_list.append(tile)
        if not winning:
            p.replacement_kind = "flower"
            p.kong_liability_payer = None
            if self.rules.is_old or self.rules.is_lianhuise:
                p.consecutive_kongs = 0
        p.has_draw_slot = False
        self._event("buhua",index,tile=tile,buhua_tile=tile,is_mo_buhua=is_drawn and not initial)

    def _continue_opening(self):
        if self._initial_cursor == len(self._initial_queue):
            if self._initial_next:
                self._initial_queue = self._initial_next
                self._initial_next = []
                self._initial_cursor = 0
                self._initial_pass += 1
            else:
                return self._finish_opening()
        index,tile = self._initial_queue[self._initial_cursor]
        return self._replacement_window(index,tile,initial=True)

    def _draw_opening_replacement(self,index):
        p = self.player_list[index]
        tile = self.tiles_list.pop()
        p.hand_tiles.append(tile)
        p.tail_draws += 1
        p.has_draw_slot = True
        self._event("deal_buhua_tile",index,tile=tile)
        if tile in FLOWERS:
            if self.rules.is_lianhuise or self.rules.is_remix:
                self._initial_queue.insert(self._initial_cursor,(index,tile))
            else:
                self._initial_next.append((index,tile))
        offer = self._offer_flowers(("initial",))
        return offer or self._continue_opening()

    def _finish_opening(self):
        self.opening_flower_stage = False
        for p in self.player_list:
            p.has_draw_slot = p.player_index==0 and not self.rules.is_sixteen
            if p.has_draw_slot:
                p.hand_tiles[:] = sorted(p.hand_tiles[:-1])+p.hand_tiles[-1:]
            else:
                p.hand_tiles.sort()
            p.waiting_tiles = structural_waits(p.hand_tiles,p.combination_tiles,self.rules)
            if self.rules.is_old and p.player_index!=0 and len(p.huapai_list)==7:
                p.initial_seven_pending = True
        self.current_player_index = 0
        self.record_opening_complete()
        if self.rules.is_sixteen:
            actions = empty_actions()
            for p in self.player_list:
                if p.waiting_tiles:
                    actions[p.player_index] = ["ding_initial","pass"]
            return self._window(P.INITIAL_READY,0,actions=actions)
        return self.begin_turn(0)

    def resolve_initial_ready(self,responses):
        for index,data in responses.items():
            if data["action_type"] == "ding_initial":
                self.declare_ready(index,"heaven")
        return self.draw_for(0,normal=True)

    def draw_for(self,index,*,normal=False,kind=""):
        p = self.player_list[index]
        self.current_player_index = index
        if normal and self.rules.is_old and p.initial_seven_pending:
            p.initial_seven_pending = False
            offer = self._offer_flowers(("natural",index),initial_seven=index)
            if offer:
                return offer
        self.begin_new_draw(index,normal=normal)
        if kind == "flower":
            p.kong_liability_payer = None
        if self.tiles_list:
            tile = self.tiles_list.pop(0) if normal else self.tiles_list.pop()
            p.hand_tiles.append(tile)
            p.has_draw_slot = True
            if not normal:
                p.tail_draws += 1
            p.replacement_kind = kind
            action = "deal_tile" if normal else "deal_gang_tile" if kind=="kong" else "deal_buhua_tile"
            self._event(action,index,tile=tile)
            if tile not in FLOWERS:
                return self.begin_turn(index)
            offer = self._offer_flowers(("replacement",index))
            return offer or self._replacement_window(index,tile)
        return self.end_draw()

    def _offer_flowers(self,continuation,*,initial_seven=None):
        if not self.rules.flowers or self.rules.is_remix:
            return None
        for p in self.player_list:
            index = p.player_index
            count = len(p.huapai_list)+sum(t in FLOWERS for t in p.hand_tiles)
            kind,payer = "",None
            if count==8:
                kind = "eight"
            elif count==7:
                if self.rules.is_lianhuise:
                    kind = "seven"
                elif self.rules.is_old:
                    if self.opening_flower_stage and index!=0:
                        p.initial_seven_pending = True
                        continue
                    # Initial nondealer seven-flower wins wait until the first
                    # natural turn; all intermediate claim/kong actions revoke it.
                    if p.initial_seven_pending and initial_seven != index:
                        continue
                    if self.opening_flow_interrupted and p.draw_count==0 and index!=0:
                        continue
                    kind = "seven"
                else:
                    holders = [other.player_index for other in self.player_list if other is not p
                               and len(other.huapai_list)+sum(t in FLOWERS for t in other.hand_tiles)==1]
                    if holders:
                        kind,payer = "seven_steal",holders[0]
            if not kind or kind in p.flower_choice_declined:
                continue
            source = "self_draw" if payer is None else "discard"
            if not self.can_win(index,source,0,payer=payer,flower=kind):
                p.flower_choice_declined.add(kind)
                self._flower_midway_award(index,kind,payer)
                continue
            self.flower_pending = dict(winner=index,kind=kind,payer=payer,continuation=continuation)
            actions = empty_actions()
            actions[index] = ["hu_self" if payer is None else "hu","pass"]
            phase = P.FLOWER if payer is None else P.FLOWER_RESPONSE
            return self._window(phase,index,actions=actions,flower=kind)
        return None

    def resolve_flower(self,responses):
        pending = self.flower_pending
        index,kind,payer = pending["winner"],pending["kind"],pending["payer"]
        action = responses[index]["action_type"]
        self.flower_pending = None
        if action in ("hu","hu_self"):
            # Choosing a flower win exposes the winning flowers, without
            # replacing them or silently consuming the player's decision.
            for owner in (index,) if payer is None else (index,payer):
                for tile in list(self.player_list[owner].hand_tiles):
                    if tile in FLOWERS:
                        self._reveal_flower(owner,tile,initial=self.opening_flower_stage,winning=self.rules.is_lianhuise)
            return self.settle_winners([index],"self_draw" if payer is None else "discard",0,payer=payer,flower=kind)
        p = self.player_list[index]
        p.flower_choice_declined.add(kind)
        if self.rules.is_sixteen:
            self.remember_pass(index,0)
        self._flower_midway_award(index,kind,payer)
        continuation = pending["continuation"]
        if continuation[0] == "initial":
            return self._continue_opening()
        if continuation[0] == "natural":
            return self.draw_for(continuation[1],normal=True)
        p = self.player_list[continuation[1]]
        if p.hand_tiles and p.hand_tiles[-1] in FLOWERS:
            return self._replacement_window(continuation[1],p.hand_tiles[-1])
        return self.draw_for(continuation[1],kind="flower")

    def _flower_midway_award(self,index,kind,payer):
        if not self.rules.is_sixteen or (index,kind) in self.flower_awards:
            return
        self.flower_awards.add((index,kind))
        changes = [0]*4
        if kind == "eight":
            changes = [-40]*4
            changes[index] = 120
        elif kind == "seven_steal":
            changes[index] = 30
            changes[payer] = -30
        self.apply_immediate_points(changes,"两台花" if kind=="eight" else "七抢一")
