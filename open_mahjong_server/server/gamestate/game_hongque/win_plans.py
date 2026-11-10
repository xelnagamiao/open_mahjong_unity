"""Authoritative, selectable Hongque settlement plans and wire candidates."""
from __future__ import annotations

import hashlib
import json

from .profile import heavenly, score_for_state
from .scoring import all_win_results
from .w_rules import is_w


def can_supplement(state, player) -> bool:
    return player.supplements < 2 and bool(state.wall)


def win_plans_for_state(state, player, hand=None, *, self_draw=True) -> list[dict]:
    codes = list(player.hand if hand is None else hand)
    if is_w(state):
        result = score_for_state(state, player, codes, self_draw=self_draw)
        results = [{**result, "kong_steps": list(result.get("kong_path", ()))}] if result else []
    else:
        results = all_win_results(codes, player.melds, self_draw=self_draw,
            before_first_discard=self_draw and heavenly(state, player),
            wall_empty=not state.wall, allow_kong_win=self_draw)
    plans = []
    for index, result in enumerate(results):
        shape = sorted(sorted(group) for group in result["groups"])
        digest = hashlib.sha256(json.dumps(shape, separators=(",", ":")).encode()).hexdigest()[:16]
        plan_id = f"{'win' if self_draw else 'ron'}-{digest}"
        if not self_draw and index == 0:
            plan_id = "ron"  # Legacy clients submit this id for the best ron.
        plans.append({**result, "id": plan_id, "kind": "win",
            "tiles": [code for group in result["groups"] for code in group], "hand_tiles": []})
    return plans


def self_draw_plans(state, player) -> list[dict]:
    plans = win_plans_for_state(state, player)
    if state.game_status != "onlycut_after_action":
        return plans
    # A plain call still requires a supplement. When none is possible, permit
    # an empty-hand win or the last tile's kong-win instead of a dead end.
    if can_supplement(state, player):
        return []
    if not player.hand:
        return plans
    if len(player.hand) == 1:
        return [plan for plan in plans if plan["kong_steps"]]
    return []


def selected_win_plan(plans: list[dict], candidate_id: str | None) -> dict:
    if candidate_id is not None:
        plan = next((item for item in plans if item["id"] == candidate_id), None)
        if plan is None:
            raise ValueError("和牌候选无效或已过期")
        return plan
    if not plans:
        raise ValueError("当前手牌不能和牌")
    # Existing clients and bots send plain win; modern clients select an id.
    return plans[0]
