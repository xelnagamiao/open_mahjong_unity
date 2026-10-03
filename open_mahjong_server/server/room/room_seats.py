"""Lobby seats stay fixed; player_list retains arrival order for host succession."""


def get_seats(room: dict) -> list:
    if "seat_list" not in room:
        players = room.get("player_list", [])
        room["seat_list"] = list(players) + [-1] * (room.get("max_player", 4) - len(players))
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
