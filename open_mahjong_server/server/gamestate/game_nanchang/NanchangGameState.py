import random
from collections import Counter
from functools import lru_cache

from ..game_zhongyong.ZhongyongGameState import ZhongyongGameState
from ..public.random_seed_manager import derive_round_seed
from ...game_calculation.nanchang import rules as book
from .actions import ActionPolicy, legal_cuts
from .flow import HandFlow
from .player import NanchangPlayer
from .protocol import Protocol
from .recording import Recording
from .runtime import Runtime
from .settlement import EndOfHand
from .state_machine import Phase as P, StateMachine


class NanchangGameState(HandFlow,Protocol,Recording,Runtime,EndOfHand,ZhongyongGameState):
    def __init__(self,game_server=None,room_data=None,calculation_service=None,db_manager=None,gamestate_id="nanchang-test"):
        data = dict(room_data or self._default_room_data())
        if data.get("sub_rule",book.SUB_RULE) != book.SUB_RULE or len(data["player_list"]) != 4:
            raise ValueError("南昌MIL2026仅开放四人单打")
        config = book.normalize_config(data.get("detailed_config"))
        if any(data.get(k,False) for k in ("use_flowers","open_cuohe","claim_protection","tian_di_ren_he")):
            raise ValueError("南昌标准规则不支持此选项")
        data.update(room_rule="nanchang",sub_rule=book.SUB_RULE)
        self.machine = StateMachine()
        super().__init__(game_server,data,calculation_service,db_manager,gamestate_id)
        self.player_list = [NanchangPlayer(**vars(p)) for p in self.player_list]
        self.action_policy = ActionPolicy()
        self.rule_version,self.detailed_config = book.RULE_VERSION,config
        self.dead_wall_count,self.hepai_limit = 0,1
        self.tactical_call = False
        self._tactical_recheck_active = self._tactical_in_application = False
        self.bot_speed = data.get("bot_speed","normal")
        self.match_finishing = False
        self.next_dealer = 0
        self.dealer_streak = 0
        self.indicator,self.indicator_index = 11,-1
        self.dice,self.jing_dice,self.domain_events = [],[],[]
        self.round_changes,self.round_start_scores = [0]*4,[0]*4
        self.round_settlement = None
        self.kong_ledger,self.jing_transfers = [],[]
        self._wait_cache = {}

    @property
    def game_status(self):
        return self.machine.phase.value

    @game_status.setter
    def game_status(self,value):
        self.machine.transition(value)

    @staticmethod
    def _default_room_data():
        return dict(room_id="nanchang-test",room_rule="nanchang",sub_rule=book.SUB_RULE,
                    player_list=[101,102,103,104],player_settings={},game_round=1,round_timer=0,
                    step_timer=0,room_type="custom",tips=False,random_seed=1)

    def _derive_round_seed(self):
        return derive_round_seed(self.master_seed,self.round_index)

    def initialize_round(self,*,wall=None,jing_dice=None):
        if self.machine.phase != P.START:
            raise RuntimeError("须完成上一副准备阶段")
        self.reset_round_state()
        self.round_settlement = None
        self.round_changes = [0]*4
        self.round_start_scores = [p.score for p in self.player_list]
        self.domain_events,self.kong_ledger,self.jing_transfers = [],[],[]
        self._wait_cache = {}
        self.tail_upper_next = True
        self._action_clocks,self._action_deadlines = {},{}
        physical = [t for t in book.TILES for _ in range(4)]
        rng = random.Random(self.round_random_seed)
        self.dice = [rng.randint(1,6),rng.randint(1,6)]
        if wall is None:
            rng.shuffle(physical)
            offset = ((sum(self.dice)-1)%4*34+min(self.dice)*2)%136
            wall = physical[offset:]+physical[:offset]
        elif Counter(wall) != Counter(physical):
            raise ValueError("牌墙须为136张四份无花牌组")
        self.tiles_list = list(wall)
        for _ in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4])
                del self.tiles_list[:4]
        final,self.tiles_list = self.tiles_list[:5],self.tiles_list[5:]
        self.player_list[0].hand_tiles.extend((final[0],final[4]))
        for i in (1,2,3):
            self.player_list[i].hand_tiles.append(final[i])
        self.player_list[0].has_draw_slot = True
        self.player_list[0].draw_kind = "normal"
        self.dealer_index = self.current_player_index = 0
        self.jing_dice = list(jing_dice or [rng.randint(1,6),rng.randint(1,6)])
        if len(self.jing_dice) != 2 or any(type(d) is not int or not 1 <= d <= 6 for d in self.jing_dice):
            raise ValueError("翻精骰点无效")
        self.indicator_index = len(self.tiles_list)-2*sum(self.jing_dice)
        self.indicator = self.tiles_list[self.indicator_index]

    def score_win(self,index,source,tile,*,payer=None,hand=None,discarded=None,private=False,preview=False):
        p = self.player_list[index]
        own = list(p.hand_tiles if hand is None else hand)
        if source != "self_draw":
            own.append(tile)
        if tile not in own:
            return None
        other_has_jing = True if private else any(any(t in book.jing_tiles(self.indicator) for t in q.hand_tiles)
                                                 for i,q in enumerate(self.player_list) if i != index)
        heavenly = not preview and index == 0 and source == "self_draw" and not self.domain_events
        earthly = not preview and source == "discard" and payer == 0 and len(self.discard_log) == 1 and all(not q.combination_tiles for q in self.player_list)
        return book.score_hand(own,p.combination_tiles,indicator=self.indicator,winning_tile=tile,
            self_draw=source == "self_draw",discarded=p.discard_origin_tiles if discarded is None else discarded,
            other_has_jing=other_has_jing,declared_jingdiao=p.declared_jingdiao,after_kong=not preview and p.after_kong,
            heavenly=heavenly,earthly=earthly,winner=index,dealer=0,payer=payer)

    def jingdiao_floor(self,index,hand=None,discarded=None):
        p = self.player_list[index]
        own = p.hand_tiles if hand is None else hand
        key = (tuple(sorted(own)),tuple(p.combination_tiles),tuple(p.discard_origin_tiles if discarded is None else discarded),self.indicator,index)
        cache = self.__dict__.setdefault("_floor_cache",{})
        if key in cache:
            return cache[key]
        values = []
        for t in book.TILES:
            quote = book.score_hand(list(own)+[t],p.combination_tiles,indicator=self.indicator,
                winning_tile=t,self_draw=False,discarded=p.discard_origin_tiles if discarded is None else discarded,
                other_has_jing=True,winner=index,dealer=0,payer=(index+1)%4)
            if quote:
                values.append(min(6,quote["qualifying_fan"]+int(index == 0)))
        cache[key] = min(values,default=6)
        if len(cache) > 512:
            cache.pop(next(iter(cache)))
        return cache[key]

    def win_status(self,index,source,tile,*,payer=None,hand=None,discarded=None,private=False,preview=False):
        p = self.player_list[index]
        quote = self.score_win(index,source,tile,payer=payer,hand=hand,discarded=discarded,private=private,preview=preview)
        if not quote or "peida" in p.tag_list:
            return "unmet",quote
        if not quote["qualifying_fan"]:
            return "below_minimum",quote
        if source != "self_draw":
            own = p.hand_tiles if hand is None else hand
            if p.declared_jingdiao or quote["effective_fan"] <= p.missed_fan:
                return "self_draw_only",quote
            if not quote["fixed"] and book.is_jingdiao(own,p.combination_tiles,indicator=self.indicator):
                if min(6,quote["qualifying_fan"]+int(index == 0)) <= self.jingdiao_floor(index,hand,discarded):
                    return "self_draw_only",quote
        return "allowed",quote

    def can_win(self,index,source,tile,*,payer=None):
        p = self.player_list[index]
        if p.is_hu or source not in ("self_draw","discard") or source == "self_draw" and not p.has_draw_slot:
            return False
        return self.win_status(index,source,tile,payer=payer)[0] == "allowed"

    def jingdiao_after_kong(self,index,tile,kind):
        p = self.player_list[index]
        hand,melds = list(p.hand_tiles),list(p.combination_tiles)
        for _ in range(4 if kind == "angang" else 3 if kind == "gang" else 1):
            if tile not in hand:
                return False
            hand.remove(tile)
        if kind == "jiagang":
            melds[melds.index(f"k{tile}")] = f"g{tile}"
        else:
            melds.append(("G" if kind == "angang" else "g")+str(tile))
        return book.is_jingdiao(hand,melds,indicator=self.indicator)

    def legal_discard_tiles(self,index):
        return legal_cuts(self,index)

    def advance_round_after_ready(self):
        if self.machine.phase != P.READY or self.match_finishing:
            raise RuntimeError("当前不能开始下一副")
        shift = self.next_dealer
        self.player_list = self.player_list[shift:]+self.player_list[:shift]
        self.dealer_streak = self.dealer_streak+1 if not shift else 0
        for i,p in enumerate(self.player_list):
            p.player_index = i
        self.current_round += 1
        self.round_index += 1
        self.current_player_index = self.dealer_index = 0
        self.machine.transition(P.START)

    def is_match_end(self):
        return self.match_finishing
