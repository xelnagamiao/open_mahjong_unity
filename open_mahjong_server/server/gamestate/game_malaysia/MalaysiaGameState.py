"""Three-seat authoritative windows over the existing turn-based transport."""
import random
from collections import Counter
from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ...game_calculation.malaysia import rules as R
from .player import MalaysiaPlayer
from .phases import Machine, P
from .flow import HandFlow
from .runtime import Runtime
from .protocol import Protocol
from .recording import Recording
from .settlement import Settlement


class MalaysiaGameState(HandFlow, Runtime, Protocol, Recording, Settlement, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None, db_manager=None, gamestate_id="malaysia-test"):
        data = dict(room_data or self._default_room_data())
        if len(data["player_list"]) != 3 or data.get("sub_rule", R.SUB_RULE) != R.SUB_RULE:
            raise ValueError("马来西亚传统版固定三人")
        if any(data.get(key, False) for key in ("open_cuohe", "claim_protection", "tactical_call", "tian_di_ren_he")) or data.get("use_flowers", True) is not True:
            raise ValueError("不支持的马来西亚房间配置")
        self.machine = Machine()
        data.update(room_rule="malaysia", sub_rule=R.SUB_RULE)
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [MalaysiaPlayer(**vars(p)) for p in self.player_list]
        self.detailed_config = R.normalize_config(data.get("detailed_config"))
        self.rule_version, self.hepai_limit, self.dead_wall_count = R.RULE_VERSION, 5, 0
        self.bot_speed = data.get("bot_speed", "fast")
        self.use_flowers = True
        self.round_settlement = None
        self.round_start_scores, self.round_changes = [0] * 3, [0] * 3
        self.ledger = []
        self.next_dealer = 0
        self.player_entry_order = [p.user_id for p in self.player_list]
        self.match_tier, self.event_id = data.get("match_tier"), data.get("event_id")

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="malaysia-test", player_list=[101, 102, 103], player_settings={},
            game_round=1, step_timer=0, round_timer=0, tips=False, room_type="custom", random_seed=1)

    def initialize_round(self, *, wall=None):
        if self.machine.phase != P.START:
            raise ValueError("上局确认尚未完成")
        self.reset_round_state()
        self.round_random_seed = derive_round_seed(self.master_seed, self.round_index)
        rng = random.Random(self.round_random_seed)
        self.dice = [rng.randint(1, 6) for _ in range(4)]
        if wall is None:
            wall = R.tile_pool()
            rng.shuffle(wall)
            broken = (sum(self.dice[:2]) - 1) % 3
            offset = (broken * 28 + sum(self.dice) * 2) % 84
            wall = wall[offset:] + wall[:offset]
        elif Counter(wall) != Counter(R.tile_pool()):
            raise ValueError("牌墙必须为完整84张")
        self.tiles_list = list(wall)
        self.round_start_scores = [p.score for p in self.player_list]
        self.round_changes, self.ledger, self.domain_events = [0] * 3, [], []
        self.round_settlement = self.pending_kong = self.flower_pending = None
        self.opening, self.opening_interrupted, self.sea_discard = True, False, False
        self.opening_queue = []
        self.current_player_index = self.dealer_index = 0
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        for p in self.player_list:
            p.hand_tiles.append(self.tiles_list.pop(0))
        p = self.player_list[0]
        p.hand_tiles.append(self.tiles_list.pop(0))
        p.last_drawn_tile, p.has_draw_slot, p.draw_kind = p.hand_tiles[-1], True, "normal"

    def score_win(self, i, source, tile, *, automatic=False):
        p = self.player_list[i]
        self_draw = source == "self_draw"
        if not automatic and self_draw and (not p.has_draw_slot or tile is None):
            return None
        if source not in ("self_draw", "discard", "rob_kong"):
            return None
        hand = list(p.hand_tiles) + ([] if self_draw else [tile])
        first = not self.opening_interrupted and not p.discard_origin_tiles and (
            (self_draw and p.natural_draws <= 1) or (source == "discard" and len(self.discard_log) == 1))
        return R.score_hand(hand, p.melds, p.huapai_list, seat=i, winning_tile=tile, self_draw=self_draw,
            after_flower=p.draw_kind == "flower", after_kong=p.draw_kind == "kong",
            consecutive_kongs=p.consecutive_kongs, rob_kong=source == "rob_kong", first_tile=first,
            last_tile=p.last_wall if self_draw else self.sea_discard, opening_four_fly=automatic,
            **self.detailed_config)
