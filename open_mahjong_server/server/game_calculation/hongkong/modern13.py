"""Guangdong thirteen-tile new-style fan table (Mahjong Wiki IGS profile).

The public reading source is Mahjong Wiki, Guangdong Style Scoring, IGS tab.
Source provenance and translation corrections are retained in
other/rule/hongkong/SOURCES.md; legacy wire identifiers remain unchanged.
Online completion rules, including normalized point units, live in the versioned
platform supplement. Keep explicit source exceptions (notably pung-first ron).
"""

from collections import Counter
from itertools import combinations

from .models import Fan, HandContext, HongKongRules, ScoreResult, NUMBERS, HONORS, WINDS, DRAGONS, ORPHANS
from .solver import decompositions, parse_meld, structural_waits
from .old_style import nine_gates


def score_modern13(ctx: HandContext, rules: HongKongRules) -> ScoreResult:
    if ctx.flowers or ctx.flower_win:
        return ScoreResult(rule_version=rules.version)
    shapes = decompositions(ctx.hand,ctx.melds,rules,ctx.winning_tile)
    if not shapes or ctx.winning_tile not in ctx.hand:
        return ScoreResult(rule_version=rules.version)
    if not ctx.self_draw:
        pung_wins = [s for s in shapes if s.kind == "standard" and s.winning_component >= 0
                     and s.melds[s.winning_component].kind == "triplet"]
        if pung_wins:
            shapes = pung_wins
    before = list(ctx.hand)
    before.remove(ctx.winning_tile)
    waits = structural_waits(before,ctx.melds,rules)
    results = [_score(ctx,rules,s,waits) for s in shapes]
    return max(results,key=lambda r:(r.fan,r.fan_ids))


def _score(ctx,rules,shape,waits):
    fans = {}
    def add(key,name,value,condition=True):
        if condition:
            fans[key] = Fan(key,name,value)
    all_tiles = ctx.hand + tuple(t for c in ctx.melds for t in parse_meld(c).tiles)
    counter = Counter(all_tiles)
    suits = {t//10 for t in all_tiles if t in NUMBERS}
    honors = any(t in HONORS for t in all_tiles)
    closed = all(c[0] == "G" for c in ctx.melds)
    if closed and ctx.self_draw:
        add("concealed_self_draw","门清自摸",3)
    else:
        add("concealed","门清",1,closed)
        add("self_draw","自摸",1,ctx.self_draw)
    add("orphans","十三么",40,shape.kind == "orphans")
    add("seven_pairs","七对子",7,shape.kind == "seven_pairs")
    add("half_flush","凑一色",3,len(suits)==1 and honors)
    gates = nine_gates(ctx,require_nine_wait_on_discard=False)
    add("nine_gates","九莲宝灯",40,gates)
    add("full_flush","清一色",10,(len(suits)==1 and not honors or not suits) and not gates)
    add("all_honors","字一色",40,not suits)
    add("green","绿一色",40,set(all_tiles) <= {32,33,34,36,38,46})
    add("missing_suit","缺门",1,len(suits)==2 and not honors)
    add("missing_five","缺五",1,not any(t in NUMBERS and t%10 == 5 for t in all_tiles))
    add("simples","短么",1,not honors and all(2 <= t%10 <= 8 for t in all_tiles))
    add("terminals_honors","全么",10,set(all_tiles) <= ORPHANS)
    categories = {t//10 if t in NUMBERS else 4 if t in WINDS else 5 for t in all_tiles}
    add("five_categories","五门齐",5,len(categories)==5)
    add("single_wait","独听",1,len(waits)==1)
    add("heavenly","天胡",20,ctx.heavenly)
    add("earthly","地胡",10,ctx.earthly or ctx.humanly)
    if ctx.ready:
        if ctx.ready == "heaven":
            add("heaven_ready","天听",8)
        elif ctx.ready == "earth":
            add("earth_ready","地听",4)
        else:
            add("ready","立直",1,closed)
    add("last_tile","海底捞月",1,ctx.self_draw and ctx.last_tile)
    add("kong_draw","杠上花",1,ctx.self_draw and ctx.replacement=="kong")
    add("rob_kong","抢杠胡",1,ctx.rob_kong)
    if shape.kind == "standard":
        _standard(ctx,shape,all_tiles,add)
    result = tuple(fans[key] for key in sorted(fans))
    value = sum(f.value for f in result)
    return ScoreResult(True,result,value,value,shape.kind,rules.version)


def _standard(ctx,shape,all_tiles,add):
    ms = shape.melds
    pungs = {m.tile for m in ms if m.kind != "sequence"}
    dragons,winds = pungs & set(DRAGONS),pungs & set(WINDS)
    chows = [m for m in ms if m.kind == "sequence"]
    all_pungs = len(chows)==0
    add("all_chows","平胡",1,len(chows)==4)
    add("all_pungs","对对胡",3,all_pungs)
    add("pair_258","将",1,shape.pair in NUMBERS and shape.pair%10 in (2,5,8))
    full_258 = all_pungs and all(t in NUMBERS and t%10 in (2,5,8) for t in all_tiles)
    add("pungs_258","全将碰",20,full_258)
    ranks = {t%10 if t in NUMBERS else 10 if t in WINDS else 11 for t in all_tiles}
    add("two_numbers","两数",20,all_pungs and len(ranks)==2)
    add("three_numbers","三数",10,all_pungs and len(ranks)==3 and not full_258)
    add("little_dragons","小三元",5,len(dragons)==2 and shape.pair in DRAGONS)
    add("big_dragons","大三元",10,len(dragons)==3)
    add("little_winds","小四喜",20,len(winds)==3 and shape.pair in WINDS)
    add("big_winds","大四喜",40,len(winds)==4)
    for tile in sorted(dragons):
        add(f"dragon_{tile}","三元牌",1)
    add("seat_wind","门风",1,ctx.seat_wind in pungs)
    add("round_wind","圈风",1,ctx.round_wind in pungs)
    kongs = sum(m.kind == "kong" for m in ms)
    if kongs>=2:
        add("kongs",f"{kongs}杠",{2:1,3:10,4:40}[kongs])
    concealed = len(shape.concealed_pungs(ctx.self_draw))
    if concealed>=3:
        add("concealed_pungs",f"{concealed}暗刻",2 if concealed==3 else 30)
    if len(ctx.melds)==4 and all(c[0] in "sk" for c in ctx.melds):
        add("begging","半求" if ctx.self_draw else "全求",2 if ctx.self_draw else 3)
    add("four_called_pungs","清四碰",1,len(ctx.melds)==4 and all(c[0]=="k" for c in ctx.melds))
    groups = [m.tiles for m in ms] + [(shape.pair,shape.pair)]
    shared = set.intersection(*[{t%10 for t in g if t in NUMBERS} for g in groups])
    add("full_rank","全带",10,bool(shared))
    outside = all(any(t in ORPHANS for t in g) for g in groups)
    has_honors = any(t in HONORS for t in all_tiles)
    add("outside","浑带么" if has_honors else "清么",2 if has_honors else 3,outside)
    add("pure_old_heads","清老头",40,all_pungs and all(t in NUMBERS and t%10 in (1,9) for t in all_tiles))
    add("mixed_old_heads","混老头",2,all_pungs and has_honors and any(t in NUMBERS for t in all_tiles) and set(all_tiles)<=ORPHANS)
    for suit in (1,2,3):
        concealed_chows = Counter(m.tile%10 for m in chows if not m.external and m.tile//10==suit)
        add(f"straight_{suit}","一条龙",3,all(concealed_chows[r] for r in (2,5,8)))
        pung_ranks = sorted(t%10 for t in pungs if t//10==suit)
        longest = max((n for n in (3,4) if any(set(range(r,r+n))<=set(pung_ranks) for r in range(1,11-n))),default=0)
        if longest:
            add(f"linked_{suit}",f"{longest}连刻",2 if longest==3 else 40)
        chow_ranks = {m.tile%10 for m in chows if m.tile//10==suit}
        add(f"old_young_{suit}","老少",1,{2,8}<=chow_ranks or {1,9}<=set(pung_ranks))
    concealed_chows = Counter(m.tile for m in chows if not m.external)
    double_count = sum(c//2 for c in concealed_chows.values())
    if double_count>=2:
        add("double_identical","双龙抱",10)
    elif double_count:
        add("identical_chows","般高",1)
    for rank in range(1,10):
        same_pungs = all(s*10+rank in pungs for s in (1,2,3))
        same_chows = all(any(m.tile==s*10+rank for m in chows) for s in (1,2,3))
        add(f"same_pungs_{rank}","三色同刻",2,same_pungs)
        add(f"three_meet_{rank}","三相逢",3,same_pungs or same_chows)
    if shape.winning_component>=0:
        m = ms[shape.winning_component]
        add("middle_five","卡五",1,m.kind=="sequence" and m.tile==ctx.winning_tile and m.tile%10==5)
    for tile,c in Counter(all_tiles).items():
        if c==4:
            used = [g for g in groups if tile in g]
            if len(used)==2 and any(g.count(tile)==3 for g in used):
                add(f"return_one_{tile}","四归一",1)
            elif len(used)==3 and shape.pair==tile:
                add(f"return_two_{tile}","四归二",2)


def new13_payments(fan,winner,*,discarder=None,liability=None,full_shoot=True,nine_exposed=False):
    """Normalized platform unit = one base point plus additive table fan.

    Source payment ratios: ron 4× (or 2+1+1), tsumo 2× each. Currency, service
    fees and proprietary room stakes are not part of this competitive profile.
    """
    if fan<0 or winner not in range(4):
        raise ValueError("Invalid new-style score")
    unit = fan+1
    delta = [0]*4
    if discarder is not None:
        if full_shoot or nine_exposed:
            delta[discarder] = -4*unit
        else:
            delta = [-unit]*4
            delta[winner] = 0
            delta[discarder] = -2*unit
    elif liability is not None:
        delta[liability] = -6*unit
    else:
        delta = [-2*unit]*4
        delta[winner] = 0
    if delta[winner]:
        raise ValueError("Winner cannot pay itself")
    delta[winner] = -sum(delta)
    return delta
