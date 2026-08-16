"""
管理后台赛事空房间 HTTP 接口（仅供 Node 管理端 localhost 代理调用）。
创建 / 列出 / 删除比赛场空房间。
"""
import logging
from typing import Any, Dict, List, Optional

from fastapi import FastAPI, HTTPException, Query
from pydantic import BaseModel, Field

logger = logging.getLogger(__name__)


class AdminEventRoomCreateBody(BaseModel):
    event_id: str = Field(..., min_length=1)
    room_rule: str = Field(..., min_length=1)
    room_config: Optional[Dict[str, Any]] = None
    password: str = ""
    created_by: Optional[int] = None


class AdminEventSeatBody(BaseModel):
    event_id: str = Field(..., min_length=1)
    user_ids: List[int]
    room_rule: str = Field(default="guobiao", min_length=1)
    room_config: Optional[Dict[str, Any]] = None
    created_by: Optional[int] = None


def register_admin_event_room_routes(app: FastAPI, game_server) -> None:
    @app.post("/admin/event/rooms/create")
    async def admin_event_room_create(body: AdminEventRoomCreateBody):
        try:
            response = await game_server.room_manager.create_empty_event_room(
                event_id=body.event_id,
                room_rule=body.room_rule,
                room_config=body.room_config or {},
                password=body.password or "",
                created_by=body.created_by,
            )
        except Exception as exc:
            logger.error("创建赛事空房间失败: %s", exc, exc_info=True)
            raise HTTPException(status_code=500, detail="创建房间失败") from exc
        if not response.success:
            raise HTTPException(status_code=400, detail=response.message or "创建房间失败")
        return {
            "success": True,
            "message": response.message,
            "room_info": response.room_info,
        }

    @app.get("/admin/event/rooms")
    async def admin_event_room_list(event_id: str = Query(..., min_length=1)):
        try:
            items = game_server.room_manager.list_event_rooms(event_id)
        except Exception as exc:
            logger.error("列出赛事房间失败: %s", exc, exc_info=True)
            raise HTTPException(status_code=500, detail="获取房间列表失败") from exc
        return {"success": True, "items": items}

    @app.delete("/admin/event/rooms/{room_id}")
    async def admin_event_room_delete(
        room_id: str,
        event_id: Optional[str] = Query(None),
    ):
        try:
            response = await game_server.room_manager.admin_destroy_event_room(
                room_id=room_id,
                event_id=event_id,
            )
        except Exception as exc:
            logger.error("删除赛事房间失败: %s", exc, exc_info=True)
            raise HTTPException(status_code=500, detail="删除房间失败") from exc
        if not response.success:
            raise HTTPException(status_code=400, detail=response.message or "删除房间失败")
        return {"success": True, "message": response.message}

    @app.post("/admin/event/rooms/seat")
    async def admin_event_room_seat(body: AdminEventSeatBody):
        if not body.created_by:
            raise HTTPException(status_code=400, detail="缺少管理员")
        try:
            response = await game_server.room_manager.seat_event_table(
                admin_user_id=body.created_by,
                event_id=body.event_id,
                user_ids=body.user_ids,
                room_rule=body.room_rule,
                room_config=body.room_config or {},
            )
        except Exception as exc:
            logger.error("赛事组桌失败: %s", exc, exc_info=True)
            raise HTTPException(status_code=500, detail="组桌失败") from exc
        if not response.success:
            raise HTTPException(status_code=400, detail=response.message or "组桌失败")
        return {
            "success": True,
            "message": response.message,
            "room_info": response.room_info,
        }
