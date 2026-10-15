"""Replay counters for shared-format rules, including deferred blood-battle settlements."""

from decimal import Decimal, ROUND_HALF_UP

from .guobiao.record_analyzer import (
    _parse_hu_tick, _tick_int, RON_ACTIONS, VISIBLE_FULU_CODES, reconstruct_round_win_turns,
)
from .guobiao.round_score_utils import _parse_score_changes, resolve_round_seats
from .statistics_values import sichuan_win_fan, statistic_units


def analyze_shared_record_for_player(record, original_index):
    out = dict(total_rounds=0, win_count=0, self_draw_count=0, deal_in_count=0,
               total_fan_score=0, total_win_turn=0, total_fangchong_score=0,
               fulu_round_count=0, cuohe_count=0, total_round_score=0)
    title = record.get("game_title") or {}
    win_unit, loss_unit = statistic_units(title)
    sichuan = title.get("rule") == "sichuan"
    exchange_six = title.get("rule") == "changsha" and title.get("sub_rule") == "changsha/exchange_six"
    own_turns = sichuan or title.get("rule") in ("zhongyong", "jiandan", "nanque", "nanjing", "nanchang", "malaysia", "fuzhou")
    dealer_cut_turns = exchange_six or title.get("rule") in ("qingque", "classical", "shenyang", "wuhan")
    fractional_fan_total = Decimal(0)
    for rd in (record.get("game_round") or {}).values():
        if not isinstance(rd, dict):
            continue
        if exchange_six:
            from .changsha.exchange_six_record import statistics_round
            rd = statistics_round(rd)
        ticks = rd.get("action_ticks") or []
        if not any(isinstance(t, list) and t and t[0] == "end" for t in ticks):
            continue
        seats = rd.get("seats")
        if not isinstance(seats, list) or len(seats) not in (3, 4):
            seats = resolve_round_seats(rd)
        try:
            seats = [int(s) for s in seats]
        except (TypeError, ValueError):
            continue
        if sorted(seats) != list(range(len(seats))):
            continue
        if original_index not in range(len(seats)):
            continue
        seat = seats[original_index]
        count = len(seats)
        out["total_rounds"] += 1
        called = False
        actor = rd.get("start_player_index", rd.get("dealer_index", 0)) or 0
        opening = True
        discards = [0] * count
        retired = set()
        classical_fu = None
        has_final_hu = any(_parse_hu_tick(t) for t in ticks if isinstance(t, list))
        if title.get("rule") == "classical":
            for event in ticks:
                if isinstance(event, list) and len(event) > 2 and event[0] == "shuhewei":
                    classical_fu = _parse_score_changes(event[1])
        turn_totals = reconstruct_round_win_turns({**rd, "seats": seats})
        if not own_turns and not dealer_cut_turns:
            out["total_win_turn"] += turn_totals.get(seat, 0)
        for tick_index, tick in enumerate(ticks):
            if not isinstance(tick, list) or not tick:
                continue
            code = tick[0]
            if code == "end":
                break
            if code == "reset":
                actor = _tick_int(tick, 1, actor) % count
            elif code in ("d", "mo"):
                explicit = _tick_int(tick, 2)
                if explicit is not None:
                    actor = explicit % count
                elif not opening:
                    actor = (actor + 1) % count
                    if sichuan and title.get("blood_battle"):
                        for _ in range(count):
                            if actor not in retired:
                                break
                            actor = (actor + 1) % count
                opening = False
            elif code == "gd":
                actor = _tick_int(tick, 2, actor) % count
            elif code in ("bh", "bd"):
                actor = _tick_int(tick, 2, actor) % count
            elif code in VISIBLE_FULU_CODES:
                actor = _tick_int(tick, 2, actor) % count
                called |= actor == seat
                opening = False
            elif code == "c":
                discards[actor] += 1
                opening = False

            hu = _parse_hu_tick(tick)
            changes = hu["score_changes"] if hu else None
            if hu:
                winner = hu["winner_seat"]
                fan_score = hu["fan_score"]
                invalid = any("错和" in str(fan) for fan in (hu["yaku"] or []))
                if sichuan and not invalid:
                    fan_score = sichuan_win_fan(title, rd, tick, tick_index)
                if title.get("rule") == "malaysia":
                    # This rule records basic payment points in the hu tick.
                    # Fan aggregates are intentionally deferred, not payment totals.
                    fan_score = 0
                if title.get("rule") == "classical":
                    # hu_score records total income; cumulative counters use total fu.
                    # The shuhewei panel preserves each player's exact total fu.
                    fan_score = (classical_fu[winner] if classical_fu is not None
                                 else fan_score // (6 if winner == 0 else 4))
                if win_unit == "points" and changes is not None:
                    fan_score = max(0, changes[winner])
                if invalid:
                    out["cuohe_count"] += int(winner == seat)
                elif winner == seat:
                    out["win_count"] += 1
                    out["self_draw_count"] += int(hu["hu_class"] == "hu_self")
                    if title.get("rule") == "qingque":
                        fractional_fan_total += Decimal(str(tick[2]))
                    else:
                        out["total_fan_score"] += fan_score
                    if own_turns:
                        out["total_win_turn"] += discards[seat] + 1
                    elif dealer_cut_turns:
                        # These engines increment on dealer cuts, not seat wraparound.
                        out["total_win_turn"] += discards[0] + 1
                if not invalid and hu["hu_class"] in RON_ACTIONS:
                    # hu_first/second/third encode the winner's distance from the payer.
                    payer = (winner - {"hu_first": 1, "hu_second": 2, "hu_third": 3}[hu["hu_class"]]) % count
                    if sichuan:
                        # 四川的 hu_first/second 是同一弃牌的和牌顺序，不是座位距离。
                        # 扩展 tick 明确保存点炮者；旧格式沿用当前出牌者。
                        payer = _tick_int(tick, 7, actor) if len(tick) >= 9 else actor
                    if payer == seat:
                        out["deal_in_count"] += 1
                        if loss_unit == "points" and changes is not None:
                            out["total_fangchong_score"] += max(0, -changes[seat])
                        else:
                            out["total_fangchong_score"] += fan_score
                if sichuan and not invalid and title.get("blood_battle"):
                    retired.add(winner)
                    actor = winner
            elif exchange_six and code == "changsha_four_joy":
                # Four-joy dictionaries use original indices; hu vectors use seats.
                delta = tick[4]
                changes = [0] * count
                for original, mapped in enumerate(seats):
                    changes[mapped] = int(delta.get(original, delta.get(str(original), 0)))
            elif code == "blood" and len(tick) > 6 and tick[1] == "settle_hu":
                # Visible hu ticks already supplied the counters; this event supplies payment only.
                changes = _parse_score_changes(tick[6])
            elif sichuan and code == "liuju" and len(tick) > 6 and tick[1] in ("settle_hu", "chajiao"):
                # 可见和牌 tick 的支付为零，实际和牌支付/查叫/退税在终局分步入账。
                changes = _parse_score_changes(tick[6 if tick[1] == "settle_hu" else 5])
            elif code == "shuhewei" and len(tick) > 2:
                # Classical's final hu tick already includes the complete shuhewei
                # payment. In a drawn round there is no hu tick, so count the panel.
                if title.get("rule") != "classical" or not has_final_hu:
                    changes = _parse_score_changes(tick[2])
            elif code == "sy" and len(tick) > 1 and isinstance(tick[1], dict) and tick[1].get("kind") == "egg":
                delta = tick[1].get("changes") or {}
                changes = _parse_score_changes([delta.get(i, delta.get(str(i), 0)) for i in range(count)])
            elif code == "singapore" and len(tick) > 2 and tick[1] == "payment":
                changes = _parse_score_changes(tick[2])
            elif title.get("rule") == "malaysia" and code == "malaysia" and len(tick) > 2 and tick[1] == "payment":
                changes = _parse_score_changes(tick[2])
            elif "gs" in tick:
                at = tick.index("gs")
                changes = _parse_score_changes(tick[at + 1:at + 1 + count])
            if changes is not None and seat < len(changes):
                out["total_round_score"] += changes[seat]
        out["fulu_round_count"] += int(called)
    if title.get("rule") == "qingque":
        # Preserve decimal fan values until the per-game INTEGER cast, as PostgreSQL
        # does for the live cumulative writer; truncating every win loses fan points.
        out["total_fan_score"] = int(fractional_fan_total.quantize(Decimal(1), rounding=ROUND_HALF_UP))
    return out
