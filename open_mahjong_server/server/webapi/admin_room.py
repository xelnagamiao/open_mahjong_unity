"""仅供本机 Node 管理端代理调用的房间管理接口。"""
from typing import Any
import hashlib
import hmac
import time

from fastapi import Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel, Field

from ..room.admin_rooms import close_admin_room, list_admin_rooms


class CreateRoomBody(BaseModel):
    room_rule: str = Field(min_length=1, max_length=40)
    room_config: dict[str, Any] = Field(default_factory=dict)
    password: str = Field(default="", max_length=128)
    created_by: int = Field(gt=10)


class CloseRoomBody(BaseModel):
    instance_id: str = Field(min_length=1, max_length=100)


def register_admin_room_routes(app: FastAPI, game_server, *, proxy_secret: str):
    async def require_local_proxy(request: Request):
        # 回环地址本身不能认证：公开反向代理的请求也可能来自本机。
        stamp = request.headers.get("x-room-admin-time", "")
        signature = request.headers.get("x-room-admin-signature", "")
        try:
            timely = abs(time.time() - int(stamp)) <= 30
        except (ValueError, OverflowError):
            timely = False
        if (not proxy_secret or not timely or not request.client
                or request.client.host not in ("127.0.0.1", "::1") or request.headers.get("origin")):
            raise HTTPException(status_code=403, detail="请通过管理后台操作")
        digest = hashlib.sha256(await request.body()).hexdigest()
        message = f"room-admin\n{stamp}\n{request.method}\n{request.url.path}\n{digest}"
        expected = hmac.new(proxy_secret.encode(), message.encode(), hashlib.sha256).hexdigest()
        if not hmac.compare_digest(signature.encode(), expected.encode()):
            raise HTTPException(status_code=403, detail="管理端代理认证失败")

    @app.get("/admin/rooms", dependencies=[Depends(require_local_proxy)])
    async def rooms():
        return {"success": True, "items": list_admin_rooms(game_server.room_manager)}

    @app.post("/admin/rooms/create", dependencies=[Depends(require_local_proxy)])
    async def create(body: CreateRoomBody):
        if body.room_config.get("sub_rule") in ("sichuan/doubles_duplicate", "sichuan/xueliu_doubles_duplicate"):
            raise HTTPException(status_code=400, detail="复式双打请选择比赛场创建")
        try:
            response = await game_server.room_manager.create_empty_event_room(
                None, body.room_rule, body.room_config, body.password, body.created_by, broadcast=False)
        except ValueError as error:
            raise HTTPException(status_code=400, detail=str(error)) from error
        if not response.success:
            raise HTTPException(status_code=400, detail=response.message)
        room = response.room_info
        # 管理员预先创建的空房等候玩家入座，由管理员主动关闭。
        room["persist_empty"] = True
        get_room = game_server.room_manager.rooms[room["room_id"]]
        get_room["persist_empty"] = True
        from ..room.room_seats import get_seats
        get_seats(get_room)
        return {"success": True, "message": "房间已创建", "room_id": room["room_id"]}

    @app.post("/admin/rooms/{room_id}/close", dependencies=[Depends(require_local_proxy)])
    async def close(room_id: str, body: CloseRoomBody):
        try:
            closed_ids = await close_admin_room(game_server.room_manager, room_id, body.instance_id)
        except LookupError as error:
            raise HTTPException(status_code=404, detail=str(error)) from error
        except ValueError as error:
            raise HTTPException(status_code=409, detail=str(error)) from error
        return {"success": True, "message": "房间已关闭", "closed_room_ids": closed_ids}
