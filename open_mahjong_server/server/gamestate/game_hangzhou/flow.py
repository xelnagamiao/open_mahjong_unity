"""Authoritative tile ownership and the lifetime of each discarded-cai lock."""

from ...game_calculation.hangzhou.rules import JOKER, score_ten_winds
from .actions import CHOWS, claim_actions, claim_tiles, empty_actions, kong_tiles, legal_cuts, turn_actions
from .state_machine import Phase as P


class HandFlow:
    def _window(self, phase, actor=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile, actions=actions or empty_actions())

    def _event(self, action, actor, **fields):
        event = dict(action=action, player=actor, **fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def is_cai_locked(self, index):
        return bool(self.cai_discard_locks - {index})

    def _owner_operation(self, index, *, meld=False):
        # Locks overlap if a forced draw-discard is itself another white.
        # Only the matching owner's chi/peng/kong/draw ends that owner's lock.
        self.cai_discard_locks.discard(index)
        if meld:
            p = self.player_list[index]
            p.had_meld_action = True
            # PDF 5-7 releases the call lock here. The 8-6 scoring history is
            # consecutive own white discards, cleared only by an ordinary cut.
            p.drawn_after_cai = False

    def opening_window(self):
        return self.begin_turn(0)

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
            if not p.has_draw_slot or p.hand_tiles[-1] != tile or position not in (-1, len(p.hand_tiles) - 1):
                raise ValueError("摸切牌不匹配")
            position = len(p.hand_tiles) - 1
        elif drawn is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:
                raise ValueError("手切牌不在手牌中")
            position = held.index(tile)
        elif position < 0:
            position = len(p.hand_tiles) - 1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        elif p.hand_tiles[position] != tile:
            raise ValueError("出牌索引与牌张不一致")
        if self.is_cai_locked(index) and (not p.has_draw_slot or position != len(p.hand_tiles) - 1):
            raise ValueError("他家打财期间只能打出刚摸的这一张")
        return tile, position, p.has_draw_slot and position == len(p.hand_tiles) - 1

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
        elif phase == P.TEN_WINDS:
            result = (self.settle_win(window["player"], "ten_winds", window["tile"])
                      if responses[window["player"]]["action_type"] == "hu_self"
                      else self._after_discard(window["player"], window["tile"]))
        else:
            raise ValueError("当前阶段不接受牌局行动")
        return self.open_action_window(result)

    def _turn(self, window, data):
        index, action = window["player"], data["action_type"]
        p = self.player_list[index]
        if action == "hu_self":
            return self.settle_win(index, "self_draw", p.hand_tiles[-1])
        if action in ("angang", "jiagang"):
            return self._self_kong(index, action, data["target_tile"])
        tile, position, drawn = self._cut_identity(index, data)
        p.hand_tiles.pop(position)
        p.hand_tiles.sort()
        p.has_draw_slot = p.after_kong = False
        p.draw_kind = ""
        p.pre_draw_tiles = []
        p.drawn_after_cai = False
        if tile == JOKER:
            p.cai_piao_count += 1
            self.cai_discard_locks.add(index)
        else:
            p.cai_piao_count = 0
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        self.discard_log.append((index, tile))
        self.last_discard_offsets[index] = len(self.discard_log) - 1
        self._event("cut", index, tile=tile, cutIndex=data.get("cutIndex", position), cutClass=drawn,
                    is_timeout_action=bool(data.get("is_timeout_action")))
        if score_ten_winds(p.hand_tiles, p.discard_origin_tiles, p.combination_tiles,
                           had_meld_action=p.had_meld_action) is not None:
            return self._window(P.TEN_WINDS, index, tile, {**empty_actions(), index: ["hu_self", "pass"]})
        return self._after_discard(index, tile)

    def _after_discard(self, index, tile):
        if len(self.tiles_list) <= 20:
            return self.end_draw()
        return self._window(P.RESPONSE, index, tile, claim_actions(self, index, tile))

    def _response(self, window, responses):
        actor, tile = window["player"], window["tile"]
        claims = [i for i, data in responses.items() if data["action_type"] not in ("pass", "force_pass")]
        if not claims:
            return self.draw_for((actor + 1) % 4)
        index = min(claims, key=lambda i: (responses[i]["action_type"] in CHOWS, (i - actor) % 4))
        action, p = responses[index]["action_type"], self.player_list[index]
        needed = claim_tiles(action, tile)
        for t in needed:
            p.hand_tiles.remove(t)
        tiles = sorted(needed + [tile])
        code = f"s{tiles[1]}" if action in CHOWS else ("g" if action == "gang" else "k") + str(tile)
        if action in CHOWS:
            mask = [1, tile] + [value for t in needed for value in (0, t)]
            p.chi_count += 1
        else:
            marked = {1: len(tiles) - 1, 2: 1, 3: 0}[(actor - index) % 4]
            mask = [value for n, t in enumerate(tiles) for value in (1 if n == marked else 0, t)]
        p.combination_tiles.append(code)
        p.combination_mask.append(mask)
        p.has_draw_slot = p.after_kong = False
        p.pre_draw_tiles = []
        p.draw_kind = ""
        self.player_list[actor].discard_tiles.pop()
        self.current_player_index = index
        self._owner_operation(index, meld=True)
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask, cut_from_player=actor)
        return self.draw_for(index, replacement=True) if action == "gang" else self.begin_turn(index, discard_only=True)

    def _self_kong(self, index, action, tile):
        p = self.player_list[index]
        drawn = p.has_draw_slot and p.hand_tiles[-1] == tile
        if action == "angang":
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code, mask = f"G{tile}", [2, tile] * 4
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
        else:
            p.hand_tiles.remove(tile)
            position = p.combination_tiles.index(f"k{tile}")
            code, mask = f"g{tile}", list(p.combination_mask[position])
            called = next(i for i in range(0, len(mask), 2) if mask[i] == 1)
            mask[called:called] = [3, tile]
            p.combination_tiles[position], p.combination_mask[position] = code, mask
        p.has_draw_slot = False
        self._owner_operation(index, meld=True)
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask, is_mo_gang=drawn)
        # Only self-draw wins are legal: there is no rob-kong response window.
        return self.draw_for(index, replacement=True)

    def draw_for(self, index, replacement=False):
        if len(self.tiles_list) < (22 if replacement else 21):
            return self.end_draw()
        p = self.player_list[index]
        self._owner_operation(index)
        p.hand_tiles.sort()
        p.pre_draw_tiles = list(p.hand_tiles)
        p.drawn_after_cai = p.cai_piao_count > 0
        if replacement:
            tile = self.tiles_list.pop()
            burned = self.tiles_list.pop()
            self.burned_tiles.append(burned)
            self._event("hangzhou_tail_burn", index, tile=burned, burn_count=len(self.burned_tiles))
        else:
            tile = self.tiles_list.pop(0)
            self.natural_draw_count[index] += 1
        p.hand_tiles.append(tile)
        p.has_draw_slot = True
        p.after_kong = replacement
        p.draw_kind = "kong" if replacement else "normal"
        self.current_player_index = index
        self._event("deal_gang_tile" if replacement else "deal_tile", index, tile=tile)
        return self.begin_turn(index)
