from ...game_calculation.wuhan import rules


def describe(hand, melds, indicator, table, index, tile, *, blocked=False, sea=False, previous_gold=None):
    if blocked or not rules.open_count(melds):
        return {"kind": "none", "label": "未满足", "points": 0}
    detail = rules.hand_detail(hand + [tile], melds, indicator, sea=sea)
    if detail is None:
        return {"kind": "none", "label": "未满足", "points": 0}
    joker, skins = rules.special_tiles(indicator)
    ron = []
    if tile not in skins and tile != joker and not sea:
        for payer in range(4):
            if payer != index:
                value = rules.settlement(detail, index, payer, table, indicator, previous_gold=previous_gold)
                if value:
                    ron.append(value["total"])
    tsumo = rules.settlement(detail, index, None, table, indicator, previous_gold=previous_gold)
    if ron:
        low, high = min(ron), max(ron)
        return {"kind": "ron", "label": f"{low}点" if low == high else f"{low}～{high}点", "points": low}
    if tsumo:
        return {"kind": "tsumo_only", "label": "仅自摸", "points": tsumo["total"]}
    return {"kind": "below_minimum", "label": "未起和", "points": 0}


def compute_hand(hand, melds, indicator, table, index, blocked=False, sea=False, previous_gold=None):
    payload = {"source_hand_tiles": list(hand), "source_melds": list(melds), "waiting_tiles": [],
        "waiting_by_discard": {}, "waiting_details": {}, "waiting_details_by_discard": {},
        "win_blocked": blocked}
    if indicator is None:
        return payload

    def rows(before):
        waits = sorted(rules.waits(before, melds, indicator))
        return waits, {t: describe(before, melds, indicator, table, index, t, blocked=blocked, sea=sea, previous_gold=previous_gold) for t in waits}

    total = len(hand) + 3 * len(melds)
    if total == 13:
        payload["waiting_tiles"], payload["waiting_details"] = rows(hand)
    elif total == 14:
        for tile in sorted(set(hand)):
            before = list(hand)
            before.remove(tile)
            waits, details = rows(before)
            payload["waiting_by_discard"][tile] = waits
            payload["waiting_details_by_discard"][tile] = details
    return payload
