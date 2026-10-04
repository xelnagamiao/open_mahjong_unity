"""Hong Kong match data and scoring context, separate from action mutation."""

from dataclasses import asdict
import random

from .state_machine import HongKongStateMachine, HongKongPhase as P
from .player import HongKongPlayer
from .action_check import HongKongActionPolicy, legal_discards, ready_discards
from ...game_calculation.hongkong import HongKongRules, HandContext, score_hand, structural_waits
from ...game_calculation.hongkong.models import TILES, FLOWERS, DRAGONS, PROFILES
from ...game_calculation.hongkong.ledger import PullLedger
from ..public.random_seed_manager import derive_round_seed


class HongKongCore:
    def __init__(self,game_server=None,room_data=None,calculation_service=None,db_manager=None,gamestate_id="hongkong-test"):
        data = dict(room_data or self._default_room_data())
        data["room_rule"] = "hongkong"
        data.setdefault("sub_rule",PROFILES[0])
        self.rules = HongKongRules.from_room(data)
        self.machine = HongKongStateMachine()
        super().__init__(game_server,data,calculation_service,db_manager,gamestate_id)
        self.player_list = [HongKongPlayer(**vars(p)) for p in self.player_list]
        for p in self.player_list:
            p.score = self.rules.starting_score
        self.action_policy = HongKongActionPolicy()
        self.dead_wall_count = 0
        self.hepai_limit = self.rules.minimum_fan
        self.detailed_config = asdict(self.rules)
        self.detailed_config["rule_version"] = self.rules.version
        self.ledger = PullLedger()
        self.dealer_streak = 0
        self.round_start_scores = [p.score for p in self.player_list]
        # Stable player identities survive dealer rotation. This state persists
        # across hands, including repeated-dealer hands.
        self.negative_score_grace = set()
        self.immediate_changes = [0]*4
        self.pending_kong = None
        self.flower_pending = None
        self.replacement_pending = None
        self.initial_flower_rounds = []
        self.opening_flower_stage = False
        self.flower_awards = set()
        self.discard_log = []
        self.match_finishing = False
        self.domain_events = []
        self.meld_actors = []
        self.won_tiles = []
        self._remix_circuit_ends = False

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self,value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="hongkong-test",room_rule="hongkong",sub_rule=PROFILES[0],
                    player_list=[101,102,103,104],player_settings={},game_round=1,
                    round_timer=0,step_timer=0,room_type="custom",tips=False,random_seed=1)

    def _derive_round_seed(self):
        # Repeated dealer hands must never replay the same wall.
        return derive_round_seed(self.master_seed,self.round_index)

    def initialize_round(self,*,wall=None):
        if self.machine.phase != P.STARTING:
            raise RuntimeError("A new hand requires the round-ready transition")
        self.reset_round_state()
        self.dealer_index = 0
        self.current_player_index = 0
        self.pending_kong = None
        self.flower_pending = None
        self.replacement_pending = None
        self.initial_flower_rounds = []
        self.opening_flower_stage = True
        self.flower_awards = set()
        self.immediate_changes = [0]*4
        self.round_start_scores = [p.score for p in self.player_list]
        self.domain_events = []
        self.meld_actors = []
        self.won_tiles = []
        self._remix_circuit_ends = False
        if wall is None:
            wall = [t for t in TILES for _ in range(4)] + (list(FLOWERS) if self.rules.flowers else [])
            random.Random(self.round_random_seed).shuffle(wall)
        else:
            from collections import Counter
            expected = Counter({t:4 for t in TILES})
            if self.rules.flowers:
                expected.update(FLOWERS)
            if Counter(wall) != expected:
                raise ValueError("A scripted wall must contain the exact physical tile set")
            wall = list(wall)
        self.tiles_list = wall
        # Deal in four-tile packets as specified by both HKMA books.
        packets = 4 if self.rules.is_sixteen else 3
        for _ in range(packets):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        if not self.rules.is_sixteen:
            # East's first and fifth tiles are the traditional jump deal.
            self.player_list[0].hand_tiles.append(self.tiles_list.pop(0))
            for p in self.player_list[1:]:
                p.hand_tiles.append(self.tiles_list.pop(0))
            self.player_list[0].hand_tiles.append(self.tiles_list.pop(0))
            self.player_list[0].has_draw_slot = True
        for p in self.player_list:
            if p.has_draw_slot:
                p.hand_tiles[:] = sorted(p.hand_tiles[:-1])+p.hand_tiles[-1:]
            else:
                p.hand_tiles.sort()
        # Ordinary initial cards, including flowers, are recorded before the
        # observable replacement sequence. No silent pre-resolution of a flower win.

    def score_win(self,index,source,tile,*,payer=None,multi=1,flower=""):
        p = self.player_list[index]
        self_draw = source == "self_draw"
        hand = tuple(p.hand_tiles) + (() if self_draw or flower else (tile,))
        first_discard = source == "discard" and len(self.discard_log)==1 and payer==0
        opening = not self.opening_flow_interrupted
        earthly = opening and index != 0 and first_discard
        human = opening and len(self.discard_log)<=4 and not earthly and not (index==0 and not self.discard_log)
        if not self.rules.is_sixteen:
            human = human and not self.rules.is_old and index != 0
        if self.rules.is_lianhuise:
            human = opening and self_draw and index != 0 and p.draw_count == 1
        if self.rules.is_remix:
            earthly = opening and self_draw and index != 0 and p.draw_count == 1
            human = opening and source == "discard" and len(self.discard_log) <= 4
        river_count = sum(len(player.discard_tiles) for player in self.player_list)
        if source == "discard":
            river_count = max(0,river_count-1)
        dealer_involved = index==0 or (not self_draw and payer==0)
        context = HandContext(hand,tuple(p.combination_tiles),tile or 0,self_draw,
            seat_wind=41+index,round_wind=41+(self.current_round-1)//4%4,
            # Flower wins are offered before the player chooses to replace the
            # last flower. Include that physical flower without moving it early.
            flowers=tuple(p.huapai_list)+tuple(t for t in p.hand_tiles if flower and t in FLOWERS),
            rob_kong=source=="rob_kong",
            last_tile=not self.tiles_list,replacement=p.replacement_kind if self_draw else "",
            consecutive_kongs=p.consecutive_kongs,tail_draws=p.tail_draws,
            heavenly=self_draw and index==0 and not self.discard_log and opening,
            earthly=earthly,humanly=human,ready=p.ready_kind if p.declared_ready else "",
            immediate=p.declared_ready and p.immediate_ready,river_count=river_count,
            multi_ron=multi,dealer=dealer_involved,dealer_streak=self.dealer_streak,flower_win=flower)
        return score_hand(context,self.rules)

    def can_win(self,index,source,tile,*,payer=None,flower=""):
        p = self.player_list[index]
        if p.is_hu:
            return False
        if self.rules.self_draw_only and source != "self_draw":
            return False
        if self.rules.is_remix and source != "self_draw" and tile in p.discard_origin_tiles:
            return False
        if self.rules.is_sixteen and (p.water or p.permanent_water):
            return False
        if not self.rules.is_sixteen and not self.rules.is_lianhuise and source != "self_draw" and not flower and tile in p.discard_win_lockout_tiles:
            return False
        quote = self.score_win(index,source,tile,payer=payer,flower=flower)
        if self.rules.is_lianhuise and source != "self_draw" and quote.raw_fan <= p.passed_win_fan:
            return False
        return quote.is_win

    def would_assume_liability(self,index,action,tile):
        p = self.player_list[index]
        if self.rules.is_sixteen:
            return False
        if self.rules.is_remix:
            # The supplying seat is needed to determine all-four-from-one;
            # remix liability is assessed after the exact claim is committed.
            return False
        if self.rules.use_dragon_liability and action in ("peng","gang") and tile in DRAGONS:
            dragons = {int(c[1:]) for c in p.combination_tiles if c[0] in "kgG" and int(c[1:]) in DRAGONS}
            if len(dragons)==2 and tile not in dragons:
                return True
        if self.rules.use_twelve_liability and len(p.combination_tiles)==3:
            if self.rules.is_lianhuise and action != "gang":
                # The winner discarded this same tile in the current circuit,
                # before a follower supplied it. Earlier circuits do not exempt it.
                own = next((n for n in range(len(self.discard_log)-2,-1,-1) if self.discard_log[n][0]==index),None)
                if own is not None and len(self.discard_log)-1-own < 4 and self.discard_log[own][1]==tile:
                    return False
            return True
        return False

    def remember_pass(self,index,tile,*,source="discard",payer=None):
        p = self.player_list[index]
        if self.rules.is_remix and source == "self_draw":
            return
        if self.rules.is_sixteen:
            p.water = True
            if p.declared_ready:
                p.permanent_water = True
        elif self.rules.is_lianhuise:
            p.passed_win_fan = max(p.passed_win_fan, self.score_win(index,source,tile,payer=payer).raw_fan)
        else:
            p.discard_win_lockout_tiles.add(tile)

    def begin_new_draw(self,index,*,normal):
        p = self.player_list[index]
        p.forbidden_discards.clear()
        if normal:
            p.water = False
            p.passed_claim_tiles.clear()
            p.discarded_since_draw.clear()
            p.replacement_kind = ""
            p.tail_draws = 0
            p.consecutive_kongs = 0
            p.draw_count += 1
            self.natural_draw_count[index] = p.draw_count
        if normal or (self.rules.is_lianhuise and not self.opening_flower_stage):
            p.passed_win_fan = -1
            p.passed_claim_tiles.clear()
        if normal:
            p.kong_liability_payer = None
        if (normal or not self.rules.is_sixteen) and not self.rules.is_remix:
            p.discard_win_lockout_tiles.clear()

    def dealer_keeps_after_hand(self, winners):
        if not self.rules.repeat_dealer:
            return False
        if winners:
            return 0 in winners
        if self.rules.is_remix and self.rules.dealer_mode == "default":
            dealer = self.player_list[0]
            return bool(structural_waits(dealer.hand_tiles,dealer.combination_tiles,self.rules))
        return True

    def interrupt_opening(self):
        self.opening_flow_interrupted = True
        for p in self.player_list:
            p.immediate_ready = False
            if p.initial_seven_pending:
                p.flower_choice_declined.add("seven")
            p.initial_seven_pending = False

    def legal_discard_tiles(self,index):
        return legal_discards(self,index)

    def _window(self,phase,player=None,*,tile=None,actions=None,**extra):
        self.machine.transition(phase)
        return dict(status=phase.value,player=player,tile=tile,actions=actions or {i:[] for i in range(4)},**extra)

    def _event(self,action,actor,**details):
        event = dict(action=action,player=actor,**details)
        self.domain_events.append(event)
        self.emit_visible_action_payloads(event,reveal_final=self.machine.phase==P.END)

    def _build_timeout_action(self,index):
        actions = self.action_dict.get(index,[])
        if "buhua" in actions:
            self.action_dict[index] = []
            if index in self.waiting_players_list:
                self.waiting_players_list.remove(index)
            self.player_list[index].remaining_time = 0
            return dict(player_index=index,action_type="buhua")
        if "cut" not in actions:
            return super()._build_timeout_action(index)
        p = self.player_list[index]
        legal = self.legal_discard_tiles(index)
        if not legal:
            raise RuntimeError("Discard window has no legal discard")
        chosen = next(i for i in range(len(p.hand_tiles)-1,-1,-1) if p.hand_tiles[i] in legal)
        tile = p.hand_tiles[chosen]
        self.action_dict[index] = []
        if index in self.waiting_players_list:
            self.waiting_players_list.remove(index)
        p.remaining_time = 0
        return dict(player_index=index,action_type="cut",TileId=tile,target_tile=None,cutIndex=chosen,
                    cutClass=p.has_draw_slot and chosen==len(p.hand_tiles)-1,is_timeout_action=True)
