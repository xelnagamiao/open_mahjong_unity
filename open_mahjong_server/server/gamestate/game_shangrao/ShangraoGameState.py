import random
from collections import Counter

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ...game_calculation.shangrao import rules as book
from .actions import ActionPolicy, legal_cuts
from .flow import HandFlow
from .player import ShangraoPlayer
from .protocol import Protocol
from .recording import Recording
from .runtime import Runtime
from .settlement import EndOfHand
from .state_machine import Phase as P, StateMachine
from .wall import Wall


class ShangraoGameState(HandFlow, Wall, Protocol, Recording, Runtime, EndOfHand, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None, db_manager=None, gamestate_id="shangrao-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE or len(data["player_list"]) != 4:
            raise ValueError("上饶麻将仅开放四人对局")
        config = book.normalize_config(data.get("detailed_config"))
        if any(data.get(k, False) for k in ("use_flowers", "open_cuohe", "claim_protection", "tactical_call")):
            raise ValueError("上饶规则不支持此选项")
        data.update(room_rule="shangrao", sub_rule=book.SUB_RULE)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [ShangraoPlayer(**vars(p)) for p in self.player_list]
        self.action_policy = ActionPolicy()
        self.rule_version, self.detailed_config = config["rule_version"], config
        self.dead_wall_count, self.hepai_limit = 1, 1
        self.tactical_call = False
        self._tactical_recheck_active = self._tactical_in_application = False
        self.bot_speed = data.get("bot_speed", "normal")
        self.match_finishing, self.next_dealer = False, 0
        self.indicator, self.indicator_index, self.indicator_position = 11, -1, -1
        self.dice, self.domain_events, self.wall_positions, self.burnt_tiles = [], [], [], []
        self.burnt_river_indices = [[] for _ in range(4)]
        self.round_changes, self.round_start_scores = [0] * 4, [0] * 4
        self.round_settlement, self.payment_ledger = None, []

    @property
    def bao(self):
        return book.bao_tile(self.indicator)

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="shangrao-test", room_rule="shangrao", sub_rule=book.SUB_RULE,
                    player_list=[101, 102, 103, 104], player_settings={}, game_round=1, round_timer=0,
                    step_timer=0, room_type="custom", tips=False, random_seed=1)

    def initialize_round(self, *, wall=None, dice=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须完成上一副准备阶段")
        self.reset_round_state()
        self.round_settlement = None
        self.round_changes = [0] * 4
        self.round_start_scores = [p.score for p in self.player_list]
        self.domain_events, self.payment_ledger, self.burnt_tiles = [], [], []
        self.burnt_river_indices = [[] for _ in range(4)]
        self._action_clocks, self._action_deadlines = {}, {}
        physical = [t for t in book.TILES for _ in range(4)]
        rng = random.Random(self.round_random_seed)
        self.dice = list(dice or [rng.randint(1, 6), rng.randint(1, 6)])
        if len(self.dice) != 2 or any(type(d) is not int or not 1 <= d <= 6 for d in self.dice):
            raise ValueError("开牌骰点无效")
        if wall is None:
            rng.shuffle(physical)
            offset = ((sum(self.dice) - 1) % 4 * 34 + min(self.dice) * 2) % 136
            wall = physical[offset:] + physical[:offset]
        elif Counter(wall) != Counter(physical):
            raise ValueError("牌墙须为136张四份无花牌组")
        self.tiles_list = list(wall)
        self.indicator_position = 136 - 2 * sum(self.dice)
        self.indicator = self.tiles_list[self.indicator_position]
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        final, self.tiles_list = self.tiles_list[:5], self.tiles_list[5:]
        self.player_list[0].hand_tiles.extend((final[0], final[4]))
        for i in (1, 2, 3):
            self.player_list[i].hand_tiles.append(final[i])
        for p in self.player_list:
            p.hand_tiles.sort()
        dealer = self.player_list[0]
        dealer.hand_tiles.remove(final[4])
        dealer.hand_tiles.append(final[4])
        dealer.has_draw_slot, dealer.draw_kind = True, "opening"
        self.dealer_index = self.current_player_index = 0
        self.wall_positions = list(range(53, 136))
        self.indicator_index = self.wall_positions.index(self.indicator_position)

    def score_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        if source != "self_draw" or payer is not None:
            return None
        return book.score_hand(p.hand_tiles, p.combination_tiles, indicator=self.indicator,
            winning_tile=tile, config=self.detailed_config, flying_bao=p.discard_origin_tiles.count(self.bao),
            flying_seven_stars=p.flying_seven_stars, after_kong=p.after_kong, opening=p.draw_kind == "opening")

    def can_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        return (not p.is_hu and p.has_draw_slot and "peida" not in p.tag_list
                and bool(self.score_win(index, source, tile, payer=payer)))

    def legal_discard_tiles(self, index):
        return legal_cuts(self, index)

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise RuntimeError("当前不能开始下一副")
        shift = self.next_dealer
        self.player_list = self.player_list[shift:] + self.player_list[:shift]
        for i, p in enumerate(self.player_list):
            p.player_index = i
        self.current_round += 1
        self.round_index += 1
        self.current_player_index = self.dealer_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
