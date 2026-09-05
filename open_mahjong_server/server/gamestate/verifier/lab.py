"""本地 FastAPI：只绑 127.0.0.1:8099。"""
from __future__ import annotations

import logging
from typing import Any, Dict, Optional

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

from .protocol import load_protocol
from .scenarios import get_scenario, list_scenarios
from .session import VerifierError, VerifierSession

logger = logging.getLogger(__name__)

app = FastAPI(title="Guobiao Verifier Lab", docs_url="/docs")
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

_SESSIONS: Dict[str, VerifierSession] = {}


class StartBody(BaseModel):
    scenario_id: Optional[str] = None
    seed: Optional[int] = None
    debug_scenario: Optional[str] = None
    tactical_call: Optional[bool] = None
    game_round: Optional[int] = None
    hepai_limit: Optional[int] = None


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


def _http(exc: VerifierError) -> HTTPException:
    return HTTPException(status_code=exc.status_code, detail=str(exc))


@app.get("/health")
async def health() -> Dict[str, Any]:
    return {"ok": True, "sessions": list(_SESSIONS)}


@app.get("/scenarios")
async def scenarios() -> Dict[str, Any]:
    return {"scenarios": list_scenarios()}


@app.get("/protocol")
async def protocol() -> Dict[str, Any]:
    return load_protocol()


@app.post("/sessions")
async def create_session(body: StartBody) -> Dict[str, Any]:
    cfg: Dict[str, Any] = {
        "seed": 72001,
        "debug_scenario": None,
        "tactical_call": False,
        "game_round": 1,
        "hepai_limit": 8,
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
    session = VerifierSession()
    try:
        await session.start(**cfg)
    except VerifierError as exc:
        await session.stop()
        raise _http(exc) from exc
    except Exception as exc:
        await session.stop()
        logger.exception("create session failed")
        raise HTTPException(status_code=500, detail=str(exc)) from exc
    _SESSIONS[session.id] = session
    return session.snapshot()


def _session(session_id: str) -> VerifierSession:
    session = _SESSIONS.get(session_id)
    if session is None:
        raise HTTPException(status_code=404, detail=f"session {session_id} 不存在")
    return session


@app.get("/sessions/{session_id}")
async def get_session(session_id: str) -> Dict[str, Any]:
    return _session(session_id).snapshot()


@app.delete("/sessions/{session_id}")
async def delete_session(session_id: str) -> Dict[str, Any]:
    session = _session(session_id)
    await session.stop()
    _SESSIONS.pop(session_id, None)
    return {"ok": True}


@app.post("/sessions/{session_id}/actions")
async def post_action(session_id: str, body: ActionBody) -> Dict[str, Any]:
    session = _session(session_id)
    try:
        return await session.submit(body.model_dump())
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/ready-all")
async def post_ready_all(session_id: str) -> Dict[str, Any]:
    try:
        return await _session(session_id).ready_all()
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/timeout")
async def post_timeout(session_id: str) -> Dict[str, Any]:
    try:
        return await _session(session_id).force_timeout()
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/restore")
async def post_restore(session_id: str, body: RestoreBody) -> Dict[str, Any]:
    try:
        return await _session(session_id).restore(body.index)
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/bookmarks")
async def post_bookmark(session_id: str, body: BookmarkBody) -> Dict[str, Any]:
    try:
        return _session(session_id).set_bookmark(body.name)
    except VerifierError as exc:
        raise _http(exc) from exc


@app.post("/sessions/{session_id}/tools/tingpai")
async def post_tingpai(session_id: str, body: TingpaiBody) -> Dict[str, Any]:
    try:
        return _session(session_id).call_tingpai(body.seat)
    except VerifierError as exc:
        raise _http(exc) from exc


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
