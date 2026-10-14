"""九江计分与逐家支付；赔付对象按 2026-10-12 用户确认解释。"""
from .patterns import (DRAGONS, FA, TILES, SUB_RULE, big_hu_candidates, structural_waits)


def flower_details(melds=(), sets=(), flowers=(), *, winning_tile=None, self_draw=False):
    fans, arrows = [], set()
    for code in tuple(melds) + tuple(sets):
        kind, tile = code[0], int(code[1:])
        if kind == "s":
            continue
        if tile in DRAGONS:
            arrows.add(tile)
            concealed = kind == "G" or (code in sets and (self_draw or tile != winning_tile))
            fans.append(("暗箭刻" if concealed else "明箭刻", 2 if concealed else 1))
        if kind in "gG":
            fans.append(("暗杠" if kind == "G" else "明杠", 2 if kind == "G" else 1))
    if len(arrows) == 2:
        fans.append(("红旗飘飘", 2))
    if flowers:
        fans.append(("发财", 1))
    if len(flowers) == 4:
        fans.append(("四方来财", 3))
    return fans


def exposed_flower_points(melds=(), flowers=()):
    """该玩家和牌前已亮明的花分；发财基础分只计一次，不含暗杠、暗箭刻。"""
    public = tuple(code for code in melds if code[0] != "G")
    return sum(value for _, value in flower_details(public, (), flowers))


def score(hand, melds=(), flowers=(), *, winning_tile=None, self_draw=False,
          after_kong=False, after_kong_discard=False, rob_kong=False, last_tile=False,
          heaven_earth=False, declared_ready=False):
    if len(flowers) > 4 or any(tile != FA for tile in flowers):
        return None
    candidates = big_hu_candidates(hand, melds)
    if not candidates:
        return None
    waiting_hand = list(hand)
    single_wait = False
    if winning_tile in waiting_hand:
        waiting_hand.remove(winning_tile)
        single_wait = len(structural_waits(tuple(sorted(waiting_hand)), tuple(melds))) == 1
    special = []
    if self_draw and after_kong:
        special.append(("杠上开花", 1))
    if not self_draw and after_kong_discard and not rob_kong:
        special.append(("杠上炮", 1))
    if rob_kong:
        special.append(("抢杠胡", 1))
    if last_tile:
        special.append(("海底", 1))
    if len(melds) == 4:
        special.append(("全求人", 1))
    if heaven_earth:
        special.append(("天地胡", 10))
    if declared_ready:
        special.append(("报听", 10))
    results = []
    for candidate in candidates:
        unit = 2 if candidate["big_points"] else 1
        extras = []
        if all(code[0] == "G" for code in melds):
            extras.append(("门清", unit))
        if self_draw:
            extras.append(("自摸", unit))
        if single_wait:
            extras.append(("独听", unit))
        flowers_fans = flower_details(melds, candidate["sets"], flowers,
                                     winning_tile=winning_tile, self_draw=self_draw)
        fans = candidate["big_fans"] + special + extras + flowers_fans
        points = sum(value for _, value in fans)
        if points < 1:
            continue
        results.append({**candidate, "is_win": True, "base_score": points,
            "flower_points": sum(value for _, value in flowers_fans),
            "exposed_flower_points": exposed_flower_points(melds, flowers),
            "fan_details": [{"name":name,"points":value} for name,value in fans],
            "fan_names": [f"{name}（{value}分）" for name,value in fans], "single_wait": single_wait})
    return max(results, key=lambda item:(item["base_score"], item["big_points"]), default=None)


def payments(winner, points, *, discarder=None, dealer=0, payer_flowers=None, ready=(), declared=()):
    """未听输家赔自己的明花分，再合并报听赔付，对涉及庄家的支付翻倍。"""
    ready, declared = set(ready), set(declared)
    payer_flowers = payer_flowers or {}
    changes, breakdown = {i:0 for i in range(4)}, []
    for payer in (range(4) if discarder is None else (discarder,)):
        if payer == winner:
            continue
        flowers = 0 if payer in ready else payer_flowers.get(payer, 0)
        reported = 10 if payer in declared else 0
        multiplier = 2 if dealer in (winner, payer) else 1
        value = (points + flowers + reported) * multiplier
        changes[payer] -= value
        changes[winner] += value
        breakdown.append({"payer":payer,"winner":winner,"base":points,"unready_flowers":flowers,
                          "declared_penalty":reported,"dealer_multiplier":multiplier,"points":value})
    return changes, breakdown
