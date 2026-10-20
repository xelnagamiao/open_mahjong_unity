"""Fuzhou authority: a five-meld domain on the shared turn-based transport."""
from collections import Counter
import random
import time

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ...game_calculation.fuzhou import rules as book
from .actions import ActionPolicy, legal_cuts
from .player import FuzhouPlayer
from .state_machine import StateMachine, Phase as P
from .flowers import FlowerFlow
from .flow import HandFlow
from .runtime import Runtime
from .protocol import Protocol
from .recording import Recording
from .settlement import EndOfHand


class FuzhouGameState(FlowerFlow, HandFlow, Runtime, Protocol, Recording, EndOfHand, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None,
                 db_manager=None, gamestate_id="fuzhou-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule", book.SUB_RULE) != book.SUB_RULE:
            raise ValueError("未开放的福州子规则")
        config = book.normalize_config(data.get("detailed_config"))
        if data.get("use_flowers", True) is not True:
            raise ValueError("福州固定使用全部字牌及八花")
        if any(data.get(k, False) for k in ("open_cuohe", "tactical_call", "claim_protection", "tian_di_ren_he")):
            raise ValueError("福州不支持此配置")
        data.update(room_rule="fuzhou", sub_rule=book.SUB_RULE, use_flowers=True)
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        if len(self.player_list) != 4:
            raise ValueError("福州需要四名玩家")
        if self.max_round not in (1,2,4):
            raise ValueError("福州时长只能为一圈、半庄或一雀")
        self.player_list = [FuzhouPlayer(**vars(p)) for p in self.player_list]
        self.player_entry_order = [p.user_id for p in self.player_list]
        self.action_policy = ActionPolicy()
        self.use_flowers, self.dead_wall_count = True, 18
        self.rule_version, self.detailed_config = book.VERSION, config
        # Draw the first dealer independently of the shuffled first-hand wall;
        # the committed master seed makes both choices reproducible.
        self.initial_dealer = random.Random(derive_round_seed(self.master_seed,0)).randrange(4)
        self.dealer_index = self.current_player_index = self.initial_dealer
        self.bot_speed = data.get("bot_speed", "normal")
        self.dealer_streak, self.dealer_changes = 0, 0
        self.match_finishing = False
        self.action_clocks = {}
        self._timing_now = time.monotonic
        self._reset_domain()

    @staticmethod
    def _default_room_data():
        return dict(room_id="fuzhou-test", room_rule="fuzhou", sub_rule=book.SUB_RULE,
                    player_list=[101,102,103,104], player_settings={}, game_round=1,
                    round_timer=0, step_timer=0, room_type="custom", tips=False, random_seed=1)

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed, self.round_index)

    def _reset_domain(self):
        self.jin = None
        self.indicator_claimed = False
        self.opening_replacement = False
        self.dealer_first_discard_pending = True
        self.replacement_pending = self.pending_kong = None
        self.in_last_four = False
        self.sea_queue = []
        self.youjin_owner = self.double_youjin_owner = None
        self.double_youjin_remaining = []
        self.round_changes = [0]*4
        self.round_start_scores = [p.score for p in self.player_list]
        self.round_settlement = None
        self.domain_events, self.dice = [], []

    def initialize_round(self, *, wall=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须完成上局确认阶段")
        self.reset_round_state()
        self._reset_domain()
        self.dead_wall_count = 18
        rng = random.Random(self.round_random_seed)
        self.dice = [rng.randint(1,6), rng.randint(1,6)]
        if wall is None:
            wall = list(book.PHYSICAL)
            rng.shuffle(wall)
            offset = (((self.dealer_index+sum(self.dice)-1) % 4)*36 + sum(self.dice)*2) % 144
            wall = wall[offset:] + wall[:offset]
        elif Counter(wall) != Counter(book.PHYSICAL):
            raise ValueError("牌墙须为完整144张牌组")
        self.tiles_list = list(wall)
        for _ in range(4):
            for shift in range(4):
                p = self.player_list[(self.dealer_index+shift)%4]
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        dealer = self.player_list[self.dealer_index]
        dealer.hand_tiles.append(self.tiles_list.pop(0))
        dealer.has_draw_slot = True
        dealer.draw_kind = "opening"
        self.current_player_index = self.dealer_index

    def update_reserve(self):
        self.dead_wall_count = book.reserved_tiles((p.combination_tiles,p.huapai_list) for p in self.player_list)

    def score_win(self, index, source, tile=None, *, special=None, heavenly=False, qiangjin=False, hand=None):
        p = self.player_list[index]
        cards = list(p.hand_tiles if hand is None else hand)
        if source in ("discard", "rob_kong"):
            cards.append(tile)
        return book.score_hand(cards, p.combination_tiles, p.huapai_list, jin=self.jin,
            base_score=self.detailed_config["base_score"], special=special,
            context=book.WinContext(source=source,winning_tile=tile,dealer_streak=self.dealer_streak,
                after_ready_kong=source=="self_draw" and p.after_ready_kong,
                heavenly=heavenly,qiangjin=qiangjin,golden_middle=p.golden_middle,youjin=p.youjin,
                last_four=self.in_last_four and p.draw_kind=="sea"))

    def can_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        if not self.jin or p.is_hu or source not in ("self_draw","discard","rob_kong"):
            return False
        if source == "self_draw":
            if not p.has_draw_slot or not p.hand_tiles or p.hand_tiles[-1] != tile:
                return False
            if (p.golden_middle or p.youjin) and tile not in p.locked_waits:
                return False
        elif p.discarded_gold or p.golden_middle or self.youjin_owner is not None or tile == self.jin:
            return False
        cards = p.hand_tiles if source=="self_draw" else p.hand_tiles+[tile]
        return book.is_complete(cards,p.combination_tiles,jin=self.jin)

    def can_flower_win(self, index):
        p = self.player_list[index]
        from ..public.ai.auto_cut_policy import is_auto_cut_bot
        if is_auto_cut_bot(p):
            return False
        return bool(self.jin and not p.is_hu and p.has_draw_slot and p.hand_tiles
                    and p.draw_kind not in ("", "opening") and len(p.huapai_list) >= 20
                    and self.score_win(index,"self_draw",p.hand_tiles[-1],special="flower") is not None)

    def can_three_gold(self, index):
        p = self.player_list[index]
        if not self.jin or p.is_hu or not p.has_draw_slot or p.draw_kind=="opening" or p.hand_tiles.count(self.jin) != 3:
            return False
        if (p.golden_middle or p.youjin) and p.hand_tiles[-1] not in p.locked_waits and not self.can_flower_win(index):
            return False
        return self.score_win(index,"self_draw",p.hand_tiles[-1],special="three_gold") is not None

    def legal_discard_tiles(self, index):
        return legal_cuts(self,index)

    def physical_inventory(self):
        cards = list(self.tiles_list)
        if self.jin is not None and not self.indicator_claimed:
            cards.append(self.jin)
        for p in self.player_list:
            cards += p.hand_tiles+p.huapai_list+p.discard_tiles+p.won_tiles
            cards += [t for m in p.combination_tiles for t in book.meld_tiles(m)]
        return Counter(cards)
