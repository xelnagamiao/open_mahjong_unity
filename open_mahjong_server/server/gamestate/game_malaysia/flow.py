from dataclasses import replace
from ...game_calculation.malaysia import rules as R
from .phases import P
from . import actions as A


class HandFlow:
    def window(self, phase, actor=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=actor, tile=tile, actions=actions or A.empty())

    def event(self, action, actor, **fields):
        event = dict(action=action, player=actor, **fields)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def opening_window(self):
        if not self.opening_queue:
            # The source requires rotations: replacements drawn as flowers wait
            # for the next circuit, rather than giving East every tail tile.
            self.opening_queue = [(i, t) for i, p in enumerate(self.player_list) for t in p.hand_tiles if t in R.BONUS]
        if self.opening_queue:
            i, tile = self.opening_queue.pop(0)
            self.flower_pending = dict(actor=i, tile=tile, opening=True, discard_only=False)
            return self.window(P.FLOWER, i, tile, {**A.empty(), i: ["buhua"]})
        self.opening = False
        for i, p in enumerate(self.player_list):
            if p.hand_tiles.count(R.FLY) == 4:
                return self.settle_win(i, "self_draw", None, automatic=True)
        return self.begin_turn(0)

    def begin_turn(self, i, discard_only=False):
        self.current_player_index = i
        p = self.player_list[i]
        bonus = next((t for t in p.hand_tiles if t in R.BONUS), None)
        if bonus is not None:
            self.flower_pending = dict(actor=i, tile=bonus, opening=False, discard_only=discard_only)
            return self.window(P.FLOWER, i, bonus, {**A.empty(), i: ["buhua"]})
        p.waiting_tiles = set(R.waits(tuple(p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles), tuple(p.melds)))
        return self.window(P.CUT if discard_only else P.TURN, i, actions=A.turn(self, i, discard_only))

    def replace_flower(self):
        pending = self.flower_pending
        i, tile = pending["actor"], pending["tile"]
        p = self.player_list[i]
        is_draw = p.has_draw_slot and p.hand_tiles[-1] == tile
        p.hand_tiles.remove(tile)
        p.huapai_list.append(tile)
        p.has_draw_slot = False
        self.event("buhua", i, tile=tile, is_mo_buhua=is_draw)
        for n, group in enumerate(R.SETS):
            if n not in p.paid_bonus_sets and set(group) <= set(p.huapai_list):
                p.paid_bonus_sets.add(n)
                self.pay_instant(i, [j for j in range(3) if j != i], 100 if pending["opening"] else 50, "起手成套花" if pending["opening"] else "成套花")
        self.flower_pending = None
        if not self.tiles_list:
            return self.end_draw()
        tile = self.tiles_list.pop()
        p.hand_tiles.append(tile)
        p.has_draw_slot = not pending["opening"] or i == 0
        p.last_drawn_tile, p.draw_kind = tile, "flower"
        p.last_wall = not self.tiles_list
        self.event("deal_buhua_tile", i, tile=tile)
        if pending["opening"]:
            return self.opening_window()
        return self.begin_turn(i, pending["discard_only"])

    def draw_for(self, i, kind="normal"):
        if not self.tiles_list:
            return self.end_draw()
        p = self.player_list[i]
        tile = self.tiles_list.pop(0 if kind == "normal" else -1)
        p.hand_tiles.append(tile)
        p.has_draw_slot, p.last_drawn_tile, p.draw_kind = True, tile, kind
        p.last_wall = not self.tiles_list
        if kind == "normal":
            p.natural_draws += 1
            p.consecutive_kongs = 0
        self.event("deal_tile" if kind == "normal" else "deal_gang_tile", i, tile=tile)
        return self.begin_turn(i)

    def cut_identity(self, i, data):
        p = self.player_list[i]
        tile, position, drawn = data.get("TileId"), data.get("cutIndex", -1), data.get("cutClass")
        if type(tile) is not int or tile not in A.cuts(p) or type(position) is not int or position < -1 or position >= len(p.hand_tiles):
            raise ValueError("非法出牌")
        if drawn is not None and type(drawn) is not bool:
            raise ValueError("出牌类型无效")
        if drawn:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile or position not in (-1, len(p.hand_tiles) - 1):
                raise ValueError("摸切牌不匹配")
            position = len(p.hand_tiles) - 1
        elif drawn is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:
                raise ValueError("手切牌不在暗手中")
            position = held.index(tile)
        elif position == -1:
            position = p.hand_tiles.index(tile)
        elif p.hand_tiles[position] != tile:
            raise ValueError("牌与索引不符")
        return tile, position, p.has_draw_slot and position == len(p.hand_tiles) - 1

    def validate_response(self, i, data, offered):
        if type(i) is not int or i not in range(3) or not isinstance(data, dict) or data.get("action_type") not in offered:
            raise ValueError("非法行动")
        a = data["action_type"]
        if a == "cut":
            self.cut_identity(i, data)
        if a in ("angang", "jiagang") and (type(data.get("target_tile")) is not int or data["target_tile"] not in A.kongs(self, i, a)):
            raise ValueError("非法杠牌")

    def apply_action_results(self, window, responses, *, settlements=None):
        if settlements is not None or window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("过期窗口或外部结算")
        offered = window["actions"]
        if set(responses) != {i for i, a in offered.items() if a}:
            raise ValueError("响应座位不完整")
        for i, data in responses.items():
            self.validate_response(i, data, offered[i])
        phase = P(window["status"])
        if phase in (P.TURN, P.CUT):
            next_window = self.apply_turn(window["player"], responses[window["player"]], phase == P.CUT)
        elif phase == P.FLOWER:
            next_window = self.replace_flower()
        elif phase in (P.RESPONSE, P.KONG):
            next_window = self.apply_responses(window, responses, phase == P.KONG)
        else:
            raise ValueError("当前阶段不能操作")
        return self.open_action_window(next_window)

    def apply_turn(self, i, data, discard_only=False):
        p, action = self.player_list[i], data["action_type"]
        if action == "hu_self":
            return self.settle_win(i, "self_draw", p.last_drawn_tile)
        if action == "malaysia_fly":
            self.flower_pending = dict(actor=i, tile=R.FLY, opening=False, discard_only=discard_only)
            return self.replace_flower()
        if action.startswith("malaysia_redeem:"):
            n, tile = int(action.split(":")[1]), p.last_drawn_tile
            m = p.melds[n]
            physical = list(m.physical)
            at = physical.index(R.FLY)
            physical[at] = tile
            p.melds[n] = replace(m, physical=tuple(physical))
            p.combination_mask[n][2 * at + 1] = tile
            p.hand_tiles[-1] = R.FLY
            p.last_drawn_tile = R.FLY
            self.event("malaysia_redeem", i, tile=tile, meld_index=n, redeemed_slot=at)
            return self.begin_turn(i)
        if action in ("angang", "jiagang"):
            tile = data["target_tile"]
            self.pending_kong = dict(actor=i, tile=tile, concealed=action == "angang", is_mo_gang=p.hand_tiles[-1] == tile)
            return self.window(P.KONG, i, tile, A.empty() if action == "angang" else A.responses(self, i, tile, rob=True))
        tile, pos, drawn = self.cut_identity(i, data)
        p.hand_tiles.pop(pos)
        p.has_draw_slot = False
        p.last_drawn_tile = None
        p.discard_tiles.append(tile)
        p.discard_origin_tiles.append(tile)
        self.sea_discard = p.last_wall
        self.discard_log.append((i, tile))
        self.event("cut", i, tile=tile, cutIndex=data.get("cutIndex", pos), cutClass=drawn, is_timeout_action=data.get("is_timeout_action", False))
        return self.window(P.RESPONSE, i, tile, A.responses(self, i, tile))

    def apply_responses(self, window, responses, rob=False):
        actor, tile = window["player"], window["tile"]
        wins = [i for i, d in responses.items() if d["action_type"] == "hu"]
        if wins:
            i = min(wins, key=lambda i: (i - actor) % 3)
            return self.settle_win(i, "rob_kong" if rob else "discard", tile, payer=actor)
        if rob:
            return self.commit_kong()
        claims = [(i, d["action_type"]) for i, d in responses.items() if d["action_type"] != "pass"]
        if not claims:
            return self.draw_for((actor + 1) % 3)
        def priority(item):
            i, action = item
            # Natural pung and natural kong beat wild pung. Chows come last.
            order = 2 if action.startswith("malaysia_chi:") else 1 if action.startswith("malaysia_peng:") and action != "malaysia_peng:0" else 0
            return order, (i - actor) % 3
        i, action = min(claims, key=priority)
        p = self.player_list[i]
        m = A.claims(p, tile, i == (actor + 1) % 3, self.tiles_list)[action]
        m = replace(m, source=actor)
        self.remember_liability(i, actor, tile)
        for t in m.physical[1:]:
            p.hand_tiles.remove(t)
        mask = [1, tile] + [x for t in m.physical[1:] for x in (0, t)]
        p.melds.append(m)
        p.combination_tiles.append(m.code())
        p.combination_mask.append(mask)
        p.has_draw_slot, p.last_drawn_tile = False, None
        self.player_list[actor].discard_tiles.pop()
        self.current_player_index = i
        self.opening_interrupted = True
        standard = "gang" if m.kind == "kong" else "peng" if m.kind == "triplet" else {0:"chi_right", 1:"chi_mid", 2:"chi_left"}[tile - min(m.logical)]
        self.event(standard, i, tile=tile, meld_code=m.code(), combination_mask=mask, cut_from_player=actor)
        if m.kind == "kong":
            p.consecutive_kongs += 1
            self.pay_instant(i, [actor], 100, "明杠")
            return self.draw_for(i, "kong")
        p.consecutive_kongs = 0
        return self.begin_turn(i, discard_only=True)

    def remember_liability(self, receiver, payer, tile):
        p = self.player_list[receiver]
        exposed = {m.logical[0] for m in p.melds if m.kind in ("triplet", "kong") and not m.concealed}
        if (tile in R.DRAGONS and len(exposed & set(R.DRAGONS)) == 2) or (tile in R.WINDS + R.DRAGONS and len(exposed & set(R.WINDS + R.DRAGONS)) == 3):
            if tile not in exposed and p.liability is None:
                p.liability = payer

    def commit_kong(self):
        q = self.pending_kong
        i, tile, concealed = q["actor"], q["tile"], q["concealed"]
        p = self.player_list[i]
        for _ in range(4 if concealed else 1):
            p.hand_tiles.remove(tile)
        m = R.Meld("kong", (tile,) * 4, (tile,) * 4, concealed=concealed)
        if concealed:
            mask, word = [2, tile] * 4, "angang"
            p.melds.append(m)
            p.combination_tiles.append(m.code())
            p.combination_mask.append(mask)
        else:
            n = next(n for n, old in enumerate(p.melds) if old.kind == "triplet" and old.logical[0] == tile)
            m = replace(m, source=p.melds[n].source)
            mask, word = [1, tile, 3, tile, 0, tile, 0, tile], "jiagang"
            p.melds[n], p.combination_tiles[n], p.combination_mask[n] = m, m.code(), mask
        p.has_draw_slot = False
        p.consecutive_kongs += 1
        self.opening_interrupted = True
        self.pending_kong = None
        self.event(word, i, tile=tile, meld_code=f"k{tile}" if not concealed else m.code(), combination_mask=mask, is_mo_gang=q["is_mo_gang"])
        self.pay_instant(i, [j for j in range(3) if j != i], 100 if concealed else 50, "暗杠" if concealed else "加杠")
        return self.draw_for(i, "kong")
