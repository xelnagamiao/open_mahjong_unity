import random
from collections import Counter

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ...game_calculation.beijing import rules as book
from .actions import ActionPolicy, legal_cuts
from .flow import HandFlow
from .player import BeijingPlayer
from .protocol import Protocol
from .recording import Recording
from .runtime import Runtime
from .settlement import EndOfHand
from .state_machine import Phase as P, StateMachine


class BeijingGameState(HandFlow, Protocol, Recording, Runtime, EndOfHand, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None, db_manager=None, gamestate_id="beijing-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE or len(data["player_list"]) != 4:
            raise ValueError("北京麻将仅开放四人对局")
        config = book.normalize_config(data.get("detailed_config"))
        if data.get("starting_score", 0) != 0 or data.get("hepai_limit", 0) != 0:
            raise ValueError("北京麻将固定0起始分、0翻起和")
        if any(data.get(k, False) for k in ("use_flowers", "open_cuohe", "claim_protection", "tactical_call", "tian_di_ren_he")):
            raise ValueError("北京规则不支持此选项")
        data.update(room_rule="beijing", sub_rule=book.SUB_RULE)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [BeijingPlayer(**vars(p)) for p in self.player_list]
        self.action_policy = ActionPolicy()
        self.rule_version, self.detailed_config = config["rule_version"], config
        self.dead_wall_count, self.hepai_limit = 0, 0
        self.bot_speed = data.get("bot_speed", "normal")
        self.match_finishing, self.next_dealer, self.repeat = False, 0, 0
        self.indicator, self.indicator_position = 11, 53
        self.dice, self.domain_events, self.wall_positions = [], [], []
        self.round_changes, self.round_start_scores = [0] * 4, [0] * 4
        self.round_settlement, self.pending_kong = None, None
        self.dingzhuang_paid, self.payment_ledger = False, []

    @property
    def hun(self):
        return book.hun_tile(self.indicator, self.detailed_config["hun_mode"])

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="beijing-test", room_rule="beijing", sub_rule=book.SUB_RULE,
                    player_list=[101, 102, 103, 104], player_settings={}, game_round=1, round_timer=0,
                    step_timer=0, room_type="custom", tips=False, random_seed=1)

    def initialize_round(self, *, wall=None, dice=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须完成上一副准备阶段")
        self.reset_round_state()
        self.round_settlement, self.pending_kong = None, None
        self.round_changes = [0] * 4
        self.round_start_scores = [p.score for p in self.player_list]
        self.domain_events, self.payment_ledger = [], []
        self.dingzhuang_paid = False
        self._action_clocks, self._action_deadlines = {}, {}
        physical = [t for t in book.TILES for _ in range(4)]
        rng = random.Random(self.round_random_seed)
        self.dice = list(dice or [rng.randint(1, 6) for _ in range(4)])
        if len(self.dice) != 4 or any(type(d) is not int or not 1 <= d <= 6 for d in self.dice):
            raise ValueError("须投掷两次两颗骰子")
        if wall is None:
            rng.shuffle(physical)
            offset = ((sum(self.dice[:2]) - 1) % 4 * 34 + sum(self.dice) * 2) % 136
            wall = physical[offset:] + physical[:offset]
        elif Counter(wall) != Counter(physical):
            raise ValueError("牌墙须为136张四份无花牌组")
        self.tiles_list = list(wall)
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        final, self.tiles_list = self.tiles_list[:6], self.tiles_list[6:]
        self.player_list[0].hand_tiles.extend((final[0], final[4]))
        for i in (1, 2, 3):
            self.player_list[i].hand_tiles.append(final[i])
        self.indicator = final[5]
        for p in self.player_list:
            p.hand_tiles.sort()
        dealer = self.player_list[0]
        dealer.hand_tiles.remove(final[4])
        dealer.hand_tiles.append(final[4])
        dealer.has_draw_slot, dealer.draw_kind = True, "opening"
        self.dealer_index = self.current_player_index = 0
        self.wall_positions = list(range(54, 136))

    def score_win(self, index, source, tile, *, payer=None, four_hun=False):
        p = self.player_list[index]
        hand = p.hand_tiles if source == "self_draw" else [*p.hand_tiles, tile]
        return book.score_hand(hand, p.combination_tiles, indicator=self.indicator, winning_tile=tile,
            config=self.detailed_config, source=source, discarded_hun=p.discarded_hun,
            after_kong=p.after_kong, last_tile=not self.tiles_list,
            heavenly=index == 0 and p.draw_kind == "opening" and not p.combination_tiles,
            earthly=index != 0 and (self.natural_draw_count[index] == 0 if source != "self_draw"
                else self.natural_draw_count[index] == 1 and p.draw_kind == "normal") and not p.discard_origin_tiles,
            tian_ting=p.tian_ting, dealer=index == 0, repeat=self.repeat, four_hun=four_hun)

    def can_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        return (not p.is_hu and "peida" not in p.tag_list and (source != "self_draw" or p.has_draw_slot)
                and bool(self.score_win(index, source, tile, payer=payer)))

    def can_four_hun(self, index):
        p = self.player_list[index]
        return (self.detailed_config["four_hun_win"] and p.has_draw_slot and not p.discarded_hun
                and "peida" not in p.tag_list and p.hand_tiles.count(self.hun) == 4)

    def legal_discard_tiles(self, index):
        return legal_cuts(self, index)

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise RuntimeError("当前不能开始下一副")
        shift = self.next_dealer
        self.player_list = self.player_list[shift:] + self.player_list[:shift]
        for i, p in enumerate(self.player_list):
            p.player_index = i
        self.current_round += int(shift != 0)
        self.repeat = self.repeat + 1 if shift == 0 else 0
        self.round_index += 1
        self.current_player_index = self.dealer_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
