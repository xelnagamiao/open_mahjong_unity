import tempfile
import unittest
from pathlib import Path
from font_pipeline import strings, codepoints, write_changed, cjk, scoped_text


class FontInventoryTests(unittest.TestCase):
    def test_csharp_strings_ignore_comments_preserve_unicode_and_real_escapes(self):
        code = '// "注释"\nvar a = "更新\\u9F98"; /* "忽略" */ var b = @"复制""文本"; var c = \'é\';'
        self.assertEqual(list(strings(Path("x.cs"), code)), ["更新龘", '复制"文本', "é"])

    def test_localization_and_nested_dynamic_templates(self):
        text = '{"entries":[{"zh":"{username} 的游戏","en":"{username}’s game"}],"traditionalCharacters":"甲乙"}'
        values = list(strings(Path("x.json"), text))
        self.assertIn("{username}’s game", values)
        self.assertNotIn("甲乙", values)

    def test_yaml_quoted_multiline(self):
        text = '  m_text: "\\u6D4B\\u8BD5\n    updated"\n  m_fontSize: 20\n  title: 更多\n'
        self.assertEqual(list(strings(Path("x.prefab"), text)), ["测试 updated", "更多"])

    def test_scalar_unicode_and_markup(self):
        self.assertEqual(codepoints('<color=#ffffff>é龘</color>\ud83d\ude00'), {233, 0x9F98, 0x1F600})
        self.assertTrue(cjk(0x9F98))
        self.assertFalse(cjk(ord("é")))

    def test_icon_private_use_and_per_font_label_collection(self):
        guid = "a" * 32
        text = '--- !u!114 &10\nMonoBehaviour:\n  m_fontAsset: {fileID: 11400000, guid: '+guid+', type: 2}\n  m_text: "\\uf007"\n'
        self.assertEqual(scoped_text(text), {guid: ["\uf007"]})
        self.assertEqual(codepoints("\uf007"), {0xf007})

    def test_identical_output_is_not_rewritten(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "manifest.json"
            self.assertTrue(write_changed(path, {"a": "文字"}))
            stamp = path.stat().st_mtime_ns
            self.assertFalse(write_changed(path, {"a": "文字"}))
            self.assertEqual(path.stat().st_mtime_ns, stamp)


if __name__ == "__main__":
    unittest.main()
