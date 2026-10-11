"""Seat-bound temporary teams; repeated bot user IDs never identify a team member."""
from ..game_calculation.sichuan.doubles_rules import is_team_sub_rule


def ensure_teams(room):
    if not is_team_sub_rule(room.get("sub_rule")):
        return None
    if room.get("duplicate_table"):
        from ..game_calculation.sichuan.duplicate_rules import team_by_seat
        room["team_by_seat"] = team_by_seat(room["duplicate_table"])
    room.setdefault("roster_revision", 0)
    seats = room["seat_list"]
    if "team_by_seat" not in room:
        teams = [-1] * len(seats)
        for index, user_id in enumerate(seats):
            if user_id >= 0:
                teams[index] = 0 if teams.count(0) <= teams.count(1) else 1
        room["team_by_seat"] = teams
    return room["team_by_seat"]


def roster_changed(room, seat_index, joined):
    teams = ensure_teams(room)
    if teams is None:
        return
    if room.get("duplicate_table"):
        invalidate_ready(room)
        return
    teams[seat_index] = -1
    if joined:
        teams[seat_index] = 0 if teams.count(0) <= teams.count(1) else 1
    invalidate_ready(room)


def invalidate_ready(room):
    room["roster_revision"] = room.get("roster_revision", 0) + 1
    room["ready_list"] = []
    if room.get("duplicate_table") and room.get("duplicate_status") == "ready" and not room.get("is_game_running"):
        room["duplicate_status"] = "waiting"


def validate_revision(room, revision):
    if type(revision) is not int or revision != room.get("roster_revision", 0):
        raise ValueError("房间阵容已变化，请刷新后重新操作")


def validate_team_start(room, revision=None):
    teams = ensure_teams(room)
    if teams is None:
        return
    validate_revision(room, revision)
    if len(room["seat_list"]) != 4 or any(uid < 0 for uid in room["seat_list"]):
        raise ValueError("双打需要四名玩家")
    if len(teams) != 4 or teams.count(0) != 2 or teams.count(1) != 2:
        raise ValueError("双打需要 A、B 两队各两名玩家")


def assign_team(room, actor, seat_index, team_id, revision):
    if room.get("duplicate_table"):
        raise ValueError("复式队伍由开闭室座位固定，请调整入座位置")
    teams = ensure_teams(room)
    if teams is None:
        raise ValueError("该房间不支持分队")
    if room.get("is_game_running"):
        raise ValueError("对局进行中，队伍已锁定")
    validate_revision(room, revision)
    seats = room["seat_list"]
    if type(seat_index) is not int or not 0 <= seat_index < len(seats) or seats[seat_index] < 0:
        raise ValueError("请选择有玩家的座位")
    if actor != room.get("host_user_id") and seats[seat_index] != actor:
        raise ValueError("只能选择自己的队伍")
    if type(team_id) is not int or team_id not in (-1, 0, 1):
        raise ValueError("队伍编号无效")
    if teams[seat_index] == team_id:
        return
    if team_id >= 0 and teams.count(team_id) >= 2:
        raise ValueError("该队已满，请先调整队伍")
    teams[seat_index] = team_id
    invalidate_ready(room)
