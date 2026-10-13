"""Physical tile operations and authoritative Vietnamese decisions.

Assignments are explicit: a joker's physical ID never becomes its represented
face. Every claim option is generated server-side, then selected by opaque ID.
"""
import random
from collections import Counter
from dataclasses import replace
from ...game_calculation.vietnamese import rules as book
from ...game_calculation.vietnamese.tiles import *
from ..public.random_seed_manager import derive_round_seed
from .state_machine import Phase as P


def actions():
    return {i:[] for i in range(4)}


class VietnameseDomain:
    def initialize_round(self,*,wall=None):
        if self.machine.phase!=P.START:
            raise RuntimeError('上一局确认尚未结束')
        self.reset_round_state()
        self.round_changes=[0]*4;self.round_start_scores=[p.score for p in self.player_list]
        self.domain_events=[];self.ledger=[];self.claim_options={i:[] for i in range(4)}
        self.pending_kong=None;self.pending_bonus_claim=None;self.replacement_batch=0;self.discard_safety={};self.safe_special=None
        rng=random.Random(self.round_random_seed)
        self.dice=[rng.randint(1,6) for _ in range(3)]
        physical=list(PHYSICAL)
        if wall is None:
            rng.shuffle(physical)
            offset=(((sum(self.dice[:2])-1)%4)*40+sum(self.dice)*2)%160
            wall=physical[offset:]+physical[:offset]
        elif Counter(wall)!=Counter(PHYSICAL):
            raise ValueError('Classic160须使用完整的160张牌组')
        self.tiles_list=list(wall)
        for packet in range(3):
            for p in self.player_list:
                p.hand_tiles.extend(self.tiles_list[:4]);del self.tiles_list[:4]
        # Dealer takes the upper tiles of first and third stacks, then others.
        last=self.tiles_list[:5];del self.tiles_list[:5]
        self.player_list[0].hand_tiles.extend((last[0],last[4]))
        for i in (1,2,3): self.player_list[i].hand_tiles.append(last[i])
        for i,p in enumerate(self.player_list):
            p.nfnl_eligible=sum(t in BONUS for t in p.hand_tiles)<=(1 if i==0 else 0)
            p.has_draw_slot=i==0
            p.last_drawn_tile=p.hand_tiles[-1] if i==0 else None
            p.draw_kind='normal'
        self.current_player_index=self.dealer_index=0
        self.opening_queue=list(range(4))

    def opening_window(self):
        while self.opening_queue:
            i=self.opening_queue.pop(0);p=self.player_list[i]
            bonuses=[t for t in p.hand_tiles if t in BONUS]
            jokers=[t for t in p.hand_tiles if t in JOKERS]
            if not bonuses and not jokers: continue
            offered=actions()
            if bonuses: offered[i].append('buhua')
            if jokers: offered[i].append('vn_replace')
            if not bonuses or i==0 and len(bonuses)==1 and p.nfnl_eligible and not p.huapai_list:
                offered[i].append('pass')
            self.current_player_index=i
            return self._window(P.REPLACEMENT,i,actions=offered)
        self.opening_queue=None;self.current_player_index=0
        for i,p in enumerate(self.player_list):
            p.has_draw_slot=i==0
            drawn=p.hand_tiles[-1] if i==0 else None
            p.hand_tiles[:]=sorted(p.hand_tiles[:-1])+[drawn] if i==0 else sorted(p.hand_tiles)
            p.last_drawn_tile=drawn
        self.record_opening_complete()
        return self.begin_turn(0)

    @staticmethod
    def physical_melds(p):
        return [list(mask[1::2]) for mask in p.combination_mask]

    @staticmethod
    def bonus_discard_allowed(p):
        return not p.huapai_list and not any(t in JOKERS for mask in p.combination_mask for t in mask[1::2])

    def legal_discard_tiles(self,index):
        p=self.player_list[index]
        return set(p.hand_tiles) if self.bonus_discard_allowed(p) else set(p.hand_tiles)&set(TILES)

    def replacement_candidates(self,index):
        p=self.player_list[index]
        if len(self.tiles_list)<=26 or self.opening_queue is None and not p.has_draw_slot:
            return []
        return [t for t in p.hand_tiles if t in JOKERS]

    def replace_bonus(self,index,selected=()):
        p=self.player_list[index]
        selected=list(t for t in p.hand_tiles if t in BONUS)+list(selected)
        if not selected: raise ValueError('没有可替换的牌')
        self.replacement_batch+=1
        opening=self.opening_queue is not None
        p.nfnl_eligible=False
        replacement_draws=[]
        for tile in selected:
            if len(self.tiles_list)<=26: return self.end_hand()
            mo=p.has_draw_slot and p.hand_tiles[-1]==tile
            at=len(p.hand_tiles)-1 if mo else p.hand_tiles.index(tile)
            p.hand_tiles.pop(at);p.huapai_list.append(tile);p.has_draw_slot=False
            if tile in JOKERS or tile==BIG_FLOWER:
                p.replacements.append(dict(tile=tile,batch=self.replacement_batch,opening=opening))
            self._event('buhua',index,tile=tile,is_mo_buhua=mo)
            drawn=self.tiles_list.pop();p.hand_tiles.append(drawn);replacement_draws.append(drawn)
            p.has_draw_slot=len(p.hand_tiles)%3==2;p.last_drawn_tile=drawn;p.draw_kind='replacement'
            self._event('deal_buhua_tile',index,tile=drawn)
        if opening:
            if any(t in BONUS for t in p.hand_tiles) or any(t in JOKERS for t in replacement_draws):self.opening_queue.append(index)
            return self.opening_window()
        return self.begin_turn(index)

    def kong_tiles(self,index,action):
        p=self.player_list[index]
        if len(self.tiles_list)<=26 or not p.has_draw_slot: return set()
        if action=='angang': return {t for t,n in Counter(p.hand_tiles).items() if n==4 and t in TILES}
        t=p.last_drawn_tile
        return {t} if t in TILES and any(c==f'k{t}' and all(v==t for v in mask[1::2]) for c,mask in zip(p.combination_tiles,p.combination_mask)) else set()

    def bounce_candidates(self,index):
        p=self.player_list[index];t=p.last_drawn_tile
        if len(self.tiles_list)<=26 or not p.has_draw_slot or t not in TILES:return []
        return [j for c,mask in zip(p.combination_tiles,p.combination_mask) if c==f'k{t}' for j in mask[1::2] if j in JOKERS]

    def special_shape(self,p,*,after_safe=False,tile=None):
        if not p.nfnl_eligible or p.huapai_list:return None
        discarded=p.discarded_bonus;n=len(discarded)
        if after_safe:
            if n>=3 and all(t in BLUE for t in discarded):return ('discarding_blue_jokers',(n-2)*6)
            if self.rules.discarding_flowers and n>=3 and all(t in BONUS for t in discarded):return ('discarding_flowers',(n-2)*6)
            if n>=6:return ('discarding_flowers_jokers',12)
        if n>=3 and tile in TILES and tile<40 and all(t in TILES and t<40 for t in p.hand_tiles) and all(int(c[1:])<40 for c in p.combination_tiles):
            return ('discarding_flowers_jokers',12 if n>=5 else 6)
        return None

    def quote(self,index,source,tile):
        p=self.player_list[index];self_draw=source in ('self_draw','special')
        hand=list(p.hand_tiles)+([] if self_draw else [tile])
        special=self.safe_special if source=='special' else self.special_shape(p,tile=tile)
        return book.score(hand,p.combination_tiles,[t for t in p.huapai_list if t in FLOWERS],p.replacements,
            physical_melds=self.physical_melds(p),winning_tile=tile,self_draw=self_draw,
            seat=index,round_wind=41+(self.current_round-1)//4,rules=self.rules,
            replacement_win=self_draw and p.draw_kind in ('replacement','kong','bounce'),
            last_tile=len(self.tiles_list)==26,rob_kong=source=='rob_kong',
            heavenly=index==0 and not self.discard_log and self_draw,
            earthly=source=='discard' and len(self.discard_log)==1 and self.discard_log[0][0]==0,
            humanly=index!=0 and p.draw_count==1 and not p.discard_origin_tiles and self_draw,
            consecutive_wins=self.consecutive_wins+1 if p.user_id==self.last_winner_user_id else 1,
            nfnl_eligible=p.nfnl_eligible,discarded_bonus=tuple(p.discarded_bonus),special=special)

    def begin_turn(self,index,*,discard_only=False):
        self.current_player_index=index;p=self.player_list[index];offered=actions()
        bonuses=any(t in BONUS for t in p.hand_tiles)
        if bonuses and len(self.tiles_list)<=26:
            # A replacement is unavailable, but a legal bonus discard may win for another player.
            if not self.bonus_discard_allowed(p):return self.end_hand()
        if bonuses and not self.bonus_discard_allowed(p):
            offered[index]=['buhua'];return self._window(P.REPLACEMENT,index,actions=offered)
        offered[index]=['cut'] if self.legal_discard_tiles(index) else []
        if not discard_only:
            if bonuses and len(self.tiles_list)>26:offered[index].append('buhua')
            if p.has_draw_slot and self.quote(index,'self_draw',p.hand_tiles[-1]):offered[index].append('hu_self')
            if self.replacement_candidates(index):offered[index].append('vn_replace')
            if self.bounce_candidates(index):offered[index].append('vn_bounce')
            for action in ('angang','jiagang'):
                if self.kong_tiles(index,action):offered[index].append(action)
        p.waiting_tiles=list(natural_waits(tuple(sorted(p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles)),len(p.combination_tiles))) if not bonuses else []
        if not offered[index]:return self.end_hand()
        self.hand_action_is_gang_draw[index]=p.draw_kind=='kong'
        return self._window(P.DISCARD_ONLY if discard_only else P.TURN,index,actions=offered)

    def build_claim_options(self,index,actor,tile):
        p=self.player_list[index]
        if len(self.tiles_list)<=26 or tile in BONUS or len(p.combination_tiles)>=4:return []
        result=[];seen=set()
        def add(kind,face,targets,assigned):
            for rest,used in consume(tuple(sorted(p.hand_tiles)),tuple(targets)):
                if kind=='g' and any(t in JOKERS for t in used):continue
                future_bonus_allowed=self.bonus_discard_allowed(p) and tile not in JOKERS and not any(t in JOKERS for t in used)
                if kind!='g' and not future_bonus_allowed and not any(t in TILES for t in rest):continue
                key=(kind,face,tuple(sorted(used)),assigned)
                if key in seen:continue
                seen.add(key)
                result.append(dict(id=len(result)+1,kind=kind,face=face,tiles=list(used),claimed_face=assigned,
                    joker_count=sum(t in JOKERS for t in used)+(tile in JOKERS),
                    versatility=sum(34 if t==ANY else 27 if t==NUMBER else 9 if t in (DOT,BAMBOO,CHARACTER) else 4 if t==WIND else 3 if t==DRAGON else 0 for t in used+(tile,))))
        for target in TILES:
            if not represents(tile,target):continue
            if tile not in p.missed_pung:add('k',target,(target,target),target)
            if tile in TILES:add('g',target,(target,)*3,target)
            if index==(actor+1)%4 and target<40:
                for first in range(max(1,target%10-2),min(7,target%10)+1):
                    group=[target//10*10+first+n for n in range(3)];group.remove(target)
                    add('s',target//10*10+first+1,tuple(group),target)
        return result

    def claim_actions(self,actor,tile,*,rob=False):
        offered=actions();self.claim_options={i:[] for i in range(4)}
        for index,p in enumerate(self.player_list):
            if index==actor:continue
            if tile not in p.missed_win and self.quote(index,'rob_kong' if rob else 'discard',tile):offered[index].append('hu')
            if not rob:
                self.claim_options[index]=self.build_claim_options(index,actor,tile)
                if self.claim_options[index]:offered[index].append('vn_claim')
            if offered[index]:offered[index].append('pass')
        return offered

    def validated_cut(self,index,data):
        p=self.player_list[index];tile=data.get('TileId');pos=data.get('cutIndex',-1);draw=data.get('cutClass')
        if type(tile) is not int or tile not in self.legal_discard_tiles(index):raise ValueError('非法舍牌')
        if type(pos) is not int or pos<-1 or pos>=len(p.hand_tiles):raise ValueError('非法舍牌位置')
        if draw is not None and type(draw) is not bool:raise ValueError('非法摸切标识')
        if draw is True:
            if not p.has_draw_slot or p.hand_tiles[-1]!=tile:raise ValueError('非法摸切')
            pos=len(p.hand_tiles)-1
        elif draw is False:
            held=p.hand_tiles[:-1] if p.has_draw_slot else p.hand_tiles
            if tile not in held:raise ValueError('手切牌不存在')
            # Unity sends a sibling index in its independently arranged UI;
            # sorting, dragging and replacement can change that visual order.
            # Authorize the physical tile and hand/draw source, not UI order.
            pos=held.index(tile)
        elif pos>=0 and p.hand_tiles[pos]!=tile:raise ValueError('牌值与位置不符')
        elif pos<0:pos=p.hand_tiles.index(tile)
        return tile,pos,p.has_draw_slot and pos==len(p.hand_tiles)-1

    def validate_response(self,index,data,offered):
        action=data.get('action_type');target=data.get('target_tile')
        if type(index) is not int or index not in range(4) or action not in offered:raise ValueError('当前没有该动作')
        if action=='cut':self.validated_cut(index,data)
        elif action in ('angang','jiagang'):
            if type(target) is not int or target not in self.kong_tiles(index,action):raise ValueError('非法杠牌')
        elif action=='vn_bounce':
            if type(target) is not int or target not in self.bounce_candidates(index):raise ValueError('非法弹鬼')
        elif action=='vn_claim':
            if type(target) is not int or not any(o['id']==target for o in self.claim_options[index]):raise ValueError('非法吃碰选项')
        elif action=='vn_replace':
            if type(target) is not int or target<=0 or target>=128:raise ValueError('非法鬼牌替换选择')
            selected={j for bit,j in enumerate(JOKERS) if target&(1<<bit)}
            if not selected<=set(self.replacement_candidates(index)):raise ValueError('替换鬼牌不在手中')

    def apply_action_results(self,window,results,*,settlements=None):
        if settlements is not None:raise ValueError('客户端不得指定结算')
        if window is not self.live_pending_window or window.get('action_tick')!=self.server_action_tick:raise ValueError('过期操作')
        if set(results)!={i for i,a in window['actions'].items() if a}:raise ValueError('应答座位不符')
        for i,data in results.items():self.validate_response(i,data,window['actions'][i])
        phase=P(window['status']);actor=window['player']
        if phase==P.REPLACEMENT or phase in (P.TURN,P.DISCARD_ONLY):
            data=results[actor];action=data['action_type'];target=data.get('target_tile')
            if action=='pass':next_window=self.opening_window()
            elif action in ('buhua','vn_replace'):
                selected=[j for bit,j in enumerate(JOKERS) if target&(1<<bit)] if action=='vn_replace' else []
                next_window=self.replace_bonus(actor,selected)
            elif action=='hu_self':next_window=self.win(actor,'self_draw',self.player_list[actor].hand_tiles[-1])
            elif action=='angang':next_window=self.commit_kong(actor,target,action)
            elif action=='jiagang':
                self.pending_kong=(actor,target)
                next_window=self._window(P.ROB,actor,target,self.claim_actions(actor,target,rob=True))
            elif action=='vn_bounce':next_window=self.bounce(actor,target)
            else:next_window=self.discard(actor,data)
        elif phase in (P.RESPONSE,P.ROB):
            tile=window['tile'];claims=[]
            for i,data in results.items():
                action=data['action_type'];p=self.player_list[i]
                if 'hu' in window['actions'][i] and action!='hu':p.missed_win.add(tile)
                if any(o['kind']=='k' for o in self.claim_options[i]) and action!='vn_claim':p.missed_pung.add(tile)
                if action=='hu':claims.append((0,0,0,(i-actor)%4,i,None))
                elif action=='vn_claim':
                    option=next(o for o in self.claim_options[i] if o['id']==data['target_tile'])
                    if option['kind']!='k' and any(o['kind']=='k' for o in self.claim_options[i]):p.missed_pung.add(tile)
                    claims.append(({'g':1,'k':2,'s':3}[option['kind']],option['joker_count'],option['versatility'],(i-actor)%4,i,option))
            if claims:
                priority,_,_,_,i,option=min(claims,key=lambda c:c[:4])
                next_window=self.win(i,'rob_kong' if phase==P.ROB else 'discard',tile,actor) if priority==0 else self.after_safe_discard(actor,tile,pending_claim=(i,actor,tile,option))
            elif phase==P.ROB:next_window=self.commit_kong(actor,tile,'jiagang')
            else:next_window=self.after_safe_discard(actor,tile)
        elif phase==P.SPECIAL:
            next_window=self.win(actor,'special',None) if results[actor]['action_type']=='hu_special' else self.continue_after_safe_discard(actor)
        else:raise ValueError('此阶段不接受动作')
        return self.open_action_window(next_window)

    def discard(self,index,data):
        p=self.player_list[index];tile,pos,mo=self.validated_cut(index,data)
        if p.nfnl_eligible:
            if p.has_draw_slot and p.last_drawn_tile in BONUS+JOKERS and tile!=p.last_drawn_tile and p.draw_count>0:p.nfnl_eligible=False
            elif index==0 and p.draw_count==0 and any(t in BONUS for t in p.hand_tiles) and tile not in BONUS:p.nfnl_eligible=False
            elif any(t in JOKERS for t in p.hand_tiles) and tile not in JOKERS+BONUS:p.nfnl_eligible=False
        self.discard_safety=self.assess_discard_safety(index,tile)
        if p.has_draw_slot:p.missed_win.clear();p.missed_pung.clear()
        p.hand_tiles.pop(pos);p.hand_tiles.sort();p.has_draw_slot=False;p.last_drawn_tile=None
        p.discard_tiles.append(tile);p.discard_origin_tiles.append(tile);self.discard_log.append((index,tile))
        self._event('cut',index,tile=tile,cutIndex=data.get('cutIndex',pos),cutClass=mo,is_timeout_action=bool(data.get('is_timeout_action')))
        return self._window(P.RESPONSE,index,tile,self.claim_actions(index,tile))

    def after_safe_discard(self,index,tile,*,pending_claim=None):
        self.pending_bonus_claim=pending_claim
        p=self.player_list[index]
        if tile in BONUS+JOKERS:
            p.discarded_bonus.append(tile)
            special=self.special_shape(p,after_safe=True)
            if special:
                self.safe_special=special;offered=actions();offered[index]=['hu_special','pass']
                return self._window(P.SPECIAL,index,actions=offered)
        return self.continue_after_safe_discard(index)

    def continue_after_safe_discard(self,index):
        pending=getattr(self,'pending_bonus_claim',None)
        self.pending_bonus_claim=None
        return self.claim(*pending) if pending else self.draw_for((index+1)%4)

    def claim(self,index,actor,tile,option):
        p=self.player_list[index];before=self.safe_tiles_by_threat(index)
        self.capture_feeder(index,actor,tile)
        for t in option['tiles']:p.hand_tiles.remove(t)
        self.player_list[actor].discard_tiles.pop()
        physical=[tile]+option['tiles'];kind=option['kind'];face=option['face']
        code=kind+str(face)
        mask=[x for i,t in enumerate(physical) for x in (1 if i==0 else 0,t)]
        p.combination_tiles.append(code);p.combination_mask.append(mask)
        if any(t in JOKERS for t in physical):p.nfnl_eligible=False
        p.has_draw_slot=False;p.last_drawn_tile=None;self.current_player_index=index
        after=self.safe_tiles_by_threat(index)
        p.lost_safe_by_call.update(key for key,safe in before.items() if safe and not after.get(key))
        action='gang' if kind=='g' else 'peng' if kind=='k' else 'chi_mid'
        self._event(action,index,tile=tile,meld_code=code,combination_mask=mask,cut_from_player=actor)
        return self.draw_for(index,'kong') if kind=='g' else self.begin_turn(index,discard_only=True)

    def commit_kong(self,index,tile,action):
        p=self.player_list[index]
        if action=='angang':
            for _ in range(4):p.hand_tiles.remove(tile)
            code=f'G{tile}';mask=[2,tile]*4;p.combination_tiles.append(code);p.combination_mask.append(mask)
        else:
            p.hand_tiles.pop();at=p.combination_tiles.index(f'k{tile}')
            code=f'g{tile}';mask=list(p.combination_mask[at])+[3,tile]
            p.combination_tiles[at]=code;p.combination_mask[at]=mask
        p.has_draw_slot=False;self.pending_kong=None
        self._event(action,index,tile=tile,meld_code=code,combination_mask=mask,is_mo_gang=True)
        return self.draw_for(index,'kong')

    def bounce(self,index,joker):
        p=self.player_list[index];tile=p.last_drawn_tile
        at=next(i for i,(c,mask) in enumerate(zip(p.combination_tiles,p.combination_mask)) if c==f'k{tile}' and joker in mask[1::2])
        mask=p.combination_mask[at];physical_at=next(i for i in range(1,len(mask),2) if mask[i]==joker)
        p.hand_tiles.pop();mask[physical_at]=tile;p.huapai_list.append(joker);p.has_draw_slot=False;p.nfnl_eligible=False
        self.replacement_batch+=1;p.replacements.append(dict(tile=joker,batch=self.replacement_batch,opening=False))
        self._event('vn_bounce',index,tile=tile,joker=joker,combination_target=f'k{tile}',combination_mask=list(mask),meld_index=at)
        return self.draw_for(index,'bounce')

    def draw_for(self,index,kind='normal'):
        if len(self.tiles_list)<=26:return self.end_hand()
        p=self.player_list[index];p.hand_tiles.sort()
        tile=self.tiles_list.pop(0 if kind=='normal' else -1);p.hand_tiles.append(tile)
        p.last_drawn_tile=tile;p.has_draw_slot=True;p.draw_kind=kind
        if kind=='normal':p.draw_count+=1
        self.current_player_index=index
        self._event('deal_tile' if kind=='normal' else 'deal_gang_tile',index,tile=tile)
        return self.begin_turn(index)

    def public_threats(self,index):
        p=self.player_list[index]
        revealed=[(c[0],int(c[1:])) for c in p.combination_tiles if c[0]!='G']
        faces=[t for k,v in revealed for t in ((v-1,v,v+1) if k=='s' else (v,)*3)]
        threats={}
        if len(revealed)>=3:
            if faces and all(t<40 and t//10==faces[0]//10 for t in faces):threats['full_flush']={t for t in TILES if t//10==faces[0]//10}
            if faces and all(t<40 and t%10 in (1,9) for t in faces):threats['all_terminals']={t for t in TILES if t<40 and t%10 in (1,9)}
            if faces and all(t>=40 for t in faces):threats['all_honors']=set(range(41,48))
        triplets={v for k,v in revealed if k!='s'}
        if len(triplets&{45,46,47})>=2:threats['big_three_dragons']={45,46,47}-triplets
        if len(triplets&{41,42,43,44})>=3:threats['four_winds']={41,42,43,44}-triplets
        return threats

    def safe_tiles_by_threat(self,index):
        p=self.player_list[index];result={};legal=self.legal_discard_tiles(index)
        for target in range(4):
            if target==index:continue
            for kind,unsafe in self.public_threats(target).items():
                result[(target,kind)]={t for t in legal if not any(represents(t,u,discard_bonus=True) for u in unsafe)}
        # Bonus tiles can complete any permitted face and are unsafe for liability.
        result[(-1,'bonus')]={t for t in p.hand_tiles if t in TILES}
        if len(self.tiles_list)-26<4:
            pool={t for player in self.player_list for t in player.discard_tiles}
            result[(-1,'last_four')]=legal & pool
        return result

    def assess_discard_safety(self,index,tile):
        p=self.player_list[index];safe=self.safe_tiles_by_threat(index)
        result={key:bool(tiles) or key in p.lost_safe_by_call for key,tiles in safe.items()}
        result['last_unseen']=tile not in {t for player in self.player_list for t in player.discard_tiles}
        return result

    def capture_feeder(self,winner,actor,tile):
        p=self.player_list[winner]
        for kind,unsafe in self.public_threats(winner).items():
            if any(represents(tile,t) for t in unsafe):
                if self.discard_safety.get((winner,kind)):
                    p.liability_feeders[kind]=actor

    def liability(self,winner,source,tile,payer,quote):
        p=self.player_list[winner];patterns=dict(quote.patterns)
        if source=='discard' and tile in BONUS+JOKERS and self.discard_safety.get((-1,'bonus')):
            return payer,replace(quote,phan=quote.phan+6,patterns=quote.patterns+(('bonus_discard_liability',6),))
        if source=='discard' and len(self.tiles_list)-26<4 and self.discard_safety.get('last_unseen') and self.discard_safety.get((-1,'last_four')):
            return payer,quote
        threats=self.public_threats(winner)
        for name in ('full_flush','all_terminals','all_honors','big_three_dragons','four_winds'):
            valid=('big_four_winds' in patterns or 'little_four_winds' in patterns) if name=='four_winds' else name in patterns
            if not valid:continue
            if source in ('self_draw','special') and name in p.liability_feeders:return p.liability_feeders[name],quote
            if source=='discard':
                dangerous=name in threats and any(represents(tile,t) for t in threats[name])
                # Only twelve-tile liability explicitly transfers payment to a
                # later discarder. Dragon/wind feeding covers a self-drawn win.
                transferred=name in ('full_flush','all_terminals','all_honors') and name in p.liability_feeders
                if (dangerous or transferred) and self.discard_safety.get((winner,name)):
                    return payer,quote
        return None,quote

    def win(self,index,source,tile,payer=None):
        if self.deferred_hu_settlements:raise ValueError('本局已结算')
        p=self.player_list[index]
        if source=='self_draw':
            if self.machine.phase!=P.TURN or index!=self.current_player_index or not p.has_draw_slot or p.hand_tiles[-1]!=tile:raise ValueError('非法自摸')
        elif source=='special':
            if self.machine.phase!=P.SPECIAL or index!=self.current_player_index or not self.safe_special:raise ValueError('非法特殊和')
        elif source in ('discard','rob_kong'):
            if self.machine.phase!=(P.ROB if source=='rob_kong' else P.RESPONSE) or payer!=self.current_player_index or payer==index:raise ValueError('非法点和')
        else:raise ValueError('未知和牌方式')
        quote=self.quote(index,source,tile)
        if quote is None:raise ValueError('和牌不成立')
        liable,quote=self.liability(index,source,tile,payer,quote)
        changes=book.payments(index,quote,payer=payer,self_draw=source in ('self_draw','special'),rules=self.rules,liability=liable)
        self.round_changes=[a+b for a,b in zip(self.round_changes,changes)]
        entry=dict(winner=index,source='self_draw' if source=='special' else source,tile=tile or 0,discarder=payer,
                   points=quote.phan,fan_ids=quote.fan_ids,score_changes=changes,hu_order=1,recycle_discard=True,
                   rule_version=book.VERSION,liability=liable,special=source=='special')
        if source=='special':entry['fan_ids'].append('VN|safe_discard_declaration|0')
        self.deferred_hu_settlements.append(entry);p.is_hu=True;p.hu_order=1;p.has_draw_slot=False
        if source=='discard':self.player_list[payer].discard_tiles.pop();p.won_tiles.append(tile)
        elif source=='rob_kong':self.player_list[payer].hand_tiles.pop();self.player_list[payer].has_draw_slot=False;p.won_tiles.append(tile)
        self._event(self._hu_action_for_settlement(entry),index,tile=tile or 0)
        return self.end_hand(index)

    def end_hand(self,winner=None):
        self.ended_by='wall' if winner is None else 'win'
        self.next_dealer=0 if winner in (None,0) else 1
        self.match_finishing=self.current_round>=self.max_round*4 and self.next_dealer==1
        self.dealer_streak=self.dealer_streak+1 if self.next_dealer==0 else 0
        if winner is not None:
            uid=self.player_list[winner].user_id
            self.consecutive_wins=self.consecutive_wins+1 if uid==self.last_winner_user_id else 1
            self.last_winner_user_id=uid
        else:self.consecutive_wins=0;self.last_winner_user_id=None
        return self._window(P.END)

    def advance_round_after_ready(self):
        if self.machine.phase!=P.READY or self.match_finishing:raise RuntimeError('当前不能开下一局')
        if self.next_dealer:
            self.player_list=self.player_list[1:]+self.player_list[:1];self.current_round+=1
        for i,p in enumerate(self.player_list):p.player_index=i
        self.round_index+=1;self.current_player_index=self.dealer_index=0;self.machine.transition(P.START)
