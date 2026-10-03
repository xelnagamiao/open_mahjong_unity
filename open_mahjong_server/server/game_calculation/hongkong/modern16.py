"""HKMA sixteen-tile scoring using the detailed table, with versioned errata.

The public book contains conflicting summary values. This profile uses the
detailed definitions; missing entries are filled from the summary, as recorded
in the platform rulebook. Numerical additions and pattern selection are pure.
"""

from collections import Counter
from itertools import combinations

from .models import Fan, HandContext, HongKongRules, ScoreResult, NUMBERS, HONORS, WINDS, DRAGONS, ORPHANS
from .solver import decompositions, parse_meld, structural_waits
from .patterns import best_local_patterns, mask_for


def _closed_meld(ctx, shape, index):
    return shape.melds[index].concealed and (ctx.self_draw or index != shape.winning_component)


def _groups(ctx, shape):
    if shape.kind == "standard":
        return [m.tiles for m in shape.melds] + [(shape.pair, shape.pair)]
    counter = Counter(ctx.hand)
    if shape.kind == "orphans":
        for meld in shape.melds:
            if not meld.external:
                counter.subtract(meld.tiles)
        return [m.tiles for m in shape.melds] + [(t,) * c for t, c in sorted(counter.items()) if c]
    if shape.kind == "likugu":
        return [g for t,c in sorted(counter.items()) for g in ([(t,t),(t,t)] if c==4 else [(t,)*c])]
    return [(t,) * c for t, c in sorted(counter.items())]


def _local_meld_patterns(ctx, shape):
    candidates = []
    exclusions = set()
    def add(key, name, value, indices):
        candidates.append(Fan(key, name, value, mask_for(indices)))
    chows = [i for i, m in enumerate(shape.melds) if m.kind == "sequence"]
    pungs = [i for i, m in enumerate(shape.melds) if m.kind != "sequence"]
    for size in range(2, 6):
        for indices in combinations(chows, size):
            ms = [shape.melds[i] for i in indices]
            values = sorted(m.tile for m in ms)
            ranks = sorted(m.tile % 10 for m in ms)
            same_suit = len({v // 10 for v in values}) == 1
            same_rank = len(set(ranks)) == 1
            dark = all(_closed_meld(ctx, shape, i) for i in indices)
            mult = 2 if dark else 1
            prefix = "暗" if dark else "明"
            if size == 2:
                if len(set(values)) == 1:
                    add("identical_chows", "一般高", 5, indices)
                elif same_rank:
                    add("mixed_chows", "相逢", 3, indices)
                if same_suit and ranks == [2, 8]:
                    add("terminal_chows", "老少顺", 3, indices)
            if size in (3, 4) and len(set(values)) == 1:
                add(f"identical_chows_{size}", prefix + ("三般高" if size == 3 else "四般高"),
                    (30 if size == 3 else 180) * mult, indices)
            if size == 3:
                if ranks == [2, 5, 8] and (same_suit or len({v // 10 for v in values}) == 3):
                    add("pure_straight" if same_suit else "mixed_straight", prefix + ("清龙" if same_suit else "杂龙"),
                        (15 if same_suit else 10) * mult, indices)
                if same_rank and len({v // 10 for v in values}) == 3:
                    add("three_mixed_chows", prefix + "三相逢", 10 * mult, indices)
            if size == 4:
                # Three partitions of four groups into two disjoint pairs.
                a, b, c, d = indices
                for left, right in (((a,b),(c,d)), ((a,c),(b,d)), ((a,d),(b,c))):
                    pairs = [[shape.melds[i].tile for i in group] for group in (left,right)]
                    if all(x == y for x,y in pairs):
                        add("double_identical_chows", "双般高", 20, indices)
                    if all(x % 10 == y % 10 and x // 10 != y // 10 for x,y in pairs):
                        add("double_mixed_chows", "双同顺", 10, indices)
                    if all(x // 10 == y // 10 and sorted((x%10,y%10)) == [2,8] for x,y in pairs):
                        add("double_terminal_chows", "双老少顺", 15, indices)
            if size >= 3:
                ordered = sorted(values, key=lambda x: x % 10)
                step = ranks[1] - ranks[0]
                consecutive = step in (1,2) and all(b-a == step for a,b in zip(ranks,ranks[1:]))
                if consecutive and same_suit:
                    if step == 1:
                        add(f"pure_steps_{size}", prefix + f"一色{size}步高", {3:20,4:60,5:200}[size] * mult, indices)
                    elif size in (3,4):
                        add(f"pure_jumps_{size}", prefix + ("连环套" if size == 3 else "金门大桥"),
                            {3:15,4:50}[size] * mult, indices)
                if consecutive and step == 1 and _cyclic_suits(ordered):
                    add(f"mixed_steps_{size}", prefix + f"三色{size}步高", {3:5,4:15,5:50}[size] * mult, indices)
    # The detailed book expressly prohibits lower step patterns even when
    # they are not logically implied by the selected larger step pattern.
    for family in ("pure_steps", "mixed_steps", "pure_jumps"):
        for high in (4,5):
            for low in range(3,high):
                exclusions.add((f"{family}_{high}", f"{family}_{low}"))

    for size in range(2, 6):
        for indices in combinations(pungs, size):
            values = [shape.melds[i].tile for i in indices]
            if any(t not in NUMBERS for t in values):
                continue
            ranks = sorted(t % 10 for t in values)
            same_suit = len({t // 10 for t in values}) == 1
            if size == 2:
                if same_suit and ranks == [1,9]:
                    add("terminal_pungs", "老少刻", 5, indices)
                if len(set(ranks)) == 1:
                    add("same_pungs_2", "两同刻", 5, indices)
            if size == 3 and len(set(ranks)) == 1:
                add("same_pungs_3", "大三同刻", 40, indices)
            if all(b-a == 1 for a,b in zip(ranks,ranks[1:])):
                if same_suit:
                    add(f"linked_pungs_{size}", ("两连刻" if size == 2 else f"大{size}连刻"),
                        {2:5,3:30,4:90,5:240}[size], indices)
                elif size >= 3 and _cyclic_suits(sorted(values, key=lambda t:t%10)):
                    add(f"mixed_linked_pungs_{size}", f"三色{size}连刻", {3:20,4:60,5:180}[size], indices)
            if size == 4:
                a,b,c,d = indices
                for left,right in (((a,b),(c,d)),((a,c),(b,d)),((a,d),(b,c))):
                    pairs = [[shape.melds[i].tile for i in group] for group in (left,right)]
                    if all(x%10 == y%10 for x,y in pairs):
                        add("double_same_pungs", "双同刻", 20, indices)
                    if all(x//10 == y//10 and abs(x-y) == 1 for x,y in pairs):
                        add("double_linked_pungs", "双连刻", 20, indices)
                    if all(x//10 == y//10 and sorted((x%10,y%10)) == [1,9] for x,y in pairs):
                        add("double_terminal_pungs", "双老少刻", 30, indices)
    if shape.pair in NUMBERS:
        pair_index = len(shape.melds)
        for size in range(2,6):
            for indices in combinations(pungs, size):
                values = [shape.melds[i].tile for i in indices] + [shape.pair]
                if any(t not in NUMBERS for t in values):
                    continue
                ranks = sorted(t%10 for t in values)
                ids = indices + (pair_index,)
                if size == 2 and len(set(ranks)) == 1 and len(set(values)) == 3:
                    add("little_same_pungs_3", "小三同刻", 20, ids)
                if all(b-a == 1 for a,b in zip(ranks,ranks[1:])):
                    if len({t//10 for t in values}) == 1:
                        add(f"little_linked_pungs_{size+1}", f"小{size+1}连刻", {2:15,3:60,4:150,5:480}[size], ids)
                    elif _cyclic_suits(sorted(values,key=lambda t:t%10)):
                        add(f"little_mixed_pungs_{size+1}", f"小三色{size+1}连刻", {2:10,3:40,4:90,5:260}[size], ids)
    return best_local_patterns(candidates, exclusions)


def _cyclic_suits(ordered):
    suits = [v//10 for v in ordered]
    return len(suits) >= 3 and len(set(suits[:3])) == 3 and all(s == suits[i%3] for i,s in enumerate(suits))


def _score_shape(ctx, rules, shape, waits):
    fans = {}
    def add(key,name,value,condition=True):
        if condition:
            fans[key] = Fan(key,name,value)
    def remove(*keys):
        for key in keys:
            fans.pop(key,None)
    all_tiles = ctx.hand + tuple(t for c in ctx.melds for t in parse_meld(c).tiles)
    counter = Counter(all_tiles)
    suits = {t//10 for t in all_tiles if t in NUMBERS}
    honors = any(t in HONORS for t in all_tiles)
    groups = _groups(ctx,shape)
    closed = all(c[0] in "gG" for c in ctx.melds)
    add("base", "底", 5)
    add("dealer", "庄家", 1, ctx.dealer)
    add("dealer_streak", "拉庄", 2*ctx.dealer_streak, ctx.dealer and ctx.dealer_streak > 0)
    add("self_draw", "自摸", 1, ctx.self_draw)
    add("concealed", "门清", 5, closed)
    if closed and ctx.self_draw:
        add("concealed_self_draw", "门摸", 5)
        remove("self_draw")
    if ctx.ready:
        if ctx.ready in ("heaven", "earth", "human"):
            add("ready_"+ctx.ready, {"heaven":"天叮","earth":"地叮","human":"人叮"}[ctx.ready],
                {"heaven":40,"earth":30,"human":20}[ctx.ready])
            if ctx.ready != "human":
                remove("concealed")
        elif closed:
            add("closed_ready", "门叮", 15)
            remove("concealed")
        else:
            add("ready", "叮牌", 5)
        add("immediate", "即食", 5, ctx.immediate)
    add("last_tile", "海底捞月", 10, ctx.last_tile)
    add("rob_kong", "抢杠", 10, ctx.rob_kong)
    add("multi_ron", "三响" if ctx.multi_ron == 3 else "双响", 10 if ctx.multi_ron == 3 else 5, ctx.multi_ron > 1)
    if ctx.self_draw and ctx.tail_draws > 1:
        add("tail_chain", "尾牌连摸", 5 * 2**(ctx.tail_draws-1))
        remove("self_draw")
    elif ctx.self_draw and ctx.replacement:
        add("replacement", "杠上自摸" if ctx.replacement == "kong" else "花上自摸", 5 if ctx.replacement == "kong" else 2)
        remove("self_draw")
    if ctx.heavenly or ctx.earthly or ctx.humanly:
        key = "heavenly" if ctx.heavenly else "earthly" if ctx.earthly else "humanly"
        add(key, {"heavenly":"天糊","earthly":"地糊","humanly":"人糊"}[key], 80 if key == "humanly" else 120)
        remove("concealed")
        if ctx.heavenly:
            remove("self_draw", "concealed_self_draw")
    else:
        for n,value in ((4,40),(7,20),(10,10)):
            if ctx.river_count <= n:
                add("early_river",f"{n}只内",value)
                break
    add("no_honors", "无字", 1, not honors)
    add("no_flowers", "无花", 1, not ctx.flowers)
    if not honors and not ctx.flowers:
        add("no_honors_flowers", "无字花", 5)
        remove("no_honors","no_flowers")
    _flowers(ctx,add)
    add("half_flush", "混一色", 40, len(suits) == 1 and honors)
    add("full_flush", "清一色", 120, len(suits) == 1 and not honors)
    add("all_honors", "字一色", 120, not suits)
    add("missing_suit", "缺一门", 10, len(suits) == 2 and not honors)
    add("missing_five", "缺五", 10, not honors and not any(t%10 == 5 for t in all_tiles))
    add("simples", "断么九", 10, not honors and all(2 <= t%10 <= 8 for t in all_tiles))
    outside = all(any(t in ORPHANS for t in group) for group in groups)
    pure_outside = outside and not honors
    all_terminals = all(t in ORPHANS for t in all_tiles)
    if all_terminals:
        add("pure_terminals" if not honors else "mixed_terminals", "清么九" if not honors else "混么九", 420 if not honors else 80)
    elif outside:
        add("pure_outside" if pure_outside else "mixed_outside", "全带么" if pure_outside else "混带么", 80 if pure_outside else 40)
    group_numbers = [{t%10 for t in g if t in NUMBERS} or set(range(1,10)) for g in groups]
    shared_rank = set.intersection(*group_numbers) if group_numbers else set()
    if shared_rank and suits:
        add("full_rank" if not honors else "mixed_rank", "满庭芳" if not honors else "混满庭芳", 120 if not honors else 40)
    if "all_honors" in fans:
        # All-honors necessarily implies mixed terminals. The book's general
        # non-compounding rule removes the lower whole-hand award.
        remove("mixed_terminals")
    if any(k in fans for k in ("full_flush","missing_suit","missing_five","simples","pure_outside","pure_terminals","full_rank")):
        remove("no_honors")
    if "pure_outside" in fans or "pure_terminals" in fans:
        remove("missing_five")
    categories = {_category(t) for t in all_tiles}
    if len(categories) == 5:
        meld_categories = {_category(m.tile) for m in shape.melds}
        big = len(meld_categories) == 5
        seven = any(f <= 54 for f in ctx.flowers) and any(f >= 55 for f in ctx.flowers)
        add("seven_categories" if seven else "five_categories", ("大" if big else "小") + ("七门齐" if seven else "五门齐"),
            (30 if big else 15) if seven else (20 if big else 10))
    if shape.kind == "standard":
        _standard(ctx,shape,waits,add,remove,fans)
        locals_ = _local_meld_patterns(ctx,shape)
        for i,fan in enumerate(locals_):
            fans[f"local_{i}"] = fan
        ids = {f.id for f in locals_}
        if ids & {"pure_steps_5","mixed_steps_5"}:
            remove("all_chows")
        if ids & {"linked_pungs_5","little_linked_pungs_6","mixed_linked_pungs_5","little_mixed_pungs_6"}:
            remove("all_pungs")
        if "little_linked_pungs_6" in ids:
            remove("full_flush")
    else:
        remove("concealed")
        _special(ctx,rules,shape,waits,add,remove)
    _returns(ctx,shape,groups,add)
    # Big chicken/duck is evaluated only after maximal interpretation. These
    # candidates are marked here and selected across shapes by score_modern16.
    result = tuple(fans.values())
    return result


def _category(t):
    return t//10 if t in NUMBERS else (4 if t in WINDS else 5)


def _flowers(ctx,add):
    flower_set = set(ctx.flowers)
    if len(flower_set) == 8:
        add("eight_flowers", "两台花", 40)
        return
    covered = set()
    for start in (51,55):
        group = set(range(start,start+4))
        if group <= flower_set:
            add(f"flower_set_{start}", "一台花", 10)
            covered |= group
    for tile in sorted(flower_set-covered):
        add(f"flower_{tile}", "花", 1)
        add(f"seat_flower_{tile}", "正花", 1, (tile-51)%4 == ctx.seat_wind-41)


def _meld_points(ctx,shape,add,remove):
    pungs = {m.tile for m in shape.melds if m.kind != "sequence"}
    dragons, winds = pungs & set(DRAGONS), pungs & set(WINDS)
    for tile in sorted(pungs & set(HONORS)):
        add(f"honor_{tile}", "字牌", 1)
        add(f"dragon_{tile}", "正字", 1, tile in DRAGONS)
    add("seat_wind", "正字（门风）", 1, ctx.seat_wind in pungs)
    add("round_wind", "正字（圈风）", 1, ctx.round_wind in pungs)
    if len(dragons) == 3 or (len(dragons) == 2 and shape.pair in DRAGONS):
        big = len(dragons) == 3
        add("big_dragons" if big else "little_dragons", "大三元" if big else "小三元", 80 if big else 40)
        for tile in dragons:
            remove(f"honor_{tile}",f"dragon_{tile}")
    wind_kind = None
    if len(winds) == 4:
        wind_kind = ("big_winds","大四喜",160)
    elif len(winds) == 3 and shape.pair in WINDS:
        wind_kind = ("little_winds","小四喜",120)
    elif len(winds) == 3:
        wind_kind = ("three_winds","大三风",60)
    elif len(winds) == 2 and shape.pair in WINDS:
        wind_kind = ("little_three_winds","小三风",30)
    if wind_kind:
        add(*wind_kind)
        for tile in winds:
            remove(f"honor_{tile}")
        # Three-wind and little-four-wind descriptions retain matching winds;
        # big four winds explicitly excludes both ordinary and matching winds.
        if wind_kind[0]=="big_winds":
            remove("seat_wind","round_wind")
    kongs = sum(m.kind == "kong" for m in shape.melds)
    add("kongs", "杠", kongs*2, kongs > 0)


def _standard(ctx,shape,waits,add,remove,fans):
    _meld_points(ctx,shape,add,remove)
    kongs = sum(m.kind == "kong" for m in shape.melds)
    add("all_pungs", "对对糊", 40, all(m.kind != "sequence" for m in shape.melds))
    concealed = len(shape.concealed_pungs(ctx.self_draw))
    if concealed == 5 and kongs == 0:
        add("five_pure_pungs","坎坎糊",240)
        remove("all_pungs","concealed")
    elif concealed >= 2:
        add("concealed_pungs",f"{concealed}暗刻",{2:5,3:20,4:60,5:180}[concealed])
        if concealed == 5:
            remove("all_pungs")
    all_chows = all(m.kind == "sequence" for m in shape.melds)
    add("all_chows", "平糊", 5, all_chows)
    if all_chows and "no_honors_flowers" in fans:
        add("big_chows","无字花平糊",15)
        remove("all_chows","no_honors_flowers")
    ranks = Counter(m.tile%10 for m in shape.melds if m.kind == "sequence")
    largest = max(ranks.values(),default=0)
    if largest >= 4:
        add("same_rank_chows", "五同顺" if largest == 5 else "四同顺",60 if largest == 5 else 20)
        if largest == 5:
            remove("all_chows")
    add("pair_258","将眼",2,shape.pair in NUMBERS and shape.pair%10 in (2,5,8))
    single = False
    if shape.winning_component == -1:
        single = True
    else:
        meld = shape.melds[shape.winning_component]
        if meld.kind == "triplet":
            add("pung_wait","对碰",1)
        elif meld.kind == "sequence":
            single = (ctx.winning_tile == meld.tile or (meld.tile%10 == 2 and ctx.winning_tile%10 == 3)
                      or (meld.tile%10 == 8 and ctx.winning_tile%10 == 7))
    if single:
        add("single_wait" if len(waits)==1 else "apparent_single", "独独" if len(waits)==1 else "假独",2 if len(waits)==1 else 1)
    if len(ctx.melds)==5 and all(code[0] in "sk" for code in ctx.melds):
        add("half_begging" if ctx.self_draw else "all_begging", "半求人" if ctx.self_draw else "全求人",20 if ctx.self_draw else 40)
        remove("single_wait","self_draw")


def _special(ctx,rules,shape,waits,add,remove):
    before = Counter(ctx.hand)
    before[ctx.winning_tile] -= 1
    if shape.kind == "orphans":
        # The seventeenth-tile meld still scores its honor/kong awards. The
        # orphan singles are not pungs, unlike this additional actual meld.
        _meld_points(ctx,shape,add,remove)
        add("orphans","十三么九",100)
        add("orphans_wait","十三扉十三么",20,len(waits)>=13)
        remove("five_categories")
    elif shape.kind == "sixteen_unconnected":
        add("unconnected","十六不搭",50)
        add("unconnected_wait","十六扉不搭",20,len(waits)>=16)
        remove("five_categories","seven_categories")
        ranks = [{t%10 for t in ctx.hand if t//10==s} for s in (1,2,3)]
        add("unconnected_same","不搭三相",20,ranks[0]==ranks[1]==ranks[2])
        add("unconnected_straight","不搭杂龙",30,set.union(*ranks)==set(range(1,10)))
    elif shape.kind == "likugu":
        add("likugu","嚦咕嚦咕",40)
        add("eight_pairs_ready","八对嚦咕",10,all(c%2==0 for c in before.values()))
        counter = Counter(ctx.hand)
        add("dragon_pairs","三元嚦咕",20,all(counter[t]>=2 for t in DRAGONS))
        add("wind_pairs","四喜嚦咕",40,all(counter[t]>=2 for t in WINDS))
        for rank in range(1,10):
            add(f"same_pairs_{rank}","三色同对",10,all(counter[s*10+rank]>=2 for s in (1,2,3)))
        pair_tiles = sorted(t for t,c in counter.items() if c>=2 and t in NUMBERS)
        candidates = []
        for n in range(3,9):
            for ids in combinations(range(len(pair_tiles)),n):
                values = [pair_tiles[i] for i in ids]
                if values[0]//10 == values[-1]//10 and all(b-a==1 for a,b in zip(values,values[1:])):
                    candidates.append(Fan(f"pairs_run_{n}",f"{n}连对",{3:5,4:15,5:30,6:60,7:120,8:420}[n],mask_for(ids)))
        for i,fan in enumerate(best_local_patterns(candidates)):
            add(f"pair_run_{i}",fan.name,fan.value)
            if fan.id == "pairs_run_8":
                remove("full_flush")


def _returns(ctx,shape,groups,add):
    count = Counter(t for g in groups for t in g)
    returns = 0
    for tile,c in count.items():
        if c != 4:
            continue
        ids = [i for i,g in enumerate(groups) if tile in g]
        if not 2 <= len(ids) <= 4:
            continue
        dark = (ctx.self_draw or ctx.winning_tile != tile) and all(
            i >= len(shape.melds) or _closed_meld(ctx,shape,i) for i in ids)
        add(f"return_{tile}", ("暗" if dark else "明")+f"四归{len(ids)}", {2:5,3:15,4:60}[len(ids)]*(2 if dark else 1))
        returns += 1
    add("eight_returns","八归",20,returns==2)
    add("twelve_returns","十二归",80,returns>=3)


def score_modern16(ctx: HandContext, rules: HongKongRules) -> ScoreResult:
    if len(set(ctx.flowers)) != len(ctx.flowers) or any(t not in range(51,59) for t in ctx.flowers):
        return ScoreResult(rule_version=rules.version)
    if ctx.flower_win:
        n = {"seven_steal":7,"eight":8}.get(ctx.flower_win,0)
        if n != len(ctx.flowers) or n == 0:
            return ScoreResult(rule_version=rules.version)
        fan = Fan("flower_win", "七抢一" if n==7 else "两台花",30 if n==7 else 40)
        return ScoreResult(True,(fan,),fan.value,fan.value,"flower",rules.version)
    shapes = decompositions(ctx.hand,ctx.melds,rules,ctx.winning_tile)
    if not shapes:
        return ScoreResult(rule_version=rules.version)
    before = list(ctx.hand)
    if ctx.winning_tile not in before:
        return ScoreResult(rule_version=rules.version)
    before.remove(ctx.winning_tile)
    waits = structural_waits(before,ctx.melds,rules)
    choices = [(shape.kind,_score_shape(ctx,rules,shape,waits)) for shape in shapes]
    best = max(choices,key=lambda x:sum(f.value for f in x[1]))
    normal = [x for x in choices if x[0]=="standard"]
    likugu = [x for x in choices if x[0]=="likugu"]
    if normal and likugu:
        a = max(normal,key=lambda x:sum(f.value for f in x[1]))[1]
        b = max(likugu,key=lambda x:sum(f.value for f in x[1]))[1]
        # One base only. All other shared awards are allowed twice by §嚦咕两食.
        merged = a + tuple(Fan("likugu_"+f.id,"嚦咕·"+f.name,f.value,f.groups) for f in b if f.id != "base")
        best = ("likugu_double",merged)
    fans = best[1]
    # Exclude base/dealer from the big-chicken eligibility test. Check the
    # maximal *ordinary* interpretation, not a deliberately low decomposition.
    ordinary = [f for f in fans if f.id not in ("base","dealer","dealer_streak")]
    chicken = len(ordinary)==1 and ordinary[0].id.startswith("flower_") and ordinary[0].value==1
    duck = len(ordinary)==2 and any(f.id=="self_draw" for f in ordinary) and any(f.id.startswith("flower_") and f.value==1 for f in ordinary)
    if chicken or duck:
        fans += (Fan("big_duck" if duck else "big_chicken","大鸭糊" if duck else "大鸡糊",20 if duck else 40),)
    total = sum(f.value for f in fans)
    return ScoreResult(True,fans,total,total,best[0],rules.version)
