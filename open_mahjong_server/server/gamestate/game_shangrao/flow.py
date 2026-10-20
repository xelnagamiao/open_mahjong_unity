from .actions import CHOWS, claim_actions, claim_tiles, empty_actions, legal_cuts, kong_tiles, turn_actions
from .state_machine import Phase as P
from ...game_calculation.shangrao import rules as book


class HandFlow:
    def _window(self, phase, actor=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile, actions=actions or empty_actions())

    def _event(self, action, actor, **fields):
        event = dict(action=action, player=actor, **fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def opening_window(self):
        for i, p in enumerate(self.player_list):
            p.drawn_bao_count = p.hand_tiles.count(self.bao)
            self.check_four_bao(i)
        return self.begin_turn(0)

    def begin_turn(self, index, after_meld=False):
        self.current_player_index = index
        self.hand_action_is_gang_draw[index] = self.player_list[index].after_kong
        return self._window(P.AFTER_MELD if after_meld else P.TURN, index,
                            actions=turn_actions(self, index, after_meld))

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
        if type(position) is not int or position < -1 or position >= len(p.hand_tiles):
            raise ValueError("出牌索引无效")
        if drawn is not None and type(drawn) is not bool:
            raise ValueError("出牌类型无效")
        if drawn is True:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile or position not in (-1, len(p.hand_tiles) - 1):
                raise ValueError("摸切身份错误")
            position = len(p.hand_tiles) - 1
        elif drawn is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:
                raise ValueError("手切牌不在手牌中")
            position = held.index(tile)
        elif position < 0:
            position = len(p.hand_tiles) - 1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        elif p.hand_tiles[position] != tile:
            raise ValueError("出牌索引与实体不一致")
        return tile, position, p.has_draw_slot and position == len(p.hand_tiles) - 1

    def apply_action_results(self, window, responses, *, settlements=None):
        if settlements or window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("客户端结算或过期窗口")
        offered = window["actions"]
        if set(responses) != {i for i, a in offered.items() if a}:
            raise ValueError("响应座位集合错误")
        for i, data in responses.items():
            self.validate_response(i, data, offered[i])
        if window["status"] in (P.TURN.value, P.AFTER_MELD.value):
            result = self._turn(window, responses[window["player"]])
        elif window["status"] == P.RESPONSE.value:
            result = self._response(window, responses)
        else:
            raise ValueError("当前阶段不能行牌")
        return self.open_action_window(result)

    def _turn(self, window, data):
        index, action = window["player"], data["action_type"]
        p = self.player_list[index]
        if action == "hu_self":
            return self.settle_win(index, "self_draw", p.hand_tiles[-1])
        if action in ("angang", "jiagang"):
            return self.commit_kong(index, data["target_tile"], action)
        tile, position, drawn = self._cut_identity(index, data)
        if tile == self.bao and self.bao in book.HONORS and (self.rule_version != book.LEGACY_VERSION or book.seven_stars(p.hand_tiles, p.combination_tiles, indicator=self.indicator)):
            p.flying_seven_stars = True
        p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.has_draw_slot = p.after_kong = False
        p.draw_kind = "none"
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        self.discard_log.append((index, tile))
        self.last_discard_offsets[index] = len(self.discard_log) - 1
        self._event("cut", index, tile=tile, cutIndex=data.get("cutIndex", position), cutClass=drawn,
                    is_timeout_action=bool(data.get("is_timeout_action")))
        return self._window(P.RESPONSE, index, tile, claim_actions(self, index, tile))

    def _response(self, window, responses):
        actor, tile = window["player"], window["tile"]
        claims = [i for i, d in responses.items() if d["action_type"] != "pass"]
        if not claims:
            return self.draw_for((actor + 1) % 4)
        index = min(claims, key=lambda i: (responses[i]["action_type"] in CHOWS, (i - actor) % 4))
        action, p = responses[index]["action_type"], self.player_list[index]
        needed = claim_tiles(action, tile)
        for t in needed:
            p.hand_tiles.remove(t)
        group = sorted(needed + [tile])
        if action in CHOWS:
            code = f"s{group[1]}"
            mask = [1, tile] + [v for t in needed for v in (0, t)]
        else:
            code = ("g" if action == "gang" else "k") + str(tile)
            marked = {1: len(group) - 1, 2: 1, 3: 0}[(actor - index) % 4]
            mask = [v for n, t in enumerate(group) for v in (1 if n == marked else 0, t)]
        p.combination_tiles.append(code)
        p.combination_mask.append(mask)
        p.has_draw_slot = p.after_kong = False
        p.draw_kind = "none"
        self.player_list[actor].discard_tiles.pop()
        self.current_player_index = index
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask, cut_from_player=actor)
        if action == "gang":
            self.record_payment(index, 1, "gang", tile)
            return self.draw_for(index, kind="kong")
        if action == "peng" and tile == self.indicator:
            self.record_payment(index, 1, "indicator_pung", tile)
        return self.begin_turn(index, after_meld=True)

    def commit_kong(self, index, tile, kind):
        if tile not in kong_tiles(self, index, kind):
            raise ValueError("杠不成立")
        p = self.player_list[index]
        drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        if kind == "angang":
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code, mask = f"G{tile}", [0, tile] * 4
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{tile}")
            code, mask = f"g{tile}", list(p.combination_mask[position])
            marked = next(i for i in range(0, len(mask), 2) if mask[i] == 1)
            mask[marked:marked] = [3, tile]
            p.combination_tiles[position], p.combination_mask[position] = code, mask
        p.has_draw_slot = False
        self._event(kind, index, tile=tile, meld_code=code, combination_mask=mask, is_mo_gang=drawn)
        self.record_payment(index, 2 if kind == "angang" else 1, kind, tile)
        return self.draw_for(index, kind="kong")

    def record_payment(self, index, points, kind, tile=None, *, announce=True):
        changes = book.payments(index, points)
        entry = dict(actor=index, tile=tile, kind=kind, points=points, changes=changes)
        self.payment_ledger.append(entry)
        for p, change in zip(self.player_list, changes):
            p.score += change
        if announce:
            self._event("shangrao_payment", index, payment=entry)
        return entry

    def check_four_bao(self, index, *, final=False):
        p = self.player_list[index]
        legacy = self.rule_version == book.LEGACY_VERSION
        if (legacy or final) and self.detailed_config["four_bao_reward"] and p.drawn_bao_count == 4 and not p.four_bao_paid:
            p.four_bao_paid = True
            self.record_payment(index, 4 if legacy else 2, "four_bao", self.bao)

    def draw_for(self, index, kind="normal"):
        if len(self.tiles_list) == 1:
            return self.end_draw()
        p = self.player_list[index]
        if kind == "kong":
            tile, burnt = self.take_kong_tile()
        else:
            tile, burnt = self.take_normal_tile(), None
        p.hand_tiles.sort()
        p.hand_tiles.append(tile)
        p.has_draw_slot, p.after_kong, p.draw_kind = True, kind == "kong", kind
        self.current_player_index = index
        if kind == "normal":
            self.natural_draw_count[index] += 1
        if burnt is not None and burnt != self.bao:
            self.burnt_river_indices[index].append(len(p.discard_tiles))
            p.discard_tiles.append(burnt)
        self._event("deal_gang_tile" if kind == "kong" else "deal_tile", index, tile=tile, burnt_tile=burnt)
        if tile == self.bao:
            p.drawn_bao_count += 1
            self.check_four_bao(index)
        return self.begin_turn(index)
