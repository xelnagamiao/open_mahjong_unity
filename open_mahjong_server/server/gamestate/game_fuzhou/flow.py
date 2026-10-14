from ...game_calculation.fuzhou import rules as book
from .actions import empty_actions, turn_actions, claim_actions, legal_cuts, kong_tiles, declaration_cuts, CHOWS
from .state_machine import Phase as P


class HandFlow:
    def _window(self,phase,actor=None,tile=None,actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value,player=actor,tile=tile,actions=actions or empty_actions())

    def _event(self,action,actor,**fields):
        event = dict(action=action,player=actor,**fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def begin_turn(self,index,discard_only=False):
        self.current_player_index = index
        p = self.player_list[index]
        flower = next((t for t in p.hand_tiles if t in book.FLOWERS),None)
        if flower is not None:
            return self._replacement_window(index,flower)
        self.hand_action_is_gang_draw[index] = p.after_ready_kong
        self.action_policy.refresh_waiting_tiles(self,index,exclude_last_tile=p.has_draw_slot)
        return self._window(P.DISCARD_ONLY if discard_only else P.TURN,index,
                            actions=turn_actions(self,index,discard_only))

    def qiangjin_cuts(self,index):
        p = self.player_list[index]
        if p.hand_tiles.count(self.jin) >= 2:
            return set()
        if index != self.dealer_index:
            return {0} if book.is_complete(p.hand_tiles+[self.jin],jin=self.jin) else set()
        return {t for t in set(p.hand_tiles) if book.is_complete(
            p.hand_tiles[:p.hand_tiles.index(t)]+p.hand_tiles[p.hand_tiles.index(t)+1:]+[self.jin],jin=self.jin)}

    def opening_claim_window(self):
        actions = empty_actions()
        for i,p in enumerate(self.player_list):
            if self.can_three_gold(i):
                actions[i].append("sanjin")
            if i == self.dealer_index and book.is_complete(p.hand_tiles,jin=self.jin):
                actions[i].append("hu_self")
            if self.qiangjin_cuts(i):
                actions[i].append("qiangjin")
            if actions[i]:
                actions[i].append("pass")
        return self._window(P.OPENING,self.dealer_index,self.jin,actions)

    def validate_response(self,index,data,offered):
        if type(index) is not int or index not in range(4) or not isinstance(data,dict):
            raise ValueError("非法行动座位或参数")
        action = data.get("action_type")
        if action not in offered:
            raise ValueError("行动不在当前窗口内")
        if action == "cut":
            self._cut_identity(index,data)
        elif action in ("angang","jiagang"):
            tile = data.get("target_tile")
            if type(tile) is not int or tile not in kong_tiles(self,index,action):
                raise ValueError("非法开杠牌")
        elif action in ("youjin","tingjin"):
            tile = data.get("target_tile")
            if type(tile) is not int or tile not in declaration_cuts(self,index,action):
                raise ValueError("非法宣言切牌")
        elif action == "qiangjin" and index == self.dealer_index:
            tile = data.get("target_tile")
            if type(tile) is not int or tile not in self.qiangjin_cuts(index):
                raise ValueError("庄家抢金须选择合法的第一张切牌")

    def _cut_identity(self,index,data):
        p = self.player_list[index]
        tile,position,drawn = data.get("TileId"),data.get("cutIndex",-1),data.get("cutClass")
        if type(tile) is not int or tile not in legal_cuts(self,index):
            raise ValueError("非法切牌")
        if type(position) is not int or position < -1 or position >= len(p.hand_tiles):
            raise ValueError("切牌索引无效")
        if drawn is not None and type(drawn) is not bool:
            raise ValueError("切牌类型无效")
        if drawn is True:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile:
                raise ValueError("摸切牌不匹配")
            # Sorting or dragging may put the drawn card anywhere in the
            # visual hand. Its physical identity is still the last held tile.
            position = len(p.hand_tiles)-1
        elif drawn is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held or p.golden_middle or p.youjin:
                raise ValueError("手切牌不匹配或手牌已锁定")
            # The Unity index is a visual coordinate: sorting and manual
            # dragging can change it independently of the authoritative hand.
            position = held.index(tile)
        elif position < 0:
            position = len(p.hand_tiles)-1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        elif p.hand_tiles[position] != tile:
            raise ValueError("索引与切牌不一致")
        if (p.golden_middle or p.youjin) and (not p.has_draw_slot or position != len(p.hand_tiles)-1):
            raise ValueError("宣言后只能摸切")
        return tile,position,p.has_draw_slot and position == len(p.hand_tiles)-1

    def apply_action_results(self,window,responses,*,settlements=None):
        if settlements or window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("过期窗口或客户端提供结算")
        offered = window.get("actions",{})
        if set(responses) != {i for i,a in offered.items() if a}:
            raise ValueError("响应座位集合错误")
        for i,data in responses.items():
            self.validate_response(i,data,offered[i])
        phase = P(window["status"])
        if phase == P.OPENING:
            result = self._opening_response(window,responses)
        elif phase in (P.TURN,P.DISCARD_ONLY):
            result = self._turn(window,responses[window["player"]])
        elif phase == P.REPLACEMENT:
            result = self.resolve_replacement()
        elif phase == P.RESPONSE:
            result = self._response(window,responses)
        elif phase == P.KONG:
            result = self._kong_response(window,responses)
        elif phase == P.SEA:
            actor = window["player"]
            action = responses[actor]["action_type"]
            result = self.next_sea_draw() if action=="pass" else self.settle_win(actor,"self_draw",
                self.player_list[actor].hand_tiles[-1],special="three_gold" if action=="sanjin" else None)
        else:
            raise ValueError("当前阶段不接受牌局行动")
        return self.open_action_window(result)

    def _opening_response(self,window,responses):
        def priority(index):
            action = responses[index]["action_type"]
            rank = 0 if action=="sanjin" else 1 if action=="hu_self" else 3 if index==self.dealer_index else 2
            return rank,(index-self.dealer_index)%4
        claims = [i for i,data in responses.items() if data["action_type"] != "pass"]
        if not claims:
            return self.begin_turn(self.dealer_index)
        winner = min(claims,key=priority)
        p,action = self.player_list[winner],responses[winner]["action_type"]
        if action == "sanjin":
            return self.settle_win(winner,"self_draw",special="three_gold")
        if action == "hu_self":
            return self.settle_win(winner,"self_draw",p.hand_tiles[-1],heavenly=True)
        if winner == self.dealer_index:
            tile = responses[winner]["target_tile"]
            self._discard(winner,dict(TileId=tile))
        # The indicator is the actual claimed seventeenth tile. Record a
        # transfer, not a fourth hidden joker or an invented river discard.
        self.indicator_claimed = True
        p.hand_tiles.append(self.jin)
        p.has_draw_slot = True
        self._event("qiangjin_take",winner,tile=self.jin)
        return self.settle_win(winner,"opening",self.jin,qiangjin=True)

    def _turn(self,window,data):
        actor,action = window["player"],data["action_type"]
        p = self.player_list[actor]
        if action in ("hu_self","sanjin"):
            return self.settle_win(actor,"self_draw",p.hand_tiles[-1],special="three_gold" if action=="sanjin" else None)
        if action in ("angang","jiagang"):
            self.pending_kong = dict(actor=actor,tile=data["target_tile"],concealed=action=="angang",
                ready=bool(p.waiting_tiles),is_mo_gang=p.hand_tiles[-1]==data["target_tile"])
            return self._window(P.KONG,actor,data["target_tile"],
                                empty_actions() if action=="angang" else claim_actions(self,actor,data["target_tile"],rob=True))
        if action in ("youjin","tingjin"):
            tile = data["target_tile"]
            self._discard(actor,dict(TileId=tile))
            if action == "tingjin":
                p.golden_middle = True
                p.locked_waits = {self.jin}
            else:
                p.youjin += 1
                p.youjin_tile = tile
                p.locked_waits = {self.jin,tile} if p.youjin==1 else {self.jin}
                self.youjin_owner = actor
                if p.youjin == 2:
                    self.double_youjin_owner = actor
                    self.double_youjin_remaining = [(actor+s)%4 for s in range(1,4)]
            self._event(action,actor,tile=tile)
        else:
            tile = self._discard(actor,data)
        return self._window(P.RESPONSE,actor,tile,claim_actions(self,actor,tile))

    def _discard(self,actor,data):
        p = self.player_list[actor]
        tile,position,drawn = self._cut_identity(actor,data)
        p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.has_draw_slot = p.after_ready_kong = False
        p.draw_kind = ""
        p.discarded_gold |= tile == self.jin
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        self.discard_log.append((actor,tile))
        self.last_discard_offsets[actor] = len(self.discard_log)-1
        display_position=data.get("cutIndex",-1)
        self._event("cut",actor,tile=tile,cutIndex=display_position if display_position>=0 else position,cutClass=drawn,
                    is_timeout_action=bool(data.get("is_timeout_action")))
        return tile

    def _response(self,window,responses):
        actor,tile = window["player"],window["tile"]
        winners = [i for i,d in responses.items() if d["action_type"]=="hu"]
        if winners:
            return self.settle_win(min(winners,key=lambda i:(i-actor)%4),"discard",tile,payer=actor)
        claims = [i for i,d in responses.items() if d["action_type"]!="pass"]
        if not claims:
            return self.draw_for(self.next_turn_index(actor))
        index = min(claims,key=lambda i:(0 if responses[i]["action_type"] in ("peng","gang") else 1,(i-actor)%4))
        action,p = responses[index]["action_type"],self.player_list[index]
        ready = bool(book.waiting_tiles(p.hand_tiles,p.combination_tiles,jin=self.jin))
        if action in CHOWS:
            offset = CHOWS.index(action)
            middle = tile+offset-1
            cards = [middle-1,middle,middle+1]
            needed = list(cards)
            needed.remove(tile)
            code = f"s{middle}"
            marked = cards.index(tile)
        else:
            cards = [tile]*(4 if action=="gang" else 3)
            needed = cards[:-1]
            code = ("g" if action=="gang" else "k")+str(tile)
            marked = {1:len(cards)-1,2:1,3:0}[(actor-index)%4]
        for t in needed:
            p.hand_tiles.remove(t)
        mask = [v for n,t in enumerate(cards) for v in (1 if n==marked else 0,t)]
        p.combination_tiles.append(code)
        p.combination_mask.append(mask)
        p.has_draw_slot = False
        p.after_ready_kong = ready and action=="gang"
        self.player_list[actor].discard_tiles.pop()
        self.current_player_index = index
        self.update_reserve()
        self._event(action,index,tile=tile,meld_code=code,combination_mask=mask,cut_from_player=actor)
        return self.draw_for(index,"kong") if action=="gang" else self.begin_turn(index,discard_only=True)

    def _kong_response(self,window,responses):
        pending = self.pending_kong
        actor,tile = pending["actor"],pending["tile"]
        winners = [i for i,d in responses.items() if d["action_type"]=="hu"]
        if winners:
            return self.settle_win(min(winners,key=lambda i:(i-actor)%4),"rob_kong",tile,payer=actor)
        p = self.player_list[actor]
        if pending["concealed"]:
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code,mask,action = f"G{tile}",[2,tile,0,tile,0,tile,2,tile],"angang"
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{tile}")
            code,mask,action = f"g{tile}",list(p.combination_mask[position]),"jiagang"
            called = next(i for i in range(0,len(mask),2) if mask[i]==1)
            mask[called:called] = [3,tile]
            p.combination_tiles[position],p.combination_mask[position] = code,mask
        p.has_draw_slot = False
        p.after_ready_kong = pending["ready"]
        self.pending_kong = None
        self.update_reserve()
        self._event(action,actor,tile=tile,meld_code=code,combination_mask=mask,is_mo_gang=pending["is_mo_gang"])
        return self.draw_for(actor,"kong")
