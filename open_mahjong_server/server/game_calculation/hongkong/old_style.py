"""HKMA classical (清章) scoring, including the book's distinct no-flower rule."""

from collections import Counter

from .models import Fan, HandContext, HongKongRules, ScoreResult, NUMBERS, HONORS, WINDS, DRAGONS
from .solver import decompositions, parse_meld


def nine_gates(ctx, *, require_nine_wait_on_discard=True):
    if ctx.melds or len(ctx.hand) != 14 or any(t not in NUMBERS for t in ctx.hand):
        return False
    suit = ctx.hand[0] // 10
    if any(t // 10 != suit for t in ctx.hand):
        return False
    base = Counter({suit * 10 + n: (3 if n in (1, 9) else 1) for n in range(1, 10)})
    hand = Counter(ctx.hand)
    if any(hand[t] < c for t, c in base.items()):
        return False
    if not ctx.self_draw and require_nine_wait_on_discard:
        hand[ctx.winning_tile] -= 1
        return +hand == base
    return True


def score_old(ctx: HandContext, rules: HongKongRules) -> ScoreResult:
    if len(set(ctx.flowers)) != len(ctx.flowers) or any(t not in range(51, 59) for t in ctx.flowers):
        return ScoreResult(rule_version=rules.version)
    if not rules.flowers and ctx.flowers:
        return ScoreResult(rule_version=rules.version)
    if ctx.flower_win:
        count = {"seven": 7, "eight": 8}.get(ctx.flower_win, -1)
        if not rules.flowers or len(ctx.flowers) != count:
            return ScoreResult(rule_version=rules.version)
        fans = [Fan("small_flower_win" if count == 7 else "big_flower_win",
                    "小花糊" if count == 7 else "大花糊", 3 if count == 7 else 10)]
        _draw_bonuses(fans, ctx, include_self=False)
        return _result(fans, "flower", rules)

    if ctx.winning_tile not in ctx.hand:
        return ScoreResult(rule_version=rules.version)

    shapes = decompositions(ctx.hand, ctx.melds, rules, ctx.winning_tile)
    candidates = []
    for shape in shapes:
        fans = []
        def add(key, name, value, condition=True):
            if condition:
                fans.append(Fan(key, name, value))
        all_tiles = ctx.hand + tuple(t for code in ctx.melds for t in parse_meld(code).tiles)
        suits = {t // 10 for t in all_tiles if t in NUMBERS}
        honors = any(t in HONORS for t in all_tiles)
        add("half_flush", "混一色", 3, len(suits) == 1 and honors)
        add("full_flush", "清一色", 7, len(suits) == 1 and not honors)
        add("all_honors", "全番子", 10, not suits)
        add("all_terminals", "清么九", 10, all(t in NUMBERS and t % 10 in (1, 9) for t in all_tiles))
        add("mixed_terminals", "花么", 1, all(t in HONORS or t % 10 in (1, 9) for t in all_tiles))
        add("concealed", "门前清", 1, not rules.flowers and not ctx.melds)
        add("orphans", "十三么", 10, shape.kind == "orphans")
        add("nine_gates", "九子连环", 10, nine_gates(ctx))
        add("heavenly", "天糊", 10, ctx.heavenly and not ctx.melds)
        add("earthly", "地糊", 10, ctx.earthly and not ctx.melds)
        add("rob_kong", "抢杠", 1, ctx.rob_kong)
        _draw_bonuses(fans, ctx)
        if rules.flowers:
            add("no_flowers", "无花", 1, not ctx.flowers)
            for flower in ctx.flowers:
                add(f"seat_flower_{flower}", "门风花", 1, (flower - 51) % 4 == ctx.seat_wind - 41)
            for start in (51, 55):
                add(f"flower_set_{start}", "一台花", 1, set(range(start, start + 4)) <= set(ctx.flowers))
        if shape.kind == "standard":
            pungs = {m.tile for m in shape.melds if m.kind != "sequence"}
            winds = pungs & set(WINDS)
            dragons = pungs & set(DRAGONS)
            add("all_chows", "平糊", 1, all(m.kind == "sequence" for m in shape.melds))
            add("all_pungs", "对对糊", 3, all(m.kind != "sequence" for m in shape.melds))
            add("little_dragons", "小三元", 3, len(dragons) == 2 and shape.pair in DRAGONS)
            add("big_dragons", "大三元", 5, len(dragons) == 3)
            add("little_winds", "小四喜", 10, len(winds) == 3 and shape.pair in WINDS)
            add("big_winds", "大四喜", 10, len(winds) == 4)
            add("four_kongs", "十八罗汉", 10, sum(m.kind == "kong" for m in shape.melds) == 4)
            add("four_concealed_pungs", "坎坎糊", 10,
                len(shape.concealed_pungs(ctx.self_draw)) == 4 and all(m.kind != "kong" for m in shape.melds))
            for dragon in sorted(dragons):
                add(f"dragon_{dragon}", "三元牌", 1)
            add("seat_wind", "门风", 1, ctx.seat_wind in pungs)
            add("round_wind", "圈风", 1, ctx.round_wind in pungs)
        candidates.append(_result(fans, shape.kind, rules))
    return max(candidates, key=lambda r: (r.fan, r.raw_fan, r.fan_ids), default=ScoreResult(rule_version=rules.version))


def _draw_bonuses(fans, ctx, include_self=True):
    if ctx.self_draw:
        if include_self:
            fans.append(Fan("self_draw", "自摸", 1))
        if ctx.last_tile:
            fans.append(Fan("last_tile", "海底捞月", 1))
        if ctx.replacement == "kong":
            fans.append(Fan("kong_draw", "杠上自摸", 1))
            if ctx.consecutive_kongs >= 2:
                fans.append(Fan("consecutive_kongs", "杠上杠自摸", 10))


def _result(fans, shape, rules):
    raw = sum(f.value for f in fans)
    return ScoreResult(raw >= 3, tuple(fans), raw, min(10, raw), shape, rules.version)


OLD_DISCARD_POINTS = {3: 32, 4: 64, 5: 96, 6: 128, 7: 192, 8: 256, 9: 384, 10: 512}


def old_payments(fan, winner, *, discarder=None, liability=None, rob_kong=False, kong_draw=False):
    """Return zero-sum deltas; fan already includes the actual win circumstances."""
    if fan < 3 or winner not in range(4):
        raise ValueError("Not a legal classical win")
    ron = OLD_DISCARD_POINTS[min(fan, 10)]
    delta = [0] * 4
    if rob_kong:
        if discarder is None:
            raise ValueError("A robbed kong needs a payer")
        delta[discarder] -= ron * 3 // 2
    elif discarder is not None:
        delta[discarder] -= ron
    elif liability is not None and not kong_draw:
        delta[liability] -= ron * 3 // 2
    else:
        for seat in range(4):
            if seat != winner:
                delta[seat] -= ron // 2
    if delta[winner]:
        raise ValueError("The winner cannot also be the payer")
    delta[winner] = -sum(delta)
    return delta
