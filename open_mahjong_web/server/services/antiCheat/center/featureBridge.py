"""One game's structural benefit/cost features; no database or game service import."""
import json
import sys
from pathlib import Path
from functools import lru_cache

ROOT = Path(__file__).resolve().parents[5]
sys.path.insert(0, str(ROOT / "open_mahjong_server"))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from server.gamestate.public.ai.guobiao_shanten import (  # noqa: E402
    guobiao_shanten_from_tiles, counts_from_tiles, effective_tiles,
    configure_shanten_cache_budget,
)
from ruleBridge import structural_waits, score  # noqa: E402

configure_shanten_cache_budget(8)


@lru_cache(maxsize=12000)
def distance(hand, melds):
    return guobiao_shanten_from_tiles(hand, len(melds))


def visible_copies(tiles, hand, melds, public):
    known = dict((int(k), int(v)) for k, v in public.items())
    for value in hand:
        known[value] = known.get(value, 0) + 1
    for meld in melds:
        if meld.startswith("G"):
            value = int(meld[1:])
            known[value] = known.get(value, 0) + 4
    return sum(max(0, 4 - known.get(t, 0)) for t in tiles)


def qualifying_waits(hand, melds, snapshot, seat):
    waits = structural_waits(tuple(sorted(hand)), tuple(melds))
    result = []
    wind = (snapshot["current_round"] - 1) // 4
    if wind not in range(4):
        return []
    flowers = snapshot["flowers"][seat]
    for value in waits:
        way = ["花牌"] * flowers + ["点和", "场风" + "东南西北"[wind], "自风" + "东南西北"[seat]]
        if len(waits) == 1:
            way.append("和单张")
        if snapshot["remaining_tiles"] == 0:
            way.append("last_cut")
        if int(snapshot["public_counts"].get(str(value), 0)) + 1 == 4:
            way.append("和绝张")
        fan, _ = score(tuple(sorted(hand)), tuple(melds), tuple(way), value)
        if fan > 0 and fan - flowers >= snapshot["hepai_limit"]:
            result.append(value)
    return result


def meld_feature(item):
    before = item["before"]
    after = item["after"]
    seat = item["recipient_seat"]
    old_hand = tuple(sorted(before["hands"][seat]))
    old_melds = tuple(before["melds"][seat])
    new_hand = list(after["hands"][seat])
    new_hand.remove(after["actual_tile"])
    new_hand = tuple(sorted(new_hand))
    new_melds = tuple(after["melds"][seat])
    if len(old_hand) + 3 * len(old_melds) != 13 or len(new_hand) + 3 * len(new_melds) != 13:
        raise ValueError("unstable_meld_comparison_hand_size")
    pre = distance(old_hand, old_melds)
    post = distance(new_hand, new_melds)
    old_effective = effective_tiles(counts_from_tiles(old_hand), len(old_melds))
    new_effective = effective_tiles(counts_from_tiles(new_hand), len(new_melds))
    old_qualified = qualifying_waits(old_hand, old_melds, before, seat) if pre == 0 else []
    new_qualified = qualifying_waits(new_hand, new_melds, after, seat) if post == 0 else []
    old_public = dict(before["public_counts"])
    old_key = str(before["actual_tile"])
    old_public[old_key] = int(old_public.get(old_key, 0)) + 1
    new_public = dict(after["public_counts"])
    new_key = str(after["actual_tile"])
    new_public[new_key] = int(new_public.get(new_key, 0)) + 1
    # The legality bridge's public-count map follows the scorer's pung/chow
    # convention. An exposed kong is nevertheless known to the acting player
    # and has no remaining live copies; concealed opponents' kongs stay hidden.
    for snapshot, public in [(before, old_public), (after, new_public)]:
        for group in snapshot["melds"]:
            for meld in group:
                if meld.startswith("g"):
                    key = str(int(meld[1:]))
                    public[key] = int(public.get(key, 0)) + 4
    old_live = visible_copies(old_qualified, old_hand, old_melds, old_public)
    new_live = visible_copies(new_qualified, new_hand, new_melds, new_public)
    improved = pre > post or pre == post == 0 and new_live > old_live
    return {
        "status": "available", "effective": improved,
        "definition": "actual_shanten_reduction_or_qualified_wait_improvement",
        "shanten_before": pre, "shanten_after": post,
        "shanten_reduction": pre - post,
        "structural_live_before": visible_copies(old_effective, old_hand, old_melds, old_public),
        "structural_live_after": visible_copies(new_effective, new_hand, new_melds, new_public),
        "qualified_waits_before": list(old_qualified), "qualified_waits_after": list(new_qualified),
        "qualified_live_before": old_live, "qualified_live_after": new_live,
        "includes_replacement_draw": item.get("type") == "GANG",
        "interpretation": "observed_structural_change_not_causal_score_gain",
    }


def donor_cost(item):
    d = item["before"]
    seat = d["donor_seat"]
    hand = list(d["hands"][seat])
    melds = tuple(d["melds"][seat])
    if len(hand) + len(melds) * 3 != 14:
        raise ValueError("donor_hand_size_not_14")
    values = {}
    for value in sorted(set(d["candidates"])):
        if value >= 51:
            continue
        remaining = hand[:]
        remaining.remove(value)
        values[value] = distance(tuple(sorted(remaining)), melds)
    if d["actual_tile"] not in values or not values:
        raise ValueError("actual_discard_not_a_candidate")
    chosen = values[d["actual_tile"]]
    best = min(values.values())
    return {
        "status": "available", "chosen_shanten": chosen, "best_shanten": best,
        "shanten_cost": chosen - best, "structural_sacrifice": chosen > best,
        "best_choices": [v for v, n in values.items() if n == best],
        "interpretation": "structural_cost_only_not_proof_of_intent",
    }


def main():
    request = json.load(sys.stdin)
    result = {"melds": {}, "donors": {}}
    for kind, function, output in [("melds", meld_feature, "melds"), ("donors", donor_cost, "donors")]:
        for item in request.get(kind, []):
            try:
                result[output][item["id"]] = function(item)
            except Exception as error:
                result[output][item["id"]] = {"status": "unavailable", "reason": str(error)[:160]}
    json.dump(result, sys.stdout, ensure_ascii=False, separators=(",", ":"))


if __name__ == "__main__":
    main()
