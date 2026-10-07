"""香港新派清章（恋绘色魔改版）: fan table plus additive points.

Source document: other/rule/hongkong/lianhuise-qingzhang-remix.docx.
Enumerate every decomposition, including duplicated pairs and knitted hands,
then choose the highest legal basic score. Flower awards never qualify a win.
"""
from collections import Counter
from itertools import combinations, permutations

from .models import Fan, ScoreResult, NUMBERS, HONORS, WINDS, DRAGONS, ORPHANS
from .solver import decompositions, parse_meld, structural_waits
from .old_style import nine_gates

FAN_POINTS = (0, 15, 30, 50, 75, 100, 125, 150, 180, 210, 240, 280, 320, 360, 420, 480, 540)


def round_five(points):
    return ((points + 2) // 5) * 5


def basic_points(fans, extras):
    limits = [f for f in fans if f.value >= 14]
    if len(limits) >= 2 or any(f.value >= 16 for f in limits):
        return sum(420 if f.value == 14 else 540 if f.value == 16 else 1080 for f in limits)
    return min(540, round_five(FAN_POINTS[min(16, sum(f.value for f in fans))] + sum(f.value for f in extras)))


def _flowers(ctx):
    flowers = set(ctx.flowers)
    fans, extras = [], []
    if len(flowers) == 8:
        return [Fan("flower_eight", "大花和", 7, unit="番")], []
    if len(flowers) == 7:
        fans.append(Fan("flower_seven", "小花和", 4, unit="番"))
    for start in (51, 55):
        group = set(range(start, start+4))
        if group <= flowers:
            if len(flowers) != 7:
                fans.append(Fan(f"flower_set_{start}", "一台花", 2, unit="番"))
            continue
        for tile in sorted(flowers & group):
            own = tile == start + ctx.seat_wind - 41
            (fans if own else extras).append(Fan(f"flower_{tile}", "正花" if own else "偏花", 1 if own else 4, unit="番" if own else "分"))
    return fans, extras


def _steps(tiles, count, same_suit, increments):
    for group in combinations(tiles, count):
        if len({t//10 for t in group}) != (1 if same_suit else count):
            continue
        ranks = sorted(t%10 for t in group)
        if any(all(b-a == step for a,b in zip(ranks, ranks[1:])) for step in increments):
            return True
    return False


def _score(ctx, rules, shape, waits):
    fans, extras = {}, {}
    def add(key, name, value, condition=True):
        if condition:
            fans[key] = Fan(key, name, value, unit="番")
    def extra(key, name, value, condition=True):
        if condition:
            extras[key] = Fan(key, name, value, unit="分")
    all_tiles = ctx.hand + tuple(t for c in ctx.melds for t in parse_meld(c).tiles)
    counts = Counter(all_tiles)
    suits = {t//10 for t in all_tiles if t in NUMBERS}
    honors = bool(set(all_tiles) & set(HONORS))
    closed = all(c[0] == "G" for c in ctx.melds)
    standard = shape.kind == "standard"
    ms = shape.melds
    pungs = {m.tile for m in ms if m.kind != "sequence"}
    chows = [m.tile for m in ms if m.kind == "sequence"]
    winds, dragons = pungs & set(WINDS), pungs & set(DRAGONS)
    concealed = set(shape.concealed_pungs(ctx.self_draw))
    kongs = sum(m.kind == "kong" for m in ms)
    value_pair = shape.pair in DRAGONS or shape.pair in (ctx.seat_wind, ctx.round_wind)
    groups = [m.tiles for m in ms] + [(shape.pair,)]
    outside = standard and all(set(g) & ORPHANS for g in groups)
    term_honors = set(all_tiles) <= ORPHANS
    categories = {t//10 if t in NUMBERS else 4 if t in WINDS else 5 for t in all_tiles}

    add("simples", "断幺九", 1, not (set(all_tiles) & ORPHANS))
    add("all_chows", "平和", 1, len(chows) == 4 and not value_pair)
    add("missing_suit", "缺一门", 1, len(suits) == 2 and not honors)
    add("begging", "全求人", 1, standard and len(ctx.melds) == 4 and not closed and all(c[0] != "G" for c in ctx.melds) and not ctx.self_draw)
    add("closed_self", "不求人", 2, closed and ctx.self_draw)
    add("kong_draw", "杠上开花", 2, ctx.self_draw and ctx.replacement == "kong")
    add("last_tile", "海底捞月", 2, ctx.last_tile)
    add("rob_kong", "抢杠", 2, ctx.rob_kong)
    add("all_pungs", "对对和", 3, standard and not chows)
    add("half_flush", "混一色", 3, len(suits) == 1 and honors)
    add("mixed_outside", "混带幺", 3, outside and honors)
    add("five_categories", "五门齐", 3, len(categories) == 5)
    add("seven_pairs", "七对", 3, shape.kind == "seven_pairs")
    add("knitted", "十三不靠", 3, shape.kind == "knitted")
    add("pure_outside", "清带幺", 4, outside and not honors)
    add("terminals_honors", "花幺", 5, term_honors)
    add("full_flush", "清一色", 7, len(suits) == 1 and not honors)
    add("little_dragons", "小三元", 4, len(dragons) == 2 and shape.pair in DRAGONS)
    add("humanly", "人和", 7, ctx.humanly and not ctx.self_draw and not ctx.melds)
    add("little_three_winds", "小三风", 3, len(winds) == 2 and shape.pair in WINDS)
    add("big_three_winds", "大三风", 9, len(winds) == 3)
    add("big_dragons", "大三元", 14, len(dragons) == 3)
    add("four_concealed", "四暗刻", 14, standard and len(concealed) == 4)
    add("orphans", "十三幺", 14, shape.kind == "orphans")
    add("little_winds", "小四喜", 16, len(winds) == 3 and shape.pair in WINDS)
    add("all_honors", "字一色", 16, not suits)
    add("all_terminals", "清幺", 16, term_honors and not honors)
    add("heavenly", "天和", 16, ctx.heavenly and not ctx.melds)
    add("earthly", "地和", 16, ctx.earthly and ctx.self_draw and not ctx.melds)
    add("big_winds", "大四喜", 32, len(winds) == 4)
    add("four_kongs", "四杠子", 32, kongs == 4)
    if kongs in (2,3):
        add("kongs", "两杠子" if kongs == 2 else "三杠子", 2 if kongs == 2 else 9)
    if len(concealed) in (2,3):
        add("concealed_pungs", "两暗刻" if len(concealed) == 2 else "三暗刻", 1 if len(concealed) == 2 else 3)
    for tile in sorted(dragons):
        add(f"dragon_{tile}", "三元牌", 1)
    add("seat_wind", "门风刻", 1, ctx.seat_wind in pungs)
    add("round_wind", "圈风刻", 1, ctx.round_wind in pungs)

    chow_counts = Counter(chows)
    repeated = max(chow_counts.values(), default=0)
    pairs = sum(n//2 for n in chow_counts.values())
    if repeated == 4:
        add("four_identical", "四般高", 32)
    elif repeated == 3:
        add("three_identical", "三般高", 9)
    elif pairs == 2:
        add("two_identical", "两般高", 5 if closed else 4)
    elif pairs:
        add("identical", "一般高", 1)
    for suit in (1,2,3):
        add(f"straight_{suit}", "清龙", 4 if closed else 3, {suit*10+r for r in (2,5,8)} <= set(chows))
    add("mixed_straight", "花龙", 3 if closed else 2, any(all(s*10+r in chows for s,r in zip(order,(2,5,8))) for order in permutations((1,2,3))))
    add("mixed_steps", "三色三步高", 2, _steps(chows,3,False,(1,)))
    add("pure_steps", "一色三步高", 5, _steps(chows,3,True,(1,2)))
    add("four_steps", "一色四步高", 9, _steps(chows,4,True,(1,2)))
    numeric_pungs = sorted(pungs & set(NUMBERS))
    add("mixed_pung_steps", "三色三节高", 3, _steps(numeric_pungs,3,False,(1,)))
    add("pure_pung_steps", "一色三节高", 7, _steps(numeric_pungs,3,True,(1,)))
    add("four_pung_steps", "一色四节高", 16, _steps(numeric_pungs,4,True,(1,)))
    add("same_chows", "三色同顺", 3, any(all(s*10+r in chows for s in (1,2,3)) for r in range(2,9)))
    add("same_pungs", "三色同刻", 9, any(all(s*10+r in pungs for s in (1,2,3)) for r in range(1,10)))

    gates = nine_gates(ctx, require_nine_wait_on_discard=False)
    if gates:
        before = Counter(ctx.hand); before[ctx.winning_tile] -= 1
        pure = all(before[next(iter(suits))*10+r] == (3 if r in (1,9) else 1) for r in range(1,10))
        add("pure_nine_gates" if pure else "nine_gates", "九莲宝灯" if pure else "九莲花灯", 32 if pure else 14)

    # Remove patterns forced by a stronger pattern; alternatives (e.g. the
    # pung/pair possibilities of 花幺) remain separate, as the book specifies.
    exclusions = {
        "four_concealed": ("all_pungs", "concealed_pungs"),
        "four_kongs": ("all_pungs", "kongs"),
        "big_dragons": tuple(f"dragon_{t}" for t in DRAGONS),
        "big_winds": ("big_three_winds", "seat_wind", "round_wind", "all_pungs"),
        "little_winds": ("big_three_winds",),
        "all_honors": ("terminals_honors", "mixed_outside"),
        "all_terminals": ("terminals_honors", "pure_outside"),
        "terminals_honors": ("mixed_outside", "pure_outside"),
        "orphans": ("terminals_honors", "five_categories"),
        "knitted": ("five_categories",),
        "four_steps": ("pure_steps",),
        "four_pung_steps": ("pure_pung_steps", "all_pungs"),
        "pure_nine_gates": ("full_flush", "nine_gates"),
        "nine_gates": ("full_flush",),
    }
    for key in tuple(fans):
        for excluded in exclusions.get(key, ()):
            fans.pop(excluded, None)
    inherent_closed = shape.kind != "standard" or any(key in fans for key in ("nine_gates", "pure_nine_gates", "four_concealed"))
    inherent_closed |= closed and any(key == "two_identical" or key == "mixed_straight" or key.startswith("straight_") for key in fans)
    if inherent_closed:
        fans.pop("closed_self", None)
    extra("self_draw", "自摸", 2, ctx.self_draw and not any(key in fans for key in ("closed_self", "kong_draw", "heavenly", "earthly")))
    extra("closed_ron", "门前清食和", 10, closed and not ctx.self_draw and not inherent_closed)
    extra("single_wait", "听一扉", 2, len(waits) == 1 and shape.kind != "seven_pairs" and "begging" not in fans)
    extra("dealer_streak", "连庄", 2*ctx.dealer_streak, ctx.dealer_streak > 0)
    extra("value_pair", "连风将牌" if shape.pair == ctx.seat_wind == ctx.round_wind else "番牌做将", 3 if shape.pair == ctx.seat_wind == ctx.round_wind else 2, standard and value_pair and "little_dragons" not in fans)
    for i,m in enumerate(ms):
        if m.kind == "sequence":
            continue
        terminal = m.tile in ORPHANS
        hidden = i in concealed
        if m.kind == "kong":
            value = (16 if terminal else 8) if hidden else (8 if terminal else 4)
        else:
            value = 4 if terminal and hidden else 0
        extra(f"meld_{i}", ("幺九" if terminal else "中张")+("暗" if hidden else "明")+("杠" if m.kind == "kong" else "刻"), value, value > 0)
    kong_tiles = {m.tile for m in ms if m.kind == "kong"}
    for tile,count in counts.items():
        extra(f"four_return_{tile}", "四归一", 3, count == 4 and tile not in kong_tiles and "four_identical" not in fans)

    flower_fans, flower_extras = _flowers(ctx)
    nonflower_fan = sum(f.value for f in fans.values())
    eligible_points = basic_points(list(fans.values()), list(extras.values()))
    all_fans = list(fans.values()) + flower_fans
    all_extras = list(extras.values()) + flower_extras
    points = basic_points(all_fans, all_extras)
    limits = [f for f in all_fans if f.value >= 14]
    if len(limits) >= 2 or any(f.value >= 16 for f in limits):
        displayed = limits
    else:
        displayed = all_fans + all_extras
    return ScoreResult(nonflower_fan >= 1 and eligible_points >= 30, tuple(displayed), sum(f.value for f in all_fans), points, shape.kind, rules.version)


def score_qingzhang_remix(ctx, rules):
    invalid = ScoreResult(rule_version=rules.version)
    if ctx.flower_win or len(set(ctx.flowers)) != len(ctx.flowers) or any(t not in range(51,59) for t in ctx.flowers):
        return invalid
    if ctx.winning_tile not in ctx.hand:
        return invalid
    shapes = decompositions(ctx.hand,ctx.melds,rules,ctx.winning_tile)
    if not shapes:
        return invalid
    before = list(ctx.hand); before.remove(ctx.winning_tile)
    waits = structural_waits(before,ctx.melds,rules)
    return max((_score(ctx,rules,s,waits) for s in shapes), key=lambda r:(r.is_win,r.fan,r.raw_fan,r.fan_ids))


def remix_payments(points, winner, *, discarder=None, liability=None, liability_kind="", head_bumped=()):
    if type(points) is not int or points < 30 or points % 5 or winner not in range(4):
        raise ValueError("Invalid remix basic points")
    if discarder is not None and (discarder not in range(4) or discarder == winner):
        raise ValueError("Invalid discarder")
    if liability is not None and (liability not in range(4) or liability == winner):
        raise ValueError("Invalid liable payer")
    if head_bumped and (discarder is None or
            any(type(i) is not int or i not in range(4) or i in (winner,discarder) for i in head_bumped) or
            len(set(head_bumped)) != len(head_bumped)):
        raise ValueError("Invalid head-bumped claimants")
    changes = [0]*4
    if liability is not None and (liability_kind == "twelve" or discarder is None or liability == discarder):
        changes[liability] = -3*points
    elif liability is not None:
        changes[liability], changes[discarder] = -2*points, -points
    elif discarder is None:
        changes = [-points if i != winner else 0 for i in range(4)]
    else:
        changes = [-30 if i != winner else 0 for i in range(4)]
        changes[discarder] = -(3*points-60)
        # Only an actual losing win declaration waives the ordinary 30-point
        # payment. Liability payments retain their separate rules above.
        for i in head_bumped:
            changes[i] = 0
            changes[discarder] -= 30
    changes[winner] = -sum(changes)
    return changes
