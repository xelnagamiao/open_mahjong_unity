from collections import Counter

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ...game_calculation.duocai import tiles as t
from ...game_calculation.duocai.config import RULE_VERSION, SUB_RULES, normalize_config
from ...game_calculation.duocai.evaluation import evaluate
from ...game_calculation.duocai.furiten import discard_furiten
from ...game_calculation.duocai.scoring import Context
from ...game_calculation.duocai.wall import TwinDecks
from .actions import ActionPolicy, legal_cuts
from .flow import HandFlow
from .player import DuocaiPlayer
from .protocol import Protocol
from .recording import Recording
from .runtime import Runtime
from .settlement import EndOfHand
from .state_machine import Phase as P, StateMachine


class DuocaiGameState(HandFlow, Protocol, Recording, Runtime, EndOfHand, ZhongyongGameState):
    def __init__(self, game_server=None, room_data=None, calculation_service=None, db_manager=None, gamestate_id='duocai-test'):
        data = dict(room_data or self._default_room_data())
        if data.get('sub_rule', SUB_RULES[0]) not in SUB_RULES or len(data['player_list']) != 4:
            raise ValueError('夺彩仅开放四人国标与立直')
        if any(data.get(k, False) for k in ('use_flowers', 'open_cuohe', 'claim_protection', 'tactical_call')):
            raise ValueError('夺彩规则不支持此选项')
        data.update(room_rule='duocai', sub_rule=data.get('sub_rule', SUB_RULES[0]))
        self.machine = StateMachine()
        super().__init__(game_server, data, calculation_service, db_manager, gamestate_id)
        self.player_list = [DuocaiPlayer(**vars(p)) for p in self.player_list]
        for p in self.player_list:
            p.score = 40000
        self.variant = self.sub_rule.split('/')[1]
        self.rule_version, self.detailed_config = RULE_VERSION, normalize_config(data.get('detailed_config'))
        self.action_policy, self.dead_wall_count = ActionPolicy(), 0
        self.hepai_limit = 8 if self.variant == 'guobiao' else 1
        self.decks, self.wall = TwinDecks(self.master_seed), None
        self.bot_speed = data.get('bot_speed', 'normal')
        self.match_finishing, self.next_dealer = False, 0
        self.honba = self.riichi_sticks = 0
        self.domain_events, self.meld_choices = [], {}
        self.round_settlement, self.pending_kong = None, None
        self.round_changes, self.round_start_scores = [0] * 4, [40000] * 4

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self, value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id='duocai-test', room_rule='duocai', sub_rule=SUB_RULES[0],
                    player_list=[101, 102, 103, 104], player_settings={}, game_round=4,
                    round_timer=20, step_timer=5, room_type='custom', tips=False, random_seed=1)

    def initialize_round(self, *, wall=None, dice=None):
        if self.machine.phase != P.START:
            raise RuntimeError('须完成上一副准备阶段')
        self.reset_round_state()
        self.round_settlement = self.pending_kong = None
        self.round_changes = [0] * 4
        self.round_start_scores = [p.score for p in self.player_list]
        self.domain_events, self.meld_choices = [], {}
        self._action_clocks, self._action_deadlines = {}, {}
        self.wall = self.decks.next_wall(dice=dice, order=wall)
        if self.variant == 'riichi':
            self.wall.set_riichi()
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.wall.draw()[0] for _ in range(4))
        for p in self.player_list:
            p.hand_tiles.append(self.wall.draw()[0])
            p.hand_tiles.sort()
        self.player_list[0].hand_tiles.append(self.wall.draw()[0])
        self.player_list[0].has_draw_slot, self.player_list[0].draw_kind = True, 'opening'
        self.current_player_index = self.dealer_index = 0
        self.sync_wall()

    def sync_wall(self):
        self.tiles_list = [self.wall.tiles[i] for i in self.wall.live]

    def context(self, index, source, tile, *, preview=False):
        p = self.player_list[index]
        normal_visible = sum(t.face(v) == t.face(tile) for other in self.player_list for v in other.discard_tiles)
        normal_visible += sum(t.face(v) == t.face(tile) for other in self.player_list for m in other.melds if not m.concealed or other is p for v in m.physical)
        if source == 'discard':
            normal_visible -= 1
        tsumo = source == 'self_draw'
        return Context(tsumo=tsumo, seat_wind=41 + index, round_wind=41 + min(3, (self.current_round - 1) // 4),
            riichi=p.riichi, ippatsu=p.ippatsu, rinshan=tsumo and p.after_kong, chankan=source == 'rob_kong',
            last=not self.wall.live and not (tsumo and p.after_kong),
            tenhou=tsumo and p.draw_kind == 'opening',
            chiihou=tsumo and index != 0 and not p.discard_origin_tiles and not self.opening_flow_interrupted,
            last_copy=bool(t.face(tile) and normal_visible == 3),
            indicators=tuple(self.wall.indicators()), ura_indicators=tuple(self.wall.indicators(True)) if p.riichi and not preview else ())

    def score_win(self, index, source, tile, *, payer=None, preview=False):
        p = self.player_list[index]
        hand = p.hand_tiles if source == 'self_draw' else [*p.hand_tiles, tile]
        return evaluate(hand, p.melds, self.variant, self.context(index, source, tile, preview=preview))

    def can_win(self, index, source, tile, *, payer=None):
        p = self.player_list[index]
        if source == 'self_draw' and (not p.has_draw_slot or not p.hand_tiles or p.hand_tiles[-1] != tile):
            return False
        quote = self.score_win(index, source, tile, payer=payer, preview=True)
        if not quote or not quote.valid:
            return False
        if self.variant == 'riichi' and source != 'self_draw':
            return not (p.temporary_furiten or p.riichi_furiten or discard_furiten(p.hand_tiles, p.melds,
                p.discard_origin_tiles, tile, self.context(index, source, tile, preview=True)))
        return True

    def legal_discard_tiles(self, index):
        return legal_cuts(self, index)

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise RuntimeError('当前不能开始下一副')
        shift = self.next_dealer
        self.player_list = self.player_list[shift:] + self.player_list[:shift]
        for i, p in enumerate(self.player_list):
            p.player_index = i
        self.current_round += int(bool(shift))
        self.round_index += 1
        self.current_player_index = self.dealer_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
