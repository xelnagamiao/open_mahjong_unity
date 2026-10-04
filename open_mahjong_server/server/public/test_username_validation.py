import unittest

from .username_validation import normalize_username, username_display_length, validate_username


class UsernameValidationTest(unittest.TestCase):
    def test_cjk_kana_hangul_and_fullwidth_count_as_two(self):
        for name in ["麻", "あ", "ア", "ｱ", "한", "Ａ", "\U00020000"]:
            with self.subTest(name=name):
                self.assertEqual(username_display_length(name), 2)
                self.assertIsNone(validate_username(name))
        self.assertEqual(username_display_length("ab"), 2)
        self.assertIsNone(validate_username("ab"))
        self.assertIsNotNone(validate_username("a"))

    def test_historical_unicode_names_and_punctuation_remain_supported(self):
        for name in ["あい", "カナ", "ｶﾅ", "山田たろう", "玖柒、97", "user_1",
                     "old player", "café", "한글", "玩家01", "😀😀"]:
            with self.subTest(name=name):
                self.assertIsNone(validate_username(name))

    def test_display_length_must_stay_between_2_and_20(self):
        for char in ["中", "あ", "ｱ", "\U00020000"]:
            with self.subTest(char=char):
                self.assertIsNone(validate_username(char * 10))
                self.assertEqual(validate_username(char * 11), "用户名显示长度不能超过20")

    def test_code_point_limit_is_separate_from_display_length(self):
        for char in ["a", "😀"]:
            with self.subTest(char=char):
                self.assertIsNone(validate_username(char * 16))
                self.assertEqual(validate_username(char * 17), "用户名不能超过16个字符")

    def test_usernames_are_trimmed_and_nfc_normalized(self):
        self.assertEqual(normalize_username(" は\u3099 "), "ば")
        self.assertIsNone(validate_username(" は\u3099 "))
        self.assertIsNone(validate_username("は\u3099" * 10))
        self.assertEqual(username_display_length("は\u3099"), 2)

    def test_control_format_and_surrogate_characters_are_rejected(self):
        for char in ["\x00", "\t", "\n", "\u200b", "\u200d", "\u202e",
                     "\ud800", "\udc00", "\u2028", "\u2029"]:
            with self.subTest(char=repr(char)):
                self.assertEqual(validate_username("a" + char + "b"),
                                 "用户名不能包含控制字符或不可见格式字符")

    def test_blank_and_combining_only_names_are_rejected(self):
        for name in [None, "", " \t ", "\u3099", "\u0301\u0301"]:
            with self.subTest(name=name):
                self.assertIsNotNone(validate_username(name))


if __name__ == "__main__":
    unittest.main()
