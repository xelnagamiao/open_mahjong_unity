"""Offline MCR opportunity calculator; JSON stdin/stdout, standard library only.

The authoritative pure calculators are imported without importing the game server.
Private recipient hands are audit labels, never inputs to a normal-choice model.
"""
import json
import sys
from functools import lru_cache
from pathlib import Path

CALCULATION_ROOT = Path(__file__).resolve().parents[4] / "open_mahjong_server" / "server" / "game_calculation"
sys.path.insert(0, str(CALCULATION_ROOT))
from gb_tingpai_check import Chinese_Tingpai_Check
from guobiao_hepai_check import Chinese_Hepai_Check

WAITING = Chinese_Tingpai_Check()
SCORER = Chinese_Hepai_Check()
FACES = tuple(range(11, 20)) + tuple(range(21, 30)) + tuple(range(31, 40)) + tuple(range(41, 48))


@lru_cache(maxsize=24000)
def structural_waits(hand, melds):
    return tuple(sorted(WAITING.tingpai_check(list(hand), list(melds))))


@lru_cache(maxsize=40000)
def score(hand, melds, way, winning_tile):
    value, fans = SCORER.hepai_check(list(hand) + [winning_tile], list(melds), list(way), winning_tile)
    value, fans = SCORER.filter_zero_value_fans(value, fans)
    return value, tuple(fans)


def one_decision(d):
    if d.get("kind") != "discard":
        return {"id": d["id"], "status": "unavailable", "reason": "special_action_opportunities_not_implemented"}
    source = d["donor_seat"]
    result = []
    candidates = sorted(set(d["candidates"]))
    wind_index = (d["current_round"] - 1) // 4
    if wind_index not in range(4):
        raise ValueError("round_wind_out_of_range")
    for recipient in range(4):
        if recipient == source:
            continue
        hand = tuple(sorted(d["hands"][recipient]))
        melds = tuple(d["melds"][recipient])
        if len(hand) + len(melds) * 3 != 13:
            raise ValueError("recipient_hand_size_not_13")
        blocked = bool(d["blocked"][recipient])
        waits = () if blocked else structural_waits(hand, melds)
        helpful = {"CHI": [], "PENG": [], "GANG": [], "MELD": [], "RON": [], "ALL": []}
        fan_values = {}
        for t in candidates:
            if t not in FACES or blocked:
                continue
            if d["remaining_tiles"] > 0:
                if recipient == (source + 1) % 4 and t < 40:
                    sequences = [(t - 2, t - 1), (t - 1, t + 1), (t + 1, t + 2)]
                    if any(a in hand and b in hand and a // 10 == t // 10 == b // 10
                           for a, b in sequences):
                        helpful["CHI"].append(t)
                if hand.count(t) >= 2:
                    helpful["PENG"].append(t)
                if hand.count(t) == 3:
                    helpful["GANG"].append(t)
            if t in waits:
                flowers = d["flowers"][recipient]
                way = ["花牌"] * flowers + ["点和", "场风" + "东南西北"[wind_index],
                                          "自风" + "东南西北"[recipient]]
                if len(waits) == 1:
                    way.append("和单张")
                if d["remaining_tiles"] == 0:
                    way.append("last_cut")
                # Snapshot is before the proposed discard; authoritative check is after it.
                if int(d["public_counts"].get(str(t), 0)) + 1 == 4:
                    way.append("和绝张")
                if (d["tian_di_ren_he"] and recipient != d["dealer_index"]
                        and source == d["dealer_index"] and sum(d["cut_counts"]) == 0
                        and not any(d["melds"])):
                    way.append("地和")
                value, fans = score(hand, melds, tuple(way), t)
                if value > 0 and value - flowers >= d["hepai_limit"]:
                    helpful["RON"].append(t)
                    fan_values[str(t)] = {"fan": value, "qualifying_fan": value - flowers, "fans": list(fans)}
        helpful["MELD"] = sorted(set(helpful["CHI"] + helpful["PENG"] + helpful["GANG"]))
        helpful["ALL"] = sorted(set(helpful["MELD"] + helpful["RON"]))
        result.append({"donor_id": d["donor_id"], "recipient_id": d["seat_users"][recipient],
                       "donor_seat": source, "recipient_seat": recipient,
                       "helpful": helpful, "fan_values": fan_values,
                       "structural_waits": list(waits), "status": "available"})
    return {"id": d["id"], "status": "available", "recipients": result}


def main():
    request = json.load(sys.stdin)
    results = []
    for decision in request.get("decisions", []):
        try:
            results.append(one_decision(decision))
        except Exception as exc:
            results.append({"id": decision.get("id"), "status": "unavailable", "reason": str(exc)[:160]})
    json.dump({"calculator": "repository_standard_mcr_v1", "results": results}, sys.stdout,
              ensure_ascii=False, separators=(",", ":"))


if __name__ == "__main__":
    main()
