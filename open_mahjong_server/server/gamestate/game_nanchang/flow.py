from .actions import CHOWS, claim_actions, claim_tiles, empty_actions, legal_cuts, kong_tiles, turn_actions
from .state_machine import Phase as P
from ...game_calculation.nanchang import rules as book


class HandFlow:
    def _window(self,phase,actor=None,tile=None,actions=None,**kwargs):
        self.machine.transition(phase)
        return dict(status=phase.value,player=actor,tile=tile,actions=actions or empty_actions(),**kwargs)

    def _event(self,action,actor,**fields):
        event = dict(action=action,player=actor,**fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def opening_window(self):
        return self.begin_turn(0)

    def begin_turn(self,index,discard_only=False):
        self.current_player_index = index
        p = self.player_list[index]
        self.hand_action_is_gang_draw[index] = p.after_kong
        self.action_policy.refresh_waiting_tiles(self,index,exclude_last_tile=p.has_draw_slot)
        return self._window(P.DISCARD_ONLY if discard_only else P.TURN,index,actions=turn_actions(self,index,discard_only))

    def validate_response(self,index,data,offered):
        if type(index) is not int or index not in range(4) or not isinstance(data,dict) or data.get("action_type") not in offered:
            raise ValueError("行动不在当前窗口内")
        action = data["action_type"]
        if action == "cut":
            self._cut_identity(index,data)
        elif action in ("angang","jiagang"):
            if type(data.get("target_tile")) is not int or data["target_tile"] not in kong_tiles(self,index,action):
                raise ValueError("不能对指定牌开杠")

    def _cut_identity(self,index,data):
        p = self.player_list[index]
        tile,position,drawn = data.get("TileId"),data.get("cutIndex",-1),data.get("cutClass")
        if type(tile) is not int or tile not in legal_cuts(self,index):
            raise ValueError("非法出牌")
        if type(position) is not int or position < -1 or position >= len(p.hand_tiles):
            raise ValueError("出牌索引无效")
        if drawn is not None and type(drawn) is not bool:
            raise ValueError("出牌类型无效")
        if drawn is True:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile or position not in (-1,len(p.hand_tiles)-1):
                raise ValueError("摸切身份错误")
            position = len(p.hand_tiles)-1
        elif drawn is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:
                raise ValueError("手切牌不在手牌中")
            position = held.index(tile)
        elif position < 0:
            position = len(p.hand_tiles)-1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        elif p.hand_tiles[position] != tile:
            raise ValueError("出牌索引与实体不一致")
        return tile,position,p.has_draw_slot and position == len(p.hand_tiles)-1

    def apply_action_results(self,window,responses,*,settlements=None):
        if settlements or window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("客户端结算或过期窗口")
        offered = window["actions"]
        if set(responses) != {i for i,a in offered.items() if a}:
            raise ValueError("响应座位集合错误")
        for i,data in responses.items():
            self.validate_response(i,data,offered[i])
        if window["status"] in (P.TURN.value,P.DISCARD_ONLY.value):
            result = self._turn(window,responses[window["player"]])
        elif window["status"] == P.RESPONSE.value:
            result = self._response(window,responses)
        else:
            raise ValueError("当前阶段不能行牌")
        return self.open_action_window(result)

    def _turn(self,window,data):
        index,action = window["player"],data["action_type"]
        p = self.player_list[index]
        if window.get("predraw"):
            if action == "nc_jingdiao":
                self.declare_jingdiao(index)
            return self.draw_for(index,kind=window["predraw"])
        if action == "hu_self":
            return self.settle_win(index,"self_draw",p.hand_tiles[-1])
        if action in ("angang","jiagang"):
            return self.commit_kong(index,data["target_tile"],action)
        tile,position,drawn = self._cut_identity(index,data)
        quote = self.score_win(index,"self_draw",tile)
        if p.has_draw_slot and quote and quote["qualifying_fan"]:
            p.missed_fan = max(p.missed_fan,quote["effective_fan"])
        elif not p.has_draw_slot and not book.is_complete(p.hand_tiles,p.combination_tiles,indicator=self.indicator):
            p.missed_fan = -1
        p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.has_draw_slot = p.after_kong = False
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        self.discard_log.append((index,tile))
        self.last_discard_offsets[index] = len(self.discard_log)-1
        self._event("cut",index,tile=tile,cutIndex=data.get("cutIndex",position),cutClass=drawn,
                    is_timeout_action=bool(data.get("is_timeout_action")))
        return self._window(P.RESPONSE,index,tile,claim_actions(self,index,tile))

    def _response(self,window,responses):
        actor,tile = window["player"],window["tile"]
        winners = [i for i,d in responses.items() if d["action_type"] == "hu"]
        if winners:
            return self.settle_win(min(winners,key=lambda i:(i-actor)%4),"discard",tile,payer=actor)
        for index,data in responses.items():
            p = self.player_list[index]
            if "hu" in window["actions"][index]:
                quote = self.score_win(index,"discard",tile,payer=actor)
                p.missed_fan = max(p.missed_fan,quote["effective_fan"])
            if data["action_type"] == "nc_jingdiao":
                self.declare_jingdiao(index)
        claims = [i for i,d in responses.items() if d["action_type"] not in ("pass","nc_jingdiao")]
        if not claims:
            return self.draw_for((actor+1)%4)
        index = min(claims,key=lambda i:(responses[i]["action_type"] in CHOWS,(i-actor)%4))
        action,p = responses[index]["action_type"],self.player_list[index]
        needed = claim_tiles(action,tile)
        for t in needed:
            p.hand_tiles.remove(t)
        group = sorted(needed+[tile])
        if action in CHOWS:
            code = f"s{group[1]}" if tile < 40 else "s"+"".join(str(t) for t in group)
            mask = [1,tile]+[v for t in needed for v in (0,t)]
        else:
            code = ("g" if action == "gang" else "k")+str(tile)
            marked = {1:len(group)-1,2:1,3:0}[(actor-index)%4]
            mask = [v for n,t in enumerate(group) for v in (1 if n == marked else 0,t)]
        p.combination_tiles.append(code)
        p.combination_mask.append(mask)
        p.has_draw_slot = p.after_kong = False
        self.player_list[actor].discard_tiles.pop()
        self.current_player_index = index
        self.transfer_jing(actor,index,tile)
        self._event(action,index,tile=tile,meld_code=code,combination_mask=mask,cut_from_player=actor)
        if action == "gang":
            self.record_kong(index,tile,action)
            return self.before_kong_draw(index)
        return self.begin_turn(index,discard_only=True)

    def transfer_jing(self,payer,receiver,tile):
        if tile in book.jing_tiles(self.indicator):
            self.jing_transfers.append(dict(payer=payer,receiver=receiver,tile=tile))

    def declare_jingdiao(self,index):
        p = self.player_list[index]
        if p.declared_jingdiao or not book.is_jingdiao(p.hand_tiles,p.combination_tiles,indicator=self.indicator):
            raise ValueError("未满足精吊牌型")
        p.declared_jingdiao = True
        p.tag_list.append("nc_jingdiao")
        self._event("nc_jingdiao",index)

    def before_kong_draw(self,index):
        p = self.player_list[index]
        if not p.declared_jingdiao and "peida" not in p.tag_list and book.is_jingdiao(p.hand_tiles,p.combination_tiles,indicator=self.indicator):
            return self._window(P.TURN,index,actions={**empty_actions(),index:["nc_jingdiao","nc_draw"]},predraw="kong")
        return self.draw_for(index,kind="kong")

    def commit_kong(self,index,tile,kind):
        if tile not in kong_tiles(self,index,kind):
            raise ValueError("杠不成立")
        p = self.player_list[index]
        drawn = p.hand_tiles[-1] == tile
        if kind == "angang":
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code,mask = f"G{tile}",[2,tile]*4
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{tile}")
            code,mask = f"g{tile}",list(p.combination_mask[position])
            marked = next(i for i in range(0,len(mask),2) if mask[i] == 1)
            mask[marked:marked] = [3,tile]
            p.combination_tiles[position],p.combination_mask[position] = code,mask
        p.has_draw_slot = False
        self._event(kind,index,tile=tile,meld_code=code,combination_mask=mask,is_mo_gang=drawn)
        self.record_kong(index,tile,kind)
        return self.before_kong_draw(index)

    def record_kong(self,index,tile,kind):
        self.kong_ledger.append(dict(actor=index,tile=tile,kind=kind,
                                     changes=book.kong_payments(index,kind,tile,indicator=self.indicator)))

    def draw_for(self,index,kind="normal"):
        if not self.tiles_list:
            return self.end_draw()
        p = self.player_list[index]
        if kind == "kong":
            position = -2 if self.tail_upper_next and len(self.tiles_list) >= 2 else -1
            self.tail_upper_next = not self.tail_upper_next
        else:
            position = 0
        absolute = position if position >= 0 else len(self.tiles_list)+position
        if absolute == self.indicator_index:
            self.indicator_index = -1
        elif self.indicator_index > absolute:
            self.indicator_index -= 1
        tile = self.tiles_list.pop(position)
        p.hand_tiles.sort()
        p.hand_tiles.append(tile)
        p.has_draw_slot,p.after_kong,p.draw_kind,p.missed_fan = True,kind == "kong",kind,-1
        self.current_player_index = index
        if kind == "normal":
            self.natural_draw_count[index] += 1
        self._event("deal_gang_tile" if kind == "kong" else "deal_tile",index,tile=tile)
        return self.begin_turn(index)
