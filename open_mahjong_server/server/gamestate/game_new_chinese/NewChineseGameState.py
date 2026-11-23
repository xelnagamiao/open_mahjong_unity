import asyncio
import random
from collections import Counter

from ...game_calculation.new_chinese import rules
from ..game_beijing.runtime import Runtime
from ..game_beijing.settlement import EndOfHand
from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from .actions import legal_cuts
from .flow import HandFlow
from .player import NewChinesePlayer
from .protocol import Protocol
from .state_machine import Phase as P, StateMachine


class NewChineseGameState(HandFlow, Protocol, Runtime, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None, db_manager=None, gamestate_id="new-chinese-test"):
        data = dict(room_data or dict(room_id="new-chinese-test", player_list=[101, 102, 103, 104], game_round=4))
        if len(data["player_list"]) != 4 or data.get("sub_rule", rules.SUB_RULE) != rules.SUB_RULE:
            raise ValueError("新中式麻将仅限四人标准规则")
        data.update(room_rule="new_chinese", sub_rule=rules.SUB_RULE)
        self.machine = StateMachine()
        self.detailed_config = rules.normalize_config(data.get("detailed_config"))
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.hepai_limit = rules.room_fan_limit(data.get("hepai_limit", 0), self.detailed_config["exchange_three"])
        self.player_list = [NewChinesePlayer(**vars(p)) for p in self.player_list]
        self.bot_speed = data.get("bot_speed", "normal")
        self.rule_version = rules.RULE_VERSION
        self.dead_wall_count = self.repeat = 0
        self.match_finishing = False
        self.bao = self.bao_revision = 0
        self.ready_order, self.supplements = [], []
        self.candidate_cuts = {}
        self.must_declare = self.pending_kong = self.round_settlement = None
        self.round_start_scores = [0] * 4

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed, self.round_index)

    async def flush_outbound_payloads(self):
        window = self.live_pending_window
        showing = window and window["status"] in (P.EXCHANGE_CHOICES, P.EXCHANGE_RECEIPT, P.BAO_REVEAL) and not window.get("broadcast_complete")
        if showing:
            window["broadcast_complete"] = False
        await super().flush_outbound_payloads()
        if showing:
            from ..public.outbound_pipe import drain_viewer
            await asyncio.gather(*(drain_viewer(self, i) for i in range(4)))
            if window is self.live_pending_window and not window.get("broadcast_complete"):
                window["deadline"] = self._timing_now() + window["duration_ms"] / 1000
                window["broadcast_complete"] = True

    async def wait_action(self, timeout=None):
        if self.machine.phase in (P.EXCHANGE_CHOICES, P.EXCHANGE_RECEIPT, P.BAO_REVEAL):
            window = self.live_pending_window
            while (remaining := window["deadline"] - self._timing_now()) > 0:
                await asyncio.sleep(remaining)
            return {}
        return await super().wait_action(timeout)

    async def submit_action(self, player_index, action_type, **fields):
        await super().submit_action(player_index, action_type, **fields)
        if self.machine.phase == P.EXCHANGE:
            self.live_pending_window.setdefault("submitted", {})[player_index] = list(fields.get("target_tile") or [])

    def initialize_round(self, *, wall=None):
        self.reset_round_state()
        self.bao = self.bao_revision = 0
        self.ready_order = []
        self.must_declare = self.pending_kong = self.round_settlement = None
        self.match_finishing = False
        self.round_start_scores = [p.score for p in self.player_list]
        self._action_clocks, self._action_deadlines = {}, {}
        self.rng = random.Random(self.round_random_seed)
        deck = [t for t in rules.TILES for _ in range(4)]
        if wall is None:
            self.rng.shuffle(deck)
        elif Counter(wall) != Counter(deck):
            raise ValueError("新中式牌墙应为136张无花牌")
        else:
            deck = list(wall)
        for offset in range(4):
            p = self.player_list[(self.dealer_index + offset) % 4]
            p.hand_tiles = sorted([deck[start + k] for start in (4*offset, 16+4*offset, 32+4*offset) for k in range(4)] + [deck[48+offset]])
        dealer = self.player_list[self.dealer_index]
        dealer.hand_tiles.append(deck[52])
        dealer.has_draw_slot = True
        self.tiles_list, self.supplements = deck[53:-10], deck[-10:]

    def river_counts(self):
        return Counter(t for p in self.player_list for t in p.discard_tiles)

    def visible_counts(self):
        counts = self.river_counts()
        for p in self.player_list:
            for code in p.combination_tiles:
                if not code.startswith("G"):
                    counts.update(rules.parse_meld(code).tiles)
        return counts

    def win_source(self, index, tile, mode, payer=None):
        if mode == "draw":
            return "bao_draw" if self.bao == tile else "self_draw"
        if mode == "seen":
            return "bao_seen"
        return "black" if mode == "rob" or not self.player_list[payer].declared else "discard"

    def quote(self, index, tile, mode, payer=None):
        p = self.player_list[index]
        if not p.declared or "peida" in p.tag_list or p.level == 4:
            return None
        source = self.win_source(index, tile, mode, payer)
        if mode in ("discard", "rob") and (tile == self.bao or p.level >= 2 or tile in p.discard_win_lockout_tiles):
            return None
        if mode == "draw" and (not p.has_draw_slot or p.level >= 3 and source != "bao_draw"):
            return None
        if mode == "seen" and (not tile or p.level == 3 and not getattr(self, "changed_bao", False)):
            return None
        hand = p.hand_tiles[:-1] if mode == "draw" else p.hand_tiles
        visible = self.visible_counts()[tile] - int(mode == "discard")
        if mode == "rob" and self.pending_kong and self.pending_kong[2] == "gang":
            visible -= 1
        if mode not in ("seen",) and tile == self.bao:
            visible += 1
        return rules.score(hand, p.combination_tiles, tile, source, after_kong=p.after_kong,
                           chain=max(p.chain, p.longest_chain), visible=visible, dealer=index == self.dealer_index,
                           payer_dealer=payer == self.dealer_index, hepai_limit=self.hepai_limit,
                           exchange_three=self.detailed_config["exchange_three"])

    def settle(self, index, tile, mode, payer=None):
        quote = self.quote(index, tile, mode, payer)
        if not quote:
            raise ValueError("和牌未成立")
        p = self.player_list[index]
        changes = rules.payments(quote, index, payer, self.dealer_index)
        source = "self_draw" if mode in ("seen", "draw") else "rob_kong" if mode == "rob" else "discard"
        if mode == "seen":
            p.hand_tiles.append(tile)
        if mode == "discard":
            self.player_list[payer].discard_tiles.pop()
            self.player_list[payer].riichi_flags.pop()
        entry = dict(winner=index, source=source, tile=tile, discarder=payer, points=changes[index],
                     fan_ids=rules.settlement_labels(quote), fan_names=list(quote["fans"]), fan_details=quote, score_changes=changes,
                     recycle_discard=mode == "discard", multi_ron=False, hu_order=1)
        self.deferred_hu_settlements.append(entry)
        p.is_hu, p.hu_order, self.hu_order_counter = True, 1, 1
        for other, delta in zip(self.player_list, changes):
            other.score += delta
        self.event(self._hu_action_for_settlement(entry), index, tile=tile)
        return self.finish(index)

    def finish(self, winner=None):
        self.dealer_changes = winner is not None and winner != self.dealer_index
        self.match_finishing = self.dealer_changes and self.current_round >= self.max_round * 4
        self.ended_by = "wall" if winner is None else "win"
        self.round_changes = [p.score - start for p, start in zip(self.player_list, self.round_start_scores)]
        self.round_settlement = dict(winner=winner, changes=self.round_changes)
        return self.window(P.END)

    apply_deferred_score_changes = EndOfHand.apply_deferred_score_changes

    def advance_round_after_ready(self):
        if self.dealer_changes:
            self.dealer_index = (self.dealer_index + 1) % 4
            self.current_round += 1
            self.repeat = 0
        else:
            self.repeat += 1
        self.round_index += 1
        self.machine.transition(P.START)

    def _build_timeout_action(self, index):
        offered = self.action_dict[index]
        if "cut" in offered or "riichi_cut" in offered:
            p = self.player_list[index]
            legal = legal_cuts(self, index)
            position = next(i for i in reversed(range(len(p.hand_tiles))) if p.hand_tiles[i] in legal)
            data = dict(action_type="cut" if "cut" in offered else "riichi_cut", TileId=p.hand_tiles[position],
                        cutIndex=position, cutClass=p.has_draw_slot and position == len(p.hand_tiles)-1,
                        player_index=index, is_timeout_action=True)
            self.waiting_players_list.remove(index)
            self.action_dict[index] = []
            p.remaining_time = 0
            return data
        return super()._build_timeout_action(index)

    def schedule_bot_actions(self):
        from .bot import play_bot
        for i in self.waiting_players_list:
            if self.player_list[i].is_bot and self.bot_action_ticks.get(i) != self.server_action_tick:
                self.bot_action_ticks[i] = self.server_action_tick
                task = asyncio.create_task(play_bot(self, i, self.server_action_tick))
                self.bot_tasks.add(task)
                task.add_done_callback(self.bot_tasks.discard)
