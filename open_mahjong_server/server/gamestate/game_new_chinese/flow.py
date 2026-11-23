from collections import Counter

from .actions import CHOWS, after_meld, claim_actions, claim_tiles, empty_actions, kong_tiles, legal_cuts, ready_cuts, wait_context
from .state_machine import Phase as P
from ...game_calculation.new_chinese import rules


class HandFlow:
    def window(self, phase, actor=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile, actions=actions or empty_actions())

    def event(self, action, actor, **fields):
        self.emit_visible_action_payloads(dict(action=action, player=actor, **fields))

    def opening_window(self):
        if self.detailed_config["exchange_three"]:
            return self.window(P.EXCHANGE, self.dealer_index, actions={i: ["exchange_three", "pass"] for i in range(4)})
        return self.turn(self.dealer_index)

    def turn(self, index, *, after_call=False):
        self.current_player_index = index
        p = self.player_list[index]
        offered = []
        self.hand_action_is_gang_draw[index] = p.after_kong
        if not after_call and p.has_draw_slot and self.quote(index, p.hand_tiles[-1], "draw"):
            offered.append("hu_self")
        offered.extend(k for k in ("angang", "jiagang") if kong_tiles(self, index, k))
        self.candidate_cuts = {} if p.declared else ready_cuts(p.hand_tiles, p.combination_tiles, **wait_context(self, index))
        if self.candidate_cuts:
            offered.append("riichi_cut")
        if self.must_declare != index:
            offered.append("cut")
        return self.window(P.AFTER_MELD if after_call else P.TURN, index, actions={index: offered})

    def cut_identity(self, index, data):
        p = self.player_list[index]
        tile = data.get("TileId")
        if type(tile) is not int or tile not in p.hand_tiles:
            raise ValueError("出牌不在手牌中")
        if data["action_type"] == "riichi_cut" and tile not in self.candidate_cuts:
            raise ValueError("出牌后不能报听")
        drawn = data.get("cutClass")
        if drawn is not None and type(drawn) is not bool:
            raise ValueError("无效的出牌类型")
        if drawn is None:
            drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        if p.declared and not drawn:
            raise ValueError("报听后只能摸切")
        if drawn:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile:
                raise ValueError("此牌不是摸牌")
            return len(p.hand_tiles) - 1, True
        if tile not in (p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles):
            raise ValueError("此牌不在手切区")
        return p.hand_tiles.index(tile), False

    def validate_response(self, index, data, offered):
        action = data.get("action_type")
        if action not in offered:
            raise ValueError("当前没有此操作")
        if action in ("cut", "riichi_cut"):
            self.cut_identity(index, data)
        elif action in ("angang", "jiagang") and data.get("target_tile") not in kong_tiles(self, index, action):
            raise ValueError("无效的杠牌")
        elif action == "exchange_three":
            tiles = data.get("target_tile")
            if (not isinstance(tiles, list) or len(tiles) != 3 or any(type(t) is not int for t in tiles)
                    or not Counter(tiles) <= Counter(self.player_list[index].hand_tiles)):
                raise ValueError("请选择手中的三张牌")

    def apply_action_results(self, window, responses, *, settlements=None):
        if settlements or window is not self.live_pending_window or window["action_tick"] != self.server_action_tick:
            raise ValueError("过期行动窗口")
        if set(responses) != {i for i, choices in window["actions"].items() if choices}:
            raise ValueError("响应座位不完整")
        for index, data in responses.items():
            self.validate_response(index, data, window["actions"][index])
        phase = self.machine.phase
        if phase == P.EXCHANGE:
            submissions = {i: list(data["target_tile"]) for i, data in responses.items() if data["action_type"] == "exchange_three"}
            result = self.window(P.EXCHANGE_CHOICES, self.dealer_index)
            result["submissions"] = submissions
            result["duration_ms"] = 3750 if len(submissions) >= 2 else 1000
            result["deadline"] = self._timing_now() + result["duration_ms"] / 1000
        elif phase == P.EXCHANGE_CHOICES:
            if window.get("broadcast_complete") is False or self._timing_now() < window["deadline"]:
                raise ValueError("换牌公示尚未结束")
            submissions = window["submissions"]
            if len(submissions) >= 2:
                incoming = self.exchange(submissions)
                result = self.window(P.EXCHANGE_RECEIPT, self.dealer_index)
                result.update(incoming=incoming, duration_ms=750, deadline=self._timing_now() + .75)
            else:
                result = self.turn(self.dealer_index)
                self.record_state()
        elif phase == P.EXCHANGE_RECEIPT:
            if window.get("broadcast_complete") is False or self._timing_now() < window["deadline"]:
                raise ValueError("换入手牌展示尚未结束")
            result = self.turn(self.dealer_index)
            self.record_state()
        elif phase == P.BAO_REVEAL:
            if window.get("broadcast_complete") is False or self._timing_now() < window["deadline"]:
                raise ValueError("宝牌展示尚未结束")
            if self.supplements and self.bao_discarded_three_times():
                result = self.reveal_bao()
            else:
                self.bao_offers = [i for i in self.ready_order if self.quote(i, self.bao, "seen")]
                result = self.next_bao_offer()
        elif phase == P.BAO:
            index = window["player"]
            if responses[index]["action_type"] == "hu_self":
                result = self.settle(index, self.bao, "seen")
            else:
                result = self.next_bao_offer()
        elif phase in (P.TURN, P.AFTER_MELD):
            result = self.take_turn(window["player"], responses[window["player"]], window["actions"][window["player"]])
        elif phase in (P.RESPONSE, P.ROB_KONG):
            result = self.resolve_claims(window, responses)
        else:
            raise ValueError("当前阶段不能行牌")
        return self.open_action_window(result)

    def exchange(self, submissions):
        seats = sorted(submissions)
        received = {seat: submissions[seats[n - 1]] for n, seat in enumerate(seats)}
        for index in seats:
            p = self.player_list[index]
            incoming = received[index]
            original_draw = p.hand_tiles[-1] if p.has_draw_slot else None
            for t in submissions[index]:
                p.hand_tiles.remove(t)
            keep_draw = original_draw in p.hand_tiles
            p.hand_tiles.extend(incoming)
            if p.has_draw_slot:
                draw = original_draw if keep_draw else incoming[-1]
                p.hand_tiles.remove(draw)
                p.hand_tiles.sort()
                p.hand_tiles.append(draw)
            else:
                p.hand_tiles.sort()
        return received

    def take_turn(self, index, data, offered):
        p, action = self.player_list[index], data["action_type"]
        if action == "hu_self":
            return self.settle(index, p.hand_tiles[-1], "draw")
        if "hu_self" in offered:
            tile = p.hand_tiles[-1]
            source = self.win_source(index, tile, "draw")
            waits = rules.waits(p.hand_tiles[:-1], p.combination_tiles, self_draw=True,
                                after_kong=p.after_kong, **wait_context(self, index))
            p.pass_win(source, tile, waits)
        if action == "angang":
            return self.commit_kong(index, data["target_tile"], action)
        if action == "jiagang":
            return self.rob_window(index, data["target_tile"], action, None)
        position, drawn = self.cut_identity(index, data)
        cut_index = position if p.declared else data.get("cutIndex", position)
        tile = p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        declared = action == "riichi_cut"
        p.riichi_flags.append(declared)
        p.has_draw_slot = p.after_kong = False
        p.chain = 0
        self.discard_log.append((index, tile))
        self.last_discard_offsets[index] = len(self.discard_log) - 1
        self.must_declare = None
        if declared:
            p.declared = True
            p.tag_list.append("declared_ready")
            self.ready_order.append(index)
        self.event("cut", index, tile=tile, cutIndex=cut_index, cutClass=drawn,
                   declared=declared, is_timeout_action=data.get("is_timeout_action", False))
        self.published_bao_cut = self.bao == tile
        self.after_bao = (index, tile)
        self.discard_hu_checked = False
        if declared and self.bao and not self.published_bao_cut:
            ron = {i: ["hu", "pass"] for i in range(4) if i != index and self.quote(i, tile, "discard", index)}
            if ron:
                window = self.window(P.RESPONSE, index, tile, ron)
                window["before_bao"] = True
                return window
        return self.after_declaration(index, tile, declared)

    def after_declaration(self, index, tile, declared):
        self.changed_bao = False
        if self.supplements and (self.bao_discarded_three_times() if self.bao else declared):
            return self.reveal_bao()
        self.bao_offers = [index] if declared and self.bao and self.quote(index, self.bao, "seen") else []
        return self.next_bao_offer()

    def bao_discarded_three_times(self):
        return sum(tile == self.bao for _, tile in self.discard_log) >= 3

    def reveal_bao(self):
        initial = not self.bao
        self.changed_bao = not initial
        self.bao = self.supplements.pop(self.rng.randrange(len(self.supplements)))
        self.bao_revision += 1
        for p in self.player_list:
            if p.level == 4:
                p.level = 3
        window = self.window(P.BAO_REVEAL, self.current_player_index)
        window["initial"] = initial
        window["duration_ms"] = 1000
        window["deadline"] = self._timing_now() + window["duration_ms"] / 1000
        return window

    def next_bao_offer(self):
        if self.bao_offers:
            index = self.bao_offers.pop(0)
            return self.window(P.BAO, index, actions={index: ["hu_self", "pass"]})
        index, tile = self.after_bao
        choices = empty_actions() if self.published_bao_cut else claim_actions(self, index, tile)
        if getattr(self, "discard_hu_checked", False):
            for i, offered in choices.items():
                choices[i] = [action for action in offered if action != "hu"]
                if choices[i] == ["pass"]:
                    choices[i] = []
        return self.window(P.RESPONSE, index, tile, choices)

    def draw_for(self, index, kong=False):
        wall = self.supplements if kong else self.tiles_list
        if not wall:
            return self.finish()
        p = self.player_list[index]
        tile = wall.pop(-1 if kong else 0)
        p.hand_tiles.sort()
        p.hand_tiles.append(tile)
        p.has_draw_slot, p.after_kong = True, kong
        self.current_player_index = index
        if not kong:
            self.natural_draw_count[index] += 1
        self.event("deal_gang_tile" if kong else "deal_tile", index, tile=tile)
        return self.turn(index)

    def rob_window(self, index, tile, action, discarder):
        self.pending_kong = (index, tile, action, discarder)
        self.event("declare_kan", index, tile=tile, kong_kind=action, original_source=discarder)
        return self.window(P.ROB_KONG, index, tile, claim_actions(self, index, tile, rob=True))

    def resolve_claims(self, window, responses):
        actor, tile = window["player"], window["tile"]
        rob = self.machine.phase == P.ROB_KONG
        priority = lambda i: (i - actor) % 4
        winners = sorted((i for i, d in responses.items() if d["action_type"] == "hu"), key=priority)
        if winners:
            if rob:
                self.withdraw_kong_tile()
            return self.settle(winners[0], tile, "rob" if rob else "discard", actor)
        for i, data in responses.items():
            if "hu" in window["actions"][i]:
                p = self.player_list[i]
                context = wait_context(self, i)
                if context and (not rob or self.pending_kong[2] == "gang"):
                    context["visible"][tile] -= 1
                wait = rules.waits(p.hand_tiles, p.combination_tiles, self_draw=False, **context)
                p.pass_win("discard", tile, wait)
        if window.get("before_bao"):
            self.discard_hu_checked = True
            return self.after_declaration(actor, tile, True)
        claims = {i: data["action_type"] for i, data in responses.items() if data["action_type"] != "pass"}
        if claims:
            def key(i):
                a = claims[i]
                return (0 if a.endswith("_ting") else 1 if a in ("gang", "peng") else 2, priority(i))
            index = min(claims, key=key)
            action = claims[index]
            if rob:
                self.withdraw_kong_tile(for_chow=True)
            if action == "gang":
                return self.rob_window(index, tile, action, actor)
            declare = action.endswith("_ting")
            self.meld(index, actor, tile, action.removesuffix("_ting"))
            self.must_declare = index if declare else None
            return self.turn(index, after_call=True)
        if rob:
            return self.commit_kong(*self.pending_kong)
        return self.draw_for((actor + 1) % 4)

    def withdraw_kong_tile(self, *, for_chow=False):
        index, tile, kind, source = self.pending_kong
        if kind == "jiagang":
            self.player_list[index].hand_tiles.remove(tile)
            self.player_list[index].has_draw_slot = False
        else:
            self.player_list[source].discard_tiles.pop()
            self.player_list[source].riichi_flags.pop()
        self.pending_kong = None
        if for_chow:
            self.player_list[index].discard_tiles.append(tile)
            self.player_list[index].riichi_flags.append(False)
        self.event("rob_kan", index, tile=tile, kong_kind=kind, original_source=source, for_chow=for_chow)

    def meld(self, index, source, tile, action):
        p = self.player_list[index]
        needed = claim_tiles(action, tile)
        rest, codes = after_meld(p, action, tile)
        p.hand_tiles, p.combination_tiles = rest, codes
        if action in CHOWS:
            mask = [1, tile] + [x for t in needed for x in (0, t)]
        else:
            position = {1: len(needed), 2: 1, 3: 0}[(source - index) % 4]
            mask = [x for n in range(len(needed) + 1) for x in (int(n == position), tile)]
        p.combination_mask.append(mask)
        self.player_list[source].discard_tiles.pop()
        self.player_list[source].riichi_flags.pop()
        self.current_player_index = index
        p.has_draw_slot = p.after_kong = False
        self.event(action, index, tile=tile, meld_code=codes[-1], combination_mask=mask, cut_from_player=source)

    def commit_kong(self, index, tile, kind, source=None):
        p = self.player_list[index]
        self.pending_kong = None
        p.chain += 1
        p.longest_chain = max(p.longest_chain, p.chain)
        if kind == "gang":
            self.meld(index, source, tile, kind)
        else:
            drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
            if kind == "angang":
                for _ in range(4):
                    p.hand_tiles.remove(tile)
                code, mask = f"G{tile}", [2, tile] * 4
                p.combination_tiles.append(code)
                p.combination_mask.append(mask)
            else:
                p.hand_tiles.remove(tile)
                i = p.combination_tiles.index(f"k{tile}")
                code, mask = f"g{tile}", list(p.combination_mask[i])
                j = next(j for j in range(0, len(mask), 2) if mask[j] == 1)
                mask[j:j] = [3, tile]
                p.combination_tiles[i], p.combination_mask[i] = code, mask
            p.has_draw_slot = False
            self.event(kind, index, tile=tile, meld_code=code, combination_mask=mask, is_mo_gang=drawn)
        return self.draw_for(index, kong=True)
