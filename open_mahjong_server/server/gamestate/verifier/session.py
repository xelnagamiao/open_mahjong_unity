"""进程内国标 Session：种子+操作日志驱动真实 GuobiaoGameState。"""
from __future__ import annotations

import asyncio
import logging
import time
import uuid
from typing import Any, Dict, List, Optional

from server.gamestate.game_guobiao.GuobiaoGameState import GuobiaoGameState
from server.gamestate.game_guobiao.get_action import get_ai_action
from server.gamestate.public.game_record_manager import jsonable_game_record
from server.gamestate.public.hand_slot_utils import hand_contains_tile, normalize_tile

from .dump import dump_gamestate
from .host import (
    LAB_USER_IDS,
    build_db_manager,
    build_game_server,
    build_room_data,
    install_lab_runtime,
    real_sleep,
    restore_lab_runtime,
    set_lab_turbo,
)
from .protocol import filter_client_actions
from .unity_sim import UnitySim
from .verdict import build_verdict

logger = logging.getLogger(__name__)


class VerifierError(Exception):
    def __init__(self, message: str, *, status_code: int = 400):
        super().__init__(message)
        self.status_code = status_code


class VerifierSession:
    def __init__(self, session_id: Optional[str] = None):
        self.id = session_id or uuid.uuid4().hex[:12]
        self.seed = 0
        self.debug_scenario: Optional[str] = None
        self.tactical_call = False
        self.game_round = 1
        self.hepai_limit = 8
        self.actions: List[Dict[str, Any]] = []
        self.bookmarks: Dict[str, int] = {}
        self.messages: List[Dict[str, Any]] = []
        self.trace: List[Dict[str, Any]] = []
        self.tool_calls: List[Dict[str, Any]] = []
        self.game: Optional[GuobiaoGameState] = None
        self.task: Optional[asyncio.Task] = None
        self.server = None
        self.started_at = 0.0
        self._runtime_installed = False
        self.turbo = True
        self.viewer_user_id = LAB_USER_IDS[0]
        self.sim = UnitySim(viewer_user_id=self.viewer_user_id)

    @property
    def paused(self) -> bool:
        game = self.game
        if game is None:
            return False
        waiting = getattr(game, "waiting_players_list", None) or []
        action_dict = getattr(game, "action_dict", None) or {}
        return bool(waiting) and any(action_dict.get(seat) for seat in waiting)

    def loop_error(self) -> Optional[str]:
        task = self.task
        if task is None or not task.done() or task.cancelled():
            return None
        exc = task.exception()
        if exc is None:
            return None
        return f"{type(exc).__name__}: {exc}"

    def ended(self) -> bool:
        game = self.game
        if game is None:
            return True
        if self.task is not None and self.task.done():
            return True
        return getattr(game, "game_status", None) in ("END", "finished")

    async def start(
        self,
        *,
        seed: int,
        debug_scenario: Optional[str] = None,
        tactical_call: bool = False,
        game_round: int = 1,
        hepai_limit: int = 8,
        replay_actions: Optional[List[Dict[str, Any]]] = None,
        turbo: bool = True,
    ) -> None:
        await self.stop()
        self.turbo = bool(turbo)
        install_lab_runtime(turbo=self.turbo)
        self._runtime_installed = True
        set_lab_turbo(self.turbo)
        self.seed = int(seed)
        self.debug_scenario = debug_scenario or None
        self.tactical_call = bool(tactical_call)
        self.game_round = int(game_round)
        self.hepai_limit = int(hepai_limit)
        self.actions = []
        self.messages = []
        self.trace = []
        self.started_at = time.time()
        self.viewer_user_id = LAB_USER_IDS[0]
        self.sim = UnitySim(viewer_user_id=self.viewer_user_id)

        db = build_db_manager()
        self.server = build_game_server(db, messages=self.messages, on_send=self._on_ws)
        room_id = int(uuid.uuid4().int % 1_000_000_000)
        room = build_room_data(
            seed=self.seed,
            room_id=room_id,
            game_round=self.game_round,
            tactical_call=self.tactical_call,
            hepai_limit=self.hepai_limit,
        )
        gamestate_id = f"verifier-{self.id}"
        game = GuobiaoGameState(
            self.server,
            room,
            self.server.calculation_service,
            db,
            gamestate_id,
        )
        if self.debug_scenario:
            game.Debug = True
            game.debug_scenario = self.debug_scenario
            game.hepai_limit = self.hepai_limit
        self.game = game
        self.sim.tingpai_fn = (
            lambda hand, combos, svc=game.calculation_service: svc.GB_tingpai_check(hand, combos)
        )
        self.server.gamestate_manager.gamestate_id_to_game_state[gamestate_id] = game
        self.server.gamestate_manager.room_id_to_GuobiaoGameState[room_id] = game
        self.task = asyncio.create_task(game.game_loop_chinese(), name=f"verifier-{self.id}")
        await self.wait_paused(timeout=20)
        for action in replay_actions or []:
            if action.get("action_type") == "__timeout__":
                await self.force_timeout(record=False)
                self.actions.append(dict(action))
            else:
                await self.submit(action, record=True)
        if not self.ended():
            await self.wait_paused(timeout=20)

    def _on_ws(self, entry: Dict[str, Any]) -> None:
        if entry.get("user_id") != self.viewer_user_id:
            return
        try:
            components = self.sim.apply(entry.get("message") or {})
        except Exception:
            logger.exception("UnitySim.apply failed type=%s", entry.get("type"))
            return
        self.trace.append(
            {
                "index": len(self.trace),
                "type": entry.get("type"),
                "user_id": entry.get("user_id"),
                "components": components,
            }
        )

    async def _wait_until_advanced(self, before: tuple, timeout: float = 8.0) -> None:
        game = self.game
        deadline = time.time() + timeout
        while time.time() < deadline:
            if game is None:
                return
            now = (
                getattr(game, "server_action_tick", None),
                tuple(getattr(game, "waiting_players_list", []) or []),
                getattr(game, "game_status", None),
            )
            if now != before or self.ended():
                return
            await real_sleep(0.05)
        raise VerifierError("动作提交后对局没有推进", status_code=504)

    async def stop(self) -> None:
        task = self.task
        game = self.game
        if task is not None and not task.done():
            task.cancel()
            try:
                await task
            except (asyncio.CancelledError, Exception):
                pass
        if game is not None:
            try:
                await game.cleanup_game_state()
            except Exception:
                logger.debug("cleanup_game_state failed", exc_info=True)
        self.task = None
        self.game = None
        if self._runtime_installed:
            restore_lab_runtime()
            self._runtime_installed = False

    async def wait_paused(self, timeout: float = 15.0) -> str:
        deadline = time.time() + timeout
        while time.time() < deadline:
            err = self.loop_error()
            if err:
                raise VerifierError(f"对局循环异常: {err}", status_code=500)
            if self.paused:
                return "ask"
            if self.ended():
                return "ended"
            await real_sleep(0.05)
        status = getattr(self.game, "game_status", None)
        waiting = list(getattr(self.game, "waiting_players_list", []) or [])
        raise VerifierError(
            f"等待询问超时 status={status} waiting={waiting} tick={getattr(self.game, 'server_action_tick', None)}",
            status_code=504,
        )

    def legal_actions(self) -> Dict[str, Any]:
        game = self.game
        if game is None:
            return {"seats": {}, "branches": []}
        status = getattr(game, "game_status", "") or ""
        kind = "other"
        if status in ("waiting_hand_action", "waiting_buhua_round", "waiting_ready"):
            kind = "hand"
        seats: Dict[str, Any] = {}
        branches: List[Dict[str, Any]] = []
        action_dict = getattr(game, "action_dict", {}) or {}
        waiting = set(getattr(game, "waiting_players_list", []) or [])
        for player in game.player_list:
            seat = player.player_index
            server_actions = list(action_dict.get(seat) or [])
            client_actions = filter_client_actions(server_actions, kind=kind)
            if status == "waiting_ready":
                client_actions = [action for action in server_actions if action == "ready"]
            forbidden = []
            last_ask = self._last_ask_for_seat(seat)
            if last_ask and last_ask.get("type") == "gamestate/guobiao/broadcast_hand_action":
                info = last_ask.get("message", {}).get("ask_hand_action_info") or {}
                forbidden = list(info.get("forbidden_cut_tiles") or [])
            cut_tiles = []
            if "cut" in client_actions:
                seen = set()
                for tile in player.hand_tiles:
                    if tile in seen:
                        continue
                    if any(normalize_tile(tile) == normalize_tile(item) for item in forbidden):
                        continue
                    seen.add(tile)
                    cut_tiles.append(tile)
            target_tiles = {
                "angang": list(self._angang_tiles(player)) if "angang" in client_actions else [],
                "jiagang": list(self._jiagang_tiles(player)) if "jiagang" in client_actions else [],
            }
            claim_tile = None
            if last_ask and last_ask.get("type") == "gamestate/guobiao/ask_other_action":
                info = last_ask.get("message", {}).get("ask_other_action_info") or {}
                claim_tile = info.get("cut_tile")
            seats[str(seat)] = {
                "player_index": seat,
                "user_id": player.user_id,
                "username": player.username,
                "waiting": seat in waiting,
                "server_actions": server_actions,
                "client_actions": client_actions,
                "cut_tiles": cut_tiles,
                "forbidden_cut_tiles": forbidden,
                "target_tiles": target_tiles,
                "claim_tile": claim_tile,
            }
            if seat not in waiting:
                continue
            for action in client_actions:
                if action == "cut":
                    for tile in cut_tiles:
                        branches.append(
                            {
                                "player_index": seat,
                                "action_type": "cut",
                                "tile_id": tile,
                            }
                        )
                elif action in ("angang", "jiagang"):
                    for tile in target_tiles.get(action) or []:
                        branches.append(
                            {
                                "player_index": seat,
                                "action_type": action,
                                "target_tile": tile,
                            }
                        )
                else:
                    branches.append(
                        {
                            "player_index": seat,
                            "action_type": action,
                            "target_tile": claim_tile,
                        }
                    )
        return {"kind": kind, "game_status": status, "seats": seats, "branches": branches}

    def _angang_tiles(self, player) -> List[int]:
        seen = []
        for tile in player.hand_tiles:
            if tile in seen:
                continue
            if player.hand_tiles.count(tile) >= 4:
                seen.append(tile)
        return seen

    def _jiagang_tiles(self, player) -> List[int]:
        out = []
        for combo in player.combination_tiles:
            if not isinstance(combo, str) or not combo.startswith("k"):
                continue
            try:
                target = normalize_tile(int(combo[1:]))
            except (TypeError, ValueError):
                continue
            if any(normalize_tile(tile) == target for tile in player.hand_tiles) and target not in out:
                out.append(target)
        return out

    def _last_ask_for_seat(self, seat: int) -> Optional[Dict[str, Any]]:
        game = self.game
        if game is None:
            return None
        user_id = next((p.user_id for p in game.player_list if p.player_index == seat), None)
        for item in reversed(self.messages):
            if item.get("user_id") != user_id:
                continue
            msg_type = item.get("type") or ""
            if msg_type in (
                "gamestate/guobiao/broadcast_hand_action",
                "gamestate/guobiao/ask_other_action",
                "gamestate/guobiao/ready_status",
            ):
                return item
        return None

    def _validate(self, action: Dict[str, Any]) -> Dict[str, Any]:
        game = self.game
        if game is None:
            raise VerifierError("没有进行中的对局")
        try:
            seat = int(action["player_index"])
        except (KeyError, TypeError, ValueError) as exc:
            raise VerifierError("缺少 player_index") from exc
        action_type = str(action.get("action_type") or "")
        if seat not in (0, 1, 2, 3):
            raise VerifierError(f"非法座位 {seat}")
        if seat not in (getattr(game, "waiting_players_list", None) or []):
            raise VerifierError(
                f"座位 {seat} 当前不能操作 waiting={list(game.waiting_players_list)}"
            )
        allowed = list((getattr(game, "action_dict", None) or {}).get(seat) or [])
        if action_type not in allowed:
            raise VerifierError(
                f"服务端不允许 {action_type}，当前 {allowed}"
            )
        legal = self.legal_actions()
        seat_info = legal["seats"].get(str(seat)) or {}
        if action_type not in (seat_info.get("client_actions") or []):
            raise VerifierError(
                f"模拟客户端不会画出 {action_type}，Unity 白名单={seat_info.get('client_actions')}"
            )
        player = game.player_list[seat]
        payload = {
            "player_index": seat,
            "action_type": action_type,
            "tile_id": action.get("tile_id"),
            "target_tile": action.get("target_tile"),
            "cutClass": bool(action.get("cutClass", False)),
            "cutIndex": action.get("cutIndex"),
        }
        if action_type == "cut":
            tile_id = int(action.get("tile_id"))
            if not hand_contains_tile(player.hand_tiles, tile_id):
                raise VerifierError(f"切牌 {tile_id} 不在手牌 {player.hand_tiles}")
            if tile_id not in (seat_info.get("cut_tiles") or []):
                raise VerifierError(f"切牌 {tile_id} 不在可切列表")
            if payload["cutIndex"] is None:
                payload["cutIndex"] = max(i for i, tile in enumerate(player.hand_tiles) if tile == tile_id)
            payload["tile_id"] = tile_id
        elif action_type == "angang":
            target = int(action.get("target_tile") if action.get("target_tile") is not None else action.get("tile_id"))
            if target not in (seat_info.get("target_tiles") or {}).get("angang", []):
                raise VerifierError(f"暗杠目标 {target} 非法")
            payload["target_tile"] = target
        elif action_type == "jiagang":
            target = int(action.get("target_tile") if action.get("target_tile") is not None else action.get("tile_id"))
            if target not in (seat_info.get("target_tiles") or {}).get("jiagang", []):
                raise VerifierError(f"加杠目标 {target} 非法")
            payload["target_tile"] = target
        return payload

    async def submit(self, action: Dict[str, Any], *, record: bool = True) -> Dict[str, Any]:
        game = self.game
        if game is None:
            raise VerifierError("没有进行中的对局")
        payload = self._validate(action)
        before = (
            getattr(game, "server_action_tick", None),
            tuple(getattr(game, "waiting_players_list", []) or []),
            getattr(game, "game_status", None),
        )
        await get_ai_action(
            game,
            payload["player_index"],
            payload["action_type"],
            bool(payload.get("cutClass")),
            int(payload["tile_id"] or 0),
            int(payload["cutIndex"] or 0),
            int(payload["target_tile"] or 0),
        )
        if record:
            self.actions.append(payload)
        await self._wait_until_advanced(before)
        if not self.ended():
            await self.wait_paused(timeout=12)
        return self.snapshot()

    async def submit_batch(self, actions: List[Dict[str, Any]], *, record: bool = True) -> Dict[str, Any]:
        game = self.game
        if game is None:
            raise VerifierError("没有进行中的对局")
        payloads = [self._validate(action) for action in actions]
        before = (
            getattr(game, "server_action_tick", None),
            tuple(getattr(game, "waiting_players_list", []) or []),
            getattr(game, "game_status", None),
        )
        for payload in payloads:
            await get_ai_action(
                game,
                payload["player_index"],
                payload["action_type"],
                bool(payload.get("cutClass")),
                int(payload["tile_id"] or 0),
                int(payload["cutIndex"] or 0),
                int(payload["target_tile"] or 0),
            )
            if record:
                self.actions.append(payload)
        await self._wait_until_advanced(before)
        if not self.ended():
            await self.wait_paused(timeout=12)
        return self.snapshot()

    async def ready_all(self) -> Dict[str, Any]:
        for _ in range(8):
            legal = self.legal_actions()
            pending = [
                int(seat)
                for seat, info in legal["seats"].items()
                if info.get("waiting") and "ready" in (info.get("client_actions") or [])
            ]
            if not pending:
                break
            await self.submit({"player_index": pending[0], "action_type": "ready"})
        return self.snapshot()

    async def force_timeout(self, *, record: bool = True) -> Dict[str, Any]:
        game = self.game
        if game is None:
            raise VerifierError("没有进行中的对局")
        waiting = list(getattr(game, "waiting_players_list", []) or [])
        if not waiting:
            raise VerifierError("当前没有等待中的操作")
        past = time.time() - (float(getattr(game, "step_time", 0) or 0) + 10)
        delivered = getattr(game, "_ask_delivered_at", None)
        if delivered is None:
            game._ask_delivered_at = {}
            delivered = game._ask_delivered_at
        for seat in waiting:
            game.player_list[seat].remaining_time = 0
            delivered[seat] = past
        if record:
            self.actions.append({"player_index": waiting[0], "action_type": "__timeout__"})
        await self._wait_until_advanced(
            (
                getattr(game, "server_action_tick", None),
                tuple(waiting),
                getattr(game, "game_status", None),
            )
        )
        if not self.ended():
            await self.wait_paused(timeout=12)
        return self.snapshot()

    async def restore(self, index: int) -> Dict[str, Any]:
        if index < 0 or index > len(self.actions):
            raise VerifierError(f"回退下标越界 index={index} len={len(self.actions)}")
        prefix = list(self.actions[:index])
        bookmarks = dict(self.bookmarks)
        await self.start(
            seed=self.seed,
            debug_scenario=self.debug_scenario,
            tactical_call=self.tactical_call,
            game_round=self.game_round,
            hepai_limit=self.hepai_limit,
            replay_actions=prefix,
            turbo=self.turbo,
        )
        self.bookmarks = bookmarks
        return self.snapshot()

    def set_bookmark(self, name: str) -> Dict[str, Any]:
        name = str(name or "").strip()
        if not name:
            raise VerifierError("书签名为空")
        self.bookmarks[name] = len(self.actions)
        return {"name": name, "index": self.bookmarks[name]}

    def record_detail(self) -> Dict[str, Any]:
        game = self.game
        players = []
        if game is not None:
            for player in game.player_list:
                players.append(
                    {
                        "user_id": player.user_id,
                        "username": player.username,
                        "score": int(player.score or 0),
                        "rank": int(getattr(player.record_counter, "rank_result", 0) or 0),
                        "original_player_index": player.original_player_index,
                    }
                )
        else:
            players = [
                {
                    "user_id": uid,
                    "username": f"P{index}",
                    "score": 0,
                    "rank": 0,
                    "original_player_index": index,
                }
                for index, uid in enumerate(LAB_USER_IDS)
            ]
        record = {}
        if game is not None:
            record = jsonable_game_record(getattr(game, "game_record", None) or {})
        return {
            "game_id": self.id,
            "created_at": "",
            "rule": "guobiao",
            "sub_rule": getattr(game, "sub_rule", "guobiao/standard") if game else "guobiao/standard",
            "room_type": "custom",
            "players": players,
            "record": record,
        }

    def call_tingpai(self, seat: int) -> Dict[str, Any]:
        game = self.game
        if game is None:
            raise VerifierError("没有进行中的对局")
        player = game.player_list[int(seat)]
        args = {
            "hand_tiles": list(player.hand_tiles),
            "combination_tiles": list(player.combination_tiles),
        }
        waiting = sorted(
            game.calculation_service.GB_tingpai_check(args["hand_tiles"], args["combination_tiles"])
        )
        result = {
            "seat": int(seat),
            "waiting_tiles": waiting,
            "player_waiting_tiles": sorted(player.waiting_tiles or []),
        }
        self.tool_calls.append({"tool": "GB_tingpai_check", "args": args, "result": result})
        return result

    def snapshot(self) -> Dict[str, Any]:
        dump = dump_gamestate(self.game) if self.game is not None else {}
        trace_meta = [{"index": frame["index"], "type": frame.get("type")} for frame in self.trace]
        payload = {
            "id": self.id,
            "seed": self.seed,
            "debug_scenario": self.debug_scenario,
            "tactical_call": self.tactical_call,
            "game_round": self.game_round,
            "hepai_limit": self.hepai_limit,
            "ended": self.ended(),
            "paused": self.paused,
            "loop_error": self.loop_error(),
            "action_count": len(self.actions),
            "actions": list(self.actions),
            "bookmarks": dict(self.bookmarks),
            "legal": self.legal_actions(),
            "dump": dump,
            "messages": list(self.messages[-80:]),
            "message_count": len(self.messages),
            "record": self.record_detail(),
            "tool_calls": list(self.tool_calls[-20:]),
            "user_ids": list(LAB_USER_IDS),
            "viewer_user_id": self.viewer_user_id,
            "turbo": self.turbo,
            "unity": self.sim.snapshot(),
            "trace_len": len(self.trace),
            "trace_meta": trace_meta,
        }
        kind = getattr(self, "catalog_kind", None) or ("debug" if self.debug_scenario else "script")
        payload.update(
            build_verdict(
                kind=kind,
                trace_meta=trace_meta,
                frames=self.trace,
                loop_error=self.loop_error(),
                pytest=getattr(self, "pytest_result", None),
            )
        )
        return payload

    def frame(self, index: int) -> Dict[str, Any]:
        if not self.trace:
            raise VerifierError("还没有轨迹")
        if index < 0:
            index = len(self.trace) + index
        if index < 0 or index >= len(self.trace):
            raise VerifierError(f"帧越界 index={index} len={len(self.trace)}")
        return self.trace[index]


class TracePlayback:
    """战鸣桥接等无 GameState 的轨迹回放。"""

    def __init__(self, frames: List[Dict[str, Any]], extra: Optional[Dict[str, Any]] = None):
        self.id = uuid.uuid4().hex[:12]
        self.trace = list(frames or [])
        self.extra = extra or {}
        self.game = None
        self.sim = None
        self.actions: List[Dict[str, Any]] = []
        self.turbo = True
        self.viewer_user_id = LAB_USER_IDS[0]

    def snapshot(self) -> Dict[str, Any]:
        last = self.trace[-1] if self.trace else {}
        trace_meta = [
            {"index": frame.get("index", i), "type": frame.get("type")}
            for i, frame in enumerate(self.trace)
        ]
        payload = {
            "id": self.id,
            "ended": True,
            "paused": False,
            "loop_error": None,
            "kind": self.extra.get("kind"),
            "unity": last.get("components") or {},
            "trace_len": len(self.trace),
            "trace_meta": trace_meta,
            "pytest": self.extra.get("pytest"),
            "assertion": self.extra.get("assertion"),
            "item": self.extra.get("item"),
            "legal": {"seats": {}, "branches": []},
            "dump": {},
            "messages": [],
            "message_count": 0,
            "actions": [],
            "action_count": 0,
        }
        payload.update(
            build_verdict(
                kind=self.extra.get("kind"),
                trace_meta=trace_meta,
                frames=self.trace,
                loop_error=None,
                pytest=self.extra.get("pytest"),
            )
        )
        return payload

    def frame(self, index: int) -> Dict[str, Any]:
        if not self.trace:
            raise VerifierError("还没有轨迹")
        if index < 0:
            index = len(self.trace) + index
        if index < 0 or index >= len(self.trace):
            raise VerifierError(f"帧越界 index={index} len={len(self.trace)}")
        return self.trace[index]

    async def stop(self) -> None:
        return
