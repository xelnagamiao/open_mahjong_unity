"""Transactional state transitions and physical tile ownership."""

from dataclasses import replace

from .actions import claim_actions, kong_tiles, legal_cuts, turn_actions
from .state_machine import Phase as P
from ...game_calculation.guizhou.ledger import Chicken, Kong
from ...game_calculation.guizhou.rules import NORMAL_CHICKENS


class HandFlow:
    def _window(self, phase, actor=None, tile=None, actions=None, **extra):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile,
                    actions=actions or {i: [] for i in range(4)}, **extra)

    def _event(self, action, actor, **fields):
        event = dict(action=action, player=actor, **fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def begin_turn(self, index, discard_only=False):
        self.current_player_index = index
        self.hand_action_is_gang_draw[index] = self.player_list[index].after_kong
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
        if p.declared_ready and (not p.has_draw_slot or position != len(p.hand_tiles)-1):
            raise ValueError("报听后只能摸切")
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
        if phase == P.INITIAL_READY:
            for index, data in responses.items():
                if data["action_type"] == "baoting_initial":
                    self.declare_ready(index, "hard_ready")
            result = self.begin_turn(0)
        elif phase in (P.TURN, P.DISCARD_ONLY):
            result = self._turn(window, responses[window["player"]])
        elif phase == P.RESPONSE:
            result = self._response(window, responses)
        elif phase == P.KONG:
            result = self._kong_response(window, responses)
        else:
            raise ValueError("当前阶段不接受牌局行动")
        return self.open_action_window(result)

    def declare_ready(self, index, kind):
        p = self.player_list[index]
        p.ready_kind, p.ready_pending, p.pending_ready_marker = kind, False, True
        p.tag_list.append("declared_ready")
        self._event("riichi", index, ready_qualification=kind)

    def _turn(self, window, data):
        index, action = window["player"], data["action_type"]
        p = self.player_list[index]
        if action in ("guizhou_ready", "guizhou_ready_cancel"):
            p.ready_pending = action == "guizhou_ready"
            selection = self.begin_turn(index)
            selection["_clocks"] = window.get("_clocks", {})
            return selection
        if action == "hu_self":
            return self.settle_winners([index], "self_draw", p.hand_tiles[-1])
        if action in ("angang", "jiagang"):
            tile = data["target_tile"]
            fresh = p.hand_tiles[-1] == tile
            opening = action == "angang" and tile in p.initial_quads and not p.discard_count
            self.pending_kong = dict(actor=index, tile=tile, concealed=action == "angang",
                                     hanbao=not (fresh or opening), is_mo_gang=fresh,
                                     opening=opening)
            return self._window(P.KONG, index, tile, actions=(None if action == "angang" else claim_actions(self, index, tile, True)))
        tile, position, drawn = self._cut_identity(index, data)
        if p.ready_pending:
            self.declare_ready(index, "soft_ready")
        p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.has_draw_slot = False
        p.discard_count += 1
        p.initial_quads.clear()
        self.hot_discard = p.after_kong
        p.after_kong = False
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        marker, p.pending_ready_marker = p.pending_ready_marker, False
        p.discard_riichi_flags.append(marker)
        p.discarded_since_action.add(tile)
        self.discard_log.append((index, tile))
        self.last_discard_offsets[index] = len(self.discard_log)-1
        if not p.declared_ready:
            quote = self.score_win(index, "discard", tile, payer=index)
            if quote:
                p.passed_points = max(p.passed_points, quote.points)
        if tile in NORMAL_CHICKENS and tile not in self.chickens:
            self.chickens[tile] = Chicken(tile, index)
        self._event("cut", index, tile=tile, cutIndex=data.get("cutIndex", position), cutClass=drawn,
                    riichi_discard=marker, is_timeout_action=bool(data.get("is_timeout_action")))
        self._reveal_opening_kongs()
        actions = claim_actions(self, index, tile)
        self.begin_claim_protection(actions, index)
        return self._window(P.RESPONSE, index, tile, actions)

    def _remember_responses(self, window, responses):
        for index, data in responses.items():
            offered = window["actions"][index]
            if "hu" in offered and data["action_type"] != "hu":
                p = self.player_list[index]
                if p.declared_ready:
                    p.permanent_ron_block = True
                else:
                    quote = self.score_win(index, "rob_kong" if self.machine.phase == P.KONG else "discard",
                                           window["tile"], payer=window["player"])
                    p.passed_points = max(p.passed_points, quote.points)
            if "peng" in offered and data["action_type"] not in ("peng", "gang", "hu"):
                self.player_list[index].passed_pungs.add(window["tile"])

    def _response(self, window, responses):
        actor, tile = window["player"], window["tile"]
        winners = sorted((i for i, d in responses.items() if d["action_type"] == "hu"), key=lambda i: (i-actor) % 4)
        if winners:
            return self.settle_winners(winners, "discard", tile, payer=actor)
        self._remember_responses(window, responses)
        claims = sorted((i for i, d in responses.items() if d["action_type"] in ("peng", "gang")), key=lambda i: (i-actor) % 4)
        if not claims:
            return self.draw_for((actor+1) % 4)
        index = claims[0]
        action, p = responses[index]["action_type"], self.player_list[index]
        count = 4 if action == "gang" else 3
        for _ in range(count-1):
            p.hand_tiles.remove(tile)
        code = ("g" if action == "gang" else "k") + str(tile)
        marked = {1: count-1, 2: 1, 3: 0}[(actor-index) % 4]
        mask = [v for n in range(count) for v in (1 if n == marked else 0, tile)]
        p.combination_tiles.append(code)
        p.combination_mask.append(mask)
        p.has_draw_slot = False
        p.passed_pungs.clear()
        p.discarded_since_action.clear()
        p.initial_quads.clear()
        owner = self.player_list[actor]
        owner.discard_tiles.pop()
        if owner.discard_riichi_flags.pop():
            owner.pending_ready_marker = True
        first = self.chickens.get(tile)
        if first and first.supplier == actor and first.claimant is None and sum(t == tile for _, t in self.discard_log) == 1:
            self.chickens[tile] = replace(first, claimant=index)
        self.current_player_index = index
        if action == "gang":
            self.kongs.append(Kong(index, tile, "direct", actor))
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask, cut_from_player=actor)
        if action == "gang":
            return self.draw_for(index, replacement=True)
        return self.begin_turn(index, discard_only=True)

    def _kong_response(self, window, responses):
        pending = self.pending_kong
        actor, tile = pending["actor"], pending["tile"]
        winners = sorted((i for i, d in responses.items() if d["action_type"] == "hu"), key=lambda i: (i-actor) % 4)
        if winners:
            return self.settle_winners(winners, "rob_kong", tile, payer=actor)
        self._remember_responses(window, responses)
        p = self.player_list[actor]
        if pending["concealed"]:
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code = f"G{tile}"
            hide_opening = pending["opening"] and not all(
                other.discard_count for i, other in enumerate(self.player_list) if i != actor)
            mask = [2, tile] * 4 if hide_opening else [2, tile, 0, tile, 0, tile, 2, tile]
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
            if hide_opening:
                self.hidden_opening_kongs.add((actor, len(p.combination_tiles)-1))
            kind, action = "concealed", "angang"
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{tile}")
            code, mask = f"g{tile}", list(p.combination_mask[position])
            called = next(i for i in range(0, len(mask), 2) if mask[i] == 1)
            mask[called:called] = [3, tile]
            p.combination_tiles[position], p.combination_mask[position] = code, mask
            kind, action = "added", "jiagang"
        p.initial_quads.discard(tile)
        p.has_draw_slot = False
        self.kongs.append(Kong(actor, tile, kind, hanbao=pending["hanbao"]))
        self._event(action, actor, tile=tile, meld_code=code, combination_mask=mask,
                    is_mo_gang=pending["is_mo_gang"], hanbao=pending["hanbao"])
        self.pending_kong = None
        return self.draw_for(actor, replacement=True)

    def _reveal_opening_kongs(self, *, at_settlement=False):
        if self.opening_revealed or (not at_settlement and not all(p.discard_count for p in self.player_list)):
            return
        self.opening_revealed = True
        for actor, position in self.hidden_opening_kongs:
            p = self.player_list[actor]
            tile = int(p.combination_tiles[position][1:])
            p.combination_mask[position] = [2, tile, 0, tile, 0, tile, 2, tile]
        if self.hidden_opening_kongs:
            self.hidden_opening_kongs.clear()
            self._event("guizhou_reveal_kongs", self.current_player_index)

    def draw_for(self, index, replacement=False):
        if not self.tiles_list:
            return self.end_draw()
        p = self.player_list[index]
        p.passed_pungs.clear()
        p.discarded_since_action.clear()
        p.passed_points = -1
        p.hand_tiles.sort()
        if replacement:
            # The back of each physical stack is its lower tile. Take the
            # upper tile first, then that same stack's lower tile.
            offset = -1 if self.tail_half_used or len(self.tiles_list) == 1 else -2
            tile = self.tiles_list.pop(offset)
            self.tail_half_used = not self.tail_half_used
        else:
            tile = self.tiles_list.pop(0)
        p.hand_tiles.append(tile)
        p.has_draw_slot = True
        p.after_kong = replacement
        if not replacement:
            p.draw_count += 1
            self.natural_draw_count[index] = p.draw_count
        self.current_player_index = index
        self._event("deal_gang_tile" if replacement else "deal_tile", index, tile=tile)
        return self.begin_turn(index)
