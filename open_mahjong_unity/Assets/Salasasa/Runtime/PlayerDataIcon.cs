using TMPro;
using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    public sealed class PlayerDataIcon : MonoBehaviour
    {
        public const int VariantCount = 9;
        public const int DefaultStyle = 0;
        public static readonly string[] Glyphs = { "\uf2bb", "\uf2b9", "\uf2bd", "\uf2c1", "\uf681", "\uf15c", "\uf007", "\uf201", "\uf080" };
        public static readonly string[] Labels = { "资料卡", "个人档案", "头像圆章", "身份牌", "统计面板", "对局档案", "人物剪影", "战绩曲线", "成绩柱图" };
        public static readonly string[] InventoryGlyphs = { "\uf290", "\uf0b1", "\uf49e" };
        public static readonly string[] InventoryLabels = { "随身袋", "旅行箱", "物品箱" };
        public bool inventory;
        public TMP_Text glyph;
        public float visualSize = 46;
        [SerializeField] int style;
        public int Style => style;

        void OnEnable() => Apply(style);

        public void Apply(int value)
        {
            var symbols = inventory ? InventoryGlyphs : Glyphs;
            style = Mathf.Clamp(value, 0, symbols.Length - 1);
            if (!glyph || !glyph.font) return;
            if (!glyph.font.characterLookupTable.TryGetValue(symbols[style][0], out var character)) return;
            var metrics = character.glyph.metrics;
            float scale = glyph.font.faceInfo.scale * character.scale * character.glyph.scale;
            glyph.text = symbols[style];
            glyph.fontSize = visualSize * glyph.font.faceInfo.pointSize
                / (Mathf.Max(metrics.width, metrics.height) * scale);
            glyph.fontStyle = FontStyles.Normal;
            glyph.fontWeight = FontWeight.Regular;
            glyph.alignment = TextAlignmentOptions.MidlineGeoAligned;
            glyph.enableAutoSizing = false;
            glyph.textWrappingMode = TextWrappingModes.NoWrap;
            glyph.overflowMode = TextOverflowModes.Overflow;
            glyph.margin = Vector4.zero;
            glyph.richText = false;
            glyph.raycastTarget = false;
        }
    }
}
