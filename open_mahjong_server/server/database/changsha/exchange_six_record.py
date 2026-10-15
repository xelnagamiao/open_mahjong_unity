"""Adapt exchange-six payment groups to the shared statistics contract.

The stored terminal hu tick is a whole-round UI summary, not an additional win.
Four-joy payments change scores and fan counts but do not end a hand.
"""


def statistics_round(round_data):
    source = round_data.get("action_ticks") or []
    if not any(t and t[0] == "changsha_exchange_six_win" for t in source):
        has_grouped_win = False
    else:
        has_grouped_win = True
    ticks = []
    initial = list(round_data.get("initial_hu_events") or [])
    for tick in source:
        if not tick:
            continue
        action = tick[0]
        if action == "changsha_exchange_six_win":
            for win in tick[1]:
                winner = int(win["winner"])
                details = win["payer_details"]
                changes = [0] * 4
                for payment in details:
                    changes[int(payment["payer"])] -= int(payment["payment"])
                    changes[winner] += int(payment["payment"])
                self_draw = len(details) == 3
                distance = (winner - int(details[0]["payer"])) % 4
                code = "hu_self" if self_draw else ("hu_first", "hu_second", "hu_third")[distance - 1]
                fans = [fan for group in win["groups"] for fan in group["fans"]]
                ticks.append([code, winner, changes[winner], fans, changes, win["groups"][0]["tiles"][0]])
        elif has_grouped_win and str(action).startswith("hu_"):
            continue
        else:
            ticks.append(tick)
            if action == "changsha_four_joy":
                initial.append({"seat": int(tick[1]), "fans": ["四喜"]})
            if action in ("changsha_actual_draw", "changsha_batch_discard", "changsha_kong_continue"):
                ticks.append(["reset", int(tick[1])])
    return {**round_data, "action_ticks": ticks, "initial_hu_events": initial}
