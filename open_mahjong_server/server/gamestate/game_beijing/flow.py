from .actions import CHOWS, claim_actions, claim_tiles, empty_actions, legal_cuts, kong_tiles, turn_actions
from .state_machine import Phase as P
from ...game_calculation.beijing import rules as book


class HandFlow:
    def _window(self, phase, actor=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile, actions=actions or empty_actions())

    def _event(self, action, actor, **fields):
        event = dict(action=action, player=actor, **fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def opening_window(self):
        actions = empty_actions()
        for i in (1, 2, 3):
            p = self.player_list[i]
            if book.logical_waits(tuple(p.hand_tiles), (), self.hun):
                actions[i] = ["tianting", "pass"]
        return self._window(P.TIANTING, 0, actions=actions) if any(actions.values()) else self.begin_turn(0)

    def begin_turn(self, index, after_meld=False):
        self.current_player_index = index
        self.hand_action_is_gang_draw[index] = self.player_list[index].after_kong
        return self._window(P.AFTER_MELD if after_meld else P.TURN, index, actions=turn_actions(self, index, after_meld))

    def validate_response(self, index, data, offered):
        if type(index) is not int or index not in range(4) or not isinstance(data, dict) or data.get("action_type") not in offered:
            raise ValueError("行动不在当前窗口内")
        action = data["action_type"]
        if action == "cut":
            self._cut_identity(index, data)
        elif action in ("angang", "jiagang"):
            if type(data.get("target_tile")) is not int or data["target_tile"] not in kong_tiles(self, index, action):
                raise ValueError("不能对指定牌开杠")

    def _cut_identity(self, index, data):
        p = self.player_list[index]
        tile, position, drawn = data.get("TileId"), data.get("cutIndex", -1), data.get("cutClass")
        if type(tile) is not int or tile not in legal_cuts(self, index):
            raise ValueError("非法出牌")
        if type(position) is not int or position < -1:
            raise ValueError("出牌索引无效")
        if drawn is not None and type(drawn) is not bool:
            raise ValueError("出牌类型无效")
        if drawn is True:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile:
                raise ValueError("摸切身份错误")
            position = len(p.hand_tiles) - 1
        elif drawn is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:
                raise ValueError("手切牌不在手牌中")
            position = held.index(tile)
        else:
            position = len(p.hand_tiles) - 1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        actual_drawn = p.has_draw_slot and position == len(p.hand_tiles) - 1
        if drawn is False and actual_drawn:
            raise ValueError("手切索引指向摸牌")
        return tile, position, actual_drawn

    def apply_action_results(self, window, responses, *, settlements=None):
        if settlements or window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("客户端结算或过期窗口")
        offered = window["actions"]
        if set(responses) != {i for i, a in offered.items() if a}:
            raise ValueError("响应座位集合错误")
        for i, data in responses.items():
            self.validate_response(i, data, offered[i])
        if window["status"] == P.TIANTING.value:
            for i, data in responses.items():
                if data["action_type"] == "tianting":
                    self.player_list[i].tian_ting = True
                    self.player_list[i].tag_list.append("tianting")
                    self._event("tianting", i)
            result = self.begin_turn(0)
        elif window["status"] in (P.TURN.value, P.AFTER_MELD.value):
            result = self._turn(window, responses[window["player"]])
        elif window["status"] in (P.RESPONSE.value, P.ROB_KONG.value):
            result = self._response(window, responses)
        else:
            raise ValueError("当前阶段不能行牌")
        return self.open_action_window(result)

    def break_tianting(self, index):
        p = self.player_list[index]
        p.tian_ting = False
        if "tianting" in p.tag_list:
            p.tag_list.remove("tianting")

    def _turn(self, window, data):
        index, action = window["player"], data["action_type"]
        p = self.player_list[index]
        if action in ("hu_self", "hun_gang"):
            return self.settle_win(index, "self_draw", p.hand_tiles[-1], four_hun=action == "hun_gang")
        if action == "jiagang":
            self.pending_kong = (index, data["target_tile"])
            self._event("declare_kan", index, tile=data["target_tile"])
            return self._window(P.ROB_KONG, index, data["target_tile"], claim_actions(self, index, data["target_tile"], rob=True))
        if action == "angang":
            return self.commit_kong(index, data["target_tile"], action)
        tile, position, drawn = self._cut_identity(index, data)
        if not drawn:
            self.break_tianting(index)
        p.discarded_hun = p.discarded_hun or tile == self.hun
        if p.discarded_hun and "discarded_hun" not in p.tag_list:
            p.tag_list.append("discarded_hun")
        p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.has_draw_slot = p.after_kong = False
        p.draw_kind, p.kong_payer = "none", None
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        self.discard_log.append((index, tile))
        self.last_discard_offsets[index] = len(self.discard_log) - 1
        self._event("cut", index, tile=tile, cutIndex=data.get("cutIndex", position), cutClass=drawn,
                    is_timeout_action=bool(data.get("is_timeout_action")))
        self.check_dingzhuang()
        return self._window(P.RESPONSE, index, tile, claim_actions(self, index, tile))

    def check_dingzhuang(self):
        if (not self.detailed_config["dingzhuang"] or self.dingzhuang_paid
                or any(not p.discard_origin_tiles for p in self.player_list)):
            return
        self.dingzhuang_paid = True
        if len({p.discard_origin_tiles[0] for p in self.player_list}) == 1:
            changes = [-6, 2, 2, 2]
            payment = dict(kind="dingzhuang", changes=changes)
            self.payment_ledger.append(payment)
            for p, change in zip(self.player_list, changes):
                p.score += change
            self._event("beijing_payment", 0, payment=payment)

    def _response(self, window, responses):
        actor, tile = window["player"], window["tile"]
        rob = window["status"] == P.ROB_KONG.value
        winners = sorted((i for i, d in responses.items() if d["action_type"] == "hu"), key=lambda i: (i - actor) % 4)
        if winners:
            if not self.detailed_config["multi_ron"]:
                winners = winners[:1]
            return self.settle_winners(winners, "rob_kong" if rob else "discard", tile, actor)
        if rob:
            _, pending = self.pending_kong
            self.pending_kong = None
            return self.commit_kong(actor, pending, "jiagang")
        claims = [i for i, d in responses.items() if d["action_type"] != "pass"]
        if not claims:
            return self.draw_for((actor + 1) % 4)
        priority = {"gang": 0, "peng": 1, **{a: 2 for a in CHOWS}}
        index = min(claims, key=lambda i: (priority[responses[i]["action_type"]], (i - actor) % 4))
        action, p = responses[index]["action_type"], self.player_list[index]
        needed = claim_tiles(action, tile)
        for t in needed:
            p.hand_tiles.remove(t)
        group = sorted(needed + [tile])
        if action in CHOWS:
            code, mask = f"s{group[1]}", [1, tile] + [v for t in needed for v in (0, t)]
        else:
            code = ("g" if action == "gang" else "k") + str(tile)
            marked = {1: len(group) - 1, 2: 1, 3: 0}[(actor - index) % 4]
            mask = [v for n, t in enumerate(group) for v in (1 if n == marked else 0, t)]
        p.combination_tiles.append(code)
        p.combination_mask.append(mask)
        p.open_calls += 1
        if p.open_calls == 4:
            p.fourth_call_payer = actor
        p.has_draw_slot = p.after_kong = False
        p.draw_kind, p.kong_payer = "none", None
        self.break_tianting(index)
        self.player_list[actor].discard_tiles.pop()
        self.current_player_index = index
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask, cut_from_player=actor)
        return self.draw_for(index, kind="kong", payer=actor) if action == "gang" else self.begin_turn(index, after_meld=True)

    def commit_kong(self, index, tile, kind):
        if tile not in kong_tiles(self, index, kind):
            raise ValueError("杠不成立")
        p = self.player_list[index]
        drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        if kind == "angang":
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code, mask = f"G{tile}", [2, tile] * 4
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{tile}")
            code, mask = f"g{tile}", list(p.combination_mask[position])
            marked = next(i for i in range(0, len(mask), 2) if mask[i] == 1)
            mask[marked:marked] = [3, tile]
            p.combination_tiles[position], p.combination_mask[position] = code, mask
        self.break_tianting(index)
        p.has_draw_slot = False
        self._event(kind, index, tile=tile, meld_code=code, combination_mask=mask, is_mo_gang=drawn)
        return self.draw_for(index, kind="kong")

    def draw_for(self, index, kind="normal", payer=None):
        if not self.tiles_list:
            return self.end_draw()
        p = self.player_list[index]
        position = -1 if kind == "kong" else 0
        tile = self.tiles_list.pop(position)
        self.wall_positions.pop(position)
        p.hand_tiles.sort()
        p.hand_tiles.append(tile)
        p.has_draw_slot, p.after_kong, p.draw_kind = True, kind == "kong", kind
        p.kong_payer = payer if kind == "kong" else None
        self.current_player_index = index
        if kind == "normal":
            self.natural_draw_count[index] += 1
        self._event("deal_gang_tile" if kind == "kong" else "deal_tile", index, tile=tile)
        return self.begin_turn(index)
