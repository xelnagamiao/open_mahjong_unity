"""MIL Hangzhou policies on the existing queued turn-based transport shell."""

import random
from collections import Counter

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ...game_calculation.hangzhou.rules import (
    RULE_VERSION, SUB_RULE, TILES, WinContext, evaluate_win, normalize_config, score_ten_winds,
)
from .actions import ActionPolicy, legal_cuts
from .flow import HandFlow
from .player import HangzhouPlayer
from .protocol import Protocol
from .recording import Recording
from .runtime import Runtime
from .settlement import EndOfHand
from .tactical import Tactical
from .state_machine import Phase as P, StateMachine


class HangzhouGameState(HandFlow, Protocol, Recording, Tactical, Runtime, EndOfHand, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None,
                 db_manager=None, gamestate_id="hangzhou-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", SUB_RULE) != SUB_RULE:
            raise ValueError("未开放的杭州麻将子规则")
        config = normalize_config(data.get("detailed_config"))
        if any(data.get(key, False) for key in ("use_flowers", "open_cuohe", "claim_protection", "tian_di_ren_he")):
            raise ValueError("杭州 MIL 标准规不支持此选项")
        if len(data["player_list"]) != 4:
            raise ValueError("杭州麻将必须四人开局")
        data.update(room_rule="hangzhou", sub_rule=SUB_RULE)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [HangzhouPlayer(**vars(p)) for p in self.player_list]
        self.tactical_call = bool(data.get("tactical_call", False))
        self.tactical_commit_lock = False  # MIL 6-4/6: changed circumstances allow upgrading a declaration.
        from ..public.tactical_claim import TACTICAL_GRACE_SECONDS
        self.tactical_grace_seconds = TACTICAL_GRACE_SECONDS
        self.tactical_pre_grace_delay = 0.5
        self._tactical_recheck_active = self._tactical_in_application = False
        self._tactical_initial_submission = None
        self.action_priority = {"pass": 0, "force_pass": 0, "chi_left": 1, "chi_mid": 1,
                                "chi_right": 1, "peng": 2, "gang": 2, "hu_self": 3}
        self.sync_tactical_enabled()
        if room_data is not None and not self.tactical_call:
            room_data["tactical_call"] = False
        self.action_policy = ActionPolicy()
        self.dead_wall_count = 20
        self.rule_version = RULE_VERSION
        self.detailed_config = config
        self.bot_speed = data.get("bot_speed", "normal")
        self.match_finishing = False
        self.next_dealer = 0
        self.dealer_streak = 1
        self.round_start_scores = [p.score for p in self.player_list]
        self.round_changes = [0] * 4
        self.round_settlement = None
        self.cai_discard_locks = set()
        self.burned_tiles = []
        self.domain_events = []
        self.dice = []
        self._recorded_hint_keys = {}
        self._prepared_hints = {}
        self._async_hints_enabled = False

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="hangzhou-test", room_rule="hangzhou", sub_rule=SUB_RULE,
                    player_list=[101, 102, 103, 104], player_settings={}, game_round=1,
                    round_timer=0, step_timer=0, room_type="custom", tips=False, random_seed=1)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed, self.round_index)

    def initialize_round(self, *, wall=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须先结束上局的确认阶段")
        self.reset_round_state()
        self._action_clocks, self._action_deadlines = {}, {}
        self.round_settlement = None
        self.round_changes = [0] * 4
        self.round_start_scores = [p.score for p in self.player_list]
        self.cai_discard_locks = set()
        self.burned_tiles, self.domain_events = [], []
        self._recorded_hint_keys = {}
        self._prepared_hints = {}
        physical = [t for t in TILES for _ in range(4)]
        rng = random.Random(self.round_random_seed)
        self.dice = [rng.randint(1, 6), rng.randint(1, 6)]
        if wall is None:
            rng.shuffle(physical)
            offset = ((sum(self.dice) - 1) % 4 * 34 + min(self.dice) * 2) % 136
            wall = physical[offset:] + physical[:offset]
        elif Counter(wall) != Counter(physical):
            raise ValueError("牌墙须为136张、含七种字牌且无花的完整牌组")
        self.tiles_list = list(wall)
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        final, self.tiles_list = self.tiles_list[:5], self.tiles_list[5:]
        for index, offset in enumerate((0, 1, 3, 4)):
            self.player_list[index].hand_tiles.append(final[offset])
            self.player_list[index].hand_tiles.sort()
        dealer = self.player_list[0]
        dealer.pre_draw_tiles = list(dealer.hand_tiles)
        dealer.hand_tiles.append(final[2])
        dealer.has_draw_slot = True
        dealer.draw_kind = "normal"
        self.dealer_index = self.current_player_index = 0

    def score_win(self, index, source, tile):
        p = self.player_list[index]
        if source == "ten_winds":
            return score_ten_winds(p.hand_tiles, p.discard_origin_tiles, p.combination_tiles,
                                   had_meld_action=p.had_meld_action)
        if source != "self_draw":
            return None
        return evaluate_win(p.hand_tiles, p.combination_tiles, tile,
            WinContext(self_drawn=True, after_kong=p.after_kong,
                       cai_piao_count=p.cai_piao_count if p.drawn_after_cai else 0,
                       pre_draw_tiles=tuple(p.pre_draw_tiles)))

    def can_win(self, index, source, tile):
        p = self.player_list[index]
        if p.is_hu or source not in ("self_draw", "ten_winds"):
            return False
        if source == "self_draw" and not p.has_draw_slot:
            return False
        return self.score_win(index, source, tile) is not None

    def legal_discard_tiles(self, index):
        return legal_cuts(self, index)

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise RuntimeError("当前不能开始下一局")
        rotation = self.next_dealer
        self.player_list = self.player_list[rotation:] + self.player_list[:rotation]
        self.dealer_streak = 1 if rotation else min(3, self.dealer_streak + 1)
        for index, p in enumerate(self.player_list):
            p.player_index = index
        self.current_round += 1
        self.round_index += 1
        self.dealer_index = self.current_player_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
