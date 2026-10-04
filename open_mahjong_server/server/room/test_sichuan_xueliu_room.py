import pytest

from .room_validators import SichuanRoomValidator


def _base(**kwargs):
    values = {
        "room_name": "血流成河",
        "game_round": 1,
        "round_timer": 20,
        "step_timer": 5,
    }
    values.update(kwargs)
    return values


def test_room_validator_accepts_xueliu_sub_rule():
    room = SichuanRoomValidator(**_base(sub_rule="sichuan/xueliu"))
    assert room.sub_rule == "sichuan/xueliu"


def test_room_validator_rejects_unknown_sichuan_sub_rule():
    with pytest.raises(ValueError):
        SichuanRoomValidator(**_base(sub_rule="sichuan/unknown"))
