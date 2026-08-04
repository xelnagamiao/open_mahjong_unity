import unittest

from .username_validation import normalize_username, username_display_length, validate_username


class UsernameValidationTest(unittest.TestCase):
    def test_single_kana_is_a_valid_double_width_name(self):
        self.assertEqual(username_display_length("麻"), 2)
        self.assertEqual(username_display_length("あ"), 2)
        self.assertEqual(username_display_length("ｶ"), 2)
        self.assertIsNone(validate_username("あ"))

    def test_normalizes_decomposed_kana(self):
        self.assertEqual(normalize_username(" は\u3099 "), "ば")
        self.assertEqual(username_display_length(normalize_username("は\u3099")), 2)

    def test_enforces_weight_and_code_point_limits(self):
        self.assertIsNone(validate_username("あ" * 10))
        self.assertEqual(validate_username("あ" * 11), "用户名显示长度不能超过20")
        self.assertIsNone(validate_username("a" * 16))
        self.assertEqual(validate_username("a" * 17), "用户名不能超过16个字符")

    def test_rejects_invisible_format_characters(self):
        self.assertEqual(
            validate_username("a\u200db"),
            "用户名不能包含控制字符或不可见格式字符",
        )


if __name__ == "__main__":
    unittest.main()
