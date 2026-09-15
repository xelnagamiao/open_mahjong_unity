"""验证器宿主：mock 连接、捕获 WS、压缩演出 sleep、拉长 ready 等待。"""
from __future__ import annotations

import asyncio
import logging
from types import SimpleNamespace
from typing import Any, Dict, List, Optional
from unittest.mock import AsyncMock, MagicMock

from server.game_calculation.game_calculation_service import GameCalculationService

logger = logging.getLogger(__name__)

LAB_USER_IDS = (101, 102, 103, 104)
LAB_REMAINING_TIME = 86400
_PATCH_DEPTH = 0
_REAL_SLEEP = None
_REAL_READY_WAIT = None
_REAL_READY_PRE = None
_TURBO = False


class CaptureSocket:
    """代替 websocket：把 send_json 记进共享日志，并可选回调给 UnitySim。"""

    def __init__(self, user_id: int, messages: List[Dict[str, Any]], on_send=None):
        self.user_id = user_id
        self._messages = messages
        self._on_send = on_send

    async def send_json(self, payload: Any) -> None:
        message = payload if isinstance(payload, dict) else {"raw": payload}
        entry = {
            "user_id": self.user_id,
            "type": message.get("type"),
            "message": message,
        }
        self._messages.append(entry)
        if self._on_send is not None:
            self._on_send(entry)


def build_room_data(
    *,
    seed: int,
    room_id: str,
    game_round: int = 1,
    tactical_call: bool = False,
    hepai_limit: int = 8,
    tips: bool = True,
) -> dict:
    settings = {
        uid: {"username": f"P{index}"}
        for index, uid in enumerate(LAB_USER_IDS)
    }
    return {
        "room_id": room_id,
        "player_list": list(LAB_USER_IDS),
        "player_settings": settings,
        "tips": tips,
        "game_round": game_round,
        "step_timer": LAB_REMAINING_TIME,
        "round_timer": LAB_REMAINING_TIME,
        "room_rule": "guobiao",
        "room_type": "custom",
        "sub_rule": "guobiao/standard",
        "random_seed": seed,
        "open_cuohe": False,
        "cuohe_type": 0,
        "show_moqie_hint": False,
        "hepai_limit": hepai_limit,
        "tactical_call": tactical_call,
        "claim_protection": False,
        "tactical_pre_grace_delay": 0.0,
        "tactical_grace_seconds": 0.05 if tactical_call else 0.0,
        "claim_protect_delay": 0.0,
        "claim_meld_followup_gap": 0.0,
        "claim_meld_post_gap": 0.0,
        "allow_spectator": False,
        "match_queue_type": None,
        "match_tier": None,
        "event_id": None,
        "is_game_running": True,
    }


def build_db_manager() -> MagicMock:
    db = MagicMock()
    db.get_rank_data.return_value = None
    db.store_guobiao_game_record.return_value = None
    db.store_guobiao_game_stats.return_value = None
    db.store_guobiao_fan_stats.return_value = None
    db.update_rank_data.return_value = None
    return db


def build_game_server(
    db_manager: MagicMock,
    *,
    messages: List[Dict[str, Any]],
    on_send=None,
) -> SimpleNamespace:
    gsm = SimpleNamespace(
        room_id_to_GuobiaoGameState={},
        room_id_to_QingqueGameState={},
        room_id_to_ChangshaGameState={},
        room_id_to_ClassicalGameState={},
        room_id_to_RiichiGameState={},
        room_id_to_SichuanGameState={},
        room_id_to_JiandanGameState={},
        room_id_to_TaiwanGameState={},
        gamestate_id_to_game_state={},
        user_id_to_game_state={},
        game_server=None,
    )

    async def cleanup_game_state_complete(gamestate_id: str = None, room_id: str = None):
        game_state = None
        if gamestate_id:
            game_state = gsm.gamestate_id_to_game_state.get(gamestate_id)
        if game_state is None:
            return
        for player in game_state.player_list:
            gsm.user_id_to_game_state.pop(player.user_id, None)
        gsm.room_id_to_GuobiaoGameState.pop(game_state.room_id, None)
        gsm.gamestate_id_to_game_state.pop(game_state.gamestate_id, None)
        await game_state.cleanup_game_state()

    gsm.cleanup_game_state_complete = cleanup_game_state_complete
    gsm.get_game_state_by_gamestate_id = lambda gid: gsm.gamestate_id_to_game_state.get(gid)

    players = {}
    connections = {}
    for uid in LAB_USER_IDS:
        sock = CaptureSocket(uid, messages, on_send=on_send)
        conn = SimpleNamespace(
            user_id=uid,
            websocket=sock,
            current_room_id=None,
            username=f"P{uid}",
        )
        players[f"conn-{uid}"] = conn
        connections[uid] = conn

    server = SimpleNamespace(
        user_id_to_connection=connections,
        players=players,
        db_manager=db_manager,
        calculation_service=GameCalculationService(),
        gamestate_manager=gsm,
        room_manager=SimpleNamespace(
            rooms={},
            finish_custom_game_room=AsyncMock(),
        ),
        match_manager=None,
        friend_manager=None,
    )
    gsm.game_server = server
    return server


def _lab_sleep_factory(real_sleep):
    async def lab_sleep(delay=0, result=None):
        try:
            seconds = float(delay or 0)
        except (TypeError, ValueError):
            seconds = 0.0
        # turbo：含 wait_action 的 1s 轮询全部压成 0，可视化跟不上是预期。
        # 非 turbo：wait_action 的 sleep(1) 必须保留，注入才能立刻唤醒。
        if _TURBO:
            await real_sleep(0)
        elif seconds >= 1.5:
            await real_sleep(0)
        elif seconds >= 0.99:
            await real_sleep(seconds)
        else:
            await real_sleep(0)
        return result

    return lab_sleep


def set_lab_turbo(enabled: bool) -> None:
    global _TURBO
    _TURBO = bool(enabled)


def install_lab_runtime(*, turbo: bool = True) -> None:
    """进程级补丁：压缩演出 sleep，并把局终 ready 时限拉到一天。"""
    global _PATCH_DEPTH, _REAL_SLEEP, _REAL_READY_WAIT, _REAL_READY_PRE
    set_lab_turbo(turbo)
    _PATCH_DEPTH += 1
    if _PATCH_DEPTH > 1:
        return

    import server.gamestate.public.ready_phase as ready_phase

    _REAL_SLEEP = asyncio.sleep
    asyncio.sleep = _lab_sleep_factory(_REAL_SLEEP)  # type: ignore[assignment]
    _REAL_READY_WAIT = ready_phase.hu_result_ready_wait_seconds
    _REAL_READY_PRE = ready_phase.hu_result_ready_pre_panel_seconds
    ready_phase.hu_result_ready_wait_seconds = lambda *a, **k: float(LAB_REMAINING_TIME)
    ready_phase.hu_result_ready_pre_panel_seconds = lambda: 0.0


def real_sleep(delay=0):
    """未被 lab 压缩的真实 asyncio.sleep（用于轮询询问）。"""
    sleeper = _REAL_SLEEP if _REAL_SLEEP is not None else asyncio.sleep
    return sleeper(delay)


def restore_lab_runtime() -> None:
    global _PATCH_DEPTH, _REAL_SLEEP, _REAL_READY_WAIT, _REAL_READY_PRE
    if _PATCH_DEPTH <= 0:
        return
    _PATCH_DEPTH -= 1
    if _PATCH_DEPTH > 0:
        return
    import server.gamestate.public.ready_phase as ready_phase

    if _REAL_SLEEP is not None:
        asyncio.sleep = _REAL_SLEEP  # type: ignore[assignment]
    if _REAL_READY_WAIT is not None:
        ready_phase.hu_result_ready_wait_seconds = _REAL_READY_WAIT
    if _REAL_READY_PRE is not None:
        ready_phase.hu_result_ready_pre_panel_seconds = _REAL_READY_PRE
    _REAL_SLEEP = None
    _REAL_READY_WAIT = None
    _REAL_READY_PRE = None
    set_lab_turbo(False)
