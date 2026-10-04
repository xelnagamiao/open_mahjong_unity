"""Validate complete response windows, then commit one physical transition."""

from .actions import CHOWS, claim_actions, empty_actions, kong_tiles, natural, needed_tiles, physical_for, turn_actions
from .state_machine import Phase as P


class HandFlow:
    def _window(self, phase, actor=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile, actions=actions or empty_actions())

    def _event(self, action, actor, **fields):
        event = dict(action=action, player=actor, **fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def opening_window(self):
        return self.begin_turn(0)

    def begin_turn(self, index, discard_only=False):
        self.current_player_index = index
        p = self.player_list[index]
        self.hand_action_is_gang_draw[index] = p.draw_kind == "kong"
        self.action_policy.refresh_waiting_tiles(self, index, exclude_last_tile=p.has_draw_slot)
        return self._window(P.DISCARD_ONLY if discard_only else P.TURN, index,
                            actions=turn_actions(self, index, discard_only))

    def can_draw(self):
        # The flipped tile is retained separately and sits behind these four
        # reserved tiles: two stacks plus the final, uncounted indicator.
        return len(self.tiles_list) > 4

    def draw_for(self, index, *, kind="normal"):
        if not self.can_draw():
            return self.end_draw()
        p = self.player_list[index]
        tile = self.tiles_list.pop(-1 if kind == "kong" else 0)
        p.hand_tiles.sort()
        p.hand_tiles.append(tile)
        p.has_draw_slot, p.draw_kind, p.passed_win_multiplier = True, kind, 0
        self.current_player_index = index
        self.natural_draw_count[index] += int(kind == "normal")
        self._event("deal_gang_tile" if kind == "kong" else "deal_tile", index, tile=tile)
        return self.begin_turn(index)

    def _cut_identity(self, index, data):
        p = self.player_list[index]
        tile, position, drawn = data.get("TileId"), data.get("cutIndex", -1), data.get("cutClass")
        if type(tile) is not int or tile not in p.hand_tiles:
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

    def apply_action_results(self, window, responses, *, settlements=None):
        if settlements or window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("客户端结算或过期行动窗口")
        offered = window.get("actions") or {}
        if set(responses) != {i for i, actions in offered.items() if actions}:
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
        else:
            raise ValueError("当前阶段不接受牌局行动")
        return self.open_action_window(result)

    def _turn(self, window, data):
        index, action = window["player"], data["action_type"]
        p = self.player_list[index]
        if action == "hu_self":
            return self.settle_win(index, "self_draw", p.hand_tiles[-1])
        if action in ("angang", "jiagang"):
            tile = data["target_tile"]
            self.pending_kong = dict(actor=index, tile=tile, concealed=action == "angang",
                                     is_mo_gang=p.has_draw_slot and p.hand_tiles[-1] == tile)
            # Even a no-response concealed kong crosses one explicit window.
            return self._window(P.KONG, index, tile,
                                empty_actions() if action == "angang" else claim_actions(self, index, tile, rob=True))
        tile, position, drawn = self._cut_identity(index, data)
        p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.has_draw_slot, p.draw_kind = False, ""
        # The book includes one's own discarded winning tile. Compare as ron,
        # not the self-draw double, or passing a draw would over-block soft wins.
        self.remember_pass(index, tile)
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        self.discard_log.append((index, tile))
        self.last_discard_offsets[index] = len(self.discard_log)-1
        self._event("cut", index, tile=tile, cutIndex=data.get("cutIndex", position), cutClass=drawn,
                    is_timeout_action=bool(data.get("is_timeout_action")))
        return self._window(P.RESPONSE, index, tile, claim_actions(self, index, tile))

    def remember_pass(self, index, tile):
        multiplier = self.win_multiplier(index, "discard", tile)
        if multiplier:
            p = self.player_list[index]
            p.passed_win_multiplier = max(p.passed_win_multiplier, multiplier)

    def _remember_responses(self, window, responses):
        for index, data in responses.items():
            if "hu" in window["actions"][index] and data["action_type"] != "hu":
                self.remember_pass(index, window["tile"])

    @staticmethod
    def _winner(window, responses):
        winners = [i for i, data in responses.items() if data["action_type"] == "hu"]
        return min(winners, key=lambda i: (i-window["player"]) % 4) if winners else None

    def _response(self, window, responses):
        actor, tile = window["player"], window["tile"]
        winner = self._winner(window, responses)
        if winner is not None:
            return self.settle_win(winner, "discard", tile, payer=actor)
        self._remember_responses(window, responses)
        claims = [i for i, data in responses.items() if data["action_type"] != "pass"]
        if not claims:
            return self.draw_for((actor+1) % 4)
        index = min(claims, key=lambda i: (responses[i]["action_type"] in CHOWS, (i-actor) % 4))
        return self._claim(index, responses[index]["action_type"], actor, tile)

    def _claim(self, index, action, actor, tile):
        p = self.player_list[index]
        logical = natural(tile, self.caishen)
        needed = needed_tiles(action, logical)
        held = physical_for(p.hand_tiles, needed, self.caishen)
        if tile == self.caishen or held is None:
            raise ValueError("不能用财神或不存在的手牌副露")
        for t in held:
            p.hand_tiles.remove(t)
        logical_tiles = sorted(needed + [logical])
        code = f"s{logical_tiles[1]}" if action in CHOWS else ("g" if action == "gang" else "k") + str(logical)
        if action in CHOWS:
            mask = [1, tile] + [value for t in held for value in (0, t)]
        else:
            marked = {1:len(held), 2:1, 3:0}[(actor-index) % 4]
            physical = list(held)
            physical.insert(marked, tile)
            mask = [value for n, t in enumerate(physical) for value in (1 if n == marked else 0, t)]
        self._add_meld(p, code, mask)
        p.has_draw_slot, p.draw_kind = False, ""
        # Only a pung which breaks the winning shape clears overwater (MIL VI.6).
        if action == "peng" and self.win_multiplier(index, "self_draw", None) is None:
            p.passed_win_multiplier = 0
        self.player_list[actor].discard_tiles.pop()
        self.current_player_index = index
        changes = self.settle_kong(index, "ming") if action == "gang" else None
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask,
                    cut_from_player=actor, gang_score_changes=changes)
        return self.draw_for(index, kind="kong") if action == "gang" else self.begin_turn(index, discard_only=True)

    def _add_meld(self, p, code, mask, position=None):
        physical = mask[1::2]
        record = dict(code=code, physical=physical, logical=[natural(t, self.caishen) for t in physical])
        if position is None:
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
            p.meld_records.append(record)
        else:
            p.combination_tiles[position], p.combination_mask[position], p.meld_records[position] = code, mask, record

    def _kong_response(self, window, responses):
        pending = self.pending_kong
        actor, tile = pending["actor"], pending["tile"]
        winner = self._winner(window, responses)
        if winner is not None:
            return self.settle_win(winner, "rob_kong", tile, payer=actor)
        self._remember_responses(window, responses)
        p = self.player_list[actor]
        claims = [i for i, data in responses.items() if data["action_type"] in ("peng", "gang")]
        if claims:
            # The original pung survives. Only the added physical tile moves.
            p.hand_tiles.remove(tile)
            p.hand_tiles.sort()
            p.has_draw_slot, p.draw_kind = False, ""
            p.discard_tiles.append(tile)
            p.discard_origin_tiles.append(tile)
            self._event("wenzhou_kong_claim_source", actor, tile=tile, is_mo_gang=pending["is_mo_gang"])
            self.pending_kong = None
            index = min(claims, key=lambda i: (i-actor) % 4)
            return self._claim(index, responses[index]["action_type"], actor, tile)
        logical = natural(tile, self.caishen)
        if pending["concealed"]:
            held = physical_for(p.hand_tiles, [logical]*4, self.caishen)
            for t in held:
                p.hand_tiles.remove(t)
            code, mask, action = f"G{logical}", [v for t in held for v in (2,t)], "angang"
            self._add_meld(p, code, mask)
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{logical}")
            code, mask, action = f"g{logical}", list(p.combination_mask[position]), "jiagang"
            called = next(i for i in range(0,len(mask),2) if mask[i] == 1)
            mask[called:called] = [3,tile]
            self._add_meld(p, code, mask, position)
        p.hand_tiles.sort()
        p.has_draw_slot = False
        changes = self.settle_kong(actor, action)
        self._event(action, actor, tile=tile, meld_code=code, combination_mask=mask,
                    is_mo_gang=pending["is_mo_gang"], gang_score_changes=changes)
        self.pending_kong = None
        return self.draw_for(actor, kind="kong")
