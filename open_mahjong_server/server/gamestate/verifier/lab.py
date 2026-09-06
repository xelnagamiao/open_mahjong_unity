"""本地 FastAPI：只绑 127.0.0.1:8099。Unity 组件测试台；中间牌桌视觉复制 2D 牌谱阅览 MahjongScene。"""
from __future__ import annotations

import logging
from typing import Any, Dict, List, Optional, Union

from fastapi import FastAPI, HTTPException, Query
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

from .autoplay import autoplay, step_once
from .catalog import find_item, list_catalog, match_tactical_item, run_pytest_node, run_tactical_item
from .protocol import load_protocol
from .scenarios import get_scenario, list_scenarios
from .session import TracePlayback, VerifierError, VerifierSession

logger = logging.getLogger(__name__)

app = FastAPI(title="Guobiao Unity Sim Lab", docs_url="/docs")
app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "http://localhost:5173",
        "http://127.0.0.1:5173",
        "http://localhost:4173",
        "http://127.0.0.1:4173",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

_SESSIONS: Dict[str, Union[VerifierSession, TracePlayback]] = {}


class StartBody(BaseModel):
    scenario_id: Optional[str] = None
    seed: Optional[int] = None
    debug_scenario: Optional[str] = None
    tactical_call: Optional[bool] = None
    game_round: Optional[int] = None
    hepai_limit: Optional[int] = None
    turbo: bool = True
    autoplay: bool = False
    max_steps: int = 400
    policy: str = "heuristic"
    script: Optional[List[Dict[str, Any]]] = None


class ActionBody(BaseModel):
    player_index: int
    action_type: str
    tile_id: Optional[int] = None
    target_tile: Optional[int] = None
    cutClass: bool = False
    cutIndex: Optional[int] = None


class RestoreBody(BaseModel):
    index: int


class BookmarkBody(BaseModel):
    name: str = Field(min_length=1)


class TingpaiBody(BaseModel):
    seat: int


class AutoplayBody(BaseModel):
    max_steps: int = 400
    policy: str = "heuristic"
    script: Optional[List[Dict[str, Any]]] = None


class StepBody(BaseModel):
    policy: str = "heuristic"
    include_self: Optional[bool] = None


class RunBody(BaseModel):
    item_id: str
    max_steps: int = 400
    policy: str = "heuristic"
    turbo: bool = True
    autoplay: bool = True


def _http(exc: VerifierError) -> HTTPException:
    return HTTPException(status_code=exc.status_code, detail=str(exc))


def _live(session: Union[VerifierSession, TracePlayback]) -> VerifierSession:
    if not isinstance(session, VerifierSession):
        raise HTTPException(status_code=400, detail="该轨迹不是可操作的对局 Session")
    return session


@app.get("/health")
async def health() -> Dict[str, Any]:
    return {"ok": True, "sessions": list(_SESSIONS)}


@app.get("/scenarios")
async def scenarios() -> Dict[str, Any]:
    return {"scenarios": list_scenarios()}


@app.get("/protocol")
async def protocol() -> Dict[str, Any]:
    return load_protocol()


@app.get("/tests")
async def tests(refresh: bool = False) -> Dict[str, Any]:
    return list_catalog(refresh=refresh)


def _scenario_cfg(body: StartBody) -> Dict[str, Any]:
    cfg: Dict[str, Any] = {
        "seed": 72001,
        "debug_scenario": None,
        "tactical_call": False,
        "game_round": 1,
        "hepai_limit": 8,
        "turbo": body.turbo,
    }
    if body.scenario_id:
        try:
            scenario = get_scenario(body.scenario_id)
        except KeyError as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc
        cfg["seed"] = scenario.get("seed", cfg["seed"])
        cfg["debug_scenario"] = scenario.get("debug_scenario")
        cfg["tactical_call"] = bool(scenario.get("tactical_call", False))
        cfg["game_round"] = int(scenario.get("game_round", 1))
        cfg["hepai_limit"] = int(scenario.get("hepai_limit", 8))
    if body.seed is not None:
        cfg["seed"] = body.seed
    if body.debug_scenario is not None:
        cfg["debug_scenario"] = body.debug_scenario or None
    if body.tactical_call is not None:
        cfg["tactical_call"] = body.tactical_call
    if body.game_round is not None:
        cfg["game_round"] = body.game_round
    if body.hepai_limit is not None:
        cfg["hepai_limit"] = body.hepai_limit
    return cfg


@app.post("/sessions")
async def create_session(body: StartBody) -> Dict[str, Any]:
    cfg = _scenario_cfg(body)
    session = VerifierSession()
    try:
        await session.start(**cfg)
        if body.autoplay:
            await autoplay(
                session,
                max_steps=body.max_steps,
                policy=body.policy,
                script=body.script,
            )
    except VerifierError as exc:
        await session.stop()
        raise _http(exc) from exc
    except Exception as exc:
        await session.stop()
        logger.exception("create session failed")
        raise HTTPException(status_code=500, detail=str(exc)) from exc
    _SESSIONS[session.id] = session
    return session.snapshot()


def _session(session_id: str) -> Union[VerifierSession, TracePlayback]:
    session = _SESSIONS.get(session_id)
    if session is None:
        raise HTTPException(status_code=404, detail=f"session {session_id} 不存在")
    return session


@app.get("/sessions/{session_id}")
async def get_session(session_id: str) -> Dict[str, Any]:
    return _session(session_id).snapshot()


@app.get("/sessions/{session_id}/trace")
async def get_trace(
    session_id: str,
    frame: Optional[int] = Query(default=None),
) -> Dict[str, Any]:
    session = _session(session_id)
    if frame is None:
        snap = session.snapshot()
        return {
            "id": snap.get("id"),
            "trace_len": snap.get("trace_len"),
            "trace_meta": snap.get("trace_meta"),
        }
    try:
        return session.frame(frame)
    except VerifierError as exc:
        raise _http(exc) from exc


@app.delete("/sessions/{session_id}")
async def delete_session(session_id: str) -> Dict[str, Any]:
    session = _session(session_id)
    await session.stop()
    _SESSIONS.pop(session_id, None)
    return {"ok": True}


@app.post("/sessions/{session_id}/actions")
async def post_action(session_id: str, body: ActionBody) -> Dict[str, Any]:
    session = _live(_session(session_id))
    try:
        return await session.submit(body.model_dump())
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/autoplay")
async def post_autoplay(session_id: str, body: AutoplayBody) -> Dict[str, Any]:
    session = _live(_session(session_id))
    try:
        return await autoplay(
            session,
            max_steps=body.max_steps,
            policy=body.policy,
            script=body.script,
        )
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/step")
async def post_step(session_id: str, body: StepBody) -> Dict[str, Any]:
    session = _live(_session(session_id))
    try:
        return await step_once(session, policy=body.policy, include_self=body.include_self)
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/ready-all")
async def post_ready_all(session_id: str) -> Dict[str, Any]:
    try:
        return await _live(_session(session_id)).ready_all()
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/timeout")
async def post_timeout(session_id: str) -> Dict[str, Any]:
    try:
        return await _live(_session(session_id)).force_timeout()
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/restore")
async def post_restore(session_id: str, body: RestoreBody) -> Dict[str, Any]:
    try:
        return await _live(_session(session_id)).restore(body.index)
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/bookmarks")
async def post_bookmark(session_id: str, body: BookmarkBody) -> Dict[str, Any]:
    try:
        return _live(_session(session_id)).set_bookmark(body.name)
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/tools/tingpai")
async def post_tingpai(session_id: str, body: TingpaiBody) -> Dict[str, Any]:
    try:
        return _live(_session(session_id)).call_tingpai(body.seat)
    except VerifierError as exc:
        raise _http(exc) from exc


def _store_playback(frames: List[Dict[str, Any]], extra: Dict[str, Any]) -> TracePlayback:
    playback = TracePlayback(frames, extra)
    _SESSIONS[playback.id] = playback
    return playback


@app.post("/tests/run")
async def tests_run(body: RunBody) -> Dict[str, Any]:
    try:
        item = find_item(body.item_id)
    except KeyError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc

    kind = item.get("kind")
    try:
        if kind == "tactical":
            result = run_tactical_item(item)
            playback = _store_playback(
                (result.get("trace") or {}).get("frames") or [],
                {
                    "kind": "tactical",
                    "ok": result.get("ok"),
                    "pytest": result.get("pytest"),
                    "assertion": (result.get("trace") or {}).get("assertion"),
                    "item": item,
                },
            )
            snap = playback.snapshot()
            snap["pytest"] = result.get("pytest")
            return snap

        if kind == "pytest":
            pytest_out = run_pytest_node(item["nodeid"])
            bridge = match_tactical_item(item["nodeid"])
            frames: List[Dict[str, Any]] = []
            assertion = None
            if bridge:
                bridged = run_tactical_item(bridge)
                frames = (bridged.get("trace") or {}).get("frames") or []
                assertion = (bridged.get("trace") or {}).get("assertion")
            playback = _store_playback(
                frames,
                {
                    "kind": "pytest",
                    "ok": pytest_out.get("ok"),
                    "pytest": pytest_out,
                    "assertion": assertion,
                    "item": item,
                },
            )
            snap = playback.snapshot()
            snap["pytest"] = pytest_out
            return snap
    except Exception as exc:
        logger.exception("tests/run %s failed", item.get("id"))
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    cfg = {
        "seed": 72001,
        "debug_scenario": None,
        "tactical_call": False,
        "game_round": 1,
        "hepai_limit": 8,
        "turbo": body.turbo,
    }
    scenario_id = item.get("scenario_id")
    if scenario_id:
        try:
            scenario = get_scenario(scenario_id)
        except KeyError as exc:
            raise HTTPException(status_code=404, detail=str(exc)) from exc
        cfg["seed"] = scenario.get("seed", cfg["seed"])
        cfg["debug_scenario"] = scenario.get("debug_scenario")
        cfg["tactical_call"] = bool(scenario.get("tactical_call", False))
        cfg["game_round"] = int(scenario.get("game_round", 1))
        cfg["hepai_limit"] = int(scenario.get("hepai_limit", 8))
    session = VerifierSession()
    session.catalog_kind = kind
    try:
        await session.start(**cfg)
        if body.autoplay:
            await autoplay(
                session,
                max_steps=body.max_steps,
                policy=body.policy,
                script=item.get("script") or [],
            )
        elif item.get("script"):
            for action in item["script"]:
                if session.ended():
                    break
                await session.submit(action)
            if not session.ended():
                await session.wait_paused(timeout=12)
    except VerifierError as exc:
        await session.stop()
        raise _http(exc) from exc
    except Exception as exc:
        await session.stop()
        logger.exception("tests/run failed")
        raise HTTPException(status_code=500, detail=str(exc)) from exc
    _SESSIONS[session.id] = session
    snap = session.snapshot()
    snap["item"] = item
    return snap


def main() -> None:
    import uvicorn

    logging.basicConfig(level=logging.INFO)
    uvicorn.run(
        "server.gamestate.verifier.lab:app",
        host="127.0.0.1",
        port=8099,
        reload=False,
        log_level="info",
    )


if __name__ == "__main__":
    main()
