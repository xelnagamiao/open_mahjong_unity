"""Derived, signed same-seat IMP; independent of the official team result."""
import json

from ..game_calculation.sichuan.duplicate_rules import imp


def personal_imp(match):
    unavailable = {"version": 1, "status": "unavailable"}
    try:
        profile = match.get("rule_profile") or {}
        scale = profile.get("imp_scale")
        if (match.get("completeness") != "complete"
                or profile.get("dealer_policy") != "fixed_ESWN" or scale not in ("sbr", "jj")):
            return unavailable
        by_table = {}
        for table_id in ("open", "closed"):
            table = match["tables"][table_id]
            if table.get("record_state") != "complete":
                return unavailable
            boards = {}
            for key, rd in table["payload"]["game_round"].items():
                meta = table["round_meta"][key]
                board = meta.get("board_instance_id")
                seats = rd.get("seats")
                if (not board or board in boards or meta.get("alignment_status") != "exact"
                        or not isinstance(seats, list) or any(type(seat) is not int for seat in seats)
                        or sorted(seats) != [0, 1, 2, 3]):
                    return unavailable
                summaries = [tick[2] for tick in rd["action_ticks"] if len(tick) >= 3
                             and tick[0] in ("liuju", "blood") and tick[1] == "board_summary"]
                if len(summaries) != 1:
                    return unavailable
                summary = json.loads(summaries[0]) if isinstance(summaries[0], str) else summaries[0]
                changes = summary["changes"]
                values = [changes[str(seat)] for seat in seats]
                if any(type(value) is not int for value in values):
                    return unavailable
                boards[board] = values
            by_table[table_id] = boards
        opened, closed = by_table["open"], by_table["closed"]
        if not opened or opened.keys() != closed.keys():
            return unavailable
        scores = [0] * 4
        for board, values in opened.items():
            for seat in range(4):
                difference = values[seat] - closed[board][seat]
                scores[seat] += (1 if difference > 0 else -1 if difference < 0 else 0) * imp(abs(difference), scale=scale)
        return {"version": 1, "status": "complete", "method": "same_seat_per_board",
                "unit": "IMP", "imp_scale": scale, "board_count": len(opened),
                "tables": {"open": scores, "closed": [-value for value in scores]}}
    except (KeyError, TypeError, ValueError):
        return unavailable
