using System;
using TMPro;
using UnityEngine;
using Salasasa.Home;

namespace Salasasa.Leaderboards
{
    public sealed class LeaderboardPaletteEditor : MonoBehaviour
    {
        public enum ColorSlot { Panel, Header, Row, Alternate, Text, Accent, Line, Mine }
        public const int PresetCount = 8;
        public const int DefaultPreset = 0;
        public const int ColorCount = 8;
        public const string SelectionPreference = "SalasasaInterface.LeaderboardPalette.V4.Selected";
        public const string PresetPreferencePrefix = "SalasasaInterface.LeaderboardPalette.V4.Preset.";
        public static readonly string[] PresetNames = { "原版白橙", "深蓝瓷白", "暖白赭棕", "原版石墨", "夜色深蓝", "炭黑香槟", "樱灰酒红", "银灰墨黑" };
        public static readonly string[] ColorNames = { "面板", "标题栏", "列表", "交替行", "文字", "强调", "分隔线", "我的排名" };

        [Serializable] public sealed class ShapeBinding
        {
            public SettingsShapeGraphic graphic;
            public ColorSlot fill, border, against;
            public bool readable, applyBorder = true;
            public float alpha = 1;
        }
        [Serializable] public sealed class TextBinding
        {
            public TMP_Text text;
            public ColorSlot fill, against;
            public bool readable, muted;
        }
        [Serializable] sealed class SavedPalette { public string[] colors; }


        public LeaderboardController controller;
        public LeaderboardView view;
        public ShapeBinding[] shapes;
        public TextBinding[] texts;
        public int SelectedPreset { get; private set; }
        public event Action ColorsApplied;
        Color[] colors;
        bool initialized;
        void Awake() => Initialize();
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            controller.DataApplied += ApplyColors;
            RestoreAndApply();
        }
        public void RestoreAndApply()
        {
            if (!initialized) { Initialize(); return; }
            int previous = Mathf.Clamp(PlayerPrefs.GetInt(LeaderboardController.StylePreference, 0), 0, 3);
            int[] migration = { DefaultPreset, 3, 2, 7 };
            SelectedPreset = Mathf.Clamp(PlayerPrefs.GetInt(SelectionPreference, migration[previous]), 0, PresetCount - 1);
            colors = ReadPreset(SelectedPreset);
            view.gameObject.SetActive(true);
            ApplyColors();
        }
        public void ResetAllPresets()
        {
            Initialize();
            for (int i = 0; i < PresetCount; i++) PlayerPrefs.DeleteKey(PresetPreferencePrefix + i);
            PlayerPrefs.DeleteKey(SelectionPreference);
            PlayerPrefs.DeleteKey(LeaderboardController.StylePreference);
            SelectedPreset = DefaultPreset;
            colors = DefaultColors(DefaultPreset);
            ApplyColors(); PlayerPrefs.Save();
        }
        public void SelectPreset(int index)
        {
            Initialize();
            SelectedPreset = Mathf.Clamp(index, 0, PresetCount - 1);
            colors = ReadPreset(SelectedPreset);
            ApplyColors();
            PlayerPrefs.SetInt(SelectionPreference, SelectedPreset); PlayerPrefs.Save();
        }
        public Color GetAppliedColor(int index)
        {
            var applied = colors != null && colors.Length == ColorCount ? colors : DefaultColors(SelectedPreset);
            return applied[Mathf.Clamp(index, 0, ColorCount - 1)];
        }

        public void ApplyColors()
        {
            if (colors == null) return;
            foreach (var target in shapes)
            {
                if (!target.graphic) continue;
                Color fill = colors[(int)target.fill];
                if (target.readable) fill = Readable(fill, colors[(int)target.against]);
                fill.a *= target.alpha; target.graphic.color = fill;
                if (target.applyBorder) target.graphic.borderColor = colors[(int)target.border];
                target.graphic.SetVerticesDirty();
            }
            foreach (var target in texts)
            {
                if (!target.text) continue;
                Color fill = colors[(int)target.fill], background = colors[(int)target.against];
                if (target.readable) fill = Readable(fill, background);
                if (target.muted) fill = Color.Lerp(background, fill, .72f);
                target.text.color = fill;
            }
            Color ink = colors[(int)ColorSlot.Text], accent = colors[(int)ColorSlot.Accent], mine = colors[(int)ColorSlot.Mine];
            int myId = controller.RankingUserId;
            for (int i = 0; i < view.rows.Length; i++)
            {
                var row = view.rows[i];
                row.normalFill = colors[(int)(i % 2 == 0 ? ColorSlot.Row : ColorSlot.Alternate)]; row.ownFill = mine;
                bool isMine = row.entry != null && myId > 0 && row.entry.user_id == myId;
                row.surface.color = isMine ? mine : row.normalFill;
                Color background = row.surface.color;
                row.position.color = i < 3 ? Readable(accent, background) : Color.Lerp(background, Readable(ink, background), .8f);
                row.username.color = row.detail.color = Readable(ink, background);
                if (row.uid) row.uid.color = Color.Lerp(background, Readable(ink, background), .8f);
                row.score.color = Readable(accent, background);
            }
            ColorsApplied?.Invoke();
        }

        Color[] ReadPreset(int index)
        {
            var result = DefaultColors(index);
            string raw = PlayerPrefs.GetString(PresetPreferencePrefix + index, "");
            if (string.IsNullOrEmpty(raw)) return result;
            try
            {
                var saved = JsonUtility.FromJson<SavedPalette>(raw);
                if (saved?.colors == null || saved.colors.Length != ColorCount) return result;
                var parsed = new Color[ColorCount];
                for (int i = 0; i < ColorCount; i++) if (!ColorUtility.TryParseHtmlString(saved.colors[i], out parsed[i])) return result;
                return parsed;
            }
            catch (ArgumentException) { return result; }
        }

        void OnDestroy() { if (controller) controller.DataApplied -= ApplyColors; }
        public void SetBakedDefault() { SelectedPreset = DefaultPreset; colors = DefaultColors(DefaultPreset); ApplyColors(); }
        public static Color[] DefaultColors(int index)
        {
            string[][] values = {
                new[] { "F4F5F7", "FFFFFF", "FFFFFF", "F0F2F4", "252D36", "B95120", "CDD4DC", "E5EDF6" },
                new[] { "F3F4F6", "183A5B", "FFFFFF", "EEF2F6", "243344", "2A65AD", "C9D4E0", "DDEBFA" },
                new[] { "F5F1E9", "49372A", "FFFDFA", "ECE6DB", "3C3026", "95532B", "CFC2B2", "EFE0CD" },
                new[] { "3A3A3A", "202020", "555555", "414141", "FFFFFF", "ECA15B", "737373", "68584A" },
                new[] { "182331", "0A111B", "243447", "1D2C3C", "EDF3F9", "86C6F1", "465B70", "314B62" },
                new[] { "25282D", "111315", "363A40", "2C3035", "F0F0ED", "E0BA69", "60666E", "494333" },
                new[] { "F6F3F4", "46323B", "FFFFFF", "F0ECEF", "3B3036", "AD3F59", "D5C9CF", "F3DCE3" },
                new[] { "E8EAEC", "292B2E", "FAFAFA", "E0E3E6", "202124", "454C55", "B9BFC5", "D1D7DD" }
            };
            var result = new Color[ColorCount];
            for (int i = 0; i < ColorCount; i++) result[i] = C(values[Mathf.Clamp(index, 0, PresetCount - 1)][i]);
            return result;
        }
        static Color C(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }
        static float Luminance(Color color) { var linear = color.linear; return .2126f * linear.r + .7152f * linear.g + .0722f * linear.b; }
        static Color Readable(Color ink, Color background)
        {
            float a = Luminance(ink), b = Luminance(background);
            if ((Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f) >= 3f) return ink;
            return (1.05f / (b + .05f)) >= ((b + .05f) / .05f) ? Color.white : new Color32(24, 29, 35, 255);
        }
    }
}
