"""Authoritative classic16 flow on the existing turn-based transport/record shell.

As in Guobiao, a discard opens a separate response window and only the server
selects the winning claim. Domain validation precedes every table mutation.
"""
import asyncio
import random
from collections import Counter

from ...game_calculation.filipino import rules as book
from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ..public.vote_manager import vote_checkpoint
from .player import FilipinoPlayer
from .protocol import Protocol
from .recording import Recording
from .state_machine import Phase as P, StateMachine


def empty_actions():
    return {i: [] for i in range(4)}


class FilipinoGameState(Protocol, Recording, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None,
                 db_manager=None, gamestate_id="filipino-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("未知菲律宾子规则")
        if len(data.get("player_list", [])) != 4:
            raise ValueError("classic16需要四名玩家")
        self.rules = book.Rules.from_dict(data.get("detailed_config"))
        data.update(room_rule="filipino", sub_rule=book.SUB_RULE, claim_protection=False)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [FilipinoPlayer(**vars(p)) for p in self.player_list]
        self.player_entry_order = [p.user_id for p in self.player_list]
        self.detailed_config = self.rules.snapshot()
        self.rule_version = book.VERSION
        self.dead_wall_count = 0
        self.bot_speed = data.get("bot_speed", "normal")
        self.match_finishing = False
        self.opening_queue = None
        self.round_changes = [0] * 4
        self.round_start_scores = [p.score for p in self.player_list]
        self.ledger = []
        self.domain_events = []
        self.dice = []
        self.hand_multiplier = 1
        self.next_dealer = 1
        self.dealer_streak = 0

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="filipino-test", room_rule="filipino", sub_rule=book.SUB_RULE,
                    player_list=[101, 102, 103, 104], player_settings={}, game_round=1,
                    round_timer=0, step_timer=0, room_type="custom", tips=False, random_seed=1)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed, self.round_index)

    def _window(self, phase, player=None, tile=None, actions=None):
        self.machine.transition(phase)
        return dict(status=phase.value, player=player, tile=tile, actions=actions or empty_actions())

    def _event(self, action, actor, **details):
        from copy import deepcopy
        event = deepcopy(dict(action=action, player=actor, **details))
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event)

    def initialize_round(self, *, wall=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须先完成上一局的确认阶段")
        self.reset_round_state()
        self.round_changes = [0] * 4
        self.round_start_scores = [p.score for p in self.player_list]
        self.ledger, self.domain_events = [], []
        rng = random.Random(self.round_random_seed)
        self.dice = [rng.randint(1, 6), rng.randint(1, 6)]
        self.hand_multiplier = 2 if self.rules.dice_double and self.dice[0] == self.dice[1] else 1
        physical = list(book.PHYSICAL)
        if wall is None:
            rng.shuffle(physical)
            offset = (((sum(self.dice) - 1) % 4) * 36 + sum(self.dice) * 2) % 144
            wall = physical[offset:] + physical[:offset]
        elif Counter(wall) != Counter(physical):
            raise ValueError("须使用108张数牌、28张字牌及8花的完整牌组")
        self.tiles_list = list(wall)
        # Ging: eight tiles each, then nine for mano and eight for others.
        for packet in (0, 1):
            for index, player in enumerate(self.player_list):
                count = 9 if packet == 1 and index == 0 else 8
                player.hand_tiles.extend(self.tiles_list[:count])
                del self.tiles_list[:count]
        self.player_list[0].has_draw_slot = True
        self.player_list[0].last_drawn_tile = self.player_list[0].hand_tiles[-1]
        self.player_list[0].draw_kind = "normal"
        self.current_player_index = self.dealer_index = 0
        self.opening_queue = list(range(4))

    def opening_window(self):
        while self.opening_queue:
            index = self.opening_queue.pop(0)
            if any(t in book.FLOWERS for t in self.player_list[index].hand_tiles):
                self.current_player_index = index
                actions = empty_actions()
                actions[index] = ["buhua"]
                return self._window(P.REPLACEMENT, index, actions=actions)
        self.opening_queue = None
        for index, p in enumerate(self.player_list):
            if (self.rules.opening_no_flowers and not p.huapai_list) or (self.rules.opening_one_flower and len(p.huapai_list) == 1):
                self.award(index, 1, "起手无花" if not p.huapai_list else "起手一花")
        self.current_player_index = 0
        for p in self.player_list:
            p.has_draw_slot = p.player_index == 0
            drawn = p.hand_tiles[-1] if p.has_draw_slot else None
            p.hand_tiles[:] = sorted(p.hand_tiles[:-1]) + [drawn] if p.has_draw_slot else sorted(p.hand_tiles)
            p.last_drawn_tile = drawn
        return self.begin_turn(0)

    def replace_flowers(self, index):
        p = self.player_list[index]
        flowers = [t for t in p.hand_tiles if t in book.FLOWERS]
        if not flowers:
            raise ValueError("没有可补的花牌")
        for tile in flowers:
            mo = p.has_draw_slot and p.hand_tiles[-1] == tile
            position = len(p.hand_tiles) - 1 if mo else p.hand_tiles.index(tile)
            p.hand_tiles.pop(position)
            p.huapai_list.append(tile)
            p.has_draw_slot = False
            self._event("buhua", index, tile=tile, is_mo_buhua=mo)
            thresholds = (13, 16, 19) if self.rules.extra_flower_thresholds else (13,)
            if len(p.huapai_list) in thresholds and len(p.huapai_list) not in p.flower_awards:
                p.flower_awards.add(len(p.huapai_list))
                self.award(index, 1, f"{len(p.huapai_list)}花")
            if not self.tiles_list:
                return self.end_hand()
            drawn = self.tiles_list.pop()
            p.hand_tiles.append(drawn)
            p.last_drawn_tile = drawn
            p.has_draw_slot = len(p.hand_tiles) % 3 == 2
            p.draw_kind = "flower"
            self._event("deal_buhua_tile", index, tile=drawn)
        if self.opening_queue is not None:
            if any(t in book.FLOWERS for t in p.hand_tiles):
                self.opening_queue.append(index)
            return self.opening_window()
        return self.begin_turn(index)

    def legal_discard_tiles(self, index):
        return set(self.player_list[index].hand_tiles).intersection(book.TILES)

    def kong_tiles(self, index, action):
        p = self.player_list[index]
        if not self.tiles_list or not p.has_draw_slot:
            return set()
        if action == "angang":
            return {t for t, count in Counter(p.hand_tiles).items() if count == 4 and t in book.TILES}
        # Sagasa must use the newly drawn physical fourth tile.
        tile = p.last_drawn_tile
        return {tile} if tile in book.TILES and f"k{tile}" in p.combination_tiles else set()

    def quote(self, index, source, tile):
        p = self.player_list[index]
        hand = list(p.hand_tiles) + ([] if source == "self_draw" else [tile])
        return book.score(hand, p.combination_tiles, p.huapai_list, winning_tile=tile,
                          self_draw=source == "self_draw", discard_count=len(self.discard_log), rules=self.rules)

    def begin_turn(self, index, *, discard_only=False):
        self.current_player_index = index
        p = self.player_list[index]
        actions = empty_actions()
        if not discard_only and any(t in book.FLOWERS for t in p.hand_tiles):
            actions[index] = ["buhua"]
            return self._window(P.REPLACEMENT, index, actions=actions)
        actions[index] = ["cut"]
        if not discard_only:
            if p.has_draw_slot and self.quote(index, "self_draw", p.hand_tiles[-1]):
                actions[index].append("hu_self")
            for action in ("angang", "jiagang"):
                if self.kong_tiles(index, action):
                    actions[index].append(action)
        p.waiting_tiles = book.waits(p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles, p.combination_tiles, self.rules)
        self.hand_action_is_gang_draw[index] = p.draw_kind == "kong"
        return self._window(P.DISCARD_ONLY if discard_only else P.TURN, index, actions=actions)

    @staticmethod
    def claim_tiles(action, tile):
        return {"peng": (tile, tile), "gang": (tile,) * 3,
                "chi_left": (tile - 2, tile - 1), "chi_mid": (tile - 1, tile + 1),
                "chi_right": (tile + 1, tile + 2)}[action]

    def claim_actions(self, actor, tile):
        actions = empty_actions()
        for index, p in enumerate(self.player_list):
            if index == actor:
                continue
            if self.quote(index, "discard", tile):
                actions[index].append("hu")
            if self.tiles_list and len(p.combination_tiles) < 5:
                choices = ["peng", "gang"] + (["chi_left", "chi_mid", "chi_right"] if index == (actor + 1) % 4 else [])
                for action in choices:
                    needed = self.claim_tiles(action, tile)
                    if all(t in book.TILES and t // 10 == tile // 10 for t in needed) and not Counter(needed) - Counter(p.hand_tiles):
                        actions[index].append(action)
            if actions[index]:
                actions[index].append("pass")
        return actions

    def validate_response(self, index, data, offered):
        action = data.get("action_type")
        if type(index) is not int or index not in range(4) or action not in offered:
            raise ValueError("当前没有该操作")
        if action == "cut":
            self.validated_cut(index, data)
        elif action in ("angang", "jiagang"):
            if type(data.get("target_tile")) is not int or data["target_tile"] not in self.kong_tiles(index, action):
                raise ValueError("非法杠牌")

    def validated_cut(self, index, data):
        p = self.player_list[index]
        tile, position = data.get("TileId"), data.get("cutIndex", -1)
        if type(tile) is not int or tile not in self.legal_discard_tiles(index):
            raise ValueError("非法舍牌")
        if type(position) is not int or position < -1 or position >= len(p.hand_tiles):
            raise ValueError("非法舍牌位置")
        draw = data.get("cutClass")
        if draw is not None and type(draw) is not bool:
            raise ValueError("非法摸切标识")
        if draw is True:
            if not p.has_draw_slot or p.hand_tiles[-1] != tile:
                raise ValueError("摸切必须是当前摸牌")
            position = len(p.hand_tiles) - 1
        elif draw is False:
            held = p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:
                raise ValueError("手切牌不存在")
            # cutIndex is a Unity sibling index; sorting/manual arrangement is
            # local to the client. Validate tile ownership and hand/draw source.
            if position >= len(held):
                raise ValueError("手切位置越界")
            position = held.index(tile)
        elif position >= 0:
            if p.hand_tiles[position] != tile:
                raise ValueError("舍牌牌值不符")
        else:
            position = len(p.hand_tiles) - 1 if p.has_draw_slot and p.hand_tiles[-1] == tile else p.hand_tiles.index(tile)
        return tile, position, p.has_draw_slot and position == len(p.hand_tiles) - 1

    def apply_action_results(self, window, results, *, settlements=None):
        if settlements is not None:
            raise ValueError("客户端不得指定结算")
        if window is not self.live_pending_window or window.get("action_tick") != self.server_action_tick:
            raise ValueError("过期的操作窗口")
        offered = window["actions"]
        if set(results) != {i for i, actions in offered.items() if actions}:
            raise ValueError("应答座位不符")
        for index, data in results.items():
            self.validate_response(index, data, offered[index])
        phase = P(window["status"])
        if phase == P.REPLACEMENT:
            next_window = self.replace_flowers(window["player"])
        elif phase in (P.TURN, P.DISCARD_ONLY):
            index = window["player"]
            data = results[index]
            action = data["action_type"]
            if action == "hu_self":
                next_window = self.win(index, "self_draw", self.player_list[index].hand_tiles[-1])
            elif action in ("angang", "jiagang"):
                next_window = self.commit_kong(index, data["target_tile"], action)
            else:
                tile, position, mo = self.validated_cut(index, data)
                p = self.player_list[index]
                p.hand_tiles.pop(position)
                p.hand_tiles.sort()
                p.has_draw_slot = False
                p.last_drawn_tile = None
                p.discard_tiles.append(tile)
                p.discard_origin_tiles.append(tile)
                self.discard_log.append((index, tile))
                self._event("cut", index, tile=tile, cutIndex=data.get("cutIndex", position), cutClass=mo,
                            is_timeout_action=bool(data.get("is_timeout_action")))
                next_window = self._window(P.RESPONSE, index, tile, self.claim_actions(index, tile))
        elif phase == P.RESPONSE:
            actor, tile = window["player"], window["tile"]
            priority = {"hu": 3, "gang": 2, "peng": 2, "chi_left": 1, "chi_mid": 1, "chi_right": 1}
            claims = [(-priority[d["action_type"]], (i - actor) % 4, i, d["action_type"])
                      for i, d in results.items() if d["action_type"] in priority]
            if not claims:
                next_window = self.draw_for((actor + 1) % 4)
            else:
                _, _, index, action = min(claims)
                next_window = self.win(index, "discard", tile, actor) if action == "hu" else self.claim(index, actor, tile, action)
        else:
            raise ValueError("此阶段不接受对局操作")
        return self.open_action_window(next_window)

    def claim(self, index, actor, tile, action):
        p = self.player_list[index]
        needed = self.claim_tiles(action, tile)
        for t in needed:
            p.hand_tiles.remove(t)
        self.player_list[actor].discard_tiles.pop()
        if action.startswith("chi"):
            code = "s" + str(sorted((*needed, tile))[1])
            mask = [1, tile, 0, needed[0], 0, needed[1]]
        else:
            code = ("g" if action == "gang" else "k") + str(tile)
            count = 4 if action == "gang" else 3
            marked = {1: count - 1, 2: 1, 3: 0}[(actor - index) % 4]
            mask = [part for n in range(count) for part in (int(n == marked), tile)]
        p.combination_tiles.append(code)
        p.combination_mask.append(mask)
        p.has_draw_slot = False
        p.last_drawn_tile = None
        self.current_player_index = index
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask, cut_from_player=actor)
        if action == "gang":
            self.award(index, self.rules.kong_points, "明杠")
            return self.draw_for(index, kind="kong")
        return self.begin_turn(index, discard_only=True)

    def commit_kong(self, index, tile, action):
        p = self.player_list[index]
        if action == "angang":
            for _ in range(4):
                p.hand_tiles.remove(tile)
            code, mask = f"G{tile}", [2, tile] * 4
            p.combination_tiles.append(code)
            p.combination_mask.append(mask)
        else:
            p.hand_tiles.pop()  # validated Sagasa is the current draw
            position = p.combination_tiles.index(f"k{tile}")
            code = f"g{tile}"
            mask = list(p.combination_mask[position]) + [3, tile]
            p.combination_tiles[position] = code
            p.combination_mask[position] = mask
        p.has_draw_slot = False
        self._event(action, index, tile=tile, meld_code=code, combination_mask=mask, is_mo_gang=True)
        self.award(index, self.rules.secret_points if action == "angang" else self.rules.sagasa_points,
                   "暗杠" if action == "angang" else "Sagasa")
        return self.draw_for(index, kind="kong")

    def draw_for(self, index, kind="normal"):
        if not self.tiles_list:
            return self.end_hand()
        p = self.player_list[index]
        p.hand_tiles.sort()
        tile = self.tiles_list.pop(0 if kind == "normal" else -1)
        p.hand_tiles.append(tile)
        p.last_drawn_tile = tile
        p.has_draw_slot = True
        p.draw_kind = kind
        self.current_player_index = index
        self._event("deal_tile" if kind == "normal" else "deal_gang_tile", index, tile=tile)
        return self.begin_turn(index)

    def award(self, index, points, reason):
        if not points:
            return
        amount = points * self.hand_multiplier
        changes = [-amount] * 4
        changes[index] = 3 * amount
        self.ledger.append(dict(player=index, reason=reason, changes=changes))
        for i, change in enumerate(changes):
            self.round_changes[i] += change
        self._event("filipino_score", index, score_changes=changes, reason=reason)

    def win(self, index, source, tile, payer=None):
        if self.machine.phase not in (P.TURN, P.RESPONSE) or self.deferred_hu_settlements:
            raise ValueError("当前阶段不能和牌")
        if source == "self_draw":
            if self.machine.phase != P.TURN or index != self.current_player_index or not self.player_list[index].has_draw_slot or self.player_list[index].hand_tiles[-1] != tile:
                raise ValueError("非法自摸")
        elif source != "discard" or self.machine.phase != P.RESPONSE or payer != self.current_player_index or index == payer or not self.discard_log or self.discard_log[-1] != (payer, tile):
            raise ValueError("非法点和")
        quote = self.quote(index, source, tile)
        if quote is None:
            raise ValueError("和牌牌形不成立")
        changes = book.payments(index, quote.points, payer=payer, self_draw=source == "self_draw",
                                rules=self.rules, multiplier=self.hand_multiplier)
        for i, change in enumerate(changes):
            self.round_changes[i] += change
        entry = dict(winner=index, source=source, tile=tile, discarder=payer,
                     points=quote.points, fan_ids=quote.fan_ids, score_changes=changes,
                     hu_order=1, recycle_discard=True, rule_version=book.VERSION)
        self.deferred_hu_settlements.append(entry)
        p = self.player_list[index]
        p.is_hu, p.hu_order, p.has_draw_slot = True, 1, False
        if source == "discard":
            self.player_list[payer].discard_tiles.pop()
            p.won_tiles.append(tile)
        self._event(self._hu_action_for_settlement(entry), index, tile=tile)
        return self.end_hand(index)

    def end_hand(self, winner=None):
        self.ended_by = "wall" if winner is None else "win"
        self.match_finishing = self.current_round >= self.max_round * 4
        if self.rules.dealer_mode == "rotate" or winner is None:
            self.next_dealer = 1
        elif self.rules.dealer_mode == "winner":
            self.next_dealer = winner
        else:
            rng = random.Random(derive_round_seed(self.master_seed, self.round_index + 100000))
            self.next_dealer = (winner + rng.randint(1, 6) + rng.randint(1, 6) - 1) % 4
        return self._window(P.END)

    def apply_deferred_score_changes(self):
        if self.deferred_scores_applied:
            return
        for index, p in enumerate(self.player_list):
            change = self.round_changes[index]
            p.score += change
            p.record_counter.round_score_total = getattr(p.record_counter, "round_score_total", 0) + change
            p.score_history.append(f"{change:+d}" if change else "0")
            p.round_number_history.append(self.current_round)
        self.deferred_scores_applied = True

    def open_action_window(self, window):
        self._active_deadlines = {}
        self._received_at = {}
        for index in range(4):
            self.action_events[index].clear()
            while not self.action_queues[index].empty():
                self.action_queues[index].get_nowait()
        return super().open_action_window(window)

    async def submit_action(self, player_index, action_type, **kwargs):
        if type(player_index) is not int or player_index not in range(4) or not self.action_queues[player_index].empty():
            raise ValueError("非法座位或重复应答")
        self.validate_response(player_index, dict(action_type=action_type, **kwargs), self.action_dict.get(player_index, []))
        now = asyncio.get_running_loop().time()
        deadline = getattr(self, '_active_deadlines', {}).get(player_index)
        if deadline is not None and now > deadline:
            raise ValueError("操作时限已过")
        await super().submit_action(player_index, action_type, **kwargs)
        self._received_at[player_index] = now

    def _build_timeout_action(self, index):
        actions = self.action_dict[index]
        if "buhua" in actions:
            data = dict(action_type="buhua")
        elif "cut" in actions:
            p = self.player_list[index]
            position = max(i for i, t in enumerate(p.hand_tiles) if t in self.legal_discard_tiles(index))
            data = dict(action_type="cut", TileId=p.hand_tiles[position], cutIndex=position,
                        cutClass=p.has_draw_slot and position == len(p.hand_tiles) - 1, is_timeout_action=True)
        else:
            data = dict(action_type="ready" if "ready" in actions else "pass")
        self.action_dict[index] = []
        if index in self.waiting_players_list:
            self.waiting_players_list.remove(index)
        return dict(player_index=index, **data)

    def schedule_bot_actions(self):
        from .bot import play_bot
        for index in self.waiting_players_list:
            if self.player_list[index].is_bot and self.bot_action_ticks.get(index) != self.server_action_tick:
                self.bot_action_ticks[index] = self.server_action_tick
                task = asyncio.create_task(play_bot(self, index, self.server_action_tick))
                self.bot_tasks.add(task)
                task.add_done_callback(self.bot_tasks.discard)

    async def wait_action(self, timeout=None):
        self.schedule_bot_actions()
        loop = asyncio.get_running_loop()
        started, results = loop.time(), {}
        deadlines = {}
        for index in self.waiting_players_list:
            seconds = timeout if self.machine.phase == P.READY and timeout is not None else self.step_time + self.player_list[index].remaining_time
            if self.player_list[index].is_bot:
                from ..public.ai.pacing import bot_delay
                seconds = max(seconds, bot_delay(self) + 0.2)
            deadlines[index] = started + max(0, seconds)
        self._active_deadlines = deadlines
        while self.waiting_players_list:
            for index in list(self.waiting_players_list):
                if not self.action_queues[index].empty():
                    results[index] = self.action_queues[index].get_nowait()
                    self.waiting_players_list.remove(index)
                    self.action_dict[index] = []
                    self.action_events[index].clear()
                    if self.machine.phase != P.READY:
                        self._consume_time_bank(index, max(0, self._received_at.pop(index, loop.time()) - started))
                elif loop.time() >= deadlines[index]:
                    results[index] = self._build_timeout_action(index)
                    if self.machine.phase != P.READY:
                        self.player_list[index].remaining_time = 0
            if self.waiting_players_list:
                tasks = [asyncio.create_task(self.action_events[i].wait()) for i in self.waiting_players_list]
                try:
                    await asyncio.wait(tasks, timeout=max(0, min(deadlines[i] for i in self.waiting_players_list) - loop.time()), return_when=asyncio.FIRST_COMPLETED)
                finally:
                    for task in tasks:
                        task.cancel()
                    await asyncio.gather(*tasks, return_exceptions=True)
        return results

    async def run_game_loop(self):
        try:
            self.start_game_recording()
            while True:
                self.initialize_round()
                self.start_round_recording()
                self.emit_game_start_payloads()
                self.open_action_window(self.opening_window())
                await self.flush_outbound_payloads()
                while self.machine.phase != P.END:
                    await vote_checkpoint(self)
                    await self.resolve_action_window(timeout=self.estimated_action_window_timeout())
                    await self.flush_outbound_payloads()
                self.finalize_round_recording()
                await self.run_round_ready_phase(timeout=self.estimated_round_result_ready_timeout())
                if self.match_finishing:
                    self.machine.transition(P.FINISHED)
                    break
                self.advance_round_after_ready()
            self.finalize_game_recording()
            await self.complete_game_lifecycle()
        finally:
            for task in list(self.bot_tasks):
                task.cancel()

    async def run_round_ready_phase(self, timeout=None):
        self.machine.transition(P.READY)
        self.live_pending_window = None
        self._active_deadlines = {}
        self._received_at = {}
        for i in range(4):
            self.action_events[i].clear()
            while not self.action_queues[i].empty():
                self.action_queues[i].get_nowait()
        pending = [] if self.match_finishing else [i for i, p in enumerate(self.player_list) if not p.is_bot]
        self.server_action_tick += 1
        self.action_dict = {i: ["ready"] if i in pending else [] for i in range(4)}
        self.waiting_players_list = pending
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()
        if pending:
            await self.wait_action(timeout)
        self.action_dict = empty_actions()
        self.emit_ready_status_payloads()
        await self.flush_outbound_payloads()

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise RuntimeError("当前不能开下一局")
        self.player_list = self.player_list[self.next_dealer:] + self.player_list[:self.next_dealer]
        for index, p in enumerate(self.player_list):
            p.player_index = index
        self.current_round += 1
        self.round_index += 1
        self.current_player_index = self.dealer_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
