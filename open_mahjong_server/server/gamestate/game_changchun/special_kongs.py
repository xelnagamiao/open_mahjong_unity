"""Special meld actions and the rob-pung/rob-kong transaction."""
from ...game_calculation.changchun import rules as book
from ..game_taiwan import action_check as actions
from ..public.hand_slot_utils import clear_draw_slot


class SpecialKongFlow:
    def kong_allowed(self,index,tile,kind):
        player = self.player_list[index]
        if (type(tile) is not int or tile not in book.TILES or kind not in ("concealed","added","direct")
                or not self.can_establish_kong() or self.cc_window != "normal"):
            return False
        if kind != "direct" and player.cc_no_kong_until_draw:
            return False
        hand,melds = list(player.hand_tiles),list(player.combination_tiles)
        amount = {"concealed":4,"added":1,"direct":3}[kind]
        if hand.count(tile)<amount or kind=="added" and f"k{tile}" not in melds:
            return False
        for _ in range(amount): hand.remove(tile)
        if kind=="added": melds[melds.index(f"k{tile}")] = f"g{tile}"
        else: melds.append(f"{'G' if kind=='concealed' else 'g'}{tile}")
        if book.valid_hand(hand,melds) is None:
            return False
        if any(c.startswith("s") for c in melds) and len(hand)+4*sum(c.startswith("G") for c in melds)<=1:
            return False
        if player.ready_locked:
            if kind!="direct" and tile!=player.last_drawn_tile:
                return False
            return bool(player.locked_waits) and book.waits(hand,melds,declaration=True)==player.locked_waits
        return True

    def special_candidates(self,index):
        player = self.player_list[index]
        if (self.cc_window!="normal" or player.discard_count!=0 or player.ready_locked
                or len(player.combination_tiles)>=4 or not self.can_take_supplement_tile()):
            return []
        result=[]
        for code in book.initial_special_candidates(player.hand_tiles,player.combination_tiles):
            meld=book.parse_meld(code)
            hand=list(player.hand_tiles)
            for t in meld.physical: hand.remove(t)
            codes=player.combination_tiles+[code]
            if book.valid_hand(hand,codes,complete=True) is None:
                continue
            if any(c.startswith("s") for c in codes) and len(hand)-1+4*sum(c.startswith("G") for c in codes)<=1:
                continue
            result.append({"token":len(result),"code":code,"label":book.SPECIAL_NAMES[meld.kind],
                           "physical":list(meld.physical),"logical":list(meld.logical)})
        return result

    def special_added_candidates(self,index):
        player=self.player_list[index]
        if (self.cc_window!="normal" or not self.can_establish_kong() or player.cc_no_kong_until_draw
                or player.discard_count==0 and player.cc_first_from_claim
                or player.last_drawn_tile is None and player.discard_count>0):
            return []
        result=[]
        for position,code in enumerate(player.combination_tiles):
            if not code.startswith("C"): continue
            meld=book.parse_meld(code)
            for tile in sorted(set(player.hand_tiles)):
                if player.ready_locked and tile!=player.last_drawn_tile: continue
                targets=book.SPECIAL_DOMAINS[meld.kind] if tile==book.ONE_BAMBOO else (tile,)
                for logical in targets:
                    try: added=book.add_special(code,tile,logical)
                    except ValueError: continue
                    hand,codes=list(player.hand_tiles),list(player.combination_tiles)
                    hand.remove(tile);codes[position]=added
                    if book.valid_hand(hand,codes) is None: continue
                    if player.ready_locked and book.waits(hand,codes,declaration=True)!=player.locked_waits: continue
                    result.append({"token":len(result),"position":position,"code":added,"tile":tile,
                        "represented":logical,"label":"加"+book.SPECIAL_NAMES[meld.kind],
                        "physical":[tile],"logical":[logical]})
        return result

    async def execute_special(self,index,token):
        options=self.special_candidates(index)
        if type(token) is not int or not 0<=token<len(options) or index!=self.current_player_index:
            return False
        option=options[token]
        player=self.player_list[index]
        for tile in option["physical"]: player.hand_tiles.remove(tile)
        if player.last_drawn_tile in option["physical"]:
            clear_draw_slot(player);player.last_drawn_tile=None
        player.combination_tiles.append(option["code"])
        player.combination_mask.append([x for tile in option["physical"] for x in (0,tile)])
        self.table_claim_or_kong=True
        await self.emit_cc_event({"kind":"special","player":index,"has_draw_slot":player.last_drawn_tile is not None,**option})
        await self._pay_kong(index,"special")
        self.action_dict=self.check_hand_actions(index)
        self.game_status="waiting_hand_action"
        return True

    async def execute_special_added(self,index,token):
        options=self.special_added_candidates(index)
        if (type(token) is not int or not 0<=token<len(options) or index!=self.current_player_index
                or self.cc_pending_special is not None or self._pending_jiagang is not None):
            return False
        option=options[token]
        player=self.player_list[index]
        player.hand_tiles.remove(option["tile"])
        clear_draw_slot(player);player.last_drawn_tile=None
        self.cc_pending_special={**option,"player":index}
        self.jiagang_tile=option["tile"]
        await self.emit_cc_event({"kind":"added_offer","player":index,**option})
        self.action_dict=self.check_added_kong_actions(option["tile"])
        if any(self.action_dict.values()):
            self.game_status="waiting_action_qianggang"
        else:
            await self.finalize_jiagang()
        return True

    async def _pay_kong(self,index,kind,tile=None):
        delta=book.kong_payments(index,kind,tile)
        for i,score in delta.items(): self.player_list[i].score+=score
        self.kong_ledger.append({"player":index,"kind":kind,"tile":tile,"delta":delta})
        await self.emit_cc_event({"kind":"kong_score","player":index,"kong_kind":kind,"tile":tile,"delta":delta})

    async def execute_angang(self,index,tile):
        if not self.kong_allowed(index,tile,"concealed"):
            return
        await super().execute_angang(index,tile)
        await self._pay_kong(index,"concealed",tile)

    async def execute_jiagang(self,index,tile):
        if not self.kong_allowed(index,tile,"added") or self.cc_pending_special is not None or self._pending_jiagang is not None:
            return
        await super().execute_jiagang(index,tile)

    async def finalize_jiagang(self):
        special=self.cc_pending_special
        ordinary=self._pending_jiagang
        if special is None and ordinary is None:
            return
        if special is not None:
            player=self.player_list[special["player"]]
            player.combination_tiles[special["position"]]=special["code"]
            meld=book.parse_meld(special["code"])
            player.combination_mask[special["position"]]=[x for tile in meld.physical for x in (0,tile)]
            self.cc_pending_special=None
            self.jiagang_tile=None
            await self.emit_cc_event({"kind":"added_commit",**special})
            await self._pay_kong(player.player_index,"special_added",special["tile"])
            self.next_supplement_kind="jiagang"
            self.game_status="deal_card_after_gang"
        else:
            await self._pay_kong(ordinary["player_index"],"added",ordinary["normal"])
            await super().finalize_jiagang()

    async def _withdraw_added(self):
        special=self.cc_pending_special
        tile=self.jiagang_tile
        if special is not None:
            self.cc_pending_special=None
        else:
            self._rollback_pending_jiagang(consume_robbed_tile=True)
        self.jiagang_tile=None
        await self.emit_cc_event({"kind":"added_robbed","player":self.current_player_index,
                                  "tile":tile,"special":special is not None})

    async def resolve_rob_kong_responses(self,responses,allowed):
        tile=self.jiagang_tile
        if tile is None:
            return
        source=self.current_player_index
        wins=[];claims=[]
        for index,offered in allowed.items():
            chosen=responses.get(index,{}).get("action_type","pass")
            if chosen not in offered: chosen="pass"
            if chosen in actions.HU_ACTIONS:
                wins.append((index,chosen))
            elif any(a in actions.HU_ACTIONS for a in offered):
                self.enter_water(index)
            if chosen in ("peng","gang"):
                claims.append((index,chosen))
        wins=self._selected_winners(wins)
        if wins:
            await self._withdraw_added()
            self._queue_winner_resolution([self._build_pending_winner(index,"robbing_kong",action,
                self.result_dict[action],tile,source) for index,action in wins])
        elif claims:
            index,action=min(claims,key=lambda item:(item[0]-source)%4)
            await self._withdraw_added()
            self.player_list[source].discard_tiles.append(tile)
            await self.emit_cc_event({"kind":"rob_claim","player":source,"tile":tile,"claimant":index})
            await self.execute_claim(index,action)
        else:
            await self.finalize_jiagang()

    async def execute_claim(self,index,action):
        source=self.current_player_index
        if not self.player_list[source].discard_tiles:
            return
        tile=self.player_list[source].discard_tiles[-1]
        if action not in self.check_discard_actions(tile).get(index,[]):
            return
        player=self.player_list[index]
        if player.discard_count==0:
            player.cc_first_from_claim=True
        self.cc_turn_serial+=1
        player.cc_no_kong_until_draw=action=="peng"
        await super().execute_claim(index,action)
        if action=="gang":
            await self._pay_kong(index,"direct",tile)
        elif not book.shapes(player.hand_tiles,player.combination_tiles):
            player.passed_fan=-1
