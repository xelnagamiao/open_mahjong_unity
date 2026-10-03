import pytest

from .xueliu_rules import (
    XUELIU_RULE_PROFILE,
    XUELIU_SUB_RULE,
    calculate_xueliu_fan,
    calculate_xueliu_fan_from_sichuan_result,
    xueliu_base_from_fan,
    is_valid_discard_three,
    validate_sichuan_sub_rule,
)


def test_xueliu_profile_discards_three_without_exchange():
    assert XUELIU_RULE_PROFILE.sub_rule == XUELIU_SUB_RULE
    assert XUELIU_RULE_PROFILE.discard_three is True
    assert XUELIU_RULE_PROFILE.discard_three_same_suit is True
    assert XUELIU_RULE_PROFILE.hu_winner_stays_active is True
    assert XUELIU_RULE_PROFILE.allow_post_hu_ming_gang is False


@pytest.mark.parametrize(
    ("kwargs", "expected", "primary"),
    [
        ({}, 1, "基本胡"),
        ({"pengpeng": True}, 4, "碰碰胡"),
        ({"qingyise": True}, 6, "清一色"),
        ({"pengpeng": True, "qingyise": True}, 12, "清碰"),
        ({"menqing": True, "zimo": True}, 2, "门清自摸"),
        ({"minggang_count": 1}, 2, "明杠"),
        ({"angang_count": 1}, 3, "暗杠"),
    ],
)
def test_xueliu_fan_values(kwargs, expected, primary):
    fan, names = calculate_xueliu_fan(**kwargs)
    assert fan == expected
    assert primary in names


def test_xueliu_fan_adds_gangs_to_shape_tier():
    fan, names = calculate_xueliu_fan(
        pengpeng=True, minggang_count=1, angang_count=1
    )
    assert fan == 7  # 碰碰胡4 + 明杠1 + 暗杠2
    assert names.count("明杠") == 1
    assert names.count("暗杠") == 1


def test_xueliu_fan_values_are_direct_settlement_amounts():
    assert xueliu_base_from_fan(1) == 1
    assert xueliu_base_from_fan(4) == 4
    assert xueliu_base_from_fan(12) == 12


def test_translate_existing_sichuan_shape_names():
    fan, names = calculate_xueliu_fan_from_sichuan_result(
        ["清一色", "大对子"], ["g11", "G22"], zimo=True
    )
    # 清碰12 + 暗杠2 + 明杠1；副露后不再有门清自摸。
    assert fan == 15
    assert names[0] == "清碰"


def test_validate_sichuan_sub_rule():
    assert validate_sichuan_sub_rule(None) == "sichuan/standard"
    assert validate_sichuan_sub_rule("sichuan/xueliu") == "sichuan/xueliu"
    with pytest.raises(ValueError):
        validate_sichuan_sub_rule("sichuan/unknown")


def test_discard_three_requires_three_same_suit_number_tiles():
    assert is_valid_discard_three([11, 12, 19])
    assert not is_valid_discard_three([11, 21, 31])
    assert not is_valid_discard_three([11, 12])
    assert not is_valid_discard_three([11, 12, 41])
