import pytest
from pydantic import ValidationError

from .duocai_room import DuocaiRoomValidator


@pytest.mark.parametrize('sub_rule,rounds', [('duocai/guobiao', 4), ('duocai/riichi', 2)])
def test_duocai_round_count_is_validated_after_inherited_fields(sub_rule, rounds):
    room = DuocaiRoomValidator(room_name='夺彩测试', sub_rule=sub_rule, game_round=rounds)
    assert room.sub_rule == sub_rule and room.game_round == rounds


@pytest.mark.parametrize('sub_rule,rounds', [('duocai/guobiao', 2), ('duocai/riichi', 4)])
def test_duocai_rejects_other_variants_round_count(sub_rule, rounds):
    with pytest.raises(ValidationError, match='夺彩国标采用全庄，立直采用东南战'):
        DuocaiRoomValidator(room_name='夺彩测试', sub_rule=sub_rule, game_round=rounds)
