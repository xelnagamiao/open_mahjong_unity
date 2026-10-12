"""Classic 160-tile profile; optional rules stay explicit in the room snapshot.

Authority: How to Play Vietnamese Mahjong, Revised and Expanded Edition,
Bridget Young, 2026, PDF modified 2026-08-22. See other/rules/vietnamese.md.
"""
from dataclasses import dataclass, asdict, fields
from .tiles import *

SUB_RULE = 'vietnamese/classic160'
VERSION = 'young-revised-20260822-classic160-v1'


@dataclass(frozen=True)
class Rules:
    seven_pairs: bool = False
    same_number_bouquet: bool = False
    eight_flowers: bool = False
    discarding_flowers: bool = False
    blessing_of_man: bool = False
    six_consecutive_wins: bool = False
    orphans_phan: int = 13
    additional_phan: int = 6
    mun_bonus: int = 0
    quad_bonus: int = 0

    @classmethod
    def from_dict(cls, data=None):
        if data is None:
            return cls()
        if not isinstance(data,dict):
            raise ValueError('越南规则配置须为对象')
        data=dict(data)
        if data.pop('rule_version',VERSION)!=VERSION or set(data)-{f.name for f in fields(cls)}:
            raise ValueError('未知越南规则版本或配置项')
        default=cls()
        if any(type(v) is not type(getattr(default,k)) for k,v in data.items()):
            raise ValueError('越南规则配置类型错误')
        result=cls(**data)
        if result.orphans_phan not in (12,13) or result.additional_phan not in (1,2,6) or result.mun_bonus not in (0,6,10,40) or result.quad_bonus not in (0,5,6):
            raise ValueError('越南规则配置值无效')
        return result

    def snapshot(self):
        return dict(asdict(self),rule_version=VERSION)


@dataclass(frozen=True)
class Score:
    phan: int
    patterns: tuple
    quads: int = 0
    liability: str | None = None

    @property
    def points(self):
        return self.phan

    @property
    def fan_ids(self):
        return [f'VN|{name}|{amount}' for name,amount in self.patterns]


def progressive(patterns):
    """PDF pp.55–56: only hand patterns participate, Little Dragons excluded."""
    patterns=dict(patterns)
    triple=all(patterns.get(k)==1 for k in ('single_wait','all_runs','fully_closed'))
    if triple:
        for k in ('single_wait','all_runs','fully_closed'):
            del patterns[k]
        patterns['wait_runs_closed']=3
    threes=[k for k,v in patterns.items() if v==3 and k in ('all_called','half_flush','all_sets','wait_runs_closed')]
    big=any(v>=6 and k not in ('little_three_dragons','no_jokers_bonus','wait_closed_bonus') for k,v in patterns.items())
    upgraded=False
    if big:
        # 9+3 is already two mun; the first three is not promoted.
        exempt=1 if 'big_three_dragons' in patterns and not any(v>=6 and k not in ('big_three_dragons','little_three_dragons','no_jokers_bonus','wait_closed_bonus') for k,v in patterns.items()) else 0
        for k in threes[exempt:]:
            patterns[k]=6; upgraded=True
    elif len(threes)>=3:
        for k in threes[2:]:
            patterns[k]=6; upgraded=True
    if upgraded:
        patterns.pop('small_no_jokers_bonus',None)
    return patterns


def bonus_score(flowers, replacements, hand_jokers, seat, rules):
    patterns={}
    scored=set()
    bouquets=[]
    flowers=set(flowers)
    for start in (51,55,59,63):
        group=set(range(start,start+4))
        if group<=flowers:
            bouquets.append((f'bouquet_{start}',group,6))
    if rules.same_number_bouquet:
        for n in range(4):
            group={51+n+4*k for k in range(4)}
            if group<=flowers:
                bouquets.append((f'number_bouquet_{n}',group,12 if n==seat else 6))
    for name,group,amount in bouquets:
        patterns[name]=amount;scored.update(group)
    seat_count=sum((t-51)%4==seat for t in flowers-scored)
    if seat_count:
        patterns['seat_flowers']=seat_count
    if rules.eight_flowers and len(flowers)>=8:
        patterns['eight_flowers']=12 if len(flowers)>=10 else 6
    # replacements contain physical tile, batch identity, and initial-deal flag.
    replaced={r['tile']:r for r in replacements}
    suppress=set()
    for color,group in (('blue',{ANY,DOT,BAMBOO,CHARACTER}),('red',{BIG_FLOWER,WIND,DRAGON,NUMBER})):
        if group <= (set(hand_jokers)|set(replaced)):
            all_bonus=group<=set(replaced)
            patterns[f'{color}_joker_set']=12 if all_bonus else 6
            if all_bonus:
                suppress.update(group)
    a,n=replaced.get(ANY),replaced.get(NUMBER)
    # A joker already scored as part of a fully replaced set is not used twice.
    if a and n and not ({ANY,NUMBER}&suppress) and a['batch']==n['batch']:
        patterns['matching_any_number']=9 if a['opening'] and n['opening'] else 6
        suppress.update((ANY,NUMBER))
    for tile,r in replaced.items():
        if tile in suppress:
            continue
        if tile==BIG_FLOWER:
            value=2
        elif tile in (ANY,NUMBER):
            value=3 if r['opening'] or seat==(0 if tile==ANY else 3) else 2
        else:
            value=2 if seat=={DOT:1,BAMBOO:2,CHARACTER:3,WIND:1,DRAGON:2}[tile] else 1
        patterns[f'replaced_{tile}']=value
    return patterns


def score(hand, melds=(), flowers=(), replacements=(), *, winning_tile=None, self_draw=False,
          seat=0, round_wind=41, rules=None, physical_melds=None, replacement_win=False,
          last_tile=False, rob_kong=False, heavenly=False, earthly=False, humanly=False,
          consecutive_wins=0, nfnl_eligible=False, discarded_bonus=(), special=None):
    rules=rules or Rules()
    hand=tuple(sorted(hand)); melds=tuple(melds)
    discard_bonus=not self_draw and winning_tile in BONUS
    if physical_melds is None:
        physical_melds=[meld_tiles(c) for c in melds]
    physical=list(hand)+[t for group in physical_melds for t in group]+list(flowers)+[r['tile'] for r in replacements]
    if Counter(physical)-Counter(PHYSICAL):
        return None
    if any(t in BONUS and not(discard_bonus and t==winning_tile) for t in hand):
        return None
    no_jokers=not any(t in JOKERS or t in BONUS for t in list(hand)+[t for g in physical_melds for t in g])
    closed=all(c[0]=='G' for c in melds)
    candidates=[]
    shapes=standard(hand,len(melds),discard_bonus)
    for pair,sets in shapes:
        allsets=list(sets)+[(c[0],int(c[1:])) for c in melds]
        faces=[pair,pair]+[t for k,v in allsets for t in ((v-1,v,v+1) if k=='s' else (v,)* (4 if k in 'gG' else 3))]
        triplets={v for k,v in allsets if k!='s'}
        quads=sum(k in 'gG' for k,v in allsets)
        concealed_quads=sum(k=='G' for k,v in allsets)
        all_sets=all(k!='s' for k,v in allsets)
        dragon_sets=triplets&{45,46,47};wind_sets=triplets&{41,42,43,44}
        suits={t//10 for t in faces if t<40};honors=any(t>=40 for t in faces)
        pat={}
        if self_draw and closed:
            pat['fully_closed']=1
        if self_draw and winning_tile in hand:
            before=list(hand);before.remove(winning_tile)
            waits=natural_waits(tuple(sorted(before)),len(melds))
            if len(waits)==1 and (represents(winning_tile,pair) or any(k=='s' and represents(winning_tile,t) for k,v in sets for t in (v-1,v,v+1))):
                pat['single_wait']=1
        if all(k=='s' for k,v in allsets) and pair not in (41+seat,round_wind,45,46,47):
            pat['all_runs']=1
        if 41+seat in triplets: pat['seat_wind']=1
        if round_wind in triplets: pat['round_wind']=1
        if dragon_sets: pat['dragon_sets']=len(dragon_sets)
        if quads: pat['quads']=quads
        if not self_draw and len(melds)==4 and all(c[0]!='G' for c in melds): pat['all_called']=3
        if len(suits)==1: pat['half_flush' if honors else 'full_flush']=3 if honors else 6
        if all_sets: pat['all_sets']=3
        if len(dragon_sets)==2 and pair in {45,46,47}-dragon_sets: pat['little_three_dragons']=6
        if all(t>=40 or t%10 in (1,9) for t in faces):
            name='all_honors' if not suits else 'all_terminals' if not honors else 'terminals_honors'
            pat[name]=6 if name=='terminals_honors' else 24;pat.pop('all_sets',None)
        if len(dragon_sets)==3: pat['big_three_dragons']=9;pat.pop('dragon_sets',None)
        if self_draw and closed and all_sets: pat['four_closed_sets']=18;pat.pop('all_sets',None)
        if len(wind_sets)==3 and pair in {41,42,43,44}-wind_sets:
            pat['little_four_winds']=24;pat.pop('seat_wind',None);pat.pop('round_wind',None)
        if quads==4:
            pat['four_closed_quads' if concealed_quads==4 else 'four_quads']=30 if concealed_quads==4 else 24
            for key in ('all_sets','quads','four_closed_sets'):
                pat.pop(key,None)
        if len(wind_sets)==4:
            pat['big_four_winds']=48
            for key in ('all_sets','half_flush','seat_wind','round_wind'):
                pat.pop(key,None)
        if closed and not melds:
            for suit in (1,2,3):
                base=tuple(suit*10+n for n in (1,1,1,2,3,4,5,6,7,8,9,9,9))
                if any(fits(hand,base+(suit*10+n,),discard_bonus) for n in range(1,10)):
                    pat['nine_gates']=18;pat.pop('full_flush',None);break
        if nfnl_eligible and no_jokers and not honors and not flowers and not replacements:
            pat['true_no_flowers_leaves']=6
            if len(discarded_bonus)>=3: pat['discarding_flowers_jokers']=12 if len(discarded_bonus)>=5 else 6
        candidates.append((pat,quads))
    if not melds and any(fits(hand,ORPHANS+(t,),discard_bonus) for t in ORPHANS):
        pat={'thirteen_orphans':rules.orphans_phan,'terminals_honors':6}
        if self_draw:pat['fully_closed']=1
        candidates.append((pat,0))
    if not melds and len(hand)==14 and rules.seven_pairs and self_draw and seven_pairs(hand):
        for faces in pair_faces(hand):
            pat={'seven_pairs':6,'fully_closed':1}
            suits={t//10 for t in faces if t<40};honors=any(t>=40 for t in faces)
            if len(suits)==1:pat['half_flush' if honors else 'full_flush']=3 if honors else 6
            if all(t>=40 or t%10 in (1,9) for t in faces):
                name='all_honors' if not suits else 'all_terminals' if not honors else 'terminals_honors'
                pat[name]=6 if name=='terminals_honors' else 24
            if winning_tile in hand:
                before=list(hand);before.remove(winning_tile)
                waits=[t for t in TILES if before.count(t)<4 and seven_pairs(tuple(sorted(before+[t])))]
                if len(waits)==1:pat['single_wait']=1
            candidates.append((pat,0))
    if special:
        candidates.append(({special[0]:special[1]},0))
    if not candidates:
        return None
    bonus=bonus_score(flowers,replacements,[t for t in physical if t in JOKERS],seat,rules)
    if nfnl_eligible and len(discarded_bonus)<3:
        for k,v in bonus_score((),[dict(tile=t,batch=i,opening=False) for i,t in enumerate(discarded_bonus) if t in JOKERS],(),seat,rules).items(): bonus[k]=bonus.get(k,0)+v
    situation={}
    for flag,name in ((replacement_win,'replacement_win'),(last_tile,'last_tile'),(rob_kong,'rob_kong')):
        if flag: situation[name]=1
    if heavenly: situation['blessing_of_heaven']=12
    elif earthly: situation['blessing_of_earth']=12
    elif humanly and rules.blessing_of_man: situation['blessing_of_man']=12
    if consecutive_wins>=6 and rules.six_consecutive_wins: situation['six_consecutive_wins']=6
    scored=[]
    for pat,quads in candidates:
        if no_jokers and any(v>=6 and k not in ('true_no_flowers_leaves','discarding_flowers_jokers','discarding_blue_jokers','discarding_flowers') for k,v in pat.items()):
            pat['no_jokers_bonus']=6
        elif no_jokers and 'all_sets' in pat: pat['small_no_jokers_bonus']=1
        if pat.get('single_wait') and pat.get('fully_closed') and any(v>=12 for v in pat.values()):
            del pat['single_wait'];del pat['fully_closed'];pat['wait_closed_bonus']=6
        pat=progressive(pat)
        pat.update(bonus);pat.update(situation)
        if not pat and no_jokers: pat['no_jokers_mosquito']=6
        scored.append(Score(sum(pat.values()),tuple(sorted(pat.items())),quads))
    return max(scored,key=lambda q:(payment_value(q.phan,True,rules,q.quads),q.phan,q.patterns))


def payment_value(phan,double,rules=None,quads=0):
    rules=rules or Rules()
    mun,extra=divmod(phan,6)
    if mun:
        return mun*32*(2 if double else 1)+extra*rules.additional_phan+mun*rules.mun_bonus+quads*rules.quad_bonus
    return max(1,2**phan//(1 if double else 2))+quads*rules.quad_bonus


def payments(winner,quote,*,payer=None,self_draw=False,rules=None,liability=None):
    rules=rules or Rules()
    changes=[0]*4
    for i in range(4):
        if i!=winner: changes[i]=-payment_value(quote.phan,self_draw or i==payer,rules,quote.quads)
    if any(name=='rob_kong' for name,value in quote.patterns) and payer is not None:
        total=-sum(changes)
        for i in range(4):
            if i not in (winner,payer): changes[i]=-payment_value(quote.phan-1,False,rules,quote.quads)
        changes[payer]=-total-sum(changes[i] for i in range(4) if i not in (winner,payer))
    if liability is not None:
        total=sum(changes);changes=[0]*4;changes[liability]=total
    changes[winner]=-sum(changes)
    return changes
