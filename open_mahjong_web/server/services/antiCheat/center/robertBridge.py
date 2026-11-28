"""Offline policy audit using Robert's real decision code and redacted inputs only."""
import json
import sys
import math
import hashlib
from dataclasses import asdict
from pathlib import Path
from time import perf_counter

ROOT = Path(__file__).resolve().parents[5]
sys.path.insert(0, str(ROOT / "open_mahjong_server"))
from server.gamestate.public.ai import guobiao_heuristic_logic as policy
from server.gamestate.public.ai.guobiao_shanten import (
    configure_shanten_cache_budget, counts_from_tiles, remove_tile, pack_counts,
)

configure_shanten_cache_budget(16)
DEFAULT_THRESHOLDS = {"shanten": 1, "legal_ukeire": 4, "ukeire": 4, "fan": 8,
                      "ron_ukeire": 4, "reshape_progress": 24}
POLICY_SOURCE_SHA256 = hashlib.sha256(Path(policy.__file__).read_bytes()).hexdigest()


def redacted_context(item):
    # Explicit field selection prevents accidental use of raw replay hidden state.
    hand = [int(t) for t in item["hand"]]
    melds = [str(m) for m in item["combination_tiles"]]
    if any(t not in policy.ALL_TILE_IDS for t in hand):
        raise ValueError("unsupported_hand_tile")
    if len(hand) + 3 * len(melds) != 14:
        raise ValueError("discard_hand_size_not_14")
    visible = {int(k): int(v) for k, v in item["visible"].items()}
    if any(n < 0 or n > 4 for n in visible.values()):
        raise ValueError("invalid_visible_tile_count")
    for t in set(hand):
        if visible.get(t, 0) < hand.count(t):
            raise ValueError("own_hand_missing_from_visible")
    wall = int(item["wall_left"])
    if wall < 0:
        raise ValueError("invalid_wall_count")
    if int(item["round_wind"]) not in range(4) or int(item["seat_wind"]) not in range(4):
        raise ValueError("unsupported_wind_context")
    return policy.HeuristicContext(
        hand=hand, combination_tiles=melds, visible=visible, wall_left=wall,
        min_fan=int(item.get("min_fan", 8)), round_wind=int(item["round_wind"]),
        seat_wind=int(item["seat_wind"]), flower_count=int(item.get("flower_count", 0)),
        scorer=policy.make_default_scorer(), fan_memo={},
    )


def context_key(context):
    """All semantic inputs, including hypothetical contexts; memo kwargs are caches only."""
    return (tuple(context.hand), tuple(context.combination_tiles),
            tuple(sorted(context.visible.items())), context.wall_left,
            context.min_fan, context.round_wind, context.seat_wind,
            context.flower_count, id(context.scorer))


def comparator_first_difference(best, actual, near, thresholds):
    """Mirror is_better_discard's first priority only, never a lower-level delta."""
    bw, aw = policy.winning_shanten(best), policy.winning_shanten(actual)
    if bw != aw:
        return ("effective_shanten", aw - bw, "shanten_steps", thresholds["shanten"], "comparable")
    if policy.is_cheap_tenpai(best) != policy.is_cheap_tenpai(actual):
        return ("cheap_tenpai_preference", None, "qualitative_legal_shape_preference", None, "incomparable")
    if best.shanten != actual.shanten:
        return ("structural_shanten", actual.shanten - best.shanten, "shanten_steps", thresholds["shanten"], "comparable")
    both_legal = bw == aw == 0
    slack = 0 if both_legal and (near or min(actual.ukeire, best.ukeire) <= policy.THIN_LEGAL_UKEIRE) else policy.FAN_UKEIRE_SLACK
    if abs(best.ukeire - actual.ukeire) > slack:
        return ("ukeire", best.ukeire - actual.ukeire, "weighted_live_tile_copies", thresholds["ukeire"], "comparable")
    if best.max_wait_fan != actual.max_wait_fan:
        return ("fan", best.max_wait_fan - actual.max_wait_fan, "max_qualifying_fan", thresholds["fan"], "comparable")
    if best.ukeire != actual.ukeire:
        return ("ukeire", best.ukeire - actual.ukeire, "weighted_live_tile_copies", thresholds["ukeire"], "comparable")
    if best.ron_ukeire != actual.ron_ukeire:
        return ("ron_ukeire", best.ron_ukeire - actual.ron_ukeire, "ron_live_tile_copies", thresholds["ron_ukeire"], "comparable")
    if both_legal and best.tsumo_only_kinds != actual.tsumo_only_kinds:
        return ("tsumo_only_kinds_tiebreak", None, "repository_minor_tiebreak", None, "minor")
    if both_legal and best.ron_wait_kinds != actual.ron_wait_kinds:
        return ("ron_wait_kinds_tiebreak", None, "repository_minor_tiebreak", None, "minor")
    if best.shed_priority != actual.shed_priority:
        return ("shed_priority_tiebreak", None, "repository_minor_tiebreak", None, "minor")
    return ("tie", 0, "none", None, "recommended")


def compare_one(item, thresholds):
    started = perf_counter()
    ctx = redacted_context(item)
    actual = int(item["actual_tile"])
    if actual not in ctx.hand:
        raise ValueError("actual_discard_not_in_own_hand")
    score_cache, legal_cache, reshape_cache = {}, {}, {}
    score_fn, legal_fn, reshape_fn = (policy.score_discard,
        policy.qualifying_tenpai_ukeire_one_draw, policy.thick_one_shanten_ukeire_after_one_draw)

    def cached_score(context, tile):
        key = (context_key(context), tile)
        if key not in score_cache:
            score_cache[key] = score_fn(context, tile)
        return score_cache[key]

    def cached_legal(context, remaining, combos, n_melds, **kwargs):
        if set(kwargs) - {"qualifies_memo", "shanten_memo"}:
            return legal_fn(context, remaining, combos, n_melds, **kwargs)
        key = (context_key(context), pack_counts(remaining), tuple(combos), n_melds)
        if key not in legal_cache:
            legal_cache[key] = legal_fn(context, remaining, combos, n_melds, **kwargs)
        return legal_cache[key]

    def cached_reshape(remaining, n_melds, visible, **kwargs):
        if set(kwargs) - {"shanten_memo"}:
            return reshape_fn(remaining, n_melds, visible, **kwargs)
        key = (pack_counts(remaining), n_melds, tuple(sorted(visible.items())))
        if key not in reshape_cache:
            reshape_cache[key] = reshape_fn(remaining, n_melds, visible, **kwargs)
        return reshape_cache[key]

    # Policy's actual selection path is executed unchanged. These wrappers only
    # reuse identical candidate evaluations in this one redacted decision.
    policy.score_discard, policy.qualifying_tenpai_ukeire_one_draw, policy.thick_one_shanten_ukeire_after_one_draw = cached_score, cached_legal, cached_reshape
    try:
        best = policy._choose_discard_plan(ctx)
    finally:
        policy.score_discard, policy.qualifying_tenpai_ukeire_one_draw, policy.thick_one_shanten_ukeire_after_one_draw = score_fn, legal_fn, reshape_fn
    if best is None:
        raise ValueError("repository_policy_no_discard")
    counts = counts_from_tiles(ctx.hand)
    candidates = list(counts)
    remaining = {t: remove_tile(counts, t) for t in candidates}
    scores = {t: cached_score(ctx, t) for t in candidates}
    near = ctx.wall_left < policy.NEAR_EXHAUST_WALL
    n_melds = len(ctx.combination_tiles)
    base_context = context_key(ctx)
    base_legal = [value for key, value in legal_cache.items()
                  if key[0] == base_context and key[2] == tuple(ctx.combination_tiles) and key[3] == n_melds]
    stage = "discard_comparator"
    if best.legal_ukeire is not None:
        stage = "qualifying_one_draw"
    elif reshape_cache and best.score.shanten == 2 and base_legal and max(base_legal) <= 0:
        stage = "dead_shape_reshape"

    def candidate_primary(tile):
        score = scores[tile]
        if stage == "qualifying_one_draw":
            if policy.winning_shanten(score) != 1:
                return None
            return cached_legal(ctx, remaining[tile], list(ctx.combination_tiles), n_melds)
        if stage == "dead_shape_reshape":
            if score.shanten != 2:
                return None
            return cached_reshape(remaining[tile], n_melds, ctx.visible)
        return 0

    primary = {t: candidate_primary(t) for t in candidates}
    best_primary = primary[best.tile]

    def strictly_better(a, b):
        av, bv = primary[a], primary[b]
        if av is None or bv is None:
            # A candidate outside the selected specialized stage cannot tie the
            # chosen stage. Do not manufacture a scalar utility across stages.
            return av is not None and bv is None
        if not math.isclose(av, bv, abs_tol=1e-9):
            return av > bv
        return policy.is_better_discard(scores[a], scores[b], near)

    acceptable = sorted(t for t in candidates
        if primary[t] is not None and not strictly_better(best.tile, t)
        and not strictly_better(t, best.tile))
    if best.tile not in acceptable:
        acceptable.append(best.tile)
        acceptable.sort()
    conforms = actual in acceptable
    gap = {"policy_stage": stage, "deciding_metric": "tie", "value": 0,
           "unit": "none", "threshold": None, "direction": "larger_is_worse",
           "candidate_rank_gap": sum(1 for t in candidates if strictly_better(t, actual)),
           "obviously_worse": False}
    a, b = scores[actual], best.score
    selected = None
    comparison_status = "recommended" if conforms else "comparable"
    if not conforms:
        if stage == "dead_shape_reshape":
            if primary[actual] is not None:
                if best_primary != primary[actual]:
                    selected = ("reshape_progress", best_primary - primary[actual], "weighted_future_tile_exposure", thresholds["reshape_progress"], "comparable")
                else:
                    selected = comparator_first_difference(b, a, near, thresholds)
            else:
                selected = ("dead_shape_stage_mismatch", None, "incomparable_policy_stage", None, "incomparable")
        elif policy.winning_shanten(a) > policy.winning_shanten(b):
            selected = ("effective_shanten", policy.winning_shanten(a) - policy.winning_shanten(b), "shanten_steps", thresholds["shanten"], "comparable")
        elif stage == "qualifying_one_draw" and primary[actual] is not None and best_primary > primary[actual] + 1e-9:
            selected = ("legal_ukeire", best_primary - primary[actual], "weighted_qualifying_draw_copies", thresholds["legal_ukeire"], "comparable")
        else:
            selected = comparator_first_difference(b, a, near, thresholds)
    if selected:
        metric, value, unit, threshold, comparison_status = selected
        gap.update(deciding_metric=metric, value=value, unit=unit, threshold=threshold,
                   obviously_worse=value is not None and threshold is not None and value >= threshold)
    if not conforms and not strictly_better(best.tile, actual):
        # The comparator has a fan/ukeire slack. If its ordering is not a
        # strict dominance for this pair, do not turn a local difference into
        # a claim that the actual discard was clearly inferior.
        gap.update(deciding_metric="comparator_does_not_strictly_dominate", value=None,
                   unit="incomparable_policy_order", threshold=None, obviously_worse=False)
        comparison_status = "incomparable"
    return {"id": item["id"], "status": "available", "actual_tile": actual,
            "recommended_tile": best.tile, "acceptable_tiles": acceptable,
            "is_recommended": conforms, "obviously_worse": gap["obviously_worse"],
            "comparison_status": comparison_status, "rate_comparable": comparison_status != "incomparable",
            "gap": gap, "actual_score": asdict(a), "recommended_score": asdict(b),
            "actual_primary": primary[actual], "recommended_primary": best_primary,
            "elapsed_ms": round((perf_counter() - started) * 1000, 3),
            "policy": "repository_guobiao_heuristic_logic._choose_discard_plan",
            "information_scope": "own_hand_and_public_information_only"}


def handle_request(request):
    thresholds = dict(DEFAULT_THRESHOLDS)
    for k, value in request.get("thresholds", {}).items():
        if k not in thresholds or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
            raise ValueError("invalid_policy_gap_threshold")
        thresholds[k] = value
    results = []
    for item in request.get("decisions", []):
        try:
            results.append(compare_one(item, thresholds))
        except Exception as exc:
            results.append({"id": item.get("id"), "status": "unavailable", "reason": str(exc)[:160]})
    return {
        "results": results,
        "thresholds": thresholds,
        "policy_source_sha256": POLICY_SOURCE_SHA256,
        "utility_note": "The repository uses a multi-stage policy, not a continuous score. Gaps retain their native units.",
    }


def main():
    if "--worker" in sys.argv[1:]:
        for raw_line in sys.stdin:
            line = raw_line.strip()
            if not line:
                continue
            req_id = None
            try:
                msg = json.loads(line)
                req_id = msg.get("id")
                result = handle_request(msg)
                response = {"id": req_id, "ok": True, "result": result}
            except Exception as exc:
                response = {"id": req_id, "ok": False, "error": str(exc)[:200]}
            sys.stdout.write(json.dumps(response, ensure_ascii=False, separators=(",", ":")) + "\n")
            sys.stdout.flush()
        return

    request = json.load(sys.stdin)
    json.dump(handle_request(request), sys.stdout, ensure_ascii=False, separators=(",", ":"))


if __name__ == "__main__":
    main()
