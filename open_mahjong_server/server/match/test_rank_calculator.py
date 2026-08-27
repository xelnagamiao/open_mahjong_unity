from server.match.rank_calculator import calculate_pt


def test_first_and_second_use_format_multiplier():
    assert calculate_pt("beginner", "quanzhuang", 1, "2级") == 24
    assert calculate_pt("beginner", "banzhuang", 1, "2级") == 16.8
    assert calculate_pt("beginner", "dongfeng", 1, "2级") == 11.76
    assert calculate_pt("beginner", "quanzhuang", 2, "2级") == 6
    assert calculate_pt("beginner", "banzhuang", 2, "2级") == 4.2
    assert calculate_pt("beginner", "dongfeng", 2, "2级") == 2.94
    assert calculate_pt("advanced", "quanzhuang", 1, "四段") == 84


def test_third_and_fourth_also_use_format_multiplier():
    assert calculate_pt("beginner", "quanzhuang", 3, "2级") == -4.5
    assert calculate_pt("beginner", "banzhuang", 3, "2级") == round(-15 * 0.3 * 0.7, 2)
    assert calculate_pt("beginner", "dongfeng", 3, "2级") == round(-15 * 0.3 * 0.49, 2)
    assert calculate_pt("beginner", "quanzhuang", 4, "2级") == -10.5
    assert calculate_pt("beginner", "banzhuang", 4, "2级") == round(-15 * 0.7 * 0.7, 2)
    assert calculate_pt("beginner", "dongfeng", 4, "2级") == round(-15 * 0.7 * 0.49, 2)
    assert calculate_pt("advanced", "dongfeng", 4, "四段") == round(-95 * 0.7 * 0.49, 2)
