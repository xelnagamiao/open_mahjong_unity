"""Wenzhou domain on the existing authoritative action/transport shell."""

import random
from collections import Counter, OrderedDict

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ...game_calculation.wenzhou.rules import RULE_VERSION, SUB_RULE, hand_multiplier, normalize_config, score_hand
from .actions import ActionPolicy
from .flow import HandFlow
from .player import WenzhouPlayer
from .protocol import Protocol
from .recording import Recording
from .session import Session
from .settlement import EndOfHand
from .state_machine import Phase as P, StateMachine

TILES = tuple(s * 10 + n for s in (1,2,3) for n in range(1,10)) + tuple(range(41,48))


class WenzhouGameState(HandFlow, Protocol, Recording, Session, EndOfHand, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None,
                 db_manager=None, gamestate_id="wenzhou-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", SUB_RULE) != SUB_RULE:
            raise ValueError("未开放的温州麻将子规则")
        config = normalize_config(data.get("detailed_config"))
        if any(data.get(key, False) for key in ("open_cuohe", "tactical_call", "claim_protection", "use_flowers", "tian_di_ren_he")):
            raise ValueError("温州 MIL 规则不支持此选项")
        data.update(room_rule="wenzhou", sub_rule=SUB_RULE, use_flowers=False)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [WenzhouPlayer(**vars(p)) for p in self.player_list]
        self.action_policy = ActionPolicy()
        self.rule_version, self.detailed_config = RULE_VERSION, config
        self.use_flowers, self.dead_wall_count = False, 4
        self.bot_speed = data.get("bot_speed", "normal")
        self.match_finishing = False
        self.dealer_streak = 0
        self.next_dealer_shift, self.next_dealer_streak, self.dealer_changes = 0, 0, 0
        self.next_dealer_dice = []
        self.round_start_scores = [p.score for p in self.player_list]
        self.round_changes, self.caishen_changes = [0]*4, [0]*4
        self.caishen_counts = []
        self.round_settlement = self.pending_kong = None
        self.ledger, self.domain_events, self.dice = [], [], []
        self.caishen = self.indicator = 46
        self.indicator_index = 0
        self._wait_cache = OrderedDict()

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="wenzhou-test", room_rule="wenzhou", sub_rule=SUB_RULE,
                    player_list=[101,102,103,104], player_settings={}, game_round=1,
                    round_timer=0, step_timer=0, room_type="custom", tips=False, random_seed=1)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed, self.round_index)

    def initialize_round(self, *, wall=None, dice=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须先结束上局的确认阶段")
        physical = [t for t in TILES for _ in range(4)]
        if wall is not None and Counter(wall) != Counter(physical):
            raise ValueError("牌墙须为136张无花完整牌组")
        if dice is not None and (len(dice) != 4 or any(type(d) is not int or not 1 <= d <= 6 for d in dice)):
            raise ValueError("两次掷骰必须是四个1至6点数")
        self.reset_round_state()
        self.round_start_scores = [p.score for p in self.player_list]
        self.round_changes, self.caishen_changes, self.caishen_counts = [0]*4, [0]*4, []
        self.round_settlement = self.pending_kong = None
        self.ledger, self.domain_events, self.next_dealer_dice = [], [], []
        self._wait_cache.clear()
        self._recorded_waits = {}
        rng = random.Random(self.round_random_seed)
        self.dice = list(dice) if dice is not None else [rng.randint(1,6) for _ in range(4)]
        if wall is None:
            rng.shuffle(physical)
        else:
            physical = list(wall)
        first, second = sum(self.dice[:2]), sum(self.dice[2:])
        thrower = (first-1) % 4
        self.indicator_index = thrower*34 + 2*(first-1)
        self.caishen = self.indicator = physical[self.indicator_index]
        break_seat = (thrower+second-1) % 4
        offset = (break_seat*34 + second*2) % 136
        # Record absolute wall identity before rotating or removing the indicator.
        order = list(range(offset,136)) + list(range(offset))
        self.tiles_list = [physical[i] for i in order if i != self.indicator_index]
        for _ in range(4):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        for p in self.player_list:
            p.hand_tiles.sort()
        self.player_list[0].hand_tiles.append(self.tiles_list.pop(0))
        self.player_list[0].has_draw_slot = True
        self.player_list[0].draw_kind = "normal"
        self.dealer_index = self.current_player_index = 0

    def score_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        hand = list(p.hand_tiles)
        if source != "self_draw":
            hand.append(tile)
        return score_hand(hand, p.meld_records, caishen=self.caishen,
                          winning_tile=tile, self_draw=source == "self_draw")

    def can_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        if p.is_hu or source not in ("self_draw", "discard", "rob_kong"):
            return False
        if source == "self_draw" and not p.has_draw_slot:
            return False
        multiplier = self.win_multiplier(index, source, tile)
        return multiplier is not None and (source == "self_draw" or multiplier > p.passed_win_multiplier)

    def win_multiplier(self, index, source, tile):
        """Action checks need a value; build decomposition witnesses at settlement."""
        p = self.player_list[index]
        hand = list(p.hand_tiles)
        if source != "self_draw":
            hand.append(tile)
        return hand_multiplier(hand, p.meld_records, caishen=self.caishen,
                               winning_tile=tile, self_draw=source == "self_draw")

    def legal_discard_tiles(self, index):
        return set(self.player_list[index].hand_tiles)

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise RuntimeError("当前不能开始下一局")
        shift = self.next_dealer_shift
        self.player_list = self.player_list[shift:] + self.player_list[:shift]
        self.current_round = self.dealer_changes+1
        self.dealer_streak = self.next_dealer_streak
        for i,p in enumerate(self.player_list):
            p.player_index = i
        self.round_index += 1
        self.dealer_index = self.current_player_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
