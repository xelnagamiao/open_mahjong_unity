"""把战鸣 FakeGS 的 broadcast_do_action kwargs 译成 Unity WS，灌进 UnitySim。"""
from __future__ import annotations

import asyncio
from typing import Any, Dict, List, Optional

from .unity_sim.client import UnitySim

VIEWER_UID = 101


class _FakeGameState:
    pass


def _in_running_loop() -> bool:
    try:
        asyncio.get_running_loop()
        return True
    except RuntimeError:
        return False


def _run_isolated(fn):
    """fn 是同步函数，内部自己 asyncio.run。有运行中的 loop 时改到线程里跑。"""
    if not _in_running_loop():
        return fn()
    from concurrent.futures import ThreadPoolExecutor

    with ThreadPoolExecutor(max_workers=1) as pool:
        return pool.submit(fn).result()


def _make_grace_state(replacement_action):
    gs = _FakeGameState()
    gs.tactical_grace_seconds = 0.01
    gs.action_priority = {
        "pass": 0,
        "chi_left": 1,
        "peng": 2,
        "gang": 2,
        "hu": 3,
    }
    gs._tactical_action_snapshot = {
        0: ["chi_left"],
        1: [replacement_action],
        2: [],
        3: [],
    }
    gs._tactical_passed_players = set()
    gs._tactical_committed_players = set()
    gs.action_dict = {
        0: ["chi_left"],
        1: [replacement_action],
        2: [],
        3: [],
    }
    gs.action_events = {index: asyncio.Event() for index in range(4)}
    gs.action_queues = {index: asyncio.Queue() for index in range(4)}
    return gs


def synthetic_game_start(*, self_index: int = 0, cut_tile: Optional[int] = None) -> Dict[str, Any]:
    players = []
    for seat in range(4):
        discards = []
        if cut_tile is not None and seat == 0:
            discards = [int(cut_tile)]
        players.append(
            {
                "user_id": VIEWER_UID + seat,
                "username": f"P{seat}",
                "player_index": seat,
                "score": 0,
                "hand_tiles": [11, 12, 13] if seat == self_index else None,
                "hand_tiles_count": 13,
                "discard_tiles": discards,
                "combination_tiles": [],
                "combination_mask": [],
                "huapai_list": [],
                "tag_list": [],
            }
        )
    return {
        "type": "gamestate/guobiao/game_start",
        "success": True,
        "game_info": {
            "room_id": 1,
            "gamestate_id": "tactical-bridge",
            "room_rule": "guobiao",
            "current_round": 1,
            "tile_count": 80,
            "current_player_index": 0,
            "players_info": players,
        },
    }


def do_action_from_kwargs(kwargs: Dict[str, Any]) -> Dict[str, Any]:
    info = {
        "action_list": list(kwargs.get("action_list") or []),
        "action_player": kwargs.get("action_player"),
        "cut_tile": kwargs.get("cut_tile"),
        "cut_tiles": kwargs.get("cut_tiles"),
        "cut_from_player": kwargs.get("cut_from_player"),
        "is_claim": bool(kwargs.get("is_claim")),
        "silent": bool(kwargs.get("silent")),
        "deal_tile": kwargs.get("deal_tile"),
        "deal_tiles": kwargs.get("deal_tiles"),
        "buhua_tile": kwargs.get("buhua_tile"),
        "combination_mask": kwargs.get("combination_mask"),
        "combination_target": kwargs.get("combination_target"),
        "action_tick": kwargs.get("action_tick") or 0,
    }
    return {
        "type": "gamestate/guobiao/do_action",
        "success": True,
        "do_action_info": info,
    }


def ask_other_from_kwargs(kwargs: Dict[str, Any]) -> Dict[str, Any]:
    return {
        "type": "gamestate/guobiao/ask_other_action",
        "success": True,
        "ask_other_action_info": {
            "action_list": list(kwargs.get("action_list") or []),
            "cut_tile": kwargs.get("cut_tile"),
            "remaining_time": kwargs.get("remaining_time") or 5,
            "action_tick": kwargs.get("action_tick") or 0,
            "chi_candidates": kwargs.get("chi_candidates"),
            "is_tactical_recheck": kwargs.get("is_tactical_recheck"),
        },
    }


def apply_broadcasts(
    sent: List[Dict[str, Any]],
    *,
    initial_claim: Optional[Dict[str, Any]] = None,
    cut_tile: Optional[int] = None,
    asks: Optional[List[Dict[str, Any]]] = None,
) -> Dict[str, Any]:
    sim = UnitySim(viewer_user_id=VIEWER_UID)
    frames: List[Dict[str, Any]] = []

    def _push(message: Dict[str, Any]) -> None:
        sim.apply(message)
        frames.append(
            {
                "index": len(frames),
                "type": message.get("type"),
                "components": sim.snapshot(),
            }
        )

    _push(synthetic_game_start(cut_tile=cut_tile))
    if initial_claim:
        _push(do_action_from_kwargs(initial_claim))
    for kwargs in sent or []:
        _push(do_action_from_kwargs(kwargs))
    for kwargs in asks or []:
        _push(ask_other_from_kwargs(kwargs))
    return {
        "ok": True,
        "kind": "tactical",
        "frame_count": len(frames),
        "frames": frames,
        "unity": sim.snapshot(),
    }


def run_presubmit(replacement_action: str = "peng") -> Dict[str, Any]:
    from server.gamestate.public.tactical_claim import tactical_grace_phase

    def _sync():
        async def _go():
            gs = _make_grace_state(replacement_action)
            await gs.action_queues[1].put({"action_type": replacement_action})
            sent: List[Dict[str, Any]] = []

            async def broadcast_do_action(*_a, **kwargs):
                sent.append(kwargs)

            async def broadcast_ask_other_action(*_a, **_k):
                raise AssertionError("预提交抢断后不应再次询问")

            result = await tactical_grace_phase(
                gs,
                "chi_left",
                0,
                {"action_type": "chi_left"},
                33,
                broadcast_do_action=broadcast_do_action,
                broadcast_ask_other_action=broadcast_ask_other_action,
                initial_claim_broadcasted=True,
            )
            return list(result), sent

        return asyncio.run(_go())

    result, sent = _run_isolated(_sync)
    out = apply_broadcasts(
        sent,
        initial_claim={"action_list": ["chi_left"], "action_player": 0, "cut_tile": 33, "is_claim": True},
        cut_tile=33,
    )
    out["result"] = result
    displays = [frame["components"]["ActionDisplaySim"] for frame in out["frames"]]
    out["assertion"] = {
        "result_action": result[0],
        "result_player": result[1],
        "displays": displays,
    }
    return out


def run_tactical_switch(tactical_call: bool = True, claim_protection: bool = False) -> Dict[str, Any]:
    from server.gamestate.public.tactical_claim import apply_tactical_claim_if_needed

    def _sync():
        async def _go():
            gs = _FakeGameState()
            gs.tactical_call = tactical_call
            gs.claim_protection = claim_protection
            gs.game_status = "waiting_action_after_cut"
            gs.tactical_pre_grace_delay = 0
            gs.action_priority = {"pass": 0, "hu": 3}
            gs._tactical_action_snapshot = {0: ["hu"], 1: [], 2: [], 3: []}
            gs._tactical_passed_players = set()
            gs.player_list = [type("Player", (), {"discard_tiles": [33]})() for _ in range(4)]
            gs.current_player_index = 0
            sent: List[Dict[str, Any]] = []
            asks: List[Dict[str, Any]] = []

            async def broadcast_do_action(*_a, **kwargs):
                sent.append(kwargs)

            async def broadcast_ask_other_action(*_a, **kwargs):
                asks.append(kwargs)

            result = await apply_tactical_claim_if_needed(
                gs,
                "hu",
                0,
                {"action_type": "hu"},
                broadcast_do_action=broadcast_do_action,
                broadcast_ask_other_action=broadcast_ask_other_action,
            )
            return list(result), sent, asks

        return asyncio.run(_go())

    result, sent, asks = _run_isolated(_sync)
    out = apply_broadcasts(sent, cut_tile=33, asks=asks)
    out["result"] = result
    return out


def run_bridge(case_id: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    params = params or {}
    if case_id == "presubmit":
        return run_presubmit(str(params.get("replacement_action") or "peng"))
    if case_id == "tactical_switch":
        return run_tactical_switch(
            bool(params.get("tactical_call", True)),
            bool(params.get("claim_protection", False)),
        )
    raise KeyError(f"未知战鸣桥接: {case_id}")
