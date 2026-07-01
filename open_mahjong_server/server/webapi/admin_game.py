"""
管理后台对局实时控制 HTTP 接口（仅供 Node 管理端 localhost 代理调用）。
列出进行中的对局，并支持强制暂停 / 解除暂停 / 结束对局。
全部为按需调用，仅操作内存中已有的 game_state，无轮询、无额外协程，性能开销可忽略。
"""
import logging
from typing import Any, Dict, List

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)


class AdminGameTargetBody(BaseModel):
    gamestate_id: str = Field(..., min_length=1)


def _player_brief(p) -> Dict[str, Any]:
    return {
        "user_id": p.user_id,
        "username": getattr(p, "username", None) or str(p.user_id),
        "is_bot": bool(getattr(p, "is_bot", False)) or p.user_id <= 10,
        "player_index": getattr(p, "player_index", None),
    }


def _game_brief(gamestate_id: str, gs) -> Dict[str, Any]:
    vm = getattr(gs, "vote_manager", None)
    return {
        "gamestate_id": gamestate_id,
        "room_id": getattr(gs, "room_id", None),
        "room_type": getattr(gs, "room_type", None),
        "room_rule": getattr(gs, "room_rule", None),
        "sub_rule": getattr(gs, "sub_rule", None),
        "game_status": getattr(gs, "game_status", None),
        "vote_phase": getattr(vm, "phase", "idle") if vm else "idle",
        "players": [_player_brief(p) for p in getattr(gs, "player_list", [])],
    }


def register_admin_game_routes(app: FastAPI, game_server) -> None:
    @app.get("/admin/game/list")
    async def admin_game_list():
        gm = game_server.gamestate_manager
        items: List[Dict[str, Any]] = []
        for gamestate_id, gs in list(gm.gamestate_id_to_game_state.items()):
            try:
                items.append(_game_brief(gamestate_id, gs))
            except Exception as exc:
                logger.warning("枚举对局失败 gamestate_id=%s: %s", gamestate_id, exc)
        return {"success": True, "total": len(items), "items": items}

    async def _resolve_vm(gamestate_id: str):
        gs = game_server.gamestate_manager.get_game_state_by_gamestate_id(gamestate_id)
        if gs is None:
            raise HTTPException(status_code=404, detail="对局不存在或已结束")
        from ..gamestate.public.vote_manager import get_or_create_vote_manager
        return get_or_create_vote_manager(gs)

    @app.post("/admin/game/pause")
    async def admin_game_pause(body: AdminGameTargetBody):
        vm = await _resolve_vm(body.gamestate_id)
        ok, msg = await vm.admin_force_pause()
        if not ok:
            raise HTTPException(status_code=400, detail=msg)
        return {"success": True, "gamestate_id": body.gamestate_id, "message": msg}

    @app.post("/admin/game/resume")
    async def admin_game_resume(body: AdminGameTargetBody):
        vm = await _resolve_vm(body.gamestate_id)
        ok, msg = await vm.admin_force_resume()
        if not ok:
            raise HTTPException(status_code=400, detail=msg)
        return {"success": True, "gamestate_id": body.gamestate_id, "message": msg}

    @app.post("/admin/game/end")
    async def admin_game_end(body: AdminGameTargetBody):
        vm = await _resolve_vm(body.gamestate_id)
        ok, msg = await vm.admin_force_end()
        if not ok:
            raise HTTPException(status_code=400, detail=msg)
        return {"success": True, "gamestate_id": body.gamestate_id, "message": msg}
