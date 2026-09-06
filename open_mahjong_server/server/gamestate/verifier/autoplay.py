"""Turbo 自动打牌：启发式或简单策略立刻入队，直到终局或步数上限。"""
from __future__ import annotations

import logging
from typing import Any, Dict, List, Optional

from server.gamestate.public.hand_slot_utils import normalize_tile

from .session import VerifierError, VerifierSession

logger = logging.getLogger(__name__)

_HU_OTHER = ("hu", "hu_first", "hu_second", "hu_third")


def _waiting_seats(legal: Dict[str, Any]) -> List[Dict[str, Any]]:
    seats = []
    for info in (legal.get("seats") or {}).values():
        if info.get("waiting"):
            seats.append(info)
    seats.sort(key=lambda item: int(item.get("player_index") or 0))
    return seats


def _pick_cut_tile(session: VerifierSession, seat: int, seat_info: Dict[str, Any], policy: str) -> Optional[int]:
    tiles = list(seat_info.get("cut_tiles") or [])
    if not tiles:
        return None
    if policy != "heuristic" or session.game is None:
        return tiles[-1]
    try:
        from server.gamestate.public.ai.guobiao_heuristic_logic import (
            choose_best_discard,
            context_from_game,
            make_default_scorer,
        )

        ctx = context_from_game(session.game, seat, scorer=make_default_scorer())
        chosen = choose_best_discard(ctx)
        if chosen is None:
            return tiles[-1]
        for tile in tiles:
            if tile == chosen or normalize_tile(tile) == normalize_tile(chosen):
                return tile
        return tiles[-1]
    except Exception:
        logger.debug("heuristic cut failed, fallback last tile", exc_info=True)
        return tiles[-1]


def _pick_claim(session: VerifierSession, seat: int, seat_info: Dict[str, Any], policy: str) -> Optional[str]:
    actions = list(seat_info.get("client_actions") or [])
    claims = [item for item in actions if item in ("peng", "gang", "chi_left", "chi_mid", "chi_right")]
    if policy != "heuristic" or session.game is None or not claims:
        return None
    try:
        from server.gamestate.public.ai.guobiao_heuristic_logic import (
            choose_claim,
            context_from_game,
            make_default_scorer,
        )

        discards = session.game.player_list[session.game.current_player_index].discard_tiles
        cut_tile = discards[-1] if discards else seat_info.get("claim_tile")
        if cut_tile is None:
            return None
        ctx = context_from_game(session.game, seat, scorer=make_default_scorer())
        best = choose_claim(ctx, claims + (["pass"] if "pass" in actions else []), cut_tile)
        if best in actions:
            return best
    except Exception:
        logger.debug("heuristic claim failed", exc_info=True)
    return None


def decide_seat(session: VerifierSession, seat_info: Dict[str, Any], *, policy: str) -> Optional[Dict[str, Any]]:
    seat = int(seat_info["player_index"])
    actions = list(seat_info.get("client_actions") or [])
    if "ready" in actions:
        return {"player_index": seat, "action_type": "ready"}
    if "buhua" in actions:
        return {"player_index": seat, "action_type": "buhua"}
    if "hu_self" in actions:
        return {"player_index": seat, "action_type": "hu_self"}
    if "hu_flower" in actions:
        return {"player_index": seat, "action_type": "hu_flower"}
    for hu in _HU_OTHER:
        if hu in actions:
            return {"player_index": seat, "action_type": hu}
    if "angang" in actions:
        tiles = (seat_info.get("target_tiles") or {}).get("angang") or []
        if tiles:
            return {"player_index": seat, "action_type": "angang", "target_tile": tiles[0]}
    if "jiagang" in actions:
        tiles = (seat_info.get("target_tiles") or {}).get("jiagang") or []
        if tiles:
            return {"player_index": seat, "action_type": "jiagang", "target_tile": tiles[0]}
    claim = _pick_claim(session, seat, seat_info, policy)
    if claim:
        return {
            "player_index": seat,
            "action_type": claim,
            "target_tile": seat_info.get("claim_tile"),
        }
    if "cut" in actions:
        tile = _pick_cut_tile(session, seat, seat_info, policy)
        if tile is not None:
            return {"player_index": seat, "action_type": "cut", "tile_id": tile}
    if "pass" in actions:
        return {"player_index": seat, "action_type": "pass"}
    if "force_pass" in actions:
        return {"player_index": seat, "action_type": "force_pass"}
    if actions:
        return {"player_index": seat, "action_type": actions[0], "target_tile": seat_info.get("claim_tile")}
    return None


def decide_batch(session: VerifierSession, *, policy: str) -> List[Dict[str, Any]]:
    legal = session.legal_actions()
    batch = []
    for seat_info in _waiting_seats(legal):
        action = decide_seat(session, seat_info, policy=policy)
        if action:
            batch.append(action)
    return batch


async def step_once(
    session: VerifierSession,
    *,
    policy: str = "heuristic",
    include_self: Optional[bool] = None,
) -> Dict[str, Any]:
    """推进一步：默认先打他家，只剩本家时再打本家。"""
    if session.ended():
        snap = session.snapshot()
        snap["step_actions"] = []
        snap["step_skipped"] = "ended"
        return snap
    if not session.paused:
        await session.wait_paused(timeout=8)
    legal = session.legal_actions()
    waiting = _waiting_seats(legal)
    viewer = int((session.sim.gsm or {}).get("selfIndex") or 0)
    others = [info for info in waiting if int(info.get("player_index") or 0) != viewer]
    if include_self is False:
        targets = others
    elif include_self is True:
        targets = waiting
    else:
        targets = others if others else waiting
    batch: List[Dict[str, Any]] = []
    for info in targets:
        action = decide_seat(session, info, policy=policy)
        if action:
            batch.append(action)
    if not batch:
        raise VerifierError("当前没有可推进一步", status_code=409)
    snap = await session.submit_batch(batch)
    snap["step_actions"] = batch
    return snap


async def autoplay(
    session: VerifierSession,
    *,
    max_steps: int = 400,
    policy: str = "heuristic",
    script: Optional[List[Dict[str, Any]]] = None,
) -> Dict[str, Any]:
    """默认 turbo：脚本先走，其余座位用 bot/简单策略立刻入队。"""
    for action in script or []:
        if session.ended():
            break
        await session.submit(action)
    steps = 0
    while not session.ended() and steps < max_steps:
        if not session.paused:
            await session.wait_paused(timeout=8)
        if session.ended():
            break
        batch = decide_batch(session, policy=policy)
        if not batch:
            raise VerifierError("autoplay 找不到可提交动作", status_code=500)
        await session.submit_batch(batch)
        steps += 1
    snap = session.snapshot()
    snap["autoplay_steps"] = steps
    snap["autoplay_truncated"] = (not session.ended()) and steps >= max_steps
    return snap
