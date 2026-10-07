"""恋绘色 thirteen-tile rules, transcribed from 香港新章规则书.docx.

The source document lives in other/rule/hongkong/lianhuise-new13.docx.
Limit hands replace all other fans; ordinary hands cap at the table's 13 fans.
"""

from .models import Fan, HandContext, HongKongRules, ScoreResult, NUMBERS, HONORS, WINDS, DRAGONS
from .solver import decompositions, parse_meld
from .old_style import nine_gates


def _result(fans, shape, rules):
    raw = sum(f.value for f in fans)
    return ScoreResult(raw >= rules.minimum_fan, tuple(fans), raw, min(raw, 13), shape, rules.version)


def _circumstances(ctx, *, self_bonus=True):
    fans = []
    kong = ctx.self_draw and ctx.replacement == "kong"
    chain = kong and ctx.consecutive_kongs >= 2
    if self_bonus and ctx.self_draw and not chain:
        fans.append(Fan("self_draw", "自摸", 1))
    if chain:
        fans.append(Fan("consecutive_kongs", "连杠开花", 8))
    elif kong:
        fans.append(Fan("kong_draw", "杠上开花", 1))
    elif ctx.last_tile:
        fans.append(Fan("last_tile", "海底捞月", 1))
    if ctx.rob_kong:
        fans.append(Fan("rob_kong", "抢杠", 1))
    return fans


def _flowers(ctx, rules):
    if not rules.flowers:
        return []
    if not ctx.flowers:
        return [Fan("no_flowers", "无花", 1)]
    fans = []
    flowers = set(ctx.flowers)
    for start in (51, 55):
        if set(range(start, start + 4)) <= flowers:
            fans.append(Fan(f"flower_set_{start}", "一台花", 2))
        elif start + ctx.seat_wind - 41 in flowers:
            fans.append(Fan(f"seat_flower_{start + ctx.seat_wind - 41}", "正花", 1))
    return fans


def score_lianhuise13(ctx: HandContext, rules: HongKongRules) -> ScoreResult:
    invalid = ScoreResult(rule_version=rules.version)
    if len(set(ctx.flowers)) != len(ctx.flowers) or any(t not in range(51, 59) for t in ctx.flowers):
        return invalid
    if ctx.flowers and not rules.flowers:
        return invalid
    if ctx.flower_win:
        count = {"seven": 7, "eight": 8}.get(ctx.flower_win)
        if not rules.flowers or count != len(ctx.flowers) or not ctx.self_draw:
            return invalid
        if ctx.heavenly:
            return _result([Fan("flower_heavenly", "天和", 13)], "flower", rules)
        if ctx.humanly:
            return _result([Fan("flower_humanly", "人和", 13)], "flower", rules)
        fans = [Fan("small_flower_win" if count == 7 else "big_flower_win",
                    "花和" if count == 7 else "大花和", 3 if count == 7 else 8)]
        return _result(fans + _circumstances(ctx, self_bonus=False), "flower", rules)
    if ctx.winning_tile not in ctx.hand:
        return invalid
    shapes = decompositions(ctx.hand, ctx.melds, rules, ctx.winning_tile)
    if not shapes:
        return invalid
    all_tiles = ctx.hand + tuple(t for code in ctx.melds for t in parse_meld(code).tiles)
    suits = {t // 10 for t in all_tiles if t in NUMBERS}
    honors = any(t in HONORS for t in all_tiles)
    candidates = []
    for shape in shapes:
        pungs = {m.tile for m in shape.melds if m.kind != "sequence"}
        winds, dragons = pungs & set(WINDS), pungs & set(DRAGONS)
        limits = []
        def limit(key, name, value, condition):
            if condition:
                limits.append(Fan(key, name, value))
        limit("heavenly", "天和", 13, ctx.heavenly and not ctx.melds)
        limit("earthly", "地和", 13, ctx.earthly and not ctx.melds)
        limit("humanly", "人和", 13, ctx.humanly and ctx.self_draw and not ctx.melds)
        limit("big_winds", "大四喜", 13, len(winds) == 4)
        limit("orphans", "十三幺", 13, shape.kind == "orphans")
        limit("four_kongs", "十八罗汉", 13, sum(m.kind == "kong" for m in shape.melds) == 4)
        limit("all_honors", "字一色", 10, not suits)
        limit("all_terminals", "清幺九", 10, all(t in NUMBERS and t % 10 in (1, 9) for t in all_tiles))
        limit("nine_gates", "九莲宝灯", 10, nine_gates(ctx, require_nine_wait_on_discard=False))
        if limits:
            candidates.append(_result([max(limits, key=lambda f: (f.value, f.id))], shape.kind, rules))
            continue
        fans = []
        def add(key, name, value, condition=True):
            if condition:
                fans.append(Fan(key, name, value))
        all_pungs = all(m.kind != "sequence" for m in shape.melds)
        mixed = all_pungs and all(t in HONORS or t % 10 in (1, 9) for t in all_tiles)
        concealed_pungs = not ctx.melds and len(shape.concealed_pungs(ctx.self_draw)) == 4
        small_winds = len(winds) == 3 and shape.pair in WINDS
        small_dragons = len(dragons) == 2 and shape.pair in DRAGONS
        big_dragons = len(dragons) == 3
        add("all_chows", "平和", 1, all(m.kind == "sequence" for m in shape.melds))
        # The document says this fan is generally not counted when playing
        # with flowers. It is enabled only by the 136-tile house rule.
        add("concealed", "门前清", 1, not rules.flowers and not ctx.melds and not concealed_pungs)
        add("all_pungs", "对对和", 3, all_pungs and not mixed and not concealed_pungs)
        add("mixed_terminals", "花幺九", 4, mixed)
        add("four_concealed_pungs", "坎坎糊", 8, concealed_pungs)
        add("little_dragons", "小三元", 5, small_dragons)
        add("big_dragons", "大三元", 8, big_dragons)
        add("little_winds", "小四喜", 6, small_winds)
        add("half_flush", "混一色", 3, len(suits) == 1 and honors and not small_winds)
        add("full_flush", "清一色", 7, len(suits) == 1 and not honors)
        if not small_dragons and not big_dragons:
            for dragon in sorted(dragons):
                add(f"dragon_{dragon}", "三元牌", 1)
        add("seat_wind", "门风", 1, ctx.seat_wind in pungs)
        add("round_wind", "圈风", 1, ctx.round_wind in pungs)
        fans.extend(_flowers(ctx, rules))
        fans.extend(_circumstances(ctx, self_bonus=not concealed_pungs))
        candidates.append(_result(fans, shape.kind, rules))
    return max(candidates, key=lambda r: (r.fan, r.raw_fan, r.fan_ids), default=invalid)


# The embedded image is normative. These are ron totals, not HKMA points.
DISCARD_POINTS = (1, 2, 4, 8, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384)


def lianhuise_payments(fan, winner, *, discarder=None, liability=None):
    if type(fan) is not int or fan < 3 or winner not in range(4):
        raise ValueError("Not a legal 恋绘色 win")
    ron = DISCARD_POINTS[min(fan, 13)]
    delta = [0] * 4
    if discarder is not None:
        delta[discarder] = -ron
    elif liability is not None:
        delta[liability] = -ron * 3 // 2
    else:
        delta = [-ron // 2 if i != winner else 0 for i in range(4)]
    if delta[winner]:
        raise ValueError("The winner cannot also be the payer")
    delta[winner] = -sum(delta)
    return delta
