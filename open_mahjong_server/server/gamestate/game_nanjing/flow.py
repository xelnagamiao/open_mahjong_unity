from .actions import claim_actions, kong_tiles, legal_cuts, turn_actions
from .state_machine import Phase as P
from ...game_calculation.nanjing.rules import FLOWERS


class HandFlow:
    def _window(self, phase, actor=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile, actions=actions or {i: [] for i in range(4)})

    def _event(self, action, actor, **fields):
        event = dict(action=action, player=actor, **fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def begin_turn(self, index, discard_only=False):
        self.current_player_index = index
        p = self.player_list[index]
        flowers = [t for t in p.hand_tiles if t in FLOWERS]
        if flowers:
            return self._replacement_window(index, flowers[0])
        self.hand_action_is_gang_draw[index] = p.after_kong
        self.action_policy.refresh_waiting_tiles(self, index, exclude_last_tile=p.has_draw_slot)
        return self._window(P.DISCARD_ONLY if discard_only else P.TURN, index,
                            actions=turn_actions(self, index, discard_only))

    def validate_response(self, index, data, offered):
        if type(index) is not int or index not in range(4) or not isinstance(data, dict):
            raise ValueError("非法行动座位或参数")
        action = data.get("action_type")
        if action not in offered:
            raise ValueError("行动不在当前窗口内")
        if action == "cut":
            self._cut_identity(index, data)
        elif action in ("angang", "jiagang"):
            tile = data.get("target_tile")
            if type(tile) is not int or tile not in kong_tiles(self, index, action):
                raise ValueError("不能对指定牌开杠")

    def _cut_identity(self, index, data):
        p = self.player_list[index]
        tile, position, drawn = data.get("TileId"), data.get("cutIndex", -1), data.get("cutClass")
        if type(tile) is not int or tile not in legal_cuts(self, index):
            raise ValueError("非法出牌")
        if type(position) is not int or position < -1 or position >= len(p.hand_tiles):
            raise ValueError("出牌索引无效")
        if drawn is not None and type(drawn) is not bool:
            raise ValueError("出牌类型无效")
        if drawn is True:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile or position not in (-1, len(p.hand_tiles)-1):
                raise ValueError("摸切牌不匹配")
            position = len(p.hand_tiles)-1
        elif drawn is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:
                raise ValueError("手切牌不在手牌中")
            position = held.index(tile)
        elif position < 0:
            position = len(p.hand_tiles)-1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        elif p.hand_tiles[position] != tile:
            raise ValueError("出牌索引与牌张不一致")
        return tile, position, p.has_draw_slot and position == len(p.hand_tiles)-1

    def apply_action_results(self, window, responses, *, settlements=None):
        if settlements or window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("客户端结算或过期行动窗口")
        offered = window.get("actions") or {}
        if set(responses) != {i for i, a in offered.items() if a}:
            raise ValueError("行动座位集合不匹配")
        for index, data in responses.items():
            self.validate_response(index, data, offered[index])
        phase = P(window["status"])
        if phase in (P.TURN, P.DISCARD_ONLY):
            result = self._turn(window, responses[window["player"]])
        elif phase == P.RESPONSE:
            result = self._response(window, responses)
        elif phase == P.KONG:
            result = self._kong_response(window, responses)
        elif phase == P.REPLACEMENT:
            result = self.resolve_replacement()
        else:
            raise ValueError("当前阶段不接受牌局行动")
        return self.open_action_window(result)

    def remember_missed_win(self, index, tile, source="discard"):
        q = self.score_win(index, source, tile)
        if q is not None:
            p = self.player_list[index]
            p.passed_win_value = max(p.passed_win_value or 0, q.pattern_points)

    def _turn(self, window, data):
        index, action = window["player"], data["action_type"]
        p = self.player_list[index]
        if action == "hu_self":
            return self.settle_wins([index], "self_draw", p.hand_tiles[-1])
        if "hu_self" in window["actions"][index]:
            self.remember_missed_win(index, p.hand_tiles[-1], "self_draw")
        if action in ("angang", "jiagang"):
            tile = data["target_tile"]
            self.pending_kong = dict(actor=index, tile=tile, concealed=action == "angang", is_mo_gang=p.hand_tiles[-1] == tile)
            return self._window(P.KONG, index, tile,
                                actions=None if action == "angang" else claim_actions(self, index, tile, rob=True))
        tile, position, drawn = self._cut_identity(index, data)
        p.hand_tiles.pop(position)
        self.remember_missed_win(index, tile)
        p.hand_tiles.sort()
        p.has_draw_slot = p.after_kong = False
        p.last_draw_last_wall = False
        p.draw_kind = ""
        self.kong_chain_actor = self.kong_chain_payer = None
        self.kong_chain_count = 0
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        self.discard_log.append((index, tile))
        self.last_discard_offsets[index] = len(self.discard_log)-1
        self._event("cut", index, tile=tile, cutIndex=data.get("cutIndex", position), cutClass=drawn,
                    is_timeout_action=bool(data.get("is_timeout_action")))
        return self._window(P.RESPONSE, index, tile, claim_actions(self, index, tile))

    def _remember_responses(self, window, responses):
        for index, data in responses.items():
            p, action = self.player_list[index], data["action_type"]
            offered = window["actions"][index]
            if "hu" in offered and action != "hu":
                self.remember_missed_win(index, window["tile"], "rob_kong" if self.machine.phase == P.KONG else "discard")
            if "peng" in offered and action not in ("peng", "gang", "hu"):
                p.passed_pungs.add(window["tile"])

    def _response(self, window, responses):
        actor, tile = window["player"], window["tile"]
        winners = sorted((i for i, d in responses.items() if d["action_type"] == "hu"), key=lambda i: (i-actor) % 4)
        self.settle_follow(actor, tile)
        if winners:
            return self.settle_wins(winners, "discard", tile, payer=actor)
        self.settle_four_discards(actor, tile)
        self._remember_responses(window, responses)
        claims = [i for i, d in responses.items() if d["action_type"] != "pass"]
        if not claims:
            return self.draw_for((actor+1) % 4)
        index = min(claims, key=lambda i: (i-actor) % 4)
        action, p = responses[index]["action_type"], self.player_list[index]
        count = 3 if action == "gang" else 2
        for _ in range(count):
            p.hand_tiles.remove(tile)
        code = ("g" if action == "gang" else "k") + str(tile)
        marked = {1:count, 2:1, 3:0}[(actor-index) % 4]
        mask = [v for n in range(count+1) for v in (1 if n == marked else 0,tile)]
        p.combination_tiles.append(code)
        if action=="peng":p.pung_melds.add(len(p.combination_tiles)-1)
        p.combination_mask.append(mask)
        p.meld_sources.append(actor)
        p.pung_claimed |= action == "peng"
        p.has_draw_slot = p.after_kong = False
        self.player_list[actor].discard_tiles.pop()
        self.current_player_index = index
        self.last_follow_end = len(self.discard_log)
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask, cut_from_player=actor)
        self.establish_contracts(index)
        if action == "gang":
            self.settle_kong(index, "direct", actor)
        forced = self.forced_claim_win(index, actor, tile, action)
        if forced is not None:
            return forced
        if not self.score_win(index, "self_draw", p.hand_tiles[-1]):
            p.passed_win_value = None
        return self.draw_for(index, kind="kong") if action == "gang" else self.begin_turn(index, discard_only=True)

    def _kong_response(self, window, responses):
        pending = self.pending_kong
        actor, tile = pending["actor"], pending["tile"]
        winners = sorted((i for i, d in responses.items() if d["action_type"] == "hu"), key=lambda i: (i-actor) % 4)
        if winners:
            return self.settle_wins(winners, "rob_kong", tile, payer=actor)
        self._remember_responses(window, responses)
        p = self.player_list[actor]
        provider = None
        if pending["concealed"]:
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code, mask, action = f"G{tile}", [2,tile,0,tile,0,tile,2,tile], "angang"
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
            p.meld_sources.append(None)
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{tile}")
            provider = p.meld_sources[position]
            code, mask, action = f"g{tile}", list(p.combination_mask[position]), "jiagang"
            called = next(i for i in range(0,len(mask),2) if mask[i] == 1)
            mask[called:called] = [3,tile]
            p.combination_tiles[position], p.combination_mask[position] = code, mask
        p.has_draw_slot = False
        self._event(action, actor, tile=tile, meld_code=code, combination_mask=mask, is_mo_gang=pending["is_mo_gang"])
        self.pending_kong = None
        self.establish_contracts(actor)
        self.settle_kong(actor, "concealed" if action == "angang" else "added", provider)
        if action == "angang" and len(p.combination_tiles) == 4 and self.contracts[actor]:
            return self.settle_forced(actor, tile, None, ("全求独钓",), self.contracts[actor])
        return self.draw_for(actor, kind="kong")
