"""Private indicator lifecycle and the final four no-discard draws."""
import random

from ..game_taiwan.boardcast import broadcast_do_action
from ..game_guobiao.combination_mask_view import get_combination_fields_for_viewer
from ..public.game_record_manager import player_action_record_deal
from ..public.hand_slot_utils import clear_draw_slot


class BaoFlow:
    def can_take_normal_tile(self):
        if self.cc_tail_remaining is not None:
            return self.cc_tail_remaining > 0 and bool(self.tiles_list)
        return len(self.tiles_list)//2 > 7

    def can_take_supplement_tile(self):
        return self.cc_tail_remaining is None and len(self.tiles_list)//2 > 9

    can_establish_kong = can_take_supplement_tile
    can_keep_wall_after_kong = can_take_supplement_tile

    def playable_wall_count(self):
        if self.cc_tail_remaining is not None:
            return self.cc_tail_remaining
        return max(0, len(self.tiles_list)-14-(len(self.tiles_list)%2))

    def _sync_wall_ids(self):
        if len(self.cc_wall_ids) != len(self.tiles_list):
            if self.cc_bao_slot is not None:
                raise RuntimeError("宝牌已建立后牌墙位置不能被外部改写")
            self.cc_wall_ids = list(range(len(self.tiles_list)))

    def _pop_wall(self,index):
        self._sync_wall_ids()
        self.cc_wall_ids.pop(index)
        return self.tiles_list.pop(index)

    def _take_supplement_tile(self):
        # Tail upper tile first; when one lower tile remains take that tile.
        return self._pop_wall(-1 if len(self.tiles_list)%2 else -2)

    def bao_visible(self,index):
        player = self.player_list[index]
        return bool(player.declared_ready and self.cc_bao_tile is not None
                    and player.cc_bao_seen == self.cc_bao_revision)

    def public_bao_count(self):
        if self.cc_bao_tile is None:
            return 0
        # Indicator itself is excluded: the other three physical copies must
        # all be public. Concealed kongs count only after their end reveal.
        count = 0
        for player in self.player_list:
            count += player.discard_tiles.count(self.cc_bao_tile)
            for code in player.combination_tiles:
                if not code.startswith("G"):
                    from ...game_calculation.changchun.rules import parse_meld
                    count += parse_meld(code).physical.count(self.cc_bao_tile)
        return count

    def can_change_bao(self):
        return (self.cc_window == "before_draw" and self.bao_visible(self.current_player_index)
                and self.public_bao_count() >= 3 and not self.cc_bao_exhausted)

    def view_state(self,viewer):
        own = self.player_list[viewer]
        view = {"version": 1, "window": self.cc_window, "phase": self.game_status, "bao_revision": self.cc_bao_revision,
                "self_has_draw_slot": own.has_draw_slot,
                "self_last_drawn_tile": own.last_drawn_tile if own.has_draw_slot else 0,
                "bao_owner": self.cc_bao_owner, "bao_exhausted": self.cc_bao_exhausted,
                "bao_visible": self.bao_visible(viewer),
                "bao_tile": self.cc_bao_tile if self.bao_visible(viewer) else 0,
                "tail_remaining": self.cc_tail_remaining,
                "tail_tiles": [{"player":i,"tile":t if i==viewer or self.game_status=="END" else 0}
                               for i,t in self.cc_tail_tiles]}
        # CC events carry complete meld identity to make rollback/reconnect
        # unambiguous. Ordinary events remain handled by the existing client.
        if self.cc_event or self.game_status == "END":
            states = []
            for player in self.player_list:
                codes,masks = get_combination_fields_for_viewer(player,viewer)
                item = {"player":player.player_index, "hand_count":len(player.hand_tiles),
                        "has_draw_slot":player.has_draw_slot,
                        "melds":codes,"masks":masks,"score":player.score}
                if viewer == player.player_index or self.game_status == "END":
                    item["hand"] = list(player.hand_tiles)
                    item["last_drawn_tile"] = player.last_drawn_tile or 0
                states.append(item)
            view["players"] = states
        return view

    async def change_bao(self, *, initial=False):
        if not initial and not self.can_change_bao():
            return False
        self._sync_wall_ids()
        old_tile, old_slot = self.cc_bao_tile, self.cc_bao_slot
        if old_slot is not None:
            insert = next((i for i,x in enumerate(self.cc_wall_ids) if x > old_slot),len(self.cc_wall_ids))
            self.cc_wall_ids.insert(insert,old_slot)
            self.tiles_list.insert(insert,old_tile)
        even = len(self.tiles_list)-len(self.tiles_list)%2
        candidates = [(die,even-2*die) for die in range(1,7)
                      if even-2*die >= 0 and self.cc_wall_ids[even-2*die] not in self.cc_bao_used_slots]
        self.cc_bao_revision += 1
        self.cc_bao_tile = self.cc_bao_slot = None
        if candidates:
            seed = f"{self.round_random_seed}:bao:{self.cc_bao_revision}"
            die,pos = random.Random(seed).choice(candidates)
            self.cc_bao_slot = self.cc_wall_ids.pop(pos)
            self.cc_bao_tile = self.tiles_list.pop(pos)
            self.cc_bao_used_slots.add(self.cc_bao_slot)
            self.cc_bao_owner = self.current_player_index
            self.player_list[self.current_player_index].cc_bao_seen = self.cc_bao_revision
            await self.emit_cc_event({"kind":"bao_reveal" if initial else "bao_change",
                "player":self.current_player_index,"revision":self.cc_bao_revision,"tile":self.cc_bao_tile,
                "slot":self.cc_bao_slot,"dice":die,"old_slot":old_slot},private=True)
        else:
            self.cc_bao_exhausted = True
            self.cc_bao_owner = None
            await self.emit_cc_event({"kind":"bao_exhausted","revision":self.cc_bao_revision})
        return True

    async def _start_bao_turn(self,source):
        index = self.current_player_index
        player = self.player_list[index]
        if not player.declared_ready or player.cc_looked_turn == self.cc_turn_serial:
            return False
        player.cc_looked_turn = self.cc_turn_serial
        self.cc_resume_draw = source
        self.cc_window = "before_draw"
        if self.cc_bao_tile is None and not self.cc_bao_exhausted:
            await self.change_bao(initial=True)
        elif self.cc_bao_tile is not None:
            player.cc_bao_seen = self.cc_bao_revision
            await self.emit_cc_event({"kind":"bao_seen","player":index,"revision":self.cc_bao_revision})
        self.result_dict = {}
        self.action_dict = self.check_hand_actions(index)
        self.game_status = "waiting_hand_action"
        return True

    async def _deal_normal(self):
        if not self.can_take_normal_tile():
            self.draw_reason = "exhaustive"
            self.game_status = "END"
            return
        self.current_player_index = (self.current_player_index+1)%4
        self.cc_turn_serial += 1
        if not await self._start_bao_turn("normal"):
            await self.draw_current_player()

    async def _deal_supplement(self):
        if not await self._start_bao_turn("supplement"):
            self.cc_window = "normal"
            await super()._deal_supplement()

    async def draw_current_player(self):
        if self.cc_resume_draw == "supplement":
            self.cc_resume_draw = "normal"
            self.cc_window = "normal"
            await super()._deal_supplement()
            return
        if not self.can_take_normal_tile():
            self.game_status = "END"
            self.draw_reason = "exhaustive"
            return
        if self.cc_tail_remaining is None and len(self.tiles_list)//2 <= 9:
            self.cc_tail_remaining = 4
        self.cc_window = "final_four" if self.cc_tail_remaining is not None else "normal"
        index = self.current_player_index
        player = self.player_list[index]
        tile = self._pop_wall(0)
        player.hand_tiles.append(tile)
        player.has_draw_slot = True
        player.last_drawn_tile = tile
        player.normal_draw_count += 1
        player.passed_fan = -1
        player.cc_no_kong_until_draw = False
        self.last_draw_was_last = self.cc_window == "final_four"
        self.last_draw_after_kong = False
        if self.cc_tail_remaining is not None:
            self.cc_tail_remaining -= 1
        player_action_record_deal(self,deal_tile=tile,deal_type="d")
        await broadcast_do_action(self,action_list=["deal_tile"],action_player=index,deal_tile=tile)
        await self._prepare_hand_action_after_draw()

    async def pass_final_tile(self):
        if self.cc_window != "final_four":
            return
        player = self.player_list[self.current_player_index]
        tile = player.last_drawn_tile
        if tile is None or not player.hand_tiles or player.hand_tiles[-1] != tile:
            raise RuntimeError("末四张必须保持独立摸牌槽")
        player.hand_tiles.pop()
        clear_draw_slot(player)
        player.last_drawn_tile = None
        self.cc_tail_tiles.append((self.current_player_index,tile))
        await self.emit_cc_event({"kind":"tail_pass","player":self.current_player_index,"tile":tile},private=True)
        self.cc_window = "normal"
        self.game_status = "deal_card" if self.cc_tail_remaining else "END"
        if not self.cc_tail_remaining:
            self.draw_reason = "final_four_exhausted"
