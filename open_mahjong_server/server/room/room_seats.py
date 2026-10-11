"""Lobby seats stay fixed; player_list retains arrival order for host succession."""
from .room_teams import ensure_teams, roster_changed


def get_seats(room: dict) -> list:
    if "seat_list" not in room:
        players = room.get("player_list", [])
        room["seat_list"] = list(players) + [-1] * (room.get("max_player", 4) - len(players))
    ensure_teams(room)
    return room["seat_list"]


def _validate_index(seats: list, seat_index: int) -> None:
    if type(seat_index) is not int or not 0 <= seat_index < len(seats):
        raise ValueError("座位编号无效")


def seat_player(room: dict, user_id: int, seat_index=None) -> None:
    seats = get_seats(room)
    if seat_index is None:
        if -1 not in seats:
            raise ValueError("房间已满")
        seat_index = seats.index(-1)
    _validate_index(seats, seat_index)
    if seats[seat_index] != -1:
        raise ValueError("该座位已有人，请刷新房间后重试")
    seats[seat_index] = user_id
    room["player_list"].append(user_id)
    roster_changed(room, seat_index, joined=True)


def unseat_player(room: dict, user_id: int, seat_index=None) -> None:
    seats = get_seats(room)
    if seat_index is None:
        if user_id not in seats:
            raise ValueError("目标玩家不在房间中")
        seat_index = seats.index(user_id)
    _validate_index(seats, seat_index)
    if seats[seat_index] != user_id or user_id not in room["player_list"]:
        raise ValueError("座位信息已变化，请刷新房间后重试")
    seats[seat_index] = -1
    room["player_list"].remove(user_id)
    roster_changed(room, seat_index, joined=False)


def move_duplicate_seat(room, actor, source, target, revision):
    """Keep fixed compass teams; only the host may swap occupied seats."""
    from .room_teams import invalidate_ready, validate_revision
    if not room.get("duplicate_table"):
        raise ValueError("该房间不支持选择复式座位")
    if room.get("is_game_running") or room.get("event_seating_pending"):
        raise ValueError("对局进行中，座位已锁定")
    validate_revision(room, revision)
    seats = get_seats(room)
    _validate_index(seats, source)
    _validate_index(seats, target)
    if seats[source] < 0:
        raise ValueError("请选择有玩家的座位")
    is_host = actor == room.get("host_user_id")
    if not is_host and seats[source] != actor:
        raise ValueError("只能调整自己的座位")
    if source == target:
        return
    if not is_host and seats[target] >= 0:
        raise ValueError("该座位已有人，请由房主调整")
    seats[source], seats[target] = seats[target], seats[source]
    invalidate_ready(room)
