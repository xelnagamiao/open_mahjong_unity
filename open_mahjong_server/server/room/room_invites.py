"""Room invitations identify one lifetime of a reusable lobby number."""
import re
import uuid

from ..response import Response

EXPIRED_MESSAGE = "邀请已失效，原房间已解散。请联系好友获取新的邀请链接。"
INVALID_MESSAGE = "邀请链接不完整或格式不正确，请重新复制完整的邀请链接。"
_ROOM_ID = re.compile(r"[1-9][0-9]{0,9}\Z")
_REQUEST_ID = re.compile(r"[0-9A-Za-z_-]{1,64}\Z")


def parse_identity(room_id, instance_id):
    if not isinstance(room_id, str) or not _ROOM_ID.fullmatch(room_id):
        return None
    if not isinstance(instance_id, str) or len(instance_id) not in (32, 36):
        return None
    try:
        instance = uuid.UUID(instance_id)
    except (ValueError, AttributeError):
        return None
    if instance.version != 4:
        return None
    return room_id, str(instance)


def matching_room(manager, room_id, instance_id):
    identity = parse_identity(room_id, instance_id)
    if identity is None:
        return None
    room = manager.rooms.get(identity[0])
    if room is None or room.get("instance_id") != identity[1]:
        return None
    return room


async def handle_invite_message(manager, connection_id, message):
    """Resolve only the password requirement; joining retains every normal room check."""
    kind = message["type"].strip("/")
    request_id = message.get("invite_request_id")
    request_id = request_id if isinstance(request_id, str) and _REQUEST_ID.fullmatch(request_id) else None

    def reply(success, text, **data):
        return Response(type=kind, success=success, message=text,
                        invite_request_id=request_id, **data)

    identity = parse_identity(message.get("room_id"), message.get("room_instance_id"))
    if identity is None:
        return reply(False, INVALID_MESSAGE)
    room_id, instance_id = identity
    player = manager.game_server.players.get(connection_id)
    if player is None or not player.user_id:
        return reply(False, "请先登录")
    room = matching_room(manager, room_id, instance_id)
    if room is None:
        return reply(False, EXPIRED_MESSAGE)
    if room.get("duplicate_lobby_room_id") and message.get("duplicate_lobby_version") != 1:
        return reply(False, "请更新客户端以加入八人复式房间")

    if kind == "room/join_invite":
        password = message.get("password", "")
        if not isinstance(password, str):
            return reply(False, "房间密码格式不正确")
        result = await manager.join_room(connection_id, room_id, password,
                                         expected_instance_id=instance_id)
        current = matching_room(manager, room_id, instance_id) if result.success else None
        if result.success and current is None:
            return reply(False, EXPIRED_MESSAGE)
        if current and current.get("duplicate_lobby_room_id"):
            from .sichuan_lobby import public_lobby
            current = public_lobby(manager, current)
        return reply(result.success, result.message, room_id=room_id,
                     room_instance_id=instance_id, room_info=current)

    if room.get("admin_closing"):
        return reply(False, "房间正在关闭，请联系好友获取新的邀请链接。")
    if room.get("duplicate_match_id") and room.get("duplicate_status") in ("completed", "cancelled"):
        return reply(False, "该组复式比赛已结束，请加入新的比赛房间")
    if room.get("tourist_limit") and getattr(player, "is_tourist", False):
        return reply(False, "该房间不允许游客加入，请使用账号登录后重试。")
    denied = manager._can_join_venue_room(room.get("event_id"), player.user_id)
    if denied:
        return reply(False, denied)
    # No separate invitation detail view: only route into the existing password flow.
    info = {key: room[key] for key in ("room_id", "instance_id", "has_password")}
    return reply(True, "邀请房间信息已更新", invite_info=info,
                 room_id=room_id, room_instance_id=instance_id)
